using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace YolanLive.Core.Helper;
public static class HttpUtil
{
    private static HttpRequestMessage Request(HttpMethod method, string url, IDictionary<string,string> headers, IDictionary<string,string> query = null)
    {
        if (query != null)
        {
            var pairs = new List<string>();
            foreach (var pair in query) pairs.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value));
            url += (url.Contains("?") ? "&" : "?") + string.Join("&", pairs);
        }
        var request = new HttpRequestMessage(method, url);
        if (headers != null) foreach (var pair in headers) request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        return request;
    }
    public static async Task<string> GetString(string url, IDictionary<string,string> headers = null, IDictionary<string,string> queryParameters = null, CancellationToken cancellationToken = default)
    {
        using var response = await Get(url, headers, queryParameters, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
    public static async Task<HttpResponseMessage> Get(string url, IDictionary<string,string> headers = null, IDictionary<string,string> queryParameters = null, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get, url, headers, queryParameters);
        var response = await HttpTransport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try { response.EnsureSuccessStatusCode(); return response; }
        catch { response.Dispose(); throw; }
    }
    public static async Task<string> GetUtf8String(string url, IDictionary<string,string> headers = null, CancellationToken cancellationToken = default)
    {
        using var response = await Get(url, headers, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
    }
    public static async Task<string> PostString(string url, string data, IDictionary<string,string> headers = null, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Post, url, headers);
        request.Content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await HttpTransport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
    public static async Task<string> PostJsonString(string url, string data, IDictionary<string,string> headers = null, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Post, url, headers);
        request.Content = new StringContent(data, Encoding.UTF8, "application/json");
        using var response = await HttpTransport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode(); return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
    public static async Task<HttpResponseMessage> Head(string url, IDictionary<string,string> headers = null, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Head, url, headers);
        return await HttpTransport.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
