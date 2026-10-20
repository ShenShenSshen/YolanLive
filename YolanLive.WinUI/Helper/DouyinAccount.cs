using YolanLive.Core;
using YolanLive.WinUI.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace YolanLive.WinUI.Helper;
public sealed class DouyinAccount
{
    public static DouyinAccount Instance { get; } = new();
    public event EventHandler OnAccountChanged;
    public bool Logined { get; private set; }
    public string StatusMessage { get; private set; } = "未登录，可浏览游戏直播";
    internal static bool HasSession(string cookie) => cookie.Split(';').Any(part =>
    {
        var pair = part.Trim().Split('=', 2);
        return pair.Length == 2 && (pair[0] == "sessionid" || pair[0] == "sessionid_ss") && !string.IsNullOrWhiteSpace(pair[1]);
    });
    public async Task InitLoginInfo()
    {
        var protectedCookie = SettingHelper.GetValue(SettingHelper.DOUYIN_COOKIE, "");
        if (string.IsNullOrEmpty(protectedCookie)) return;
        try
        {
            var bytes = await new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.DecodeFromBase64String(protectedCookie));
            Apply(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, bytes));
        }
        catch { Logout(); StatusMessage = "保存的登录无法读取，请重新登录"; OnAccountChanged?.Invoke(this, EventArgs.Empty); }
    }
    public async Task SaveAsync(string cookie, string userAgent)
    {
        if (!HasSession(cookie)) throw new InvalidOperationException("未检测到抖音登录，请在官方网页完成登录后再保存");
        var protectedCookie = await new DataProtectionProvider("LOCAL=user").ProtectAsync(CryptographicBuffer.ConvertStringToBinary(cookie, BinaryStringEncoding.Utf8));
        SettingHelper.SetValue(SettingHelper.DOUYIN_USER_AGENT, userAgent);
        SettingHelper.SetValue(SettingHelper.DOUYIN_COOKIE, CryptographicBuffer.EncodeToBase64String(protectedCookie));
        SettingHelper.SetValue(SettingHelper.DOUYIN_LOGIN_EXPIRED, false);
        Apply(cookie);
    }
    private void Apply(string cookie)
    {
        var expired = SettingHelper.GetValue(SettingHelper.DOUYIN_LOGIN_EXPIRED, false);
        Logined = HasSession(cookie) && !expired;
        StatusMessage = expired ? "登录已失效，请重新登录" : Logined ? "登录已保存，尚未进行在线验证" : "未登录，可浏览游戏直播";
        var site = (Douyin)MainVM.Sites.First(s => s.LiveSite is Douyin).LiveSite;
        site.Cookie = cookie;
        var agent = SettingHelper.GetValue(SettingHelper.DOUYIN_USER_AGENT, "");
        if (!string.IsNullOrEmpty(agent)) site.UserAgent = agent;
        OnAccountChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Logout()
    {
        SettingHelper.SetValue(SettingHelper.DOUYIN_LOGIN_EXPIRED, false);
        SettingHelper.SetValue(SettingHelper.DOUYIN_COOKIE, "");
        Apply("");
    }
    internal void MarkExpired()
    {
        Logined = false;
        SettingHelper.SetValue(SettingHelper.DOUYIN_LOGIN_EXPIRED, true);
        StatusMessage = "登录已失效，请重新登录";
        OnAccountChanged?.Invoke(this, EventArgs.Empty);
    }
}
