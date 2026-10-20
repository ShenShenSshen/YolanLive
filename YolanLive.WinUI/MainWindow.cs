using YolanLive.WinUI.Helper;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;
namespace YolanLive.WinUI;
public sealed partial class MainWindow : Window
{
    public Frame Root => RootFrame;
    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);
    public static readonly string[] WindowSizeOptions = { "960×540", "1280×720", "1600×900", "1920×1080" };
    public string SelectedWindowSize { get; private set; } = GetSavedWindowSize();
    public event EventHandler WindowModeChanged;
    internal bool IsMiniMode { get; private set; }
    internal bool IsWindowDragging { get; private set; }
    internal bool CanDragVideo => !closed && AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped && (IsMiniMode || expandedWindowSize.HasValue);
    internal event EventHandler MiniToolbarLayoutChanged;
    internal double MiniToolbarHeight => miniToolbarPixels / (Root.XamlRoot?.RasterizationScale ?? 1);
    internal event EventHandler VideoDragStarted;
    internal event EventHandler VideoDragCompleted;
    internal event EventHandler VideoDoubleClicked;
    internal event EventHandler VideoPointerMoved;
    private readonly InputNonClientPointerSource videoPointerSource;
    private FrameworkElement videoDragExclusion;
    private RectInt32? nativeVideoCaption;
    private RectInt32? nativeVideoExclusion;
    private bool nativeVideoMovePending;
    private bool miniToolbarVisible;
    private int miniToolbarPixels;
    private double miniToolbarScale = 1;
    private bool updatingBounds;
    private PointInt32 normalPosition;
    private FrameworkElement playbackViewport;
    private Windows.Foundation.Size? expandedContentChrome;
    private SizeInt32? expandedWindowSize;
    private PointInt32 expandedRestoreOffset;
    private PointInt32? expandedVideoAnchor;
    private long? normalFrameBits;
    private readonly WindowSubclassProc windowSubclass;
    private bool boundsQueued;
    private bool closed;
    private readonly string placementKey;
    private bool restoringPlacement = true;
    public MainWindow(bool playback = false)
    {
        placementKey = playback ? "PlaybackWindowPlacement" : "MainWindowPlacement";
        InitializeComponent();
        windowSubclass = WindowMessage;
        if (!SetWindowSubclass(Handle, windowSubclass, 1, 0)) throw new System.ComponentModel.Win32Exception("Unable to observe native window move loop");
        videoPointerSource = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        videoPointerSource.EnteringMoveSize += NativeVideoMoveEntering;
        videoPointerSource.ExitedMoveSize += NativeVideoMoveExited;
        videoPointerSource.PointerReleased += NativeVideoPointerReleased;
        videoPointerSource.PointerMoved += NativeVideoPointerMoved;
        videoPointerSource.PointerEntered += NativeVideoPointerMoved;
        SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        Title = App.DisplayName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "YolanLive.ico"));
        Root.ActualThemeChanged += (_, _) => UpdateCaptionTheme();
        Root.RequestedTheme = (ElementTheme)SettingHelper.GetValue(SettingHelper.THEME, 0);
        Root.SizeChanged += (_, _) => QueueWindowBounds();
        Root.Loaded += (_, _) => { Root.XamlRoot.Changed -= RootDpiChanged; Root.XamlRoot.Changed += RootDpiChanged; };
        Root.Unloaded += (_, _) => { if (Root.XamlRoot != null) Root.XamlRoot.Changed -= RootDpiChanged; };
        Closed += (_, _) =>
        {
            SavePlacement(); SettingHelper.Flush(); closed = true;
            videoPointerSource.EnteringMoveSize -= NativeVideoMoveEntering;
            videoPointerSource.ExitedMoveSize -= NativeVideoMoveExited;
            videoPointerSource.PointerReleased -= NativeVideoPointerReleased;
            videoPointerSource.PointerMoved -= NativeVideoPointerMoved;
            videoPointerSource.PointerEntered -= NativeVideoPointerMoved;
            if (playbackViewport != null) playbackViewport.SizeChanged -= PlaybackViewport_SizeChanged;
            if (videoDragExclusion != null) videoDragExclusion.SizeChanged -= PlaybackViewport_SizeChanged;
            playbackViewport = videoDragExclusion = null;
            RemoveWindowSubclass(Handle, windowSubclass, 1);
        };
        AppWindow.Changed += (_, args) =>
        {
            if (updatingBounds) return;
            if (args.DidSizeChange || args.DidPositionChange || args.DidPresenterChange) ApplyWindowBounds();
            if (args.DidPresenterChange) WindowModeChanged?.Invoke(this, EventArgs.Empty);
            if (args.DidPositionChange) SavePlacement();
        };
        ExtendsContentIntoTitleBar = true;
        RestorePlacement();
        ApplyWindowBounds();
        normalFrameBits = GetWindowLongPtr(Handle, -16).ToInt64() & 0x00C40000L;
        restoringPlacement = false;
        UpdateCaptionTheme();
    }
    public static string ValidateWindowSize(string value) => WindowSizeOptions.Contains(value) ? value : "1280×720";
    public static string GetSavedWindowSize()
    {
        try { return ValidateWindowSize(SettingHelper.GetValue(SettingHelper.WINDOW_SIZE, "1280×720")); }
        catch (Newtonsoft.Json.JsonException) { return "1280×720"; }
    }
    internal static SizeInt32 GetPresetSize(string value) => ValidateWindowSize(value) switch
    {
        "960×540" => new(960, 540),
        "1280×720" => new(1280, 720),
        "1600×900" => new(1600, 900),
        "1920×1080" => new(1920, 1080),
        _ => new(1280, 720)
    };
    internal static RectInt32 FitBounds(SizeInt32 size, PointInt32 position, RectInt32 workArea, SizeInt32 chrome = default)
    {
        var availableWidth = Math.Max(1, workArea.Width - chrome.Width);
        var availableHeight = Math.Max(1, workArea.Height - chrome.Height);
        var scale = Math.Min(1d, Math.Min((double)availableWidth / size.Width, (double)availableHeight / size.Height));
        var videoWidth = Math.Max(1, (int)Math.Floor(size.Width * scale));
        var videoHeight = Math.Max(1, (int)Math.Round((double)videoWidth * size.Height / size.Width));
        var width = Math.Min(workArea.Width, videoWidth + chrome.Width);
        var height = Math.Min(workArea.Height, videoHeight + chrome.Height);
        return new RectInt32(Math.Clamp(position.X, workArea.X, workArea.X + workArea.Width - width),
            Math.Clamp(position.Y, workArea.Y, workArea.Y + workArea.Height - height), width, height);
    }
    internal SizeInt32 GetWindowChromeSize()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        var horizontal = expandedContentChrome?.Width ?? (playbackViewport?.IsLoaded == true ? Math.Max(0, Root.ActualWidth - playbackViewport.ActualWidth) : 0);
        var vertical = expandedContentChrome?.Height ?? (playbackViewport?.IsLoaded == true ? Math.Max(0, Root.ActualHeight - playbackViewport.ActualHeight) : 32);
        return new SizeInt32(Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width) + (int)Math.Round(horizontal * scale),
            Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height) + (int)Math.Round(vertical * scale));
    }
    internal void SetPlaybackViewport(FrameworkElement viewport)
    {
        if (closed) return;
        if (viewport == null)
        {
            SetMiniToolbarVisible(false);
            SetContentExpanded(false);
        }
        if (playbackViewport != null) playbackViewport.SizeChanged -= PlaybackViewport_SizeChanged;
        playbackViewport = viewport;
        if (playbackViewport != null) playbackViewport.SizeChanged += PlaybackViewport_SizeChanged;
        if (viewport == null) SetVideoDragExclusion(null);
        RefreshVideoDragRegions();
        QueueWindowBounds();
    }
    internal void SetContentExpanded(bool expanded)
    {
        if (expandedWindowSize.HasValue == expanded) return;
        SetMiniToolbarVisible(false);
        var position = AppWindow.Position;
        if (expanded)
        {
            var scale = Root.XamlRoot?.RasterizationScale ?? 1;
            var origin = new PointInt32();
            ClientToScreen(Handle, ref origin);
            var offset = playbackViewport?.IsLoaded == true ? playbackViewport.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()) : new Windows.Foundation.Point();
            var videoPosition = new PointInt32(origin.X + (int)Math.Round(offset.X * scale), origin.Y + (int)Math.Round(offset.Y * scale));
            expandedRestoreOffset = new PointInt32(videoPosition.X - position.X, videoPosition.Y - position.Y);
            expandedVideoAnchor = videoPosition;
            expandedWindowSize = playbackViewport?.IsLoaded == true
                ? new SizeInt32(Math.Max(1, (int)Math.Round(playbackViewport.ActualWidth * scale)), Math.Max(1, (int)Math.Round(playbackViewport.ActualHeight * scale)))
                : GetPresetSize(SelectedWindowSize);
            position = videoPosition;
        }
        else
        {
            expandedWindowSize = null;
            var origin = new PointInt32(); ClientToScreen(Handle, ref origin);
            position = new PointInt32(origin.X - expandedRestoreOffset.X, origin.Y - expandedRestoreOffset.Y);
            expandedVideoAnchor = null;
        }
        if (expanded && expandedContentChrome == null)
            expandedContentChrome = new Windows.Foundation.Size(
                playbackViewport?.IsLoaded == true ? Math.Max(0, Root.ActualWidth - playbackViewport.ActualWidth) : 0,
                playbackViewport?.IsLoaded == true ? Math.Max(0, Root.ActualHeight - playbackViewport.ActualHeight) : 32);
        else if (!expanded) expandedContentChrome = null;
        updatingBounds = true;
        try { AppWindow.Move(position); }
        finally { updatingBounds = false; }
        QueueWindowBounds();
    }
    private void PlaybackViewport_SizeChanged(object sender, SizeChangedEventArgs e) => QueueWindowBounds();
    private void RootDpiChanged(XamlRoot sender, XamlRootChangedEventArgs e) => QueueWindowBounds();
    private void QueueWindowBounds()
    {
        if (closed || boundsQueued) return;
        boundsQueued = true;
        DispatcherQueue.TryEnqueue(() => { boundsQueued = false; if (!closed) ApplyWindowBounds(); });
    }
    public void ApplyWindowSize(string value)
    {
        SelectedWindowSize = ValidateWindowSize(value);
        if (expandedWindowSize.HasValue && !IsMiniMode && AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped)
            expandedWindowSize = GetPresetSize(SelectedWindowSize);
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped) ApplyWindowBounds();
    }
    public void SetWindowMode(AppWindowPresenterKind kind)
    {
        SetMiniToolbarVisible(false);
        if (!IsMiniMode && AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped) { normalPosition = AppWindow.Position; SavePlacement(); }
        updatingBounds = true;
        try
        {
            if (IsMiniMode && kind != AppWindowPresenterKind.CompactOverlay && AppWindow.Presenter is OverlappedPresenter previous)
                previous.IsAlwaysOnTop = false;
            // Keep the existing mode request, but own the small window's frame instead of
            // using CompactOverlay, which locks its caption and non-client border.
            IsMiniMode = kind == AppWindowPresenterKind.CompactOverlay;
            AppWindow.SetPresenter(IsMiniMode ? AppWindowPresenterKind.Default : kind);
            if (kind == AppWindowPresenterKind.Default) AppWindow.Move(normalPosition);
        }
        finally { updatingBounds = false; }
        ApplyWindowBounds();
        WindowModeChanged?.Invoke(this, EventArgs.Empty);
    }
    private void SavePlacement()
    {
        if (restoringPlacement || closed || IsWindowDragging || IsMiniMode || AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State != OverlappedPresenterState.Restored) return;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var position = AppWindow.Position;
        if (expandedWindowSize.HasValue)
        {
            var origin = new PointInt32(); ClientToScreen(Handle, ref origin);
            position = new PointInt32(origin.X - expandedRestoreOffset.X, origin.Y - expandedRestoreOffset.Y);
        }
        SettingHelper.SetValue(placementKey, new WindowPlacement
        {
            DisplayId = display.DisplayId.Value, WorkX = display.WorkArea.X, WorkY = display.WorkArea.Y,
            OffsetX = position.X - display.WorkArea.X, OffsetY = position.Y - display.WorkArea.Y
        }, defer: true);
    }
    private void RestorePlacement()
    {
        try
        {
            var saved = SettingHelper.GetValue<WindowPlacement>(placementKey, null);
            if (saved == null) return;
            var displays = DisplayArea.FindAll();
            DisplayArea target = null, previousArea = null;
            for (var index = 0; index < displays.Count; index++)
            {
                var display = displays[index];
                if (display.DisplayId.Value == saved.DisplayId) target = display;
                if (display.WorkArea.X == saved.WorkX && display.WorkArea.Y == saved.WorkY) previousArea = display;
            }
            target ??= previousArea ?? DisplayArea.GetFromPoint(new PointInt32(saved.WorkX, saved.WorkY), DisplayAreaFallback.Nearest);
            var position = new PointInt32((int)Math.Clamp((long)target.WorkArea.X + saved.OffsetX, int.MinValue, int.MaxValue),
                (int)Math.Clamp((long)target.WorkArea.Y + saved.OffsetY, int.MinValue, int.MaxValue));
            AppWindow.MoveAndResize(FitBounds(GetPresetSize(SelectedWindowSize), position, target.WorkArea, GetWindowChromeSize()));
            normalPosition = AppWindow.Position;
        }
        catch (Newtonsoft.Json.JsonException) { }
    }
    internal sealed class WindowPlacement
    {
        public ulong DisplayId { get; set; }
        public int WorkX { get; set; }
        public int WorkY { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
    }
    private void ApplyWindowBounds()
    {
        if (closed || updatingBounds || IsWindowDragging || AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen) return;
        if (AppWindow.Presenter is OverlappedPresenter minimized && minimized.State == OverlappedPresenterState.Minimized) return;
        updatingBounds = true;
        try
        {
            var borderless = expandedWindowSize.HasValue || IsMiniMode;
            if (ExtendsContentIntoTitleBar == borderless) ExtendsContentIntoTitleBar = !borderless;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                var decorated = !borderless;
                if (presenter.HasBorder != decorated || presenter.HasTitleBar != decorated)
                    presenter.SetBorderAndTitleBar(decorated, decorated);
                if (presenter.IsResizable) presenter.IsResizable = false;
                if (presenter.IsMaximizable) presenter.IsMaximizable = false;
                if (presenter.IsAlwaysOnTop != IsMiniMode) presenter.IsAlwaysOnTop = IsMiniMode;
                if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
            }
            ApplyNativeFrame(borderless);
            var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            // This WinRT vector supports indexed access but not the projected IEnumerable interface.
            var displays = DisplayArea.FindAll();
            var workAreas = new RectInt32[displays.Count];
            for (var index = 0; index < displays.Count; index++) workAreas[index] = displays[index].WorkArea;
            var scale = Root.XamlRoot?.RasterizationScale ?? 1;
            RectInt32 bounds;
            if (IsMiniMode || expandedWindowSize.HasValue)
            {
                var videoPosition = AppWindow.Position;
                var mini = IsMiniMode;
                var nativeChrome = new SizeInt32();
                if (!mini)
                {
                    var origin = new PointInt32(); ClientToScreen(Handle, ref origin);
                    var inset = new PointInt32(origin.X - AppWindow.Position.X, origin.Y - AppWindow.Position.Y);
                    if (expandedVideoAnchor is PointInt32 anchor)
                        videoPosition = new PointInt32(anchor.X - inset.X, anchor.Y - inset.Y);
                    expandedVideoAnchor = null;
                    nativeChrome = new SizeInt32(Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width), Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height));
                }
                var size = mini ? new SizeInt32(480, 270) : expandedWindowSize.Value;
                var video = KeepReachable(new RectInt32(videoPosition.X, videoPosition.Y, size.Width + nativeChrome.Width, size.Height + nativeChrome.Height), workAreas, scale);
                var requestedHeight = miniToolbarVisible ? (int)Math.Round((mini ? 44 : 60) * scale) : 0;
                var layoutChanged = miniToolbarPixels != requestedHeight || miniToolbarScale != scale;
                miniToolbarPixels = requestedHeight;
                miniToolbarScale = scale;
                bounds = GetMiniToolbarBounds(video, requestedHeight);
                if (layoutChanged) MiniToolbarLayoutChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                miniToolbarVisible = false;
                miniToolbarPixels = 0;
                var fitted = FitBounds(GetPresetSize(SelectedWindowSize), AppWindow.Position, workArea, GetWindowChromeSize());
                // A normal window needs an accessible title area, not just a piece of video.
                var title = KeepReachable(new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, fitted.Width, Math.Max(1, (int)Math.Ceiling(32 * scale))), workAreas, scale, 200);
                bounds = new RectInt32(title.X, title.Y, fitted.Width, fitted.Height);
            }
            if (AppWindow.Size.Width != bounds.Width || AppWindow.Size.Height != bounds.Height ||
                AppWindow.Position.X != bounds.X || AppWindow.Position.Y != bounds.Y)
                AppWindow.MoveAndResize(bounds);
            ApplyNativeFrame(borderless);
            RefreshVideoDragRegions();
        }
        finally { updatingBounds = false; }
    }
    private void ApplyNativeFrame(bool borderless)
    {
        if (AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped && (borderless || normalFrameBits.HasValue))
        {
            // Remove the actual non-client frame, not only its DWM outline color.
            var style = GetWindowLongPtr(Handle, -16).ToInt64();
            var desired = (style & ~0x00C40000L) | (borderless ? 0 : normalFrameBits.Value); // WS_CAPTION | WS_THICKFRAME
            if (style != desired)
            {
                SetWindowLongPtr(Handle, -16, new nint(desired));
                SetWindowPos(Handle, 0, 0, 0, 0, 0, 0x0037); // FRAMECHANGED, preserve bounds/z-order
            }
        }
        var borderColor = borderless ? 0xFFFFFFFEu : 0xFFFFFFFFu;
        DwmSetWindowAttribute(Handle, 34 /* DWMWA_BORDER_COLOR */, ref borderColor, sizeof(uint));
    }
    internal void SetMiniToolbarVisible(bool visible)
    {
        if (miniToolbarVisible == visible || closed) return;
        miniToolbarVisible = visible;
        if (IsMiniMode || expandedWindowSize.HasValue) ApplyWindowBounds();
    }
    internal static RectInt32 GetMiniToolbarBounds(RectInt32 video, int requestedHeight)
    {
        return new RectInt32(video.X, video.Y, video.Width, video.Height + Math.Max(0, requestedHeight));
    }
    internal static RectInt32 KeepReachable(RectInt32 video, RectInt32[] workAreas, double scale, int minimumWidth = 48)
    {
        var best = video;
        var distance = double.PositiveInfinity;
        foreach (var area in workAreas)
        {
            if (area.Width <= 0 || area.Height <= 0) continue;
            var gripWidth = Math.Min(Math.Min(video.Width, area.Width), Math.Max(1, (int)Math.Ceiling(minimumWidth * scale)));
            var gripHeight = Math.Min(Math.Min(video.Height, area.Height), Math.Max(1, (int)Math.Ceiling(32 * scale)));
            var x = Math.Clamp(video.X, area.X + gripWidth - video.Width, area.X + area.Width - gripWidth);
            var y = Math.Clamp(video.Y, area.Y + gripHeight - video.Height, area.Y + area.Height - gripHeight);
            var movement = Math.Pow((double)x - video.X, 2) + Math.Pow((double)y - video.Y, 2);
            if (movement < distance) { distance = movement; best = new RectInt32(x, y, video.Width, video.Height); }
            if (distance == 0) break;
        }
        return best;
    }
    internal bool IsPointerInside()
    {
        if (closed || !GetCursorPos(out var point)) return false;
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        return point.X >= position.X && point.Y >= position.Y && point.X < position.X + size.Width && point.Y < position.Y + size.Height &&
            GetAncestor(WindowFromPoint(point), 2) == Handle;
    }
    internal void SetVideoDragExclusion(FrameworkElement exclusion)
    {
        if (closed) return;
        if (videoDragExclusion != null) videoDragExclusion.SizeChanged -= PlaybackViewport_SizeChanged;
        videoDragExclusion = exclusion;
        if (videoDragExclusion != null) videoDragExclusion.SizeChanged += PlaybackViewport_SizeChanged;
        RefreshVideoDragRegions();
    }
    internal void RefreshVideoDragRegions(bool force = false)
    {
        if (closed || IsWindowDragging) return;
        var caption = CanDragVideo ? GetClientRegion(playbackViewport) : null;
        var exclusion = caption.HasValue ? GetClientRegion(videoDragExclusion) : null;
        if (caption.HasValue)
        {
            if (force || !nativeVideoCaption.Equals(caption))
                videoPointerSource.SetRegionRects(NonClientRegionKind.Caption, new[] { caption.Value });
            if (exclusion.HasValue)
            {
                if (force || !nativeVideoExclusion.Equals(exclusion))
                    videoPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, new[] { exclusion.Value });
            }
            else if (nativeVideoExclusion.HasValue) videoPointerSource.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        else if (nativeVideoCaption.HasValue)
        {
            // Leave ordinary title-bar regions under WinUI's ownership.
            videoPointerSource.ClearRegionRects(NonClientRegionKind.Caption);
            if (nativeVideoExclusion.HasValue) videoPointerSource.ClearRegionRects(NonClientRegionKind.Passthrough);
        }
        nativeVideoCaption = caption; nativeVideoExclusion = exclusion;
    }
    private RectInt32? GetClientRegion(FrameworkElement element)
    {
        if (Root.XamlRoot == null || element?.IsLoaded != true || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return null;
        var offset = element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
        var scale = Root.XamlRoot.RasterizationScale;
        var x = Math.Max(0, (int)Math.Floor(offset.X * scale));
        var y = Math.Max(0, (int)Math.Floor(offset.Y * scale));
        var right = Math.Min(AppWindow.ClientSize.Width, (int)Math.Ceiling((offset.X + element.ActualWidth) * scale));
        var bottom = Math.Min(AppWindow.ClientSize.Height, (int)Math.Ceiling((offset.Y + element.ActualHeight) * scale));
        return right > x && bottom > y ? new RectInt32(x, y, right - x, bottom - y) : null;
    }
    private void NativeVideoMoveEntering(InputNonClientPointerSource sender, EnteringMoveSizeEventArgs args)
    {
        if (!CanDragVideo || nativeVideoMovePending) return;
        nativeVideoMovePending = true;
        VideoDragStarted?.Invoke(this, EventArgs.Empty);
    }
    private void NativeVideoMoveExited(InputNonClientPointerSource sender, ExitedMoveSizeEventArgs args) => EndNativeVideoMove();
    private void NativeVideoPointerReleased(InputNonClientPointerSource sender, NonClientPointerEventArgs args) => EndNativeVideoMove();
    private void EndNativeVideoMove()
    {
        if (!nativeVideoMovePending) return;
        nativeVideoMovePending = false;
        FinishWindowDrag();
        VideoDragCompleted?.Invoke(this, EventArgs.Empty);
    }
    private void NativeVideoPointerMoved(InputNonClientPointerSource sender, NonClientPointerEventArgs args)
    {
        if (CanDragVideo && !nativeVideoMovePending && !IsWindowDragging) VideoPointerMoved?.Invoke(this, EventArgs.Empty);
    }
    private void FinishWindowDrag()
    {
        if (!IsWindowDragging) return;
        IsWindowDragging = false;
        if (!closed) { ApplyWindowBounds(); SavePlacement(); }
    }
    private nint WindowMessage(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (CanDragVideo && nativeVideoCaption.HasValue)
        {
            if (message == 0x00A3 /* WM_NCLBUTTONDBLCLK */ && wParam == 2 /* HTCAPTION */)
            {
                DispatcherQueue.TryEnqueue(() => { if (CanDragVideo) VideoDoubleClicked?.Invoke(this, EventArgs.Empty); });
                return 0;
            }
            if ((message == 0x00A4 || message == 0x00A5 || message == 0x00A6) && wParam == 2) return 0; // Caption right-click.
            if (message == 0x007B /* WM_CONTEXTMENU */ && lParam != -1) return 0;
        }
        if (message == 0x0231 /* WM_ENTERSIZEMOVE */) IsWindowDragging = true;
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == 0x0232 /* WM_EXITSIZEMOVE */) FinishWindowDrag();
        return result;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowSubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint window, WindowSubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint window, WindowSubclassProc callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref PointInt32 point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PointInt32 point);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(PointInt32 point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, uint attribute, ref uint value, int size);
    private void UpdateCaptionTheme()
    {
        var foreground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Root.Foreground).Color;
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }
}
