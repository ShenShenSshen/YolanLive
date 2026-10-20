using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using System;
using System.Threading.Tasks;
namespace YolanLive.WinUI.Helper;
internal sealed class PageStatusRefresh
{
    private readonly FrameworkElement page;
    private readonly Func<Task> refresh;
    private readonly Action cancel;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private MainWindow window;
    private bool minimized, running, pending;
    internal PageStatusRefresh(FrameworkElement page, Func<Task> refresh, Action cancel)
    {
        this.page = page; this.refresh = refresh; this.cancel = cancel;
        page.Loaded += Loaded; page.Unloaded += Unloaded;
        timer.Tick += async (_, _) => await RefreshAsync();
        if (page.IsLoaded) Loaded(page, new RoutedEventArgs());
    }
    private void Loaded(object sender, RoutedEventArgs e)
    {
        if (window != null) { window.AppWindow.Changed -= WindowChanged; window.Closed -= Closed; }
        window = App.GetWindowFor(page);
        if (window == null) return;
        window.AppWindow.Changed += WindowChanged;
        window.Closed += Closed;
        minimized = IsMinimized();
        if (!minimized) timer.Start(); else cancel();
    }
    private void Unloaded(object sender, RoutedEventArgs e)
    {
        timer.Stop(); pending = false;
        if (window != null) { window.AppWindow.Changed -= WindowChanged; window.Closed -= Closed; }
        window = null; cancel();
    }
    private void Closed(object sender, WindowEventArgs e) => Unloaded(page, new RoutedEventArgs());
    private bool IsMinimized() => window?.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
    private async void WindowChanged(AppWindow sender, AppWindowChangedEventArgs e)
    {
        var current = IsMinimized(); if (current == minimized) return;
        minimized = current;
        if (minimized) { timer.Stop(); cancel(); }
        else { timer.Start(); await RefreshAsync(); }
    }
    internal async Task RefreshAsync()
    {
        if (!page.IsLoaded || window == null || IsMinimized()) return;
        if (running) { pending = true; return; }
        running = true;
        try { await refresh(); }
        catch (OperationCanceledException) { }
        finally { running = false; if (pending) { pending = false; await RefreshAsync(); } }
    }
}
