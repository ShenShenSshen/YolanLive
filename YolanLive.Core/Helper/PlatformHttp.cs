using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
namespace YolanLive.Core.Helper;
internal static class PlatformHttp
{
    internal static async Task<string> Send(string url, string body, string cookie, string userAgent, string referer, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.Referrer = new Uri(referer);
        if (!string.IsNullOrEmpty(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await HttpTransport.SendAsync(request, cancellationToken, redirect: false);
        if (!response.IsSuccessStatusCode) throw new LiveRequestException(response.StatusCode == HttpStatusCode.Unauthorized ? LiveFailureKind.Authentication : LiveFailureKind.Platform, "平台请求失败：" + (int)response.StatusCode);
        return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());
    }
    internal static JObject Json(string text, string code = "error")
    {
        var json = JObject.Parse(text);
        LiveRequestException.EnsureSuccess(json, code);
        return json;
    }
    internal static int Hot(JToken value)
    {
        var text = value?.ToString() ?? "0";
        if (!double.TryParse(text.Replace("万", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)) return 0;
        return (int)Math.Min(int.MaxValue, number * (text.Contains("万") ? 10000 : 1));
    }
    internal static string Room(object value)
    {
        var id = value?.ToString() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9_]{1,64}$")) throw new ArgumentException("直播间地址无效");
        return id;
    }
}
