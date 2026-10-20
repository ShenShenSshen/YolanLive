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
                !File.Exists(client.GetInstallerPath(UpdateClient.ParseRelease(Metadata().ToString())) + ".partial"), "Cancel left the download locked or executable.");
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
            var client = new UpdateClient();
            var release = await client.CheckAsync(CancellationToken.None);
            var path = await client.DownloadAsync(release, _ => { }, CancellationToken.None);
            Check(await client.VerifyAsync(release, path, CancellationToken.None), "Published installer checksum mismatch.");
            report = "PASS Public GitHub release metadata and real installer download\nTag=" + release.Tag + "\nSize=" + release.Size + "\nSHA256 verified=True\nInstaller executed=False";
            Environment.ExitCode = 0;
        }
        catch (Exception ex) { report = "FAIL Public GitHub update check\n" + ex; Environment.ExitCode = 1; }
        var directory = Directory.CreateDirectory(AppPaths.DataDirectory);
        File.WriteAllText(Path.Combine(directory.FullName, "update-check.log"), report);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class PausedStream(byte[] bytes) : MemoryStream(bytes)
    {
        private bool read;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (read) { await Task.Delay(Timeout.Infinite, token); return 0; }
            read = true;
            return await base.ReadAsync(buffer[..Math.Min(16, buffer.Length)], token);
        }
    }
}
