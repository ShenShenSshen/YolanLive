using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Controls;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using YolanLive.WinUI.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YolanLive.WinUI.Helper
{
    public static class MessageCenter
    {
        public delegate void NavigatePageHandler(Type page, object data);
        public static event NavigatePageHandler NavigatePageEvent;
        public delegate void ChangeTitleHandler(string title, string logo);
        public static event ChangeTitleHandler ChangeTitleEvent;
        public static event EventHandler<bool> HideTitlebarEvent;
        public static event EventHandler UpdateFavoriteEvent;
        public static event EventHandler UpdatePanelDisplayModeEvent;
        public static void OpenLiveRoom(ILiveSite liveSite, LiveRoomItem item) => OpenLiveRoom(liveSite, item, null);
        internal async static void OpenLiveRoom(ILiveSite liveSite, LiveRoomItem item, RandomBrowseSession randomSession)
        {
            var arg = new PageArgs()
            {
                Site = liveSite,
                Data = item,
                RandomSession = randomSession
            };

            // 如果是哔哩哔哩
            if (liveSite.Name == "哔哩哔哩直播" && !BiliAccount.Instance.Logined&&!SettingHelper.GetValue(SettingHelper.IGNORE_BILI_LOGIN_TIP,false))
            {
                // 弹窗询问是否登录
                MessageDialog dialog = new MessageDialog("您尚未登录哔哩哔哩账号，部分直播可能无法观看，是否前往登录账号？", "未登录");
                dialog.Commands.Add(new UICommand("登录", async (cmd) =>
                {
                    // 调用登录方法
                    var login = await BiliBiliLogin();
                    if (login)
                    {
                        // 登录成功后打开直播间
                        NavigatePage(typeof(LiveRoomPage), arg);
                    }
                    else
                    {
                        Utils.ShowMessageToast("未登录成功");
                        NavigatePage(typeof(LiveRoomPage), arg);
                    }
                }));
                dialog.Commands.Add(new UICommand("取消", (cmd) =>
                 {
                   NavigatePage(typeof(LiveRoomPage), arg);
                }));
                dialog.Commands.Add(new UICommand("不再提示", (cmd) =>
                {
                    SettingHelper.SetValue(SettingHelper.IGNORE_BILI_LOGIN_TIP, true);
                    NavigatePage(typeof(LiveRoomPage), arg);
                }));
                Utils.InitializeWithWindow(dialog);
                await dialog.ShowAsync();
                return;
            }

            NavigatePage(typeof(LiveRoomPage), arg);
        }


        public static void NavigatePage(Type page, object data)
        {
            if (SettingHelper.GetValue(SettingHelper.NEW_WINDOW_LIVEROOM, false) && page == typeof(LiveRoomPage))
                App.OpenWindow(page, data);
            else NavigatePageEvent?.Invoke(page, data);
        }

        public static void ChangeTitle(string title, ILiveSite site = null)
        {
            var logo = "ms-appx:///Assets/YolanLive.png";
            if (site != null)
            {
                var siteInfo = MainVM.Sites.FirstOrDefault(x => x.LiveSite.Equals(site));
                if (siteInfo != null)
                {
                    logo = siteInfo.Logo;
                }
            }

            ChangeTitleEvent?.Invoke(title, logo);
        }
        public static void HideTitlebar(bool show)
        {
            HideTitlebarEvent?.Invoke(null, show);
        }

        public static void UpdateFavorite()
        {
            UpdateFavoriteEvent?.Invoke(null, new EventArgs());
        }
        public static void UpdatePanelDisplayMode()
        {
            UpdatePanelDisplayModeEvent?.Invoke(null, new EventArgs());
        }
        public static async Task<bool> BiliBiliLogin()
        {
            BiliLoginDialog biliLoginDialog = new BiliLoginDialog { XamlRoot = App.ActiveWindow.Root.XamlRoot };
            await biliLoginDialog.ShowAsync();
            return BiliAccount.Instance.Logined;

        }
    }

}
