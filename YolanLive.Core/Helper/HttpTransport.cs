using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace YolanLive.Core.Helper;
public static class HttpTransport
{
    private static readonly SemaphoreSlim Limit = new SemaphoreSlim(4);
    private static readonly HttpClient RedirectClient = Create(true), DirectClient = Create(false);
    private static HttpClient Create(bool redirect) => new HttpClient(new HttpClientHandler
    {
        UseCookies = false, AllowAutoRedirect = redirect,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        MaxConnectionsPerServer = 4
    }) { Timeout = Timeout.InfiniteTimeSpan };
    public static async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await Limit.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(RequestMetrics.Begin());
    }
    public static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default, bool redirect = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var lease = await EnterAsync(timeout.Token).ConfigureAwait(false);
        return await (redirect ? RedirectClient : DirectClient).SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
    }
    private sealed class Lease : IDisposable
    {
        private IDisposable measured;
        internal Lease(IDisposable measured) { this.measured = measured; }
        public void Dispose() { var value = Interlocked.Exchange(ref measured, null); if (value != null) { value.Dispose(); Limit.Release(); } }
    }
}
