using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public sealed partial class HistoryPage : Page
    {
        readonly HistoryVM historyVM;
        public HistoryPage()
        {
            historyVM = new HistoryVM();
            this.InitializeComponent();
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            historyVM.LoadData();
        }

        private void ls_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as HistoryItem;
            var site = MainVM.Sites.FirstOrDefault(x => x.Name == item.SiteName);
            MessageCenter.OpenLiveRoom(site.LiveSite, new Core.Models.LiveRoomItem()
            {
                RoomID = item.RoomID
            });
        }

        private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as MenuFlyoutItem).DataContext as HistoryItem;
            historyVM.RemoveItem(item);
        }
    }
}
