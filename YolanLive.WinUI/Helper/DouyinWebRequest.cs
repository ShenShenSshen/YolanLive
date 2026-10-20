using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper;
internal sealed class DouyinWebRequest
{
    private readonly SemaphoreSlim gate = new(1);
    private CoreWebView2Controller controller;
    private MainWindow owner;
    private CancellationTokenSource ownerLifetime = new();
    private BrowserIdlePolicy idle;
    internal bool HasBrowser => controller != null;
    internal bool IsSuspended => controller?.CoreWebView2.IsSuspended ?? false;
    internal Task CheckIdleAsync(double seconds) => idle?.CheckAsync(seconds) ?? Task.CompletedTask;

    public Task<string> GetAsync(string url) => GetAsync(url, CancellationToken.None);
    public async Task<string> GetAsync(string url, CancellationToken cancellationToken)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || uri.Host != "live.douyin.com" || !uri.AbsolutePath.StartsWith("/webcast/", StringComparison.Ordinal))
            throw new InvalidOperationException("无效的抖音接口地址");
        idle ??= new BrowserIdlePolicy(gate, () => controller, Close);
        using var activity = idle.Begin();
        await gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureBrowserAsync(cancellationToken);
            using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ownerLifetime.Token);
            requestLifetime.CancelAfter(TimeSpan.FromSeconds(20));
            var requestToken = requestLifetime.Token;
            controller.CoreWebView2.Resume();
            var browser = controller.CoreWebView2;
            using var dataRequest = await YolanLive.Core.Helper.HttpTransport.EnterAsync(requestToken);
            var id = Guid.NewGuid().ToString("N");
            var result = new TaskCompletionSource<string>();
            void Received(object sender, CoreWebView2WebMessageReceivedEventArgs args)
            {
                if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var source) || source.Scheme != "https" || source.Host != "live.douyin.com") return;
                try
                {
                    var message = JObject.Parse(args.WebMessageAsJson);
                    if ((string)message["id"] != id) return;
                    if (message["error"] != null)
                    {
                        var status = (int?)message["status"] ?? 0;
                        var kind = status == 401 ? YolanLive.Core.Helper.LiveFailureKind.Authentication : status == 0 ? YolanLive.Core.Helper.LiveFailureKind.Network : YolanLive.Core.Helper.LiveFailureKind.Platform;
                        result.TrySetException(new YolanLive.Core.Helper.LiveRequestException(kind, "抖音官网请求失败"));
                    }
                    else result.TrySetResult((string)message["body"] ?? "");
                }
                catch (JsonException) { }
            }
            browser.WebMessageReceived += Received;
            try
            {
                // Use the official page's fetch hook so its SDK signs each request with the current browser session.
                var requestId = JsonConvert.SerializeObject(id);
                await browser.ExecuteScriptAsync("(()=>{window.__YolanLiveRequests ??= {}; const abort = new AbortController(); window.__YolanLiveRequests[" + requestId + "] = abort; fetch(" + JsonConvert.SerializeObject(uri.PathAndQuery) + ",{credentials:'include',signal:abort.signal}).then(r=>{if(!r.ok){chrome.webview.postMessage({id:" + requestId + ",error:true,status:r.status});return null;}return r.text()}).then(body=>{if(body!==null)chrome.webview.postMessage({id:" + requestId + ",body})}).catch(()=>chrome.webview.postMessage({id:" + requestId + ",error:true,status:0}));})();");
                return await result.Task.WaitAsync(TimeSpan.FromSeconds(15), requestToken);
            }
            finally
            {
                browser.WebMessageReceived -= Received;
                try { await browser.ExecuteScriptAsync("window.__YolanLiveRequests?.[" + JsonConvert.SerializeObject(id) + "]?.abort(); if(window.__YolanLiveRequests) delete window.__YolanLiveRequests[" + JsonConvert.SerializeObject(id) + "];").AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException or TimeoutException) { }
            }
        }
        catch { if (controller == null) Close(); throw; }
        finally { gate.Release(); }
    }

    private async Task EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (controller != null) return;
        ownerLifetime.Dispose(); ownerLifetime = new();
        owner = App.ActiveWindow ?? throw new InvalidOperationException("抖音浏览器尚未就绪");
        var dataDirectory = Environment.GetCommandLineArgs().Contains("--use-browser-login")
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayWin") : AppPaths.DataDirectory;
        var profile = Path.Combine(dataDirectory, "DouyinWebView");
        owner.Closed += OwnerClosed;
        using var initializing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ownerLifetime.Token);
        initializing.CancelAfter(TimeSpan.FromSeconds(20));
        var initToken = initializing.Token;
        var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null).AsTask(initToken);
        controller = await environment.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)owner.Handle)).AsTask(initToken);
        controller.IsVisible = false;
        var browser = controller.CoreWebView2;
        browser.IsMuted = true;
        browser.Settings.AreDevToolsEnabled = false;
        browser.Settings.AreDefaultContextMenusEnabled = false;
        await BrowserMediaBlock.ApplyAsync(browser, environment);
        browser.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var destination) || destination.Scheme != "https" || destination.Host != "live.douyin.com") args.Cancel = true;
        };
        var loaded = new TaskCompletionSource<bool>();
        void Completed(object sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
        browser.NavigationCompleted += Completed;
        try
        {
            browser.Navigate("https://live.douyin.com/");
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15), initToken)) throw new InvalidOperationException("抖音官网加载失败，请检查网络");
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (await browser.ExecuteScriptAsync("typeof window.byted_acrawler?.frontierSign === 'function'") == "true") return;
                await Task.Delay(100, initToken);
            }
            throw new InvalidOperationException("抖音官网签名组件尚未就绪，请刷新重试");
        }
        catch { Close(); throw; }
        finally { browser.NavigationCompleted -= Completed; }
    }
    private void OwnerClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args) => Close();
    private void Close()
    {
        idle?.Stop(); ownerLifetime.Cancel();
        if (owner != null) owner.Closed -= OwnerClosed;
        controller?.Close();
        controller = null;
        owner = null;
    }
}
