using YolanLive.Core.Helper;
using YolanLive.WinUI.Helper;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace YolanLive.WinUI.Controls;
public partial class WebLoginDialog : ContentDialog
{
    private readonly LiveSite platform;
    private readonly WebView2 browser = new();
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private bool closed;
    internal WebView2 Browser => browser;
    internal static string Home(LiveSite site) => site == LiveSite.Douyin ? "https://live.douyin.com/" : site == LiveSite.Huya ? "https://www.huya.com/" : site == LiveSite.Douyu ? "https://www.douyu.com/" : throw new ArgumentException("Invalid login platform.");
    internal static string Profile(LiveSite site) => Path.Combine(AppPaths.EnsureDirectory(), site + "WebView");
    internal static bool IsTrusted(LiveSite site, string url)
    {
        var domain = site == LiveSite.Douyin ? "douyin.com" : site == LiveSite.Huya ? "huya.com" : site == LiveSite.Douyu ? "douyu.com" : "";
        return domain.Length > 0 && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }
    public WebLoginDialog(LiveSite site)
    {
        platform = site;
        var name = site == LiveSite.Douyin ? "抖音" : site == LiveSite.Huya ? "虎牙" : "斗鱼";
        Title = "登录" + name; PrimaryButtonText = "保存登录"; CloseButtonText = "取消"; IsPrimaryButtonEnabled = false;
        message.Text = "在" + name + "官方网页完成登录，然后点击保存登录。";
        Resources["ContentDialogMaxWidth"] = Math.Max(280, Math.Min(980, (App.ActiveWindow?.Root.XamlRoot?.Size.Width ?? 800) - 32));
        var panel = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        panel.Children.Add(message); Grid.SetRow(browser, 1); panel.Children.Add(browser); Content = panel;
        Opened += async (_, _) => await InitializeAsync();
        Closed += (_, _) => { closed = true; browser.Close(); };
        PrimaryButtonClick += async (_, e) => { var deferral = e.GetDeferral(); try { await SaveSessionAsync(); } catch (Exception ex) { e.Cancel = true; message.Text = ex is InvalidOperationException ? ex.Message : "登录保存失败，请重试"; } finally { deferral.Complete(); } };
    }
    private async Task InitializeAsync()
    {
        try
        {
            browser.Width = Math.Max(240, Math.Min(900, XamlRoot.Size.Width - 80)); browser.Height = Math.Max(80, Math.Min(520, XamlRoot.Size.Height - 200));
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Profile(platform), null);
            await browser.EnsureCoreWebView2Async(environment);
            if (closed) return;
            var core = browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            await BrowserMediaBlock.ApplyAsync(core, environment);
            core.NavigationStarting += (_, args) => { if (!IsTrusted(platform, args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => { args.Handled = true; if (IsTrusted(platform, args.Uri)) core.Navigate(args.Uri); };
            core.NavigationCompleted += (_, args) => { if (!args.IsSuccess) message.Text = "网页加载失败，请关闭后重试"; };
            core.Navigate(Home(platform)); IsPrimaryButtonEnabled = true;
        }
        catch { if (!closed) message.Text = "无法打开登录，请检查网络及 WebView2 Runtime 是否可用"; }
    }
    internal async Task SaveSessionAsync()
    {
        var cookies = await browser.CoreWebView2.CookieManager.GetCookiesAsync(Home(platform));
        var cookie = string.Join("; ", cookies.GroupBy(c => c.Name).Select(c => c.First()).Select(c => c.Name + "=" + c.Value));
        if (platform == LiveSite.Douyin) await DouyinAccount.Instance.SaveAsync(cookie, browser.CoreWebView2.Settings.UserAgent);
        else await WebAccount.For(platform).SaveAsync(cookie, browser.CoreWebView2.Settings.UserAgent);
    }
    internal static async Task ClearCookiesAsync(LiveSite platform)
    {
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, Profile(platform), null);
        var controller = await env.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)App.ActiveWindow.Handle));
        try { controller.IsVisible = false; controller.CoreWebView2.IsMuted = true; controller.CoreWebView2.CookieManager.DeleteAllCookies(); }
        finally { controller.Close(); }
    }
}
