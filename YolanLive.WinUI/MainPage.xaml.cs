using YolanLive.Core.Interface;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using YolanLive.WinUI.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using YolanLive.Core.Models;
using Windows.ApplicationModel.Core;
using System.Threading.Tasks;
using Windows.Services.Store;
using Windows.UI.Popups;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace YolanLive.WinUI
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {

        internal Page ActivePage => frame.Content as Page;
        internal bool IsTopNavigation => navigationView.PaneDisplayMode == NavigationViewPaneDisplayMode.Top;

        private AutoSuggestBox headerSearch;

        internal void SetHeaderSearch(AutoSuggestBox search)
        {
            if (headerSearch == search) return;
            if (search != null) search.Text = headerSearch?.Text ?? searchBox.Text;
            else if (headerSearch != null) searchBox.Text = headerSearch.Text;
            headerSearch = search;
            navigationView.AutoSuggestBox = search == null ? searchBox : null;
        }

        internal void ClearHeaderSearch(AutoSuggestBox search)
        {
            if (headerSearch == search) SetHeaderSearch(null);
        }

        private bool paneInitialized;
        private bool startupUpdateChecked;
        private System.Threading.CancellationTokenSource randomLiveOperation;

        public MainPage()
        {

            this.NavigationCacheMode = NavigationCacheMode.Enabled;
            this.InitializeComponent();
            Loaded += (_, _) => { MessageCenter.UpdatePanelDisplayModeEvent -= MessageCenter_UpdatePanelDisplayModeEvent; MessageCenter.UpdatePanelDisplayModeEvent += MessageCenter_UpdatePanelDisplayModeEvent; };
            this.KeyDown += MainPage_KeyDown;
            SetPaneMode();
            Loaded += MainPage_CheckUpdateOnStartup;
            Unloaded += (_, _) => { SetHeaderSearch(null); randomLiveOperation?.Cancel(); MessageCenter.UpdatePanelDisplayModeEvent -= MessageCenter_UpdatePanelDisplayModeEvent; };
        }

        private async void MainPage_CheckUpdateOnStartup(object sender, RoutedEventArgs e)
        {
            if (startupUpdateChecked || Environment.GetCommandLineArgs().Contains("--smoke-test") || Environment.GetCommandLineArgs().Contains("--category-repro")) return;
            startupUpdateChecked = true;
            var updates = UpdateVM.Instance;
            await updates.CheckAsync();
            if (updates.AvailableVersion == null || !IsLoaded || XamlRoot == null) return;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "发现新版本 " + updates.AvailableVersion,
                Content = "游岚有新版本可用，可前往设置查看并下载安装包。",
                PrimaryButtonText = "查看更新",
                CloseButtonText = "稍后",
                DefaultButton = ContentDialogButton.Close
            };
            try
            {
                if (await dialog.ShowAsync() == ContentDialogResult.Primary && IsLoaded)
                    navigationView.SelectedItem = navigationView.SettingsItem;
            }
            catch (Exception ex)
            {
                LogHelper.Log("启动更新提示未显示", LogType.ERROR, ex);
            }
        }
        private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.GamepadMenu)
            {
                e.Handled = true;
                // 切换设置

                navigationView.SelectedItem = navigationView.SettingsItem;
            }
            else if (e.Key == Windows.System.VirtualKey.GamepadY)
            {
                e.Handled = true;
                (headerSearch ?? searchBox).Focus(FocusState.Programmatic);
            }
        }

        private void MessageCenter_UpdatePanelDisplayModeEvent(object sender, EventArgs e)
        {
            SetPaneMode();
        }

        private void SetPaneMode()
        {

            if (SettingHelper.GetValue<int>(SettingHelper.PANE_DISPLAY_MODE, 0) == 0)
            {
                navigationView.PaneDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Left;
            }
            else
            {
                navigationView.PaneDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Top;
            }
            RefreshWindowChrome();
        }

        private void NavigationView_PaneStateChanged(NavigationView sender, object args) => RefreshWindowChrome();

        private void RefreshWindowChrome()
        {
            for (DependencyObject current = this; current != null; current = VisualTreeHelper.GetParent(current))
                if (current is BaseFramePage chrome) { chrome.RefreshChrome(); break; }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = BiliAccount.Instance.InitLoginInfo();

        }

        private void NavigationView_SelectionChanged(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            var item = args.SelectedItem as Microsoft.UI.Xaml.Controls.NavigationViewItem;
            if (item == null || item == RandomLiveItem) return;
            if (item.Tag.ToString() == "设置" || item.Tag.ToString() == "Settings")
            {
                item.Tag = "SettingsPage";
            }
            SetHeaderSearch(null);
            frame.Navigate(Type.GetType("YolanLive.WinUI.Views." + item.Tag));
            RefreshWindowChrome();

        }

        private async void NavigationView_ItemInvoked(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer != RandomLiveItem || randomLiveOperation != null) return;
            var operation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
            randomLiveOperation = operation;
            RandomLiveItem.IsEnabled = false;
            RandomLiveItem.Content = "正在挑选…";
            try
            {
                var selected = await RandomLive.PickPreparedAsync(operation.Token);
                var room = selected?.Room;
                if (!IsLoaded || operation.IsCancellationRequested) return;
                if (room == null) Utils.ShowMessageToast("暂未找到已开播的游戏直播，请稍后再试");
                else MessageCenter.OpenLiveRoom(room.Source, room, new RandomBrowseSession(room, selected));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsLoaded) Utils.ShowMessageToast(ex.Message);
            }
            finally
            {
                randomLiveOperation = null;
                RandomLiveItem.Content = "随便逛逛";
                RandomLiveItem.IsEnabled = true;
                operation.Dispose();
            }
        }

        private async void searchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            await SubmitSearchAsync(args.QueryText);
        }

        internal async Task SubmitSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                Helper.Utils.ShowMessageToast("关键字不能为空");
                return;
            }
            if (!await ParseUrl(query))
            {
                this.Frame.Navigate(typeof(SearchPage), query);
            }
        }

        private async Task<bool> ParseUrl(string url)
        {
            var parseResult = await SiteParser.ParseUrl(url);
            if (parseResult.Item1 != LiveSite.Unknown && !string.IsNullOrEmpty(parseResult.Item2))
            {
                this.Frame.Navigate(typeof(LiveRoomPage), new PageArgs()
                {
                    Site = MainVM.Sites.First(x => x.SiteType == parseResult.Item1).LiveSite,
                    Data = new LiveRoomItem()
                    {
                        RoomID = parseResult.Item2,
                    }
                });
                return true;
            }
            else
            {
                return false;
            }

        }

        private void navigationView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!paneInitialized)
            {
                navigationView.IsPaneOpen = navigationView.DisplayMode == NavigationViewDisplayMode.Expanded;
                paneInitialized = true;
            }
            if (navigationView.SettingsItem is NavigationViewItem settings)
            {
                settings.MinHeight = 44; settings.Margin = new Thickness(0, 2, 0, 2);
                settings.Icon = new FontIcon { FontFamily = (FontFamily)App.Current.Resources["SegoeFluent"], FontSize = 20, Glyph = "\uE713" };
            }
        }

    }
}
