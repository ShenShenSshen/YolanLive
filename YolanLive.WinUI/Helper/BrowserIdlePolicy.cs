using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
namespace YolanLive.WinUI.Helper;
internal sealed class BrowserIdlePolicy
{
    private readonly SemaphoreSlim gate;
    private readonly Func<CoreWebView2Controller> controller;
    private readonly Action close;
    private readonly DispatcherTimer timer = new();
    private int pending;
    private DateTime lastUsed;
    internal BrowserIdlePolicy(SemaphoreSlim gate, Func<CoreWebView2Controller> controller, Action close)
    {
        this.gate = gate; this.controller = controller; this.close = close;
        timer.Tick += async (_, _) => await CheckAsync((DateTime.UtcNow - lastUsed).TotalSeconds);
    }
    internal IDisposable Begin()
    {
        pending++; timer.Stop();
        return new Activity(this);
    }
    internal bool? LastSuspendResult { get; private set; }
    internal string LastSuspendFailure { get; private set; }
    internal void Stop() => timer.Stop();
    internal async Task CheckAsync(double elapsedSeconds)
    {
        timer.Stop();
        if (pending != 0 || controller() == null || !gate.Wait(0)) return;
        try
        {
            if (elapsedSeconds >= 300) { close(); return; }
            if (elapsedSeconds >= 60)
            {
                try { LastSuspendResult = await controller().CoreWebView2.TrySuspendAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or TimeoutException or ObjectDisposedException) { LastSuspendFailure = error.GetType().Name; }
                if (pending == 0 && controller() != null) { timer.Interval = TimeSpan.FromSeconds(Math.Max(1, 300 - elapsedSeconds)); timer.Start(); }
            }
        }
        finally { gate.Release(); }
    }
    private sealed class Activity(BrowserIdlePolicy owner) : IDisposable
    {
        private BrowserIdlePolicy current = owner;
        public void Dispose()
        {
            var value = current; current = null; if (value == null) return;
            if (--value.pending == 0 && value.controller() != null)
            {
                value.lastUsed = DateTime.UtcNow; value.timer.Interval = TimeSpan.FromSeconds(60); value.timer.Start();
            }
        }
    }
}
