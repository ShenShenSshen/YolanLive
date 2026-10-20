using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan };
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
    internal async Task<string> DownloadAsync(UpdateRelease release, Action<double> progress, CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        var target = GetInstallerPath(release);
        if (await VerifyAsync(release, target, token)) { progress(100); return target; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var partial = target + ".partial";
        try
        {
            using var response = await client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long size && size != release.Size) throw new InvalidDataException("安装包大小与发布信息不一致，请重试");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                long received = 0;
                var lastPercent = -1;
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
                {
                    received += count;
                    if (received > release.Size) throw new InvalidDataException("安装包大小与发布信息不一致，请重试");
                    await file.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    var percent = (int)(received * 100d / release.Size);
                    if (percent != lastPercent) { lastPercent = percent; progress(percent); }
                }
            }
            if (!await VerifyAsync(release, partial, timeout.Token)) throw new InvalidDataException("安装包校验失败，请重新下载");
            timeout.Token.ThrowIfCancellationRequested();
            File.Move(partial, target, true);
            return target;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
