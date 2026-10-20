using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using YolanLive.Core.Helper;
using YolanLive.WinUI.Controls;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace YolanLive.WinUI.Helper;
internal sealed class DouyuScriptBrowser
{
    private readonly SemaphoreSlim gate = new(1);
    private CoreWebView2Controller controller;
    private MainWindow owner;
    private CancellationTokenSource ownerLifetime = new();
    private BrowserIdlePolicy idle;
    internal bool HasBrowser => controller != null;
    internal bool IsSuspended => controller?.CoreWebView2.IsSuspended ?? false;
    internal Task CheckIdleAsync(double seconds) => idle?.CheckAsync(seconds) ?? Task.CompletedTask;
    internal Task<string> SignAsync(string room, string script) => SignAsync(room, script, CancellationToken.None);
    internal async Task<string> SignAsync(string room, string script, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(room, "^[0-9]+$") || script.Length > 1000000) throw new InvalidOperationException("斗鱼签名参数无效");
        idle ??= new BrowserIdlePolicy(gate, () => controller, Close);
        using var activity = idle.Begin();
        await gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureAsync(cancellationToken);
            using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ownerLifetime.Token);
            requestLifetime.CancelAfter(TimeSpan.FromSeconds(20));
            var requestToken = requestLifetime.Token;
            using var measured = RequestMetrics.Begin();
            controller.CoreWebView2.Resume();
            var unpacker = Regex.Replace(script, @"eval.*?;}", "strc;}", RegexOptions.Singleline);
            var unpacked = await controller.CoreWebView2.ExecuteScriptAsync("(()=>{" + unpacker + ";return ub98484234();})()").AsTask().WaitAsync(TimeSpan.FromSeconds(5), requestToken);
            var code = JsonConvert.DeserializeObject<string>(unpacked) ?? throw new LiveRequestException(LiveFailureKind.Platform, "斗鱼签名脚本未返回结果");
            var version = Regex.Match(code, @"v=(\d+)").Groups[1].Value;
            if (version.Length == 0) throw new LiveRequestException(LiveFailureKind.Platform, "斗鱼签名格式已变化");
            var did = Guid.NewGuid().ToString("N"); var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var hash = YolanLive.Core.Helper.Utils.ToMD5(room + did + time + version);
            code = Regex.Replace(code, @"return rt;}\);?", "return rt;}");
            code = Regex.Replace(code, @"\(function\s*\(", "function sign(");
            code = Regex.Replace(code, @"CryptoJS\.MD5\(cb\)\.toString\(\)", JsonConvert.SerializeObject(hash));
            var signed = await controller.CoreWebView2.ExecuteScriptAsync("(()=>{" + code + ";return sign(" + JsonConvert.SerializeObject(room) + "," + JsonConvert.SerializeObject(did) + "," + JsonConvert.SerializeObject(time) + ");})()").AsTask().WaitAsync(TimeSpan.FromSeconds(5), requestToken);
            var args = JsonConvert.DeserializeObject<string>(signed);
            if (string.IsNullOrWhiteSpace(args) || args.Length > 20000 || !args.Contains("sign=")) throw new LiveRequestException(LiveFailureKind.Platform, "斗鱼播放签名失败");
            return args;
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException) { Close(); throw; }
        catch { if (controller == null) Close(); throw; }
        finally { gate.Release(); }
    }
    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (controller != null) return;
        ownerLifetime.Dispose(); ownerLifetime = new();
        owner = App.ActiveWindow ?? throw new InvalidOperationException("播放窗口尚未就绪");
        owner.Closed += OwnerClosed;
        using var initializing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ownerLifetime.Token);
        initializing.CancelAfter(TimeSpan.FromSeconds(20));
        var initToken = initializing.Token;
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, WebLoginDialog.Profile(LiveSite.Douyu), null).AsTask(initToken);
        controller = await env.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)owner.Handle)).AsTask(initToken);
        controller.IsVisible = false;
        var browser = controller.CoreWebView2; browser.Settings.AreDevToolsEnabled = false; browser.Settings.AreDefaultContextMenusEnabled = false;
        await BrowserMediaBlock.ApplyAsync(browser, env);
        browser.NavigationStarting += (_, e) => { if (!WebLoginDialog.IsTrusted(LiveSite.Douyu, e.Uri)) e.Cancel = true; };
        browser.NewWindowRequested += (_, e) => e.Handled = true;
        var loaded = new TaskCompletionSource<bool>();
        void Completed(object sender, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult(e.IsSuccess);
        browser.NavigationCompleted += Completed;
        try { browser.Navigate("https://www.douyu.com/"); if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15), initToken)) throw new LiveRequestException(LiveFailureKind.Network, "斗鱼官网加载失败"); }
        catch { Close(); throw; }
        finally { browser.NavigationCompleted -= Completed; }
    }
    internal bool IsMuted => controller?.CoreWebView2.IsMuted ?? true;
    private void OwnerClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs e) => Close();
    private void Close() { idle?.Stop(); ownerLifetime.Cancel(); if(owner != null) owner.Closed -= OwnerClosed; controller?.Close(); controller = null; owner = null; }
}
