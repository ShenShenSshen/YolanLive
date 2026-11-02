using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI;

internal static class UpdateChecks
{
    private static readonly byte[] Package = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static JObject Metadata(string tag = "v1.0.4", string prefix = "YolanLive-") => new()
    {
        ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = false,
        ["assets"] = new JArray(new JObject {
            ["name"] = prefix + tag[1..] + "-x64-Setup.exe",
            ["browser_download_url"] = UpdateClient.Repository + "/releases/download/" + tag + "/" + prefix + tag[1..] + "-x64-Setup.exe",
            ["size"] = Package.Length, ["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(Package)).ToLowerInvariant() })
    };
    private static string TestDirectory() => Path.Combine(AppPaths.EnsureDirectory(), "Update-check-" + Guid.NewGuid().ToString("N"));
    private static HttpResponseMessage JsonResponse(JObject data) => new(HttpStatusCode.OK) { Content = new StringContent(data.ToString()) };
    internal static void Releases()
    {
        Check(UpdateClient.ParseVersion("v1.0.10") > UpdateClient.ParseVersion("1.0.9") && UpdateClient.ParseVersion("v1.0.3") == UpdateClient.ParseVersion("1.0.3.0"), "Versions were compared as text or unequal revision defaults.");
        var release = UpdateClient.ParseRelease(Metadata().ToString());
        Check(release.Version > UpdateClient.ParseVersion("1.0.3") && release.Size == Package.Length, "New release was not recognized.");
        Check(release.DownloadUrl.AbsolutePath.EndsWith("/YolanLive-1.0.4-x64-Setup.exe"), "New installer filename was not recognized.");
        foreach (var kind in new[] { "draft", "prerelease", "tag", "asset", "url", "digest", "size" })
        {
            var json = Metadata(); var asset = (JObject)json["assets"][0];
            switch (kind)
            {
                case "draft": json["draft"] = true; break;
                case "prerelease": json["prerelease"] = true; break;
                case "tag": json["tag_name"] = "v1.0.4-beta"; break;
                case "asset": json["assets"] = new JArray(); break;
                case "url": asset["browser_download_url"] = "https://example.invalid/package.exe"; break;
                case "digest": asset["digest"] = "sha256:invalid"; break;
                case "size": asset["size"] = 0; break;
            }
            try { UpdateClient.ParseRelease(json.ToString()); throw new Exception("Invalid release accepted: " + kind); }
            catch (InvalidDataException) { }
        }
    }
    internal static async Task DownloadsAsync()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) });
        }));
        var client = new UpdateClient(http, TestDirectory()); var release = UpdateClient.ParseRelease(Metadata().ToString());
        double progress = 0;
        var path = await client.DownloadAsync(release, value => progress = value, CancellationToken.None);
        Check(progress == 100 && await client.VerifyAsync(release, path, CancellationToken.None), "Downloaded package was not completely verified.");
        Check(await client.DownloadAsync(release, _ => { }, CancellationToken.None) == path && requests == 1, "Verified package was downloaded twice.");
        var damaged = new byte[Package.Length];
        await File.WriteAllBytesAsync(path, damaged);
        Check(!await client.VerifyAsync(release, path, CancellationToken.None), "Changed package was accepted.");
        using var badHttp = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(damaged) })));
        var badClient = new UpdateClient(badHttp, TestDirectory());
        try { await badClient.DownloadAsync(release, _ => { }, CancellationToken.None); throw new Exception("Wrong checksum was accepted."); }
        catch (InvalidDataException) { }
        Check(!File.Exists(badClient.GetInstallerPath(release)) && !File.Exists(badClient.GetInstallerPath(release) + ".partial"), "Failed download remained installable.");
        using var shortHttp = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2]) })));
        var shortClient = new UpdateClient(shortHttp, TestDirectory());
        try { await shortClient.DownloadAsync(release, _ => { }, CancellationToken.None); throw new Exception("Truncated package was accepted."); }
        catch (InvalidDataException) { }
        Check(!File.Exists(shortClient.GetInstallerPath(release)), "Truncated download remained installable.");
    }
    internal static async Task StatesAsync()
    {
        foreach (var tag in new[] { "v1.0.3", "v1.0.2", "v1.0.4" })
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(JsonResponse(Metadata(tag)))));
            var vm = new UpdateVM(new UpdateClient(http, TestDirectory()), UpdateClient.ParseVersion("1.0.3"));
            await vm.CheckAsync();
            Check(vm.HideDownload == (tag != "v1.0.4") && vm.HideInstall && vm.CanCheck, "Update state did not match the running version.");
        }
        using var limitedHttp = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var limited = new UpdateVM(new UpdateClient(limitedHttp, TestDirectory()), UpdateClient.ParseVersion("1.0.3"));
        await limited.CheckAsync();
        Check(limited.Status.Contains("受限") && limited.HideDownload && limited.CanCheck, "Rate limiting looked like latest version or locked the button.");
        using var offlineHttp = new HttpClient(new Handler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("isolated network failure"))));
        var offline = new UpdateVM(new UpdateClient(offlineHttp, TestDirectory()), UpdateClient.ParseVersion("1.0.3"));
        await offline.CheckAsync();
        Check(offline.Status.Contains("网络") && !offline.Status.Contains("最新版本") && offline.HideDownload, "Network failure looked like an up-to-date result.");
    }
    internal static async Task RecoveryAsync()
    {
        using var failedDownload = new HttpClient(new Handler((request, _) => request.RequestUri.AbsoluteUri == UpdateClient.LatestUrl ?
            Task.FromResult(JsonResponse(Metadata())) : Task.FromException<HttpResponseMessage>(new HttpRequestException("Controlled download outage"))));
        var vm = new UpdateVM(new UpdateClient(failedDownload, TestDirectory()), UpdateClient.ParseVersion("1.0.3"));
        await vm.CheckAsync();
        Check(!vm.HasFailure && vm.HideRecovery && vm.DownloadButtonText == "下载安装包", "Normal update shows failure actions.");
        await vm.DownloadAsync();
        Check(vm.HasFailure && !vm.HideRecovery && vm.DownloadButtonText == "重试下载" && vm.BrowserActionText == "浏览器下载" &&
            vm.RecoveryUrl == "https://gh-proxy.org/" + UpdateClient.Repository + "/releases/download/v1.0.4/YolanLive-1.0.4-x64-Setup.exe", "Download failure has no trusted browser fallback or retry label.");
        using var offline = new HttpClient(new Handler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("Controlled metadata outage"))));
        var check = new UpdateVM(new UpdateClient(offline, TestDirectory()), UpdateClient.ParseVersion("1.0.3"));
        await check.CheckAsync();
        Check(check.HasFailure && !check.HideRecovery && check.BrowserActionText == "前往发布页" && check.CopyActionText == "复制发布页链接" &&
            check.RecoveryUrl == UpdateClient.Repository + "/releases/latest", "Metadata failure manufactured an installer URL instead of offering the official release page.");
    }
    internal static async Task BackgroundAsync(MainWindow window)
    {
        using var blocked = new PausedStream(Package);
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri.AbsoluteUri == UpdateClient.LatestUrl ?
            JsonResponse(Metadata()) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(blocked) })));
        var client = new UpdateClient(http, TestDirectory());
        var vm = new UpdateVM(client, UpdateClient.ParseVersion("1.0.3"));
        var original = window.Root.Content;
        try
        {
            var page = new Views.SettingsPage(vm); window.Root.Content = page;
            await Task.Delay(100); await vm.CheckAsync(); await Task.Delay(100);
            Check(((Button)page.FindName("BtnDownloadUpdate")).Visibility == Visibility.Visible, "Available download button is not bound.");
            var download = vm.DownloadAsync();
            for (var i = 0; i < 50 && vm.Progress == 0; i++) await Task.Delay(10);
            Check(vm.Downloading && vm.Progress > 0 && !vm.CanCheck, "Download did not report progress or concurrent check was allowed.");
            window.Root.Content = new TextBlock { Text = "模拟返回直播页面" }; await Task.Delay(100);
            Check(vm.Downloading, "Leaving settings canceled the background download.");
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            {
                page = new Views.SettingsPage(vm) { Width = 480, RequestedTheme = theme }; window.Root.Content = page;
                await Task.Delay(150);
                Check(((ProgressBar)page.FindName("UpdateProgress")).Visibility == Visibility.Visible &&
                    ((ProgressBar)page.FindName("UpdateProgress")).Value > 0 && ((Button)page.FindName("BtnCancelUpdate")).Visibility == Visibility.Visible,
                    "New settings page lost the shared download state.");
                var row = (Grid)page.FindName("SettingsUpdate");
                Check(Grid.GetRow((FrameworkElement)row.Children[1]) == 1, "Update actions overlap the label in a narrow window.");
            }
            vm.Cancel(); await download; await Task.Delay(100);
            Check(vm.Status.Contains("取消") && !vm.Busy && !vm.HideDownload && vm.HideInstall &&
                !File.Exists(client.GetInstallerPath(UpdateClient.ParseRelease(Metadata().ToString()))), "Cancel left the download locked or executable.");
            var checkCalls = 0;
            using var completeHttp = new HttpClient(new Handler((request, _) =>
            {
                checkCalls++;
                return Task.FromResult(request.RequestUri.AbsoluteUri == UpdateClient.LatestUrl ? JsonResponse(Metadata()) :
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) });
            }));
            var readyClient = new UpdateClient(completeHttp, TestDirectory());
            var ready = new UpdateVM(readyClient, UpdateClient.ParseVersion("1.0.3"));
            page = new Views.SettingsPage(ready) { Width = 480 }; window.Root.Content = page; await Task.Delay(100);
            await ready.CheckAsync(); await ready.DownloadAsync(); await Task.Delay(100);
            Check(!ready.HideInstall && ready.HideDownload && ((Button)page.FindName("BtnInstallUpdate")).Visibility == Visibility.Visible, "Verified installer was not offered for manual start.");
            await ready.CheckAsync();
            Check(!ready.HideInstall && checkCalls == 3, "Returning to a downloaded update requested another package.");
            await File.WriteAllBytesAsync(readyClient.GetInstallerPath(UpdateClient.ParseRelease(Metadata().ToString())), new byte[Package.Length]);
            Check(!await ready.StartInstallerAsync() && ready.HideInstall && !ready.HideDownload, "Modified package was launched without rechecking.");
        }
        finally { vm.Cancel(); window.Root.Content = original; }
    }
    internal static async Task LiveAsync()
    {
        string report;
        try
        {
            using var directHttp = Environment.GetCommandLineArgs().Contains("--update-no-proxy") ?
                new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan } : null;
            directHttp?.DefaultRequestHeaders.UserAgent.ParseAdd("YolanLive-update-check");
            var client = new UpdateClient(directHttp);
            var release = await client.CheckAsync(CancellationToken.None);
            var sources = new System.Collections.Generic.List<string>();
            var path = await client.DownloadAsync(release, _ => { }, CancellationToken.None, sources.Add);
            Check(await client.VerifyAsync(release, path, CancellationToken.None), "Published installer checksum mismatch.");
            report = "PASS Public GitHub release metadata and real installer download\nTag=" + release.Tag + "\nSize=" + release.Size +
                "\nSources=" + string.Join(" -> ", sources) + "\nExplicit proxy disabled=" + (directHttp != null) + "\nSHA256 verified=True\nInstaller executed=False";
            Environment.ExitCode = 0;
        }
        catch (Exception ex) { report = "FAIL Public GitHub update check\n" + ex; Environment.ExitCode = 1; }
        var directory = Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(Path.Combine(directory.FullName, "update-check.log"), report);
    }
    internal static async Task FallbackAsync()
    {
        using var http = new HttpClient(new Handler((request, _) =>
            request.RequestUri.Host == "github.com" ? Task.FromException<HttpResponseMessage>(new HttpRequestException("Direct GitHub download is unreachable")) :
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) })));
        var client = new UpdateClient(http, TestDirectory());
        var release = UpdateClient.ParseRelease(Metadata().ToString());
        var path = await client.DownloadAsync(release, _ => { }, CancellationToken.None);
        Check(await client.VerifyAsync(release, path, CancellationToken.None), "Free acceleration did not deliver the verified installer when GitHub was unreachable.");
    }
    internal static async Task OfflineAsync(MainWindow window)
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (var (name, run) in new (string, Func<Task>)[] {
            ("Release validation", () => { Releases(); return Task.CompletedTask; }),
            ("Download verification and cache", DownloadsAsync),
            ("Version and network states", StatesAsync),
            ("Background download and manual install", () => BackgroundAsync(window)),
            ("GitHub unavailable with free acceleration", FallbackAsync),
            ("Cancel and resume partial installer", ResumeAsync),
            ("Range ignored or malformed", RangeResponsesAsync),
            ("Corrupt accelerator and direct fallback", SourceFailuresAsync),
            ("Connect and stalled-body timeouts", TimeoutsAsync) })
        {
            try { await run(); lines.Add("PASS " + name); }
            catch (Exception ex) { lines.Add("FAIL " + name + "\n" + ex); }
        }
        Environment.ExitCode = lines.Any(line => line.StartsWith("FAIL")) ? 1 : 0;
        File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "update-offline.log"), lines);
    }
    internal static async Task ResumeAsync()
    {
        var directory = TestDirectory();
        var release = UpdateClient.ParseRelease(Metadata().ToString());
        using var cancel = new CancellationTokenSource();
        using var blocked = new PausedStream(Package);
        using var firstHttp = new HttpClient(new Handler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(blocked) })));
        var firstClient = new UpdateClient(firstHttp, directory);
        try { await firstClient.DownloadAsync(release, value => { if (value > 0) cancel.Cancel(); }, cancel.Token); throw new Exception("Cancel was ignored."); }
        catch (OperationCanceledException) { }
        var target = firstClient.GetInstallerPath(release);
        Check(!File.Exists(target) && File.Exists(target + ".partial") && new FileInfo(target + ".partial").Length == 16,
            "Cancel deleted resumable bytes or exposed an unverified installer.");
        var ranged = false;
        using var nextHttp = new HttpClient(new Handler((request, _) =>
        {
            ranged = request.Headers.Range?.ToString() == "bytes=16-";
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Package[16..]) };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(16, 63, 64);
            return Task.FromResult(response);
        }));
        var nextClient = new UpdateClient(nextHttp, directory);
        var path = await nextClient.DownloadAsync(release, _ => { }, CancellationToken.None);
        Check(ranged && await nextClient.VerifyAsync(release, path, CancellationToken.None) && !File.Exists(target + ".partial"),
            "Retry did not resume the exact byte range and verify the completed installer.");
    }
    internal static async Task RangeResponsesAsync()
    {
        var release = UpdateClient.ParseRelease(Metadata().ToString());
        foreach (var malformed in new[] { false, true })
        {
            using var http = new HttpClient(new Handler((_, _) =>
            {
                var response = new HttpResponseMessage(malformed ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                    { Content = new ByteArrayContent(Package) };
                if (malformed) response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 63, 64);
                return Task.FromResult(response);
            }));
            var client = new UpdateClient(http, TestDirectory());
            var path = client.GetInstallerPath(release);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            await File.WriteAllBytesAsync(path + ".partial", Package[..16]);
            if (malformed)
            {
                try { await client.DownloadAsync(release, _ => { }, CancellationToken.None); throw new Exception("Wrong range was accepted."); }
                catch (InvalidDataException) { }
                Check(!File.Exists(path) && (await File.ReadAllBytesAsync(path + ".partial")).SequenceEqual(Package[..16]),
                    "Malformed range corrupted the saved prefix or exposed an installer.");
            }
            else
            {
                await client.DownloadAsync(release, _ => { }, CancellationToken.None);
                Check(await client.VerifyAsync(release, path, CancellationToken.None), "Server ignoring Range appended duplicate bytes instead of restarting.");
            }
        }
    }
    internal static async Task SourceFailuresAsync()
    {
        var release = UpdateClient.ParseRelease(Metadata().ToString());
        foreach (var corrupt in new[] { false, true })
        {
            using var http = new HttpClient(new Handler((request, _) =>
            {
                if (!corrupt && request.RequestUri.Host != "github.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new ByteArrayContent(corrupt && request.RequestUri.Host == "gh-proxy.org" ? new byte[64] : Package) });
            }));
            var sources = new System.Collections.Generic.List<string>();
            var client = new UpdateClient(http, TestDirectory());
            var path = await client.DownloadAsync(release, _ => { }, CancellationToken.None, sources.Add);
            Check(await client.VerifyAsync(release, path, CancellationToken.None) &&
                sources.Last().Contains(corrupt ? "ghfast.top" : "GitHub"),
                "Broken accelerators blocked direct fallback or a corrupt installer was accepted.");
        }
    }
    internal static async Task TimeoutsAsync()
    {
        var release = UpdateClient.ParseRelease(Metadata().ToString());
        foreach (var stall in new[] { false, true })
        {
            using var stream = new PausedStream(Package);
            var timeoutCanceled = false;
            var resumed = false;
            using var http = new HttpClient(new Handler(async (request, token) =>
            {
                if (request.RequestUri.Host == "gh-proxy.org")
                {
                    if (stall) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
                    try { await Task.Delay(Timeout.Infinite, token); }
                    finally { timeoutCanceled = token.IsCancellationRequested; }
                }
                resumed = request.Headers.Range?.ToString() == "bytes=16-";
                var response = new HttpResponseMessage(stall ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                    { Content = new ByteArrayContent(stall ? Package[16..] : Package) };
                if (stall) response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(16, 63, 64);
                return response;
            }));
            var client = new UpdateClient(http, TestDirectory());
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var path = await client.DownloadAsync(release, _ => { }, deadline.Token);
            Check(await client.VerifyAsync(release, path, CancellationToken.None) && (stall ? resumed && stream.Canceled : timeoutCanceled),
                "A stalled source was not canceled underneath and replaced with a valid resumable source.");
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class PausedStream(byte[] bytes) : MemoryStream(bytes)
    {
        private bool read;
        internal bool Canceled { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (read)
            {
                try { await Task.Delay(Timeout.Infinite, token); return 0; }
                finally { Canceled = token.IsCancellationRequested; }
            }
            read = true;
            return await base.ReadAsync(buffer[..Math.Min(16, buffer.Length)], token);
        }
    }
}
