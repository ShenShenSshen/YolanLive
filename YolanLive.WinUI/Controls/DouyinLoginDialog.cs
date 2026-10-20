using YolanLive.Core.Helper;
using System.Threading.Tasks;
namespace YolanLive.WinUI.Controls;
public sealed partial class DouyinLoginDialog : WebLoginDialog
{
    public DouyinLoginDialog() : base(LiveSite.Douyin) { }
    internal static bool IsTrusted(string url) => WebLoginDialog.IsTrusted(LiveSite.Douyin, url);
    public static Task ClearCookiesAsync() => WebLoginDialog.ClearCookiesAsync(LiveSite.Douyin);
}
