using YolanLive.Core;
using YolanLive.Core.Helper;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
namespace YolanLive.WinUI.Helper;
internal sealed class WebAccount
{
    private static readonly Dictionary<LiveSite, WebAccount> Accounts = new();
    internal static WebAccount For(LiveSite platform)
    {
        if (platform != LiveSite.Huya && platform != LiveSite.Douyu) throw new ArgumentException("Invalid web account.");
        if (!Accounts.TryGetValue(platform, out var account)) Accounts[platform] = account = new WebAccount(platform);
        return account;
    }
    private WebAccount(LiveSite platform) { Platform = platform; }
    internal LiveSite Platform { get; }
    internal string Key => "WebLogin:" + Platform + ":";
    internal bool Saved => !string.IsNullOrEmpty(SettingHelper.GetValue(Key + "Cookie", ""));
    internal bool Logined { get; private set; }
    internal string StatusMessage { get; private set; } = "未登录，可浏览游戏直播";
    internal event EventHandler Changed;
    internal static bool HasSession(LiveSite platform, string cookie)
    {
        var parts = cookie.Split(';').Select(s => s.Trim().Split('=', 2)).Where(p => p.Length == 2 && !string.IsNullOrWhiteSpace(p[1])).GroupBy(p => p[0], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First()[1], StringComparer.Ordinal);
        return platform == LiveSite.Douyu ? parts.ContainsKey("acf_uid") && parts.ContainsKey("acf_auth") : parts.ContainsKey("udb_biztoken") && (parts.ContainsKey("udb_uid") || parts.ContainsKey("udbuid") || parts.ContainsKey("yyuid"));
    }
    internal async Task InitAsync()
    {
        if (!Saved) return;
        try
        {
            var data = await new DataProtectionProvider().UnprotectAsync(CryptographicBuffer.DecodeFromBase64String(SettingHelper.GetValue(Key + "Cookie", "")));
            Apply(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, data));
        }
        catch { Logout(); StatusMessage = "保存的登录无法读取，请重新登录"; }
    }
    internal async Task SaveAsync(string cookie, string userAgent)
    {
        if (!HasSession(Platform, cookie)) throw new InvalidOperationException("未检测到登录，请在官方网页完成登录后再保存");
        var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(CryptographicBuffer.ConvertStringToBinary(cookie, BinaryStringEncoding.Utf8));
        SettingHelper.SetValue(Key + "Cookie", CryptographicBuffer.EncodeToBase64String(encrypted));
        SettingHelper.SetValue(Key + "UserAgent", userAgent);
        SettingHelper.SetValue(Key + "Expired", false);
        Apply(cookie);
    }
    private void Apply(string cookie)
    {
        var expired = SettingHelper.GetValue(Key + "Expired", false);
        Logined = HasSession(Platform, cookie) && !expired;
        StatusMessage = expired ? "登录已失效，请重新登录" : Logined ? "登录已保存，尚未进行在线验证" : "未登录，可浏览游戏直播";
        var site = MainVM.Sites.Single(s => s.SiteType == Platform).LiveSite;
        if (site is Huya huya) { huya.Cookie = cookie; if (!string.IsNullOrEmpty(SettingHelper.GetValue(Key + "UserAgent", ""))) huya.UserAgent = SettingHelper.GetValue(Key + "UserAgent", ""); }
        if (site is Douyu douyu) { douyu.Cookie = cookie; if (!string.IsNullOrEmpty(SettingHelper.GetValue(Key + "UserAgent", ""))) douyu.UserAgent = SettingHelper.GetValue(Key + "UserAgent", ""); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    internal void Logout() { SettingHelper.SetValue(Key + "Cookie", ""); SettingHelper.SetValue(Key + "UserAgent", ""); SettingHelper.SetValue(Key + "Expired", false); Apply(""); }
    internal void MarkExpired() { SettingHelper.SetValue(Key + "Expired", true); Apply(Platform == LiveSite.Huya ? ((Huya)MainVM.Sites.Single(s => s.SiteType == Platform).LiveSite).Cookie : ((Douyu)MainVM.Sites.Single(s => s.SiteType == Platform).LiveSite).Cookie); }
}
