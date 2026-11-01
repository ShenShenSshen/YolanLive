using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper;

internal sealed record UpdateRelease(Version Version, string Tag, Uri DownloadUrl, long Size, string Sha256);

internal sealed class UpdateClient
{
    internal const string Repository = "https://github.com/ShenShenSshen/YolanLive";
    internal const string LatestUrl = "https://api.github.com/repos/ShenShenSshen/YolanLive/releases/latest";
    private static readonly HttpClient DefaultClient = CreateClient();
    private readonly HttpClient client;
    private readonly string directory;

    internal UpdateClient(HttpClient client = null, string directory = null)
    {
        this.client = client ?? DefaultClient;
        this.directory = directory ?? Path.Combine(AppPaths.DataDirectory, "Updates");
    }
    private static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("YolanLive/" + typeof(App).Assembly.GetName().Version.ToString(3));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
    internal static Version ParseVersion(string text)
    {
        if (text?.StartsWith("v", StringComparison.OrdinalIgnoreCase) == true) text = text[1..];
        if (!Version.TryParse(text, out var version)) throw new InvalidDataException("发布版本号无法识别");
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }
    internal static UpdateRelease ParseRelease(string json)
    {
        var data = JObject.Parse(json);
        if (data.Value<bool>("draft") || data.Value<bool>("prerelease")) throw new InvalidDataException("暂时没有可用的正式版本");
        var tag = data.Value<string>("tag_name");
        var version = ParseVersion(tag);
        var name = "YolanLive-" + tag.TrimStart('v', 'V') + "-x64-Setup.exe";
        var asset = (data["assets"] as JArray)?.OfType<JObject>().SingleOrDefault(a => a.Value<string>("name") == name);
        if (asset == null) throw new InvalidDataException("该版本尚未提供 x64 安装包，请稍后重试");
        var url = asset.Value<string>("browser_download_url");
        var expected = Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.AbsoluteUri != expected)
            throw new InvalidDataException("安装包下载地址不属于本项目的发布版本");
        var size = asset.Value<long>("size");
        var digest = asset.Value<string>("digest");
        if (size <= 0 || digest?.StartsWith("sha256:", StringComparison.Ordinal) != true || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException("安装包缺少有效的大小或 SHA-256 校验信息");
        return new UpdateRelease(version, tag, uri, size, digest[7..]);
    }
    internal async Task<UpdateRelease> CheckAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync(LatestUrl, timeout.Token);
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
            throw new HttpRequestException("GitHub 请求受限，请稍后重试", null, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidDataException("尚未找到可用的正式发布，请稍后重试");
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token));
    }
    internal string GetInstallerPath(UpdateRelease release) => Path.Combine(directory, "YolanLive-" + release.Version + "-x64-Setup.exe");
    internal async Task<bool> VerifyAsync(UpdateRelease release, string path, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != release.Size) return false;
        await using var file = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
        return hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    internal async Task<string> DownloadAsync(UpdateRelease release, Action<double> progress, CancellationToken token, Action<string> sourceChanged = null)
    {
        Directory.CreateDirectory(directory);
        var target = GetInstallerPath(release);
        if (await VerifyAsync(release, target, token)) { progress(100); return target; }
        if (File.Exists(target)) File.Delete(target);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        operation.CancelAfter(TimeSpan.FromMinutes(15));
        // Only relay this project's validated public asset, never account/session requests.
        var sources = new[] {
            ("免费加速 1 · gh-proxy.org", new Uri("https://gh-proxy.org/" + release.DownloadUrl.AbsoluteUri)),
            ("免费加速 2 · ghfast.top", new Uri("https://ghfast.top/" + release.DownloadUrl.AbsoluteUri)),
            ("GitHub 原站", release.DownloadUrl) };
        for (var index = 0; index < sources.Length; index++)
        {
            operation.Token.ThrowIfCancellationRequested();
            sourceChanged?.Invoke(sources[index].Item1);
            try { return await DownloadFromAsync(release, sources[index].Item2, progress, operation.Token); }
            catch (Exception ex) when (!operation.IsCancellationRequested && index + 1 < sources.Length &&
                ex is HttpRequestException or InvalidDataException or OperationCanceledException) { }
        }
        throw new HttpRequestException("所有下载源暂时不可用，请稍后重试");
    }
    private async Task<string> DownloadFromAsync(UpdateRelease release, Uri url, Action<double> progress, CancellationToken token)
    {
        var target = GetInstallerPath(release);
        var partial = target + ".partial";
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset >= release.Size)
        {
            if (await VerifyAsync(release, partial, token))
            { token.ThrowIfCancellationRequested(); File.Move(partial, target, true); progress(100); return target; }
            File.Delete(partial);
            offset = 0;
        }
        using var activity = CancellationTokenSource.CreateLinkedTokenSource(token);
        activity.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/octet-stream");
        request.Headers.AcceptEncoding.ParseAdd("identity");
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, activity.Token);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.Unit != "bytes" || range.From != offset || range.Length != release.Size || range.To != release.Size - 1)
                throw new InvalidDataException("下载源返回的续传范围无效，正在尝试其他下载源");
        }
        else if (response.StatusCode == HttpStatusCode.OK) offset = 0; // A server ignoring Range must restart, never append.
        else throw new InvalidDataException("下载源未返回安装包文件");
        if (response.Content.Headers.ContentLength is long size && size != release.Size - offset)
            throw new InvalidDataException("安装包大小与发布信息不一致，请重试");
        activity.CancelAfter(TimeSpan.FromSeconds(20));
        await using (var input = await response.Content.ReadAsStreamAsync(activity.Token))
        await using (var file = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536];
            var received = offset;
            var lastPercent = -1;
            progress((int)(received * 100d / release.Size));
            int count;
            while (true)
            {
                activity.CancelAfter(TimeSpan.FromSeconds(20));
                try { count = await input.ReadAsync(buffer, activity.Token); }
                catch (IOException ex) { throw new HttpRequestException("下载连接中断，已保留进度，可以重试", ex); }
                if (count == 0) break;
                received += count;
                if (received > release.Size) throw new InvalidDataException("安装包大小与发布信息不一致，请重试");
                await file.WriteAsync(buffer.AsMemory(0, count), activity.Token);
                var percent = (int)(received * 100d / release.Size);
                if (percent != lastPercent) { lastPercent = percent; progress(percent); }
            }
        }
        // Network failures/cancellation leave only a non-executable partial file for retry.
        if (new FileInfo(partial).Length < release.Size) throw new HttpRequestException("下载连接中断，已保留进度，可以重试");
        if (!await VerifyAsync(release, partial, token))
        {
            File.Delete(partial);
            throw new InvalidDataException("安装包校验失败，请重新下载");
        }
        token.ThrowIfCancellationRequested();
        File.Move(partial, target, true);
        return target;
    }
}
