using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;

using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace YolanLive.WinUI.Views
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        readonly UpdateVM updateVM;
        public SettingsPage() : this(UpdateVM.Instance) { }
        internal SettingsPage(UpdateVM updateVM)
        {
            this.updateVM = updateVM;
            this.InitializeComponent();
            ConfigureResponsiveCards();

            Loaded += (_, _) =>
            {
                BiliAccount.Instance.OnAccountChanged += BiliAccount_OnAccountChanged;
                DouyinAccount.Instance.OnAccountChanged += DouyinAccount_OnAccountChanged;
                WebAccount.For(LiveSite.Huya).Changed += WebAccounts_Changed;
                WebAccount.For(LiveSite.Douyu).Changed += WebAccounts_Changed;
                WebAccounts_Changed(null, EventArgs.Empty);
                DouyinAccount_OnAccountChanged(null, EventArgs.Empty);
                BiliAccount_OnAccountChanged(null, EventArgs.Empty);
            };
            Unloaded += (_, _) =>
            {
                BiliAccount.Instance.OnAccountChanged -= BiliAccount_OnAccountChanged;
                DouyinAccount.Instance.OnAccountChanged -= DouyinAccount_OnAccountChanged;
                WebAccount.For(LiveSite.Huya).Changed -= WebAccounts_Changed;
                WebAccount.For(LiveSite.Douyu).Changed -= WebAccounts_Changed;
            }; 
            LoadUI();

        }

        internal IEnumerable<Grid> SettingRows => SettingsCards.Children.OfType<Border>()
            .SelectMany(group => ((StackPanel)group.Child).Children.OfType<Grid>());

        private void ConfigureResponsiveCards()
        {
            foreach (var group in SettingsCards.Children.OfType<Border>().Select(panel => (StackPanel)panel.Child))
            foreach (var card in group.Children.OfType<Grid>())
            {
                card.BorderThickness = new Thickness(0, group.Children.IndexOf(card) == 0 ? 0 : 1, 0, 0);
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                card.SizeChanged += (_, args) =>
                {
                    var narrow = args.NewSize.Width < 640;
                    Grid.SetColumnSpan((FrameworkElement)card.Children[0], narrow ? 2 : 1);
                    for (var i = 1; i < card.Children.Count; i++)
                    {
                        var action = (FrameworkElement)card.Children[i];
                        Grid.SetRow(action, narrow ? 1 : 0);
                        Grid.SetColumn(action, narrow ? 0 : 1);
                        Grid.SetColumnSpan(action, narrow ? 2 : 1);
                        action.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                        action.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(12, 0, 0, 0);
                    }
                };
            }
        }
        private void BiliAccount_OnAccountChanged(object sender, EventArgs e)
        {
            if (BiliAccount.Instance.Logined)
            {
                txtBili.Text = BiliAccount.Instance.StatusMessage;
                BtnLoginBili.Visibility = Visibility.Collapsed;
                BtnLogoutBili.Visibility = Visibility.Visible;
            }
            else
            {
                txtBili.Text = BiliAccount.Instance.StatusMessage;
                BtnLoginBili.Visibility = Visibility.Visible;
                BtnLogoutBili.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadUI()
        {
            cbWindowSize.ItemsSource = MainWindow.WindowSizeOptions;
            cbWindowSize.SelectedItem = MainWindow.GetSavedWindowSize();
            cbWindowSize.SelectionChanged += (_, _) =>
            {
                if (cbWindowSize.SelectedItem is string size) App.SetWindowSize(size);
            };
            //主题
            cbTheme.SelectedIndex = SettingHelper.GetValue<int>(SettingHelper.THEME, 0);
            cbTheme.Loaded += new RoutedEventHandler((sender, e) =>
            {
                cbTheme.SelectionChanged += new SelectionChangedEventHandler((obj, args) =>
                {
                    SettingHelper.SetValue(SettingHelper.THEME, cbTheme.SelectedIndex);
                    Frame rootFrame = App.ActiveWindow.Root;
                    switch (cbTheme.SelectedIndex)
                    {
                        case 1:
                            rootFrame.RequestedTheme = ElementTheme.Light;
                            break;
                        case 2:
                            rootFrame.RequestedTheme = ElementTheme.Dark;
                            break;
                        default:
                            rootFrame.RequestedTheme = ElementTheme.Default;
                            break;
                    }
                    App.SetTitleBar();
                });
            });

            //导航栏显示模式
            cbPaneDisplayMode.SelectedIndex = SettingHelper.GetValue<int>(SettingHelper.PANE_DISPLAY_MODE, 0);
            cbPaneDisplayMode.Loaded += new RoutedEventHandler((sender, e) =>
            {
                cbPaneDisplayMode.SelectionChanged += new SelectionChangedEventHandler((obj, args) =>
                {
                    SettingHelper.SetValue(SettingHelper.PANE_DISPLAY_MODE, cbPaneDisplayMode.SelectedIndex);
                    MessageCenter.UpdatePanelDisplayMode();
                });
            });

            //鼠标侧键返回
            swMouseClosePage.IsOn = SettingHelper.GetValue<bool>(SettingHelper.MOUSE_BACK, true);
            swMouseClosePage.Loaded += new RoutedEventHandler((sender, e) =>
            {
                swMouseClosePage.Toggled += new RoutedEventHandler((obj, args) =>
                {
                    SettingHelper.SetValue(SettingHelper.MOUSE_BACK, swMouseClosePage.IsOn);
                });
            });
            //视频解码
            cbDecoder.SelectedIndex = SettingHelper.GetValue<int>(SettingHelper.VIDEO_DECODER, 0);
            cbDecoder.Loaded += new RoutedEventHandler((sender, e) =>
            {
                cbDecoder.SelectionChanged += new SelectionChangedEventHandler((obj, args) =>
                {
                    SettingHelper.SetValue(SettingHelper.VIDEO_DECODER, cbDecoder.SelectedIndex);
                });
            });

            //新窗口打开
            swNewWindow.IsOn = SettingHelper.GetValue<bool>(SettingHelper.NEW_WINDOW_LIVEROOM, false);
            swNewWindow.Loaded += new RoutedEventHandler((sender, e) =>
            {
                swNewWindow.Toggled += new RoutedEventHandler((obj, args) =>
                {
                    SettingHelper.SetValue(SettingHelper.NEW_WINDOW_LIVEROOM, swNewWindow.IsOn);
                });
            });
            if(BiliAccount.Instance.Logined)
            {
                txtBili.Text = BiliAccount.Instance.StatusMessage;
                BtnLoginBili.Visibility = Visibility.Collapsed;
                BtnLogoutBili.Visibility = Visibility.Visible;
            }
           
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            version.Text = typeof(App).Assembly.GetName().Version.ToString(3);
        }

        private async void BtnGithub_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri(UpdateClient.Repository));
        }
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e) => await updateVM.CheckAsync();
        private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e) => await updateVM.DownloadAsync();
        private void BtnCancelUpdate_Click(object sender, RoutedEventArgs e) => updateVM.Cancel();
        private async void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (await updateVM.StartInstallerAsync()) foreach (var window in App.Windows.ToArray()) window.Close();
        }

        private async void BtnLog_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(AppPaths.EnsureDirectory(), "log")).FullName;
            var logFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(folderPath);
            await Launcher.LaunchFolderAsync(logFolder);
        }

        private async void BtnLoginBili_Click(object sender, RoutedEventArgs e)
        {
            if (BiliAccount.Instance.Logined)
            {
                Utils.ShowMessageToast("已登录");
                return;
            }
            var result= await MessageCenter.BiliBiliLogin();
            if (result)
            {
                txtBili.Text = BiliAccount.Instance.StatusMessage;
                BtnLoginBili.Visibility = Visibility.Collapsed;
                BtnLogoutBili.Visibility = Visibility.Visible;
            }
        }

        private void DouyinAccount_OnAccountChanged(object sender, EventArgs e)
        {
            var saved = DouyinAccount.Instance.Logined;
            txtDouyin.Text = DouyinAccount.Instance.StatusMessage;
            BtnLoginDouyin.Visibility = saved ? Visibility.Collapsed : Visibility.Visible;
            BtnLogoutDouyin.Visibility = !string.IsNullOrEmpty(SettingHelper.GetValue(SettingHelper.DOUYIN_COOKIE, "")) ? Visibility.Visible : Visibility.Collapsed;
        }
        private async void BtnLoginDouyin_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new YolanLive.WinUI.Controls.DouyinLoginDialog { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
        }
        private async void BtnLogoutDouyin_Click(object sender, RoutedEventArgs e)
        {
            DouyinAccount.Instance.Logout();
            try { await YolanLive.WinUI.Controls.DouyinLoginDialog.ClearCookiesAsync(); }
            catch { Utils.ShowMessageToast("本地登录已清除；网页登录状态未清理，可在登录网页中注销"); }
        }
        private void WebAccounts_Changed(object sender, EventArgs e)
        {
            foreach (var site in new[] { LiveSite.Huya, LiveSite.Douyu })
            {
                var account = WebAccount.For(site);
                ((TextBlock)FindName("txt" + site)).Text = account.StatusMessage;
                ((Button)FindName("BtnLogin" + site)).Visibility = account.Logined ? Visibility.Collapsed : Visibility.Visible;
                ((Button)FindName("BtnLogout" + site)).Visibility = account.Saved ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private async void BtnLoginWeb_Click(object sender, RoutedEventArgs e)
        {
            var site = Enum.Parse<LiveSite>((string)((Button)sender).Tag);
            await new Controls.WebLoginDialog(site) { XamlRoot = XamlRoot }.ShowAsync();
        }
        private async void BtnLogoutWeb_Click(object sender, RoutedEventArgs e)
        {
            var site = Enum.Parse<LiveSite>((string)((Button)sender).Tag);
            WebAccount.For(site).Logout();
            try { await Controls.WebLoginDialog.ClearCookiesAsync(site); }
            catch { Utils.ShowMessageToast("本地登录已清除；网页登录状态未清理，可在登录网页中注销"); }
        }
        private void BtnLogoutBili_Click(object sender, RoutedEventArgs e)
        {
            BiliAccount.Instance.Logout();
            txtBili.Text = BiliAccount.Instance.StatusMessage;
            BtnLoginBili.Visibility = Visibility.Visible;
            BtnLogoutBili.Visibility = Visibility.Collapsed;

        }
    }
}
