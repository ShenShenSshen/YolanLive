using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
namespace YolanLive.WinUI.Helper;
internal static class BrowserMediaBlock
{
    internal static async Task ApplyAsync(CoreWebView2 browser, CoreWebView2Environment environment)
    {
        browser.IsMuted = true;
        browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Media);
        browser.WebResourceRequested += (_, e) => e.Response = environment.CreateWebResourceResponse(null, 403, "Background media disabled", "Content-Type: text/plain");
        await browser.AddScriptToExecuteOnDocumentCreatedAsync(@"
            const mediaUrl = input => /\.(flv|m3u8|mp4|m4s|ts)$/i.test(new URL(input instanceof Request ? input.url : String(input), location.href).pathname);
            const blocked = () => new DOMException('Background media disabled', 'NotAllowedError');
            const originalFetch = window.fetch;
            window.fetch = function(input) { if (mediaUrl(input)) return Promise.reject(blocked()); return originalFetch.apply(this, arguments); };
            const originalOpen = XMLHttpRequest.prototype.open;
            XMLHttpRequest.prototype.open = function(method, url) { if (mediaUrl(url)) throw blocked(); return originalOpen.apply(this, arguments); };
            HTMLMediaElement.prototype.play = function() { this.pause(); return Promise.reject(blocked()); };
            document.addEventListener('play', event => { if (event.target instanceof HTMLMediaElement) event.target.pause(); }, true);
        ");
    }
}
