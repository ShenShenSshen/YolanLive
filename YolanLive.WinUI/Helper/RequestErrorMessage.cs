using YolanLive.Core.Helper;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper;
internal static class RequestErrorMessage
{
    internal static LiveFailureKind Kind(Exception exception)
    {
        if (exception is LiveRequestException request) return request.Kind;
        if (exception is HttpRequestException http)
            return http.StatusCode == HttpStatusCode.Unauthorized ? LiveFailureKind.Authentication : http.StatusCode.HasValue ? LiveFailureKind.Platform : LiveFailureKind.Network;
        if (exception is TimeoutException || exception is TaskCanceledException || LogHelper.IsNetworkError(exception)) return LiveFailureKind.Network;
        return LiveFailureKind.Platform;
    }
    internal static string Describe(Exception exception, string platform, bool savedLogin = false)
    {
        return Kind(exception) switch
        {
            LiveFailureKind.Authentication => platform + (savedLogin ? "登录已失效，请重新登录" : "需要登录后重试"),
            LiveFailureKind.Network => "网络连接失败，请检查网络后重试",
            _ => platform + "查询失败，平台暂时不可用，请稍后重试"
        };
    }
    internal static string Report(Exception exception, string platform)
    {
        var saved = platform == "哔哩哔哩直播" ? !string.IsNullOrEmpty(BiliAccount.Instance.Cookie) : platform == "抖音直播" &&
            !string.IsNullOrEmpty(SettingHelper.GetValue(SettingHelper.DOUYIN_COOKIE, ""));
        if (platform == "虎牙直播") saved = WebAccount.For(LiveSite.Huya).Saved;
        if (platform == "斗鱼直播") saved = WebAccount.For(LiveSite.Douyu).Saved;
        if (Kind(exception) == LiveFailureKind.Authentication && saved)
        {
            if (platform == "哔哩哔哩直播") BiliAccount.Instance.MarkExpired();
            else if (platform == "抖音直播") DouyinAccount.Instance.MarkExpired();
            else if (platform == "虎牙直播") WebAccount.For(LiveSite.Huya).MarkExpired();
            else if (platform == "斗鱼直播") WebAccount.For(LiveSite.Douyu).MarkExpired();
        }
        return Describe(exception, platform, saved);
    }
}
