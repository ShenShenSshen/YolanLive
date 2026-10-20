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
    public sealed partial class FavoritePage : Page
    {
        readonly FavoriteVM favoriteVM;
        public FavoritePage()
        {
            favoriteVM = new FavoriteVM();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;
            Loaded += (_, _) => { MessageCenter.UpdateFavoriteEvent -= MessageCenter_UpdateFavoriteEvent; MessageCenter.UpdateFavoriteEvent += MessageCenter_UpdateFavoriteEvent; };
            Unloaded += (_, _) => MessageCenter.UpdateFavoriteEvent -= MessageCenter_UpdateFavoriteEvent;
            this.InitializeComponent();
            Loaded += async (_, _) => { await favoriteVM.LoadAsync();  };
            Unloaded += (_, _) => {  favoriteVM.CancelPending(); };
            _ = new PageStatusRefresh(this, () => favoriteVM.LoadAsync(), favoriteVM.CancelPending);
        }

        private void MessageCenter_UpdateFavoriteEvent(object sender, EventArgs e)
        {
            if (IsLoaded) favoriteVM.Refresh();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);



        }

        private void ls_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as FavoriteItem;
            var site = MainVM.Sites.FirstOrDefault(x => x.Name == item.SiteName);
            MessageCenter.OpenLiveRoom(site.LiveSite, new Core.Models.LiveRoomItem()
            {
                RoomID = item.RoomID
            });
        }

        private void MenuFlyoutItem_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as MenuFlyoutItem).DataContext as FavoriteItem;
            favoriteVM.RemoveItem(item);
        }
        private async void ManageFavorites_Click(object sender, RoutedEventArgs e)
        {
            await new YolanLive.WinUI.Controls.FavoriteManagementDialog { XamlRoot = XamlRoot }.ShowAsync();
        }
    }
}
