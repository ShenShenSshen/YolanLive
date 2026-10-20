using YolanLive.Core;
using YolanLive.Core.Helper;
using YolanLive.WinUI.ViewModels;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper
{
    public class BiliAccount
    {
        public event EventHandler OnAccountChanged;

        private static BiliAccount instance;
        public static BiliAccount Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new BiliAccount();
                }
                return instance;
            }
        }
        public bool Logined { get; set; } = false;
        public string UserName { get; set; } = "";
        public string StatusMessage { get; private set; } = "未登录，登录可享受高清直播";
        public long UserId {
            get
            {
                return SettingHelper.GetValue<long>(SettingHelper.BILI_USER_ID, 0L);
            }
        } 
        public string Cookie
        {
            get
            {
                return SettingHelper.GetValue<string>(SettingHelper.BILI_COOKIE, "");
            }
        }

        public async Task InitLoginInfo()
        {

            Logined = !string.IsNullOrEmpty(Cookie);
            StatusMessage = Logined ? "登录信息已保存，正在验证" : SettingHelper.GetValue(SettingHelper.BILI_LOGIN_EXPIRED, false) ? "登录已失效，请重新登录" : "未登录，登录可享受高清直播";
            if (Logined)
            {
                SetBiliSiteCookie();
                await LoadUserInfo();
            }
           

        }

        public async Task LoadUserInfo()
        {
            try
            {
                if (string.IsNullOrEmpty(Cookie))
                {
                    return;
                }
                var resp = await HttpUtil.GetString("https://api.bilibili.com/x/member/web/account", headers: new Dictionary<string, string>
                {
                    { "cookie", Cookie }
                });
                var json = JObject.Parse(resp);
                ApplyUserInfo(json);
            }
            catch (Exception ex)
            {
                LogHelper.Log("获取哔哩哔哩用户信息失败", LogType.ERROR, ex);
                StatusMessage = RequestErrorMessage.Report(ex, "哔哩哔哩直播");
                if (RequestErrorMessage.Kind(ex) != YolanLive.Core.Helper.LiveFailureKind.Authentication) StatusMessage += "；登录信息已保留";
                OnAccountChanged?.Invoke(this, EventArgs.Empty);
                Utils.ShowMessageToast(StatusMessage);
            }
        }
        internal void ApplyUserInfo(JObject json)
        {
                var code = json["code"]?.ToObject<int>() ?? int.MinValue;
                if (code == 0)
                {
                    UserName = json["data"]["uname"].ToString();

                    SettingHelper.SetValue(SettingHelper.BILI_USER_ID, json["data"]["mid"].ToObject<long>());
                   
                    Logined = true;
                    StatusMessage = "已登录：" + UserName;
                    SettingHelper.SetValue(SettingHelper.BILI_LOGIN_EXPIRED, false);
                    SetBiliSiteCookie();
                    OnAccountChanged?.Invoke(this, null);
                }
                else if (code == -101)
                {
                    MarkExpired();
                    Utils.ShowMessageToast(StatusMessage);
                }
                else
                {
                    Logined = !string.IsNullOrEmpty(Cookie);
                    StatusMessage = "平台验证暂时失败，登录信息已保留，请稍后重试";
                    OnAccountChanged?.Invoke(this, EventArgs.Empty);
                }
        }
        internal void MarkExpired()
        {
            Logout();
            SettingHelper.SetValue(SettingHelper.BILI_LOGIN_EXPIRED, true);
            StatusMessage = "登录已失效，请重新登录";
            OnAccountChanged?.Invoke(this, EventArgs.Empty);
        }


        public void SetBiliSiteCookie()
        {
            var site = MainVM.Sites.FirstOrDefault(x => x.SiteType == LiveSite.Bilibili);
            (site.LiveSite as BiliBili).Cookie = Cookie;
        }

        public void Logout()
        {
            StatusMessage = "未登录，登录可享受高清直播";
            SettingHelper.SetValue(SettingHelper.BILI_LOGIN_EXPIRED, false);
            Logined = false;
            SettingHelper.SetValue(SettingHelper.BILI_COOKIE, "");
            SettingHelper.SetValue(SettingHelper.BILI_USER_ID, 0L);
            UserName = "";
           
            SetBiliSiteCookie();
            OnAccountChanged?.Invoke(this, null);
        }

    }
}
