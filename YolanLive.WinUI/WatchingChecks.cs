using YolanLive.Core.Helper;
using YolanLive.WinUI.Helper;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Media.Playback;

namespace YolanLive.WinUI;
internal static class WatchingChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task RecoveryAsync()
    {
        var recovery = new PlaybackRecovery();
        var calls = 0;
        var result = await recovery.RunAsync(_ => { calls++; return Task.FromResult(RecoveryOutcome.Failed); }, _ => { });
        Check(result == RecoveryOutcome.Failed && calls == 3 && recovery.Attempts == 3, "Recovery exceeded or skipped its three attempts.");
        await recovery.RunAsync(_ => { calls++; return Task.FromResult(RecoveryOutcome.Failed); }, _ => { });
        Check(calls == 3, "Repeated media events restarted an exhausted retry budget.");
        recovery.Reset(); calls = 0;
        result = await recovery.RunAsync(_ => { calls++; return Task.FromResult(RecoveryOutcome.Stopped); }, _ => { });
        Check(result == RecoveryOutcome.Stopped && calls == 1, "A stopped room was retried again.");
        recovery.Reset(); calls = 0;
        var pending = recovery.RunAsync(_ => { calls++; return Task.FromResult(RecoveryOutcome.Failed); }, _ => { });
        recovery.Cancel();
        Check(await pending == RecoveryOutcome.Cancelled && calls == 0, "Leaving playback did not cancel a pending retry.");
        recovery.Reset();
        var entered = new TaskCompletionSource<bool>();
        var release = new TaskCompletionSource<RecoveryOutcome>();
        var old = recovery.RunAsync(async _ => { entered.TrySetResult(true); return await release.Task; }, _ => { });
        await entered.Task;
        recovery.Reset();
        var replacement = recovery.RunAsync(_ => Task.FromResult(RecoveryOutcome.Recovered), _ => { });
        release.SetResult(RecoveryOutcome.Recovered);
        Check(await old == RecoveryOutcome.Cancelled && recovery.IsRunning, "A stale attempt cleared the new recovery operation.");
        Check(await replacement == RecoveryOutcome.Recovered, "A replacement stream could not recover.");
    }
    internal static void LoginErrors()
    {
        Check(RequestErrorMessage.Kind(new HttpRequestException("network")) == LiveFailureKind.Network, "Network errors looked like expired login.");
        Check(RequestErrorMessage.Kind(new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden)) == LiveFailureKind.Platform, "Platform blocking looked like expired login.");
        Check(RequestErrorMessage.Kind(new TimeoutException()) == LiveFailureKind.Network, "Timeout was misclassified.");
        var cookie = "test-only-not-a-real-cookie";
        SettingHelper.SetValue(SettingHelper.BILI_COOKIE, cookie);
        BiliAccount.Instance.ApplyUserInfo(JObject.Parse("{\"code\":-412}"));
        Check(BiliAccount.Instance.Cookie == cookie && BiliAccount.Instance.StatusMessage.Contains("保留"), "Platform failure deleted the saved login.");
        BiliAccount.Instance.ApplyUserInfo(JObject.Parse("{\"code\":-101}"));
        Check(!BiliAccount.Instance.Logined && BiliAccount.Instance.StatusMessage.Contains("失效"), "Explicit authentication failure was not shown.");
        BiliAccount.Instance.Logout();
    }
    internal static async Task ManageFavoritesAsync()
    {
        DatabaseHelper.AddFavorite(new Models.FavoriteItem { SiteName = "哔哩哔哩直播", RoomID = "management-offline-test", UserName = "离线测试", Photo = "" });
        var saved = (await DatabaseHelper.GetFavorites()).Single(item => item.RoomID == "management-offline-test");
        var dialog = new Controls.FavoriteManagementDialog();
        await dialog.LoadAsync();
        var list = ((Grid)dialog.Content).Children.OfType<ListView>().Single();
        var entry = list.Items.Cast<Models.FavoriteItem>().Single(item => item.ID == saved.ID);
        list.SelectedItem = entry;
        dialog.RemoveSelected();
        Check(!(await DatabaseHelper.GetFavorites()).Any(item => item.ID == saved.ID), "All-favorites management could not remove a hidden record.");
    }
    internal static async Task WindowPlacementAsync()
    {
        var oldMain = SettingHelper.GetValue<MainWindow.WindowPlacement>("MainWindowPlacement", null);
        var oldPlayback = SettingHelper.GetValue<MainWindow.WindowPlacement>("PlaybackWindowPlacement", null);
        MainWindow first = null, next = null, playback = null;
        try
        {
            first = new MainWindow(); first.Activate(); await Task.Delay(150);
            var work = DisplayArea.GetFromWindowId(first.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            first.AppWindow.Move(new PointInt32(work.X + 70, work.Y + 60));
            await Task.Delay(150);
            var expected = first.AppWindow.Position;
            first.Close(); first = null;
            playback = new MainWindow(true); playback.Activate();
            playback.AppWindow.Move(new PointInt32(work.X + 140, work.Y + 100));
            await Task.Delay(150); playback.Close(); playback = null;
            next = new MainWindow(); next.Activate(); await Task.Delay(150);
            Check(next.AppWindow.Position.X == expected.X && next.AppWindow.Position.Y == expected.Y, "Playback position overwrote the main position or position did not restore.");
            next.Close(); next = null;
            SettingHelper.SetValue("MainWindowPlacement", new MainWindow.WindowPlacement { DisplayId = ulong.MaxValue, WorkX = 250000, WorkY = -250000, OffsetX = 300000, OffsetY = -300000 });
            next = new MainWindow(); next.Activate(); await Task.Delay(150);
            work = DisplayArea.GetFromWindowId(next.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var position = next.AppWindow.Position; var size = next.AppWindow.Size;
            Check(position.X >= work.X && position.Y >= work.Y && position.X + size.Width <= work.X + work.Width && position.Y + size.Height <= work.Y + work.Height,
                "Disconnected display restored a window off-screen.");
        }
        finally
        {
            first?.Close(); next?.Close(); playback?.Close();
            SettingHelper.SetValue("MainWindowPlacement", oldMain);
            SettingHelper.SetValue("PlaybackWindowPlacement", oldPlayback);
        }
    }
    internal static async Task PlayerControlsAsync(MainWindow window, Views.LiveRoomPage page)
    {
        WindowDragPolicy();
        const BindingFlags methods = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(Views.LiveRoomPage);
        type.GetMethod("LoadSetting", methods).Invoke(page, null);
        var qualityFrame = (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent((ComboBox)page.FindName("PlayQuality")));
        var lineFrame = (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent((ComboBox)page.FindName("PlayLine")));
        Check(qualityFrame.Width == lineFrame.Width && qualityFrame.Height == lineFrame.Height, "Playback option frames differ in size.");
        var savedPreset = window.SelectedWindowSize;
        App.SetWindowSize("960×540"); await Task.Delay(200);
        var smallOptionWidth = qualityFrame.Width;
        Check(((FrameworkElement)page.FindName("StandardControl")).Visibility == Visibility.Visible, "Normal 960px window collapsed all playback options.");
        Check(((FrameworkElement)page.FindName("WindowSizeButton")).Visibility == Visibility.Visible && ((FrameworkElement)page.FindName("DirectFullscreenButton")).Visibility == Visibility.Visible, "Size/display entrances are not directly available.");
        FrameworkElement FindPart(DependencyObject root, string name)
        {
            if (root is FrameworkElement element && element.Name == name) return element;
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var found = FindPart(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index), name);
                if (found != null) return found;
            }
            return null;
        }
        var lineSelector = (ComboBox)page.FindName("PlayLine");
        var lineText = FindPart(lineSelector, "ContentPresenter");
        var arrow = FindPart(lineSelector, "DropDownGlyph");
        var sample = new TextBlock { Text = "线路10", FontFamily = lineSelector.FontFamily, FontSize = lineSelector.FontSize };
        sample.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var textSlot = lineText == null ? 0 : Microsoft.UI.Xaml.Controls.Primitives.LayoutInformation.GetLayoutSlot(lineText).Width - lineText.Margin.Left - lineText.Margin.Right;
        Check(textSlot >= sample.DesiredSize.Width, "Line text is clipped by the selector arrow/padding: slot=" + textSlot + ", required=" + sample.DesiredSize.Width + ", selector=" + lineSelector.ActualWidth + ", arrow=" + arrow?.Width);
        Check(arrow?.Width == 10, "Compact selector did not retain its native template with a smaller arrow.");
        App.SetWindowSize("1280×720"); await Task.Delay(200);
        Check(qualityFrame.Width > smallOptionWidth && qualityFrame.Width == lineFrame.Width, "Playback options did not adapt equally to width.");
        type.GetMethod("WindowSizeMenu_Opening", methods).Invoke(page, new object[] { null, null });
        Check(((MenuFlyout)page.FindName("WindowSizeMenu")).Items.Count == MainWindow.WindowSizeOptions.Length, "Size menu lost presets.");
        App.SetWindowSize(savedPreset); await Task.Delay(200);
        var originalSize = window.AppWindow.Size;
        var originalPosition = window.AppWindow.Position;
        var viewport = (FrameworkElement)page.FindName("player");
        var videoSize = new SizeInt32((int)Math.Round(viewport.ActualWidth * page.XamlRoot.RasterizationScale), (int)Math.Round(viewport.ActualHeight * page.XamlRoot.RasterizationScale));
        type.GetMethod("SetFullWindow", methods).Invoke(page, new object[] { true });
        await Task.Delay(250);
        ((DispatcherTimer)type.GetField("miniHoverTimer", methods).GetValue(page)).Stop();
        type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false });
        await Task.Delay(150);
        // Layout can restart hover checks; freeze the fixture after it settles before measuring the hidden rail.
        for (var settle = 0; settle < 8; settle++)
        {
            ((DispatcherTimer)type.GetField("miniHoverTimer", methods).GetValue(page)).Stop();
            type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false });
            await Task.Delay(40);
            ((DispatcherTimer)type.GetField("miniHoverTimer", methods).GetValue(page)).Stop();
            type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false });
            page.UpdateLayout();
            if (Math.Abs(viewport.ActualWidth * page.XamlRoot.RasterizationScale - videoSize.Width) <= 1 &&
                Math.Abs(viewport.ActualHeight * page.XamlRoot.RasterizationScale - videoSize.Height) <= 1) break;
        }
        Check(window.AppWindow.ClientSize.Width == videoSize.Width && window.AppWindow.ClientSize.Height == videoSize.Height, "Window fullscreen included chrome in the video size: expected=" + videoSize.Width + "x" + videoSize.Height + "; actual=" + window.AppWindow.ClientSize.Width + "x" + window.AppWindow.ClientSize.Height);
        Check(Math.Abs(viewport.ActualWidth * page.XamlRoot.RasterizationScale - videoSize.Width) <= 1 && Math.Abs(viewport.ActualHeight * page.XamlRoot.RasterizationScale - videoSize.Height) <= 1, "Window fullscreen rescaled the video viewport: expected=" + videoSize.Width + "x" + videoSize.Height + ", actual=" + viewport.ActualWidth + "x" + viewport.ActualHeight + ", scale=" + page.XamlRoot.RasterizationScale);
        Check(window.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: false, HasTitleBar: false }, "Window fullscreen gained topmost or retained its caption.");
        Check(window.CanDragVideo, "Window fullscreen has no video drag entry.");
        if (DwmGetWindowAttribute(window.Handle, 34, out var borderColor, sizeof(uint)) == 0)
            Check(borderColor == 0xFFFFFFFEu, "Window fullscreen retained the system border outline.");
        window.SetMiniToolbarVisible(true);
        Check(window.AppWindow.ClientSize.Width == videoSize.Width && window.AppWindow.ClientSize.Height - (int)Math.Round(window.MiniToolbarHeight * page.XamlRoot.RasterizationScale) == videoSize.Height, "Hover rail resized the video.");
        window.SetMiniToolbarVisible(false);
        window.SetWindowMode(AppWindowPresenterKind.FullScreen);
        window.SetWindowMode(AppWindowPresenterKind.Default);
        await Task.Delay(150);
        ((DispatcherTimer)type.GetField("miniHoverTimer", methods).GetValue(page)).Stop();
        type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false });
        Check(window.AppWindow.ClientSize.Width == videoSize.Width && window.AppWindow.ClientSize.Height == videoSize.Height, "Returning from fullscreen lost window fullscreen size.");
        type.GetMethod("SetFullWindow", methods).Invoke(page, new object[] { false });
        await Task.Delay(250);
        Check(window.AppWindow.Presenter is OverlappedPresenter { HasTitleBar: true }, "Caption did not restore.");
        Check(!window.CanDragVideo, "Normal playback should retain title-bar dragging.");
        if (DwmGetWindowAttribute(window.Handle, 34, out borderColor, sizeof(uint)) == 0)
            Check(borderColor == 0xFFFFFFFFu, "Normal window did not restore system border behavior.");
        Check(window.AppWindow.Position.X == originalPosition.X && window.AppWindow.Position.Y == originalPosition.Y && window.AppWindow.Size.Width == originalSize.Width && window.AppWindow.Size.Height == originalSize.Height, "Normal window bounds did not restore: expected=" + originalPosition.X + "," + originalPosition.Y + " " + originalSize.Width + "x" + originalSize.Height + ", actual=" + window.AppWindow.Position.X + "," + window.AppWindow.Position.Y + " " + window.AppWindow.Size.Width + "x" + window.AppWindow.Size.Height);
        var slider = (Slider)page.FindName("SliderVolume");
        var initialVolume = slider.Value;
        slider.Value = .55;
        Check(Math.Abs(SettingHelper.GetValue(SettingHelper.PLAYER_VOLUME, 0d) - .55) < .001, "Toolbar volume did not persist.");
        await PlayerGestureAndBrightnessAsync(page);
        type.GetMethod("Grid_DoubleTapped", methods).Invoke(page, new object[] { null, null });
        Check(window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen, "Double-click did not enter full screen.");
        type.GetMethod("Grid_DoubleTapped", methods).Invoke(page, new object[] { null, null });
        Check(window.AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped, "Double-click did not exit full screen.");
        type.GetMethod("ShowControl", methods).Invoke(page, new object[] { true });
        type.GetMethod("Grid_DoubleTapped", methods).Invoke(page, new object[] { null, null });
        await Task.Delay(300);
        ((Button)page.FindName("BottomBtnAddFavorite")).Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        type.GetField("showControlsFlag", methods).SetValue(page, 5);
        type.GetMethod("ControlTimer_Tick", methods).Invoke(page, new object[] { null, null });
        await Task.Delay(200);
        Check(((Grid)page.FindName("control")).Visibility == Visibility.Collapsed, "Idle player controls stayed visible: visible=" + window.AppWindow.IsVisible + ", floating=" + type.GetField("floatingToolbar", methods).GetValue(page) + ", hiding=" + type.GetField("toolbarHiding", methods).GetValue(page));
        type.GetMethod("Grid_DoubleTapped", methods).Invoke(page, new object[] { null, null });
        await Task.Delay(250);
        var beforeMiniSize = window.AppWindow.Size;
        var beforeMiniPosition = window.AppWindow.Position;
        window.SetWindowMode(AppWindowPresenterKind.CompactOverlay); await Task.Delay(250);
        ((DispatcherTimer)type.GetField("miniHoverTimer", methods).GetValue(page)).Stop();
        type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false }); await Task.Delay(100);
        Check(window.IsMiniMode && window.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true, HasTitleBar: false }, "Mini mode is not a borderless topmost window.");
        Check(window.AppWindow.ClientSize.Width == 480 && window.AppWindow.ClientSize.Height == 270, "Mini video dimensions changed.");
        type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { true }); await Task.Delay(100);
        var brightnessFlyout = ((Button)page.FindName("PlayBtnBrightness")).Flyout;
        type.GetMethod("MiniBrightnessButton_Click", methods).Invoke(page, new object[] { null, null });
        await Task.Delay(150);
        type.GetMethod("ShowControl", methods).Invoke(page, new object[] { false });
        Check(brightnessFlyout.IsOpen && ((Grid)page.FindName("control")).Visibility == Visibility.Visible, "Brightness flyout did not hold the external toolbar open.");
        brightnessFlyout.Hide(); await Task.Delay(200);
        await ToolbarLayoutAsync(page);
        var railPixels = (int)Math.Round(window.MiniToolbarHeight * page.XamlRoot.RasterizationScale);
        Check(window.AppWindow.ClientSize.Width == 480 && window.AppWindow.ClientSize.Height == 270 + railPixels && Math.Abs(viewport.ActualHeight * page.XamlRoot.RasterizationScale - 270) <= 1, "Mini toolbar resized the video.");
        var miniWork = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        window.AppWindow.Move(new PointInt32(window.AppWindow.Position.X, miniWork.Y + miniWork.Height - 270)); await Task.Delay(150);
        Check(window.AppWindow.Position.Y == miniWork.Y + miniWork.Height - 270 && window.AppWindow.Size.Height == 270 + railPixels && window.AppWindow.Position.Y + window.AppWindow.Size.Height > miniWork.Y + miniWork.Height, "Mini toolbar moved above or pulled the video back into the work area.");
        type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false }); await Task.Delay(100);
        Check(window.AppWindow.Position.Y == miniWork.Y + miniWork.Height - 270 && window.AppWindow.ClientSize.Height == 270, "Mini video moved when hiding the downward rail.");
        SendMessage(window.Handle, 0x0231 /* WM_ENTERSIZEMOVE */, 0, 0);
        try
        {
            var target = new PointInt32(miniWork.X + miniWork.Width + 5000, miniWork.Y + miniWork.Height + 5000);
            window.AppWindow.Move(target); await Task.Delay(100);
            Check(window.IsWindowDragging && window.AppWindow.Position.X == target.X && window.AppWindow.Position.Y == target.Y, "Move loop still forced the window into the work area.");
        }
        finally { SendMessage(window.Handle, 0x0232 /* WM_EXITSIZEMOVE */, 0, 0); }
        Check(!window.IsWindowDragging && window.AppWindow.ClientSize.Width == 480 && window.AppWindow.ClientSize.Height == 270, "Move loop failed to restore mini dimensions.");
        var afterDrag = window.AppWindow.Position;
        var reachable = false;
        var dragDisplays = DisplayArea.FindAll();
        for (var displayIndex = 0; displayIndex < dragDisplays.Count; displayIndex++)
        {
            var area = dragDisplays[displayIndex].WorkArea;
            var overlapWidth = Math.Min(afterDrag.X + 480, area.X + area.Width) - Math.Max(afterDrag.X, area.X);
            var overlapHeight = Math.Min(afterDrag.Y + 270, area.Y + area.Height) - Math.Max(afterDrag.Y, area.Y);
            if (overlapWidth >= Math.Min(480, Math.Ceiling(48 * page.XamlRoot.RasterizationScale)) && overlapHeight >= Math.Min(270, Math.Ceiling(32 * page.XamlRoot.RasterizationScale))) reachable = true;
        }
        Check(reachable, "Move loop lost the reachable video grip.");
        type.GetMethod("Grid_DoubleTapped", methods).Invoke(page, new object[] { null, null }); await Task.Delay(200);
        Check(!window.IsMiniMode && window.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: false } && window.AppWindow.Size.Width == beforeMiniSize.Width && window.AppWindow.Size.Height == beforeMiniSize.Height && window.AppWindow.Position.X == beforeMiniPosition.X && window.AppWindow.Position.Y == beforeMiniPosition.Y, "Mini restore lost normal bounds or retained topmost: expected=" + beforeMiniPosition.X + "," + beforeMiniPosition.Y + " " + beforeMiniSize.Width + "x" + beforeMiniSize.Height + ", actual=" + window.AppWindow.Position.X + "," + window.AppWindow.Position.Y + " " + window.AppWindow.Size.Width + "x" + window.AppWindow.Size.Height + ", mini=" + window.IsMiniMode + ", topmost=" + ((OverlappedPresenter)window.AppWindow.Presenter).IsAlwaysOnTop);
        type.GetMethod("SetFullWindow", methods).Invoke(page, new object[] { true }); await Task.Delay(200);
        ((DispatcherTimer)type.GetField("miniHoverTimer", methods).GetValue(page)).Stop();
        type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false }); await Task.Delay(100);
        var expandedSize = new SizeInt32(window.AppWindow.ClientSize.Width,
            window.AppWindow.ClientSize.Height - (int)Math.Round(window.MiniToolbarHeight * page.XamlRoot.RasterizationScale));
        window.SetWindowMode(AppWindowPresenterKind.CompactOverlay); await Task.Delay(150);
        ((DispatcherTimer)type.GetField("miniHoverTimer", methods).GetValue(page)).Stop();
        type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false }); await Task.Delay(100);
        App.SetWindowSize("1600×900"); await Task.Delay(100);
        Check(window.AppWindow.ClientSize.Width == 480 && window.AppWindow.ClientSize.Height == 270, "Changing the preset enlarged mini mode.");
        App.SetWindowSize(savedPreset);
        window.SetWindowMode(AppWindowPresenterKind.FullScreen); await Task.Delay(100);
        Check(!window.IsMiniMode, "Mini flag leaked into fullscreen.");
        window.SetWindowMode(AppWindowPresenterKind.Default); await Task.Delay(200);
        ((DispatcherTimer)type.GetField("miniHoverTimer", methods).GetValue(page)).Stop();
        type.GetMethod("FinishToolbarVisibility", methods).Invoke(page, new object[] { false }); await Task.Delay(100);
        Check(!window.IsMiniMode && window.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: false } && window.AppWindow.ClientSize.Width == expandedSize.Width && window.AppWindow.ClientSize.Height - (int)Math.Round(window.MiniToolbarHeight * page.XamlRoot.RasterizationScale) == expandedSize.Height, "Mini/fullscreen switch lost video size or topmost state: expected=" + expandedSize.Width + "x" + expandedSize.Height + ", actual=" + window.AppWindow.ClientSize.Width + "x" + window.AppWindow.ClientSize.Height + ", rail=" + window.MiniToolbarHeight + ", mini=" + window.IsMiniMode + ", topmost=" + ((OverlappedPresenter)window.AppWindow.Presenter).IsAlwaysOnTop);
        type.GetMethod("SetFullWindow", methods).Invoke(page, new object[] { false }); await Task.Delay(200);
        var path = Path.Combine(AppPaths.EnsureDirectory(), "shortcut-test.wav");
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(16036); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(16000); writer.Write(new byte[16000]);
        }
        var media = (MediaPlayer)type.GetField("mediaPlayer", methods).GetValue(page);
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        using var source = Windows.Media.Core.MediaSource.CreateFromStorageFile(file);
        ((MediaPlayerElement)page.FindName("player")).SetMediaPlayer(media);
        try
        {
            media.IsMuted = true; media.IsLoopingEnabled = true; media.Source = source; media.Play();
            for (var wait = 0; wait < 30 && media.PlaybackSession.PlaybackState != MediaPlaybackState.Playing; wait++) await Task.Delay(100);
            Check(media.PlaybackSession.PlaybackState == MediaPlaybackState.Playing, "Local test media did not start.");
            page.TogglePause(); await Task.Delay(150);
            Check(media.PlaybackSession.PlaybackState == MediaPlaybackState.Paused, "Pause shortcut did not pause.");
            page.TogglePause(); await Task.Delay(150);
            Check(media.PlaybackSession.PlaybackState == MediaPlaybackState.Playing, "Pause shortcut did not resume.");
        }
        finally { media.Pause(); media.Source = null; media.IsMuted = false; media.IsLoopingEnabled = false; slider.Value = initialVolume; }
    }
    private static async Task PlayerGestureAndBrightnessAsync(Views.LiveRoomPage page)
    {
        const BindingFlags methods = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(Views.LiveRoomPage);
        var video = (Grid)page.FindName("VideoSurface");
        Check(!video.IsTapEnabled && !video.IsRightTapEnabled && !video.IsHoldingEnabled && video.ContextFlyout == null &&
            video.ManipulationMode == Microsoft.UI.Xaml.Input.ManipulationModes.None, "Video still exposes tap, right-click or slide gestures.");
        foreach (var removed in new[] { "Grid_Tapped", "Grid_PointerWheelChanged", "Grid_ManipulationDelta", "Grid_ManipulationStarted", "Grid_ManipulationCompleted", "AdvanceVideoDragGesture", "CancelVideoDragGesture" })
            Check(type.GetMethod(removed, methods) == null, "A removed gesture handler remains: " + removed);
        var brightness = (Slider)page.FindName("SliderBrightness");
        var shield = (Grid)page.FindName("BrightnessShield");
        var value = (TextBlock)page.FindName("BrightnessValue");
        var original = SettingHelper.GetValue(SettingHelper.PLAYER_BRIGHTNESS, 0d);
        try
        {
            SettingHelper.SetValue(SettingHelper.PLAYER_BRIGHTNESS, .25);
            type.GetMethod("LoadSetting", methods).Invoke(page, null);
            Check(brightness.Value == 75 && shield.Opacity == .25, "Legacy brightness was not preserved.");
            brightness.Value = 40;
            Check(Math.Abs(shield.Opacity - .6) < .001 && value.Text == "40%" &&
                Math.Abs(SettingHelper.GetValue(SettingHelper.PLAYER_BRIGHTNESS, 0d) - .6) < .001, "Brightness slider did not update/persist the dimming overlay.");
            brightness.Value = 100;
            Check(shield.Opacity == 0 && value.Text == "100%", "100% brightness changed the original video.");
            await ToolbarLayoutAsync(page);
        }
        finally
        {
            SettingHelper.SetValue(SettingHelper.PLAYER_BRIGHTNESS, original);
            type.GetMethod("LoadSetting", methods).Invoke(page, null);
        }
    }
    private static async Task ToolbarLayoutAsync(Views.LiveRoomPage page)
    {
        const BindingFlags methods = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(Views.LiveRoomPage);
        var video = (Grid)page.FindName("VideoSurface");
        var rail = (Grid)page.FindName("control");
        var oldTheme = page.RequestedTheme;
        var oldVideoWidth = video.Width; var oldRailWidth = rail.Width;
        var session = type.GetField("randomSession", methods);
        var oldSession = session.GetValue(page);
        try
        {
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            foreach (var random in new[] { false, true })
            {
                page.RequestedTheme = theme;
                session.SetValue(page, random ? new RandomBrowseSession(new YolanLive.Core.Models.LiveRoomItem { RoomID = "layout-only" }) : null);
                type.GetMethod("UpdateRandomBrowseControls", methods).Invoke(page, null);
                // Widths include the logical space available to a 480px mini at 100/150/200% DPI.
                foreach (var width in new[] { 1280d, 960d, 640d, 480d, 320d, 240d })
                {
                    video.Width = rail.Width = width;
                    page.UpdateLayout(); await Task.Delay(40);
                    type.GetMethod("UpdateToolbarDensity", methods).Invoke(page, null);
                    page.UpdateLayout();
                    var normal = ((Grid)page.FindName("StandardControl")).Visibility == Visibility.Visible;
                    if (normal)
                    {
                        var volume = (Button)page.FindName("PlayBtnVolume");
                        var brightness = (Button)page.FindName("PlayBtnBrightness");
                        var options = (StackPanel)page.FindName("PlaybackOptions");
                        var actions = (StackPanel)page.FindName("RoomActions");
                        var volumeX = volume.TransformToVisual(rail).TransformPoint(default).X;
                        var brightnessX = brightness.TransformToVisual(rail).TransformPoint(default).X;
                        var optionsX = options.TransformToVisual(rail).TransformPoint(default).X;
                        var actionsX = actions.TransformToVisual(rail).TransformPoint(default).X;
                        var play = (StackPanel)page.FindName("PlayControls");
                        var playRight = play.TransformToVisual(rail).TransformPoint(new Windows.Foundation.Point(play.ActualWidth, 0)).X;
                        Check(Math.Abs(volumeX - playRight - 24) <= 1, "Playback/audio gap changes with window width: " + width);
                        var optionFrame = (Border)page.FindName("QualityFrame");
                        var lineFrame = (Border)page.FindName("LineFrame");
                        Check(Math.Abs(optionFrame.ActualWidth - lineFrame.ActualWidth) <= 1, "Quality and line widths differ");
                        foreach (var name in new[] { "BottomBtnRefresh", "PlayerMoreButton" })
                        {
                            var action = (Button)page.FindName(name);
                            if (action.Visibility == Visibility.Visible)
                                Check(action.ActualWidth == 40 && action.ActualHeight == 40, "Action target is inconsistent: " + name);
                        }
                        Check(brightnessX >= volumeX + volume.ActualWidth && optionsX >= brightnessX + brightness.ActualWidth &&
                            actionsX >= optionsX + options.ActualWidth && actionsX + actions.ActualWidth <= width + 1,
                            "Toolbar groups overlap at " + width + " random=" + random);
                    }
                    else
                    {
                        var buttons = (FrameworkElement)page.FindName("MiniToolbarButtons");
                        Check(buttons.ActualWidth <= width - 16, "Compact toolbar is clipped at " + width);
                        Check(((Button)page.FindName("MiniBrightnessButton")).ActualWidth == 32, "Compact brightness button was scaled.");
                    }
                }
            }
        }
        finally
        {
            session.SetValue(page, oldSession); page.RequestedTheme = oldTheme;
            video.Width = oldVideoWidth; rail.Width = oldRailWidth;
            type.GetMethod("UpdateRandomBrowseControls", methods).Invoke(page, null); page.UpdateLayout();
        }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, uint attribute, out uint value, int size);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    private static void WindowDragPolicy()
    {
        var areas = new[] { new RectInt32(0, 0, 1920, 1040), new RectInt32(-1280, 0, 1280, 984) };
        foreach (var scale in new[] { 1d, 1.5d, 2d })
        {
            var video = new RectInt32(100, 770, 480, 270);
            var rail = MainWindow.GetMiniToolbarBounds(video, (int)Math.Round(44 * scale));
            Check(rail.X == video.X && rail.Y == video.Y && rail.Height == 270 + (int)Math.Round(44 * scale) && rail.Y + rail.Height > 1040, "Toolbar must expand down even beyond the screen.");
            var left = MainWindow.KeepReachable(new RectInt32(-900, 100, 480, 270), areas, scale);
            Check(left.X == -900 && left.Y == 100 && left.Width == 480 && left.Height == 270, "Cross-monitor placement was clamped to the primary display.");
            var bottomY = 1040 - (int)Math.Ceiling(32 * scale);
            var partial = MainWindow.KeepReachable(new RectInt32(1800, bottomY, 480, 270), areas, scale);
            Check(partial.X == 1800 && partial.Y == bottomY, "A reachable partially off-screen video was pulled fully inside.");
            var lost = MainWindow.KeepReachable(new RectInt32(9000, 9000, 480, 270), areas, scale);
            Check(lost.X == 1920 - (int)Math.Ceiling(48 * scale) && lost.Y == bottomY && lost.Width == 480 && lost.Height == 270, "Unreachable video did not retain only the required grip.");
        }
    }
}
