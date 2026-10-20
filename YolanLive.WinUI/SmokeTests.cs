using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YolanLive.WinUI.Helper;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using Windows.Graphics;
namespace YolanLive.WinUI;
internal static class SmokeTests
{
    public static async Task RunAsync()
    {
        var output = new List<string>();
        var failed = false;
        // Keep completed cases when a native component exits before the final report.
        void SaveProgress() => File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "smoke-test.log"), output);
        void Check(string name, Action action)
        {
            try { action(); output.Add("PASS " + name); }
            catch (Exception ex) { failed = true; output.Add("FAIL " + name + "\n" + ex); }
            finally { SaveProgress(); }
        }
        async Task CheckAsync(string name, Func<Task> action)
        {
            try { await action(); output.Add("PASS " + name); }
            catch (Exception ex) { failed = true; output.Add("FAIL " + name + "\n" + ex); }
            finally { SaveProgress(); }
        }
        var ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        Check("Published XAML resources", () =>
        {
            foreach (var file in new[] { "YolanLive.pri", "App.xbf", "MainWindow.xbf", "BaseFramePage.xbf", "MainPage.xbf", "Views/LiveRoomPage.xbf", "Controls/BiliLoginDialog.xbf" })
                if (!File.Exists(Path.Combine(AppContext.BaseDirectory, file))) throw new FileNotFoundException("Missing UI resource", file);
        });
        Check("YolanLive filenames and runtime dependency references", () =>
        {
            foreach (var file in new[] { "YolanLive.exe", "YolanLive.dll", "YolanLive.Core.dll", "YolanLive.pri", "YolanLive.deps.json", "YolanLive.runtimeconfig.json", "Assets/YolanLive.ico", "Assets/YolanLive.png" })
                if (!File.Exists(Path.Combine(AppContext.BaseDirectory, file))) throw new FileNotFoundException("Missing renamed runtime file", file);
            if (typeof(App).Assembly.GetName().Name != "YolanLive" || typeof(YolanLive.Core.BiliBili).Assembly.GetName().Name != "YolanLive.Core" ||
                typeof(App).Namespace != "YolanLive.WinUI" || typeof(YolanLive.Core.BiliBili).Namespace != "YolanLive.Core")
                throw new Exception("Legacy assembly filenames remained in the new output.");
        });
        await CheckAsync("Four-platform game browsing, badges, links and pin isolation", PlatformChecks.StructureAsync);
        await CheckAsync("Missing Huya and Douyu state is an error, not stopped", PlatformChecks.MissingStatusAsync);
        Check("Button", () => XamlReader.Load("<Button " + ns + ">Test</Button>"));
        Check("System font", () => XamlReader.Load("<FontIcon " + ns + " FontFamily=\"Segoe Fluent Icons\" Glyph=\"A\" />"));
        Check("Bundled font", () => XamlReader.Load("<FontIcon " + ns + " FontFamily=\"{StaticResource SegoeFluent}\" Glyph=\"A\" />"));
        Check("Image", () => XamlReader.Load("<Image " + ns + " Source=\"ms-appx:///Assets/YolanLive.png\" />"));
        Check("Pinned games: limit, order, removal, identity and platform isolation", BrowseChecks.Pins);
        await CheckAsync("Browse platform and group persistence without temporary reset", BrowseChecks.MemoryAsync);
        await CheckAsync("Search platform/status labels preserve original room data", BrowseChecks.SearchAsync);
        Check("Raw search status only labels explicitly known platform codes", BrowseChecks.SearchStatuses);
        await CheckAsync("Browse preferences restore in a new process", BrowseChecks.RestartAsync);
        Check("Update release versions, stable channel and package validation", UpdateChecks.Releases);
        await CheckAsync("Update package download, checksum, cache and failed-file cleanup", UpdateChecks.DownloadsAsync);
        await CheckAsync("Update states distinguish newest, available and request failures", UpdateChecks.StatesAsync);
        await CheckAsync("Light/dark theme contrast", async () =>
        {
            var root = App.ActiveWindow.Root;
            var originalTheme = root.RequestedTheme;
            var originalContent = root.Content;
            var surface = (Border)XamlReader.Load("<Border " + ns + " Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\" />");
            root.Content = surface;
            try
            {
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark, ElementTheme.Light })
                {
                    root.RequestedTheme = theme;
                    await Task.Delay(100);
                    var background = ((Microsoft.UI.Xaml.Media.SolidColorBrush)surface.Background).Color;
                    var foreground = ((Microsoft.UI.Xaml.Media.SolidColorBrush)root.Foreground).Color;
                    double Luminance(Windows.UI.Color color)
                    {
                        double Channel(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
                        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
                    }
                    var light = Math.Max(Luminance(background), Luminance(foreground));
                    var dark = Math.Min(Luminance(background), Luminance(foreground));
                    if (background.A != 255 || (light + .05) / (dark + .05) < 4.5)
                        throw new InvalidOperationException($"Unreadable {theme} theme: background={background}, foreground={foreground}");
                }
            }
            finally { root.RequestedTheme = originalTheme; root.Content = originalContent; }
        });
        foreach (var type in new[] { typeof(BaseFramePage), typeof(MainPage), typeof(Views.RecomendPage), typeof(Views.CategoryPage), typeof(Views.CategoryDetailPage), typeof(Views.SearchPage), typeof(Views.FavoritePage), typeof(Views.HistoryPage), typeof(Views.SettingsPage), typeof(Views.ParsePage), typeof(Views.LiveRoomPage), typeof(Controls.BiliLoginDialog) })
            Check(type.Name, () => Activator.CreateInstance(type));
        Check("FFmpeg desktop component", () => _ = new FFmpegInteropX.MediaSourceConfig());
        Check("YolanLive metadata and clean title bar", () =>
        {
            var page = new BaseFramePage();
            var title = (TextBlock)page.FindName("Title");
            var icon = (Image)page.FindName("AppIcon");
            var about = (TextBlock)new Views.SettingsPage().FindName("AboutAppName");
            var product = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "YolanLive.exe")).ProductName;
            if (App.ActiveWindow.Title != "游岚" || !string.IsNullOrEmpty(title.Text) || title.Visibility != Visibility.Collapsed ||
                icon.Visibility != Visibility.Collapsed || icon.Source != null || about.Text != "游岚" || product != "游岚" ||
                !File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets/YolanLive.ico")) ||
                !File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets/YolanLive.svg")))
                throw new Exception("Application metadata changed or home title bar still displays branding.");
            var titleGrid = (Grid)page.FindName("TitleBar");
            var appIcon = (Image)page.FindName("AppIcon");
            var navigation = (NavigationView)new MainPage().FindName("navigationView");
            if (titleGrid.ColumnDefinitions[0].Width.Value != navigation.CompactPaneLength ||
                appIcon.HorizontalAlignment != HorizontalAlignment.Center || appIcon.Margin != new Thickness(0) ||
                !title.FontFamily.Source.EndsWith("ChillRound.ttf#寒蝉全圆体") || title.CharacterSpacing != 35 ||
                (double)new MainPage().Resources["NavigationViewItemOnLeftIconBoxHeight"] != 20 ||
                !File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets/Fonts/ChillRound.ttf")) ||
                !File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets/Fonts/ChillRound-LICENSE.txt")))
                throw new Exception("Rounded wordmark, vector icon or navigation alignment was not preserved.");
        });
        await CheckAsync("Room title stays visible and home branding stays hidden", async () =>
        {
            var page = new BaseFramePage();
            var owner = App.OpenWindow(typeof(Page));
            try
            {
                owner.Root.Content = page; owner.Activate(); await Task.Delay(100);
                var frame = (Frame)page.FindName("MainFrame");
                var title = (TextBlock)page.FindName("Title");
                var icon = (Image)page.FindName("AppIcon");
                var navigated = typeof(BaseFramePage).GetMethod("MainFrame_Navigated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var room = new Views.LiveRoomPage();
                frame.Content = room;
                await Task.Delay(200);
                if (!room.IsLoaded) throw new Exception("Title fixture room did not load.");
                navigated.Invoke(page, new object[] { frame, null });
                MessageCenter.ChangeTitle("Test room - Test streamer", ViewModels.MainVM.Sites[0].LiveSite);
                var separateTitle = (TextBlock)room.FindName("Title");
                if (title.FontSize != 14 || title.FontWeight.Weight != 400 || icon.Width != 18 ||
                    title.Visibility != Visibility.Visible || icon.Visibility != Visibility.Visible || icon.Source == null ||
                    title.Text != "Test room - Test streamer" || title.TextTrimming != TextTrimming.CharacterEllipsis ||
                    (string)ToolTipService.GetToolTip(title) != title.Text || separateTitle.FontSize != 14 ||
                    separateTitle.FontWeight.Weight != 400 || ((Image)room.FindName("AppIcon")).Width != 18)
                    throw new Exception("Room title presentation is inconsistent.");
                frame.Content = null;
                await Task.Delay(200);
                if (room.IsLoaded) throw new Exception("Title fixture room did not unload.");
                navigated.Invoke(page, new object[] { frame, null });
                MessageCenter.ChangeTitle("Stale room title");
                if (!string.IsNullOrEmpty(title.Text) || title.Visibility != Visibility.Collapsed || icon.Visibility != Visibility.Collapsed ||
                    icon.Source != null || ((Grid)page.FindName("TitleBarGrid")).Visibility != Visibility.Visible || ((Grid)page.FindName("TitleBar")).Height != 32)
                    throw new Exception("Home branding reappeared or the title-bar drag area was removed.");
            }
            finally
            {
                owner.Root.Content = null;
                // WinUI queues Unloaded/native teardown; finish it before the next fixture creates controls.
                await Task.Delay(200);
                owner.Close();
                await Task.Delay(200);
            }
        });
        var invalidActiveWindow = App.ActiveWindow == null || !App.Windows.Any(w => ReferenceEquals(w, App.ActiveWindow));
        Check("Closing a secondary window keeps the active window registered", () =>
        {
            if (invalidActiveWindow) throw new Exception("ActiveWindow references a closed/unregistered window after secondary-window cleanup.");
        });
        if (invalidActiveWindow) { Environment.ExitCode = 1; return; } // A native getter on a closed Window is unsafe.
        Check("Native materials and solid content surface", () =>
        {
            if (App.ActiveWindow.SystemBackdrop is not Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop ||
                ((Microsoft.UI.Xaml.Media.SolidColorBrush)App.ActiveWindow.Root.Background).Color.A != 0)
                throw new Exception("Window backdrop is obscured.");
            var main = new MainPage();
            if (((Microsoft.UI.Xaml.Media.SolidColorBrush)((Frame)main.FindName("frame")).Background).Color.A != 255)
                throw new Exception("Content is translucent.");
            var room = new Views.LiveRoomPage();
            if (App.Current.Resources["PlayerToolbarSurfaceBrush"] is not Microsoft.UI.Xaml.Media.SolidColorBrush bar || bar.Color.A != 255 ||
                App.Current.Resources["PlayerControlsBrush"] is not Microsoft.UI.Xaml.Media.AcrylicBrush acrylic || acrylic.FallbackColor.A < 200)
                throw new Exception("Player material or readable fallback is missing.");
        });
        Check("Compact platform selectors and category labels", () =>
        {
            foreach (var page in new Page[] { new Views.RecomendPage(), new Views.CategoryPage() })
            {
                var picker = (ListView)page.FindName("PlatformPicker");
                if (picker.Items.Count != 4 || picker.HorizontalAlignment != HorizontalAlignment.Left ||
                    picker.ItemContainerStyle == null || picker.Items.Cast<ViewModels.Site>().Any(item => string.IsNullOrWhiteSpace(item.Label)))
                    throw new Exception("Platform selector is not compact or platforms are missing.");
            }
            var category = new Views.CategoryPage();
            var groups = (ListView)category.FindName("GroupPicker");
            if (groups.SelectionMode != ListViewSelectionMode.Single || !double.IsPositiveInfinity(groups.MaxHeight) || groups.ItemTemplate == null)
                throw new Exception("Category label list is missing or unbounded.");
        });
        Check("About version matches running executable", () =>
        {
            var frame = new Frame();
            frame.Navigate(typeof(Views.SettingsPage));
            var page = (Views.SettingsPage)frame.Content;
            var version = (Microsoft.UI.Xaml.Documents.Run)page.FindName("version");
            var actual = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "YolanLive.exe")).ProductVersion;
            if (version.Text != actual) throw new Exception("About page does not show the running executable version.");
        });
        Check("Desktop player loads without legacy Xbox controls", () =>
        {
            var page = new Views.LiveRoomPage();
            if (page.FindName("StandardControl") == null || page.FindName("XBoxSplitView") != null)
                throw new Exception("Desktop controls are missing or legacy Xbox UI remains.");
        });
        Check("Window preset validation and screen fitting", () =>
        {
            if (MainWindow.ValidateWindowSize("invalid") != "1280×720" || MainWindow.ValidateWindowSize(null) != "1280×720" || MainWindow.ValidateWindowSize("1280×800") != "1280×720")
                throw new Exception("Invalid size did not fall back.");
            foreach (var preset in MainWindow.WindowSizeOptions)
            foreach (var work in new[] { new RectInt32(0, 0, 1920, 1040), new RectInt32(-1200, 0, 1200, 700), new RectInt32(1920, 200, 800, 600) })
            {
                var size = MainWindow.GetPresetSize(preset);
                var bounds = MainWindow.FitBounds(size, new PointInt32(9999, -9999), work);
                if (bounds.X < work.X || bounds.Y < work.Y || bounds.X + bounds.Width > work.X + work.Width || bounds.Y + bounds.Height > work.Y + work.Height)
                    throw new Exception("Window lies outside work area.");
                if (Math.Abs((double)bounds.Width / bounds.Height - (double)size.Width / size.Height) > .01)
                    throw new Exception("Aspect ratio changed when fitting.");
            }
        });
        Check("Mini toolbar expands without moving or resizing the video", () =>
        {
            foreach (var scale in new[] { 1d, 1.5d, 2d })
            foreach (var y in new[] { 120, 730 })
            {
                var video = new RectInt32(-1100, y, 480, 270);
                var height = (int)Math.Round(44 * scale);
                var toolbar = MainWindow.GetMiniToolbarBounds(video, height);
                if (toolbar.Width != video.Width || toolbar.Height - height != video.Height || toolbar.X != video.X || toolbar.Y != video.Y)
                    throw new Exception("Downward toolbar changed the video bounds.");
                var collapsed = MainWindow.GetMiniToolbarBounds(video, 0);
                if (collapsed.Y != video.Y || collapsed.Height != video.Height)
                    throw new Exception("Mini toolbar did not collapse to the original video bounds.");
            }
        });
        Check("Playback viewport fitting with DPI and window chrome", () =>
        {
            foreach (var preset in MainWindow.WindowSizeOptions)
            foreach (var scale in new[] { 1d, 1.5d, 2d })
            foreach (var work in new[] { new RectInt32(0, 0, 1920, 1040), new RectInt32(-800, 100, 800, 600) })
            {
                var requested = MainWindow.GetPresetSize(preset);
                var chrome = new SizeInt32(16, 9 + (int)(80 * scale));
                var bounds = MainWindow.FitBounds(requested, new PointInt32(9999, -9999), work, chrome);
                var width = bounds.Width - chrome.Width;
                var height = bounds.Height - chrome.Height;
                if (bounds.X < work.X || bounds.Y < work.Y || bounds.X + bounds.Width > work.X + work.Width || bounds.Y + bounds.Height > work.Y + work.Height ||
                    width > requested.Width || height > requested.Height || Math.Abs((double)width / height - 16d / 9) > .006)
                    throw new Exception("Chrome or DPI changed the viewport ratio or exceeded the work area.");
            }
        });
        var window = App.ActiveWindow;
        var originalSize = window.SelectedWindowSize;
        async Task CheckWindow(string name, Action setup, Action verify)
        {
            try { setup(); await Task.Delay(200); verify(); output.Add("PASS " + name); }
            catch (Exception ex) { failed = true; output.Add("FAIL " + name + "\n" + ex); }
        }
        void CheckNormalSize()
        {
            var presenter = window.AppWindow.Presenter as OverlappedPresenter;
            var work = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var expected = MainWindow.FitBounds(MainWindow.GetPresetSize(window.SelectedWindowSize), window.AppWindow.Position, work, window.GetWindowChromeSize());
            if (presenter == null || presenter.IsResizable || presenter.IsMaximizable || window.AppWindow.Size.Width != expected.Width || window.AppWindow.Size.Height != expected.Height)
                throw new Exception($"Normal window did not restore/lock: {window.AppWindow.Size.Width}×{window.AppWindow.Size.Height}");
        }
        window.Activate();
        await CheckAsync("Update download survives settings navigation, cancels and requires manual install", () => UpdateChecks.BackgroundAsync(window));
        await CheckAsync("Merged source pagination, live filtering and favorite preservation", GameBrowseChecks.RunAsync);
        await CheckAsync("Recovery is bounded, stops offline and cancels stale operations", WatchingChecks.RecoveryAsync);
        Check("Login failure differs from network and platform failure", WatchingChecks.LoginErrors);
        await CheckAsync("Manage all favorites without live-status filtering", WatchingChecks.ManageFavoritesAsync);
        await CheckAsync("Saved window placement and missing-display fallback", WatchingChecks.WindowPlacementAsync);
        await CheckAsync("Browse scroll position survives leaving and returning", async () =>
        {
            var grid = new Controls.MyAdaptiveGridView { ItemsSource = Enumerable.Range(0, 80).Select(i => "Test " + i).ToList() };
            window.Root.Content = grid;
            await Task.Delay(250);
            var field = typeof(Controls.MyAdaptiveGridView).GetField("scrollViewer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var scroll = (ScrollViewer)field.GetValue(grid);
            scroll.ChangeView(null, 500, null, true);
            await Task.Delay(150);
            var offset = scroll.VerticalOffset;
            if (offset < 100) throw new Exception("Test list did not scroll.");
            window.Root.Content = new TextBlock();
            await Task.Delay(100);
            window.Root.Content = grid;
            await Task.Delay(250);
            if (Math.Abs(((ScrollViewer)field.GetValue(grid)).VerticalOffset - offset) > 1) throw new Exception("Returning reset the browse scroll position.");
        });
        await CheckAsync("Protected Douyin session persistence", async () =>
        {
            const string cookie = "sessionid=test-only-not-a-real-session; ttwid=anonymous-test";
            if (DouyinAccount.HasSession("ttwid=anonymous; x=sessionid=fake")) throw new Exception("Anonymous cookies were treated as login.");
            await DouyinAccount.Instance.SaveAsync(cookie, "Smoke test user agent");
            var saved = SettingHelper.GetValue(SettingHelper.DOUYIN_COOKIE, "");
            if (saved.Contains("sessionid") || saved.Contains("test-only")) throw new Exception("Login was stored in plaintext.");
            ((YolanLive.Core.Douyin)YolanLive.WinUI.ViewModels.MainVM.Sites.First(s => s.LiveSite is YolanLive.Core.Douyin).LiveSite).Cookie = "";
            await DouyinAccount.Instance.InitLoginInfo();
            if (!DouyinAccount.Instance.Logined) throw new Exception("Saved login did not reload.");
            DouyinAccount.Instance.Logout();
            if (DouyinAccount.Instance.Logined) throw new Exception("Logout did not clear login.");
        });
        foreach (var preset in MainWindow.WindowSizeOptions)
            await CheckWindow("Fixed preset " + preset, () => window.ApplyWindowSize(preset), CheckNormalSize);
        await CheckWindow("External resize cannot bypass fixed size", () => window.AppWindow.Resize(new SizeInt32(1100, 650)), CheckNormalSize);
        await CheckWindow("Maximize cannot bypass fixed size", () => ((OverlappedPresenter)window.AppWindow.Presenter).Maximize(), CheckNormalSize);
        await CheckWindow("Full screen uses display bounds", () => window.SetWindowMode(AppWindowPresenterKind.FullScreen), () =>
        {
            var display = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest).OuterBounds;
            if (window.AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen || window.AppWindow.Size.Width != display.Width || window.AppWindow.Size.Height != display.Height)
                throw new Exception("Full screen did not fill display.");
        });
        var fullSize = window.AppWindow.Size;
        await CheckWindow("Preset change during full screen is deferred", () => window.ApplyWindowSize("960×540"), () =>
        {
            if (window.AppWindow.Size.Width != fullSize.Width || window.AppWindow.Size.Height != fullSize.Height)
                throw new Exception("Preset changed full-screen bounds.");
        });
        await CheckWindow("Exit full screen restores latest preset", () => window.SetWindowMode(AppWindowPresenterKind.Default), CheckNormalSize);
        await CheckWindow("Fixed compact overlay", () => window.SetWindowMode(AppWindowPresenterKind.CompactOverlay), () =>
        {
            if (!window.IsMiniMode || window.AppWindow.Presenter is not OverlappedPresenter { IsAlwaysOnTop: true } || window.AppWindow.Size.Width != 480 || window.AppWindow.Size.Height != 270)
                throw new Exception($"Incorrect compact overlay: {window.AppWindow.Size.Width}×{window.AppWindow.Size.Height}");
        });
        await CheckWindow("Compact overlay cannot be externally resized", () => window.AppWindow.Resize(new SizeInt32(700, 400)), () =>
        {
            if (window.AppWindow.Size.Width != 480 || window.AppWindow.Size.Height != 270) throw new Exception("Compact overlay resized.");
        });
        await CheckWindow("Preset change during compact overlay is deferred", () => window.ApplyWindowSize("1600×900"), () =>
        {
            if (window.AppWindow.Size.Width != 480 || window.AppWindow.Size.Height != 270) throw new Exception("Preset changed compact overlay bounds.");
        });
        await CheckWindow("Exit compact overlay restores preset", () => window.SetWindowMode(AppWindowPresenterKind.Default), CheckNormalSize);
        var livePage = new Views.LiveRoomPage();
        await CheckWindow("Player uses proportional scaling", () => window.Root.Content = livePage, () =>
        {
            if (((MediaPlayerElement)livePage.FindName("player")).Stretch != Microsoft.UI.Xaml.Media.Stretch.Uniform)
                throw new Exception("Player stretches or crops video.");
        });
        Check("Player toolbar keeps occasional actions in More", () =>
        {
            var menu = (MenuFlyout)((Button)livePage.FindName("PlayerMoreButton")).Flyout;
            foreach (var name in new[] { "PlayTopBtnScreenshot", "BottomBtnShare", "BottomBtnOpenBrowser", "BottomBtnPlayUrl" })
                if (!menu.Items.Contains((MenuFlyoutItem)livePage.FindName(name))) throw new Exception("Action missing from More: " + name);
        });
        await CheckAsync("Player pause, volume, double-click and idle controls", async () =>
        {
            // Resize/presenter stress above must not become the initial state of an unrelated control check.
            var playerWindow = App.OpenWindow(typeof(Page));
            try
            {
                var playerFixture = new Views.LiveRoomPage(); playerWindow.Root.Content = playerFixture;
                playerWindow.Activate(); await Task.Delay(300);
                await WatchingChecks.PlayerControlsAsync(playerWindow, playerFixture);
            }
            finally { playerWindow.Close(); }
        });
        foreach (var preset in MainWindow.WindowSizeOptions)
            await CheckWindow("Playback viewport ratio " + preset, () => window.ApplyWindowSize(preset), () =>
            {
                var viewport = (MediaPlayerElement)livePage.FindName("player");
                var requested = MainWindow.GetPresetSize(preset);
                if (Math.Abs(viewport.ActualWidth / viewport.ActualHeight - (double)requested.Width / requested.Height) > .006)
                    throw new Exception($"Video area is {viewport.ActualWidth}×{viewport.ActualHeight}, expected ratio {requested.Width}:{requested.Height}.");
            });
        foreach (var mode in new[] { AppWindowPresenterKind.FullScreen, AppWindowPresenterKind.CompactOverlay, AppWindowPresenterKind.FullScreen, AppWindowPresenterKind.Default })
            await CheckWindow("Player controls follow " + mode, () => window.SetWindowMode(mode), () =>
            {
                var full = mode == AppWindowPresenterKind.FullScreen;
                var mini = mode == AppWindowPresenterKind.CompactOverlay;
                if (((MenuFlyoutItem)livePage.FindName("PlayBtnExitFullScreen")).Visibility != (full ? Visibility.Visible : Visibility.Collapsed) ||
                    (mini && ((Grid)livePage.FindName("MiniControl")).Visibility != Visibility.Visible))
                    throw new Exception("Player controls retained previous mode.");
                var row = (RowDefinition)livePage.FindName("BottomInfo");
                if ((full || mini) ? row.Height.Value != 0 : !row.Height.IsAuto)
                    throw new Exception("Room information did not hide/restore.");
                if (mode == AppWindowPresenterKind.Default)
                {
                    var viewport = (MediaPlayerElement)livePage.FindName("player");
                    if (Math.Abs(viewport.ActualWidth / viewport.ActualHeight - 16d / 9) > .006) throw new Exception("Normal playback ratio did not restore after mode changes.");
                }
            });
        foreach (var previewPage in new Page[] { new Views.RecomendPage(), new Views.CategoryPage() })
        {
            var platformTabs = (ListView)previewPage.FindName("PlatformPicker");
            ((Grid)previewPage.FindName("BrowseHeader")).Children.OfType<Border>().Single().Child = null;
            foreach (var width in new[] { 480d, 320d, 280d })
            foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                await CheckWindow("Four platform labels wrap at " + width + " in " + theme, () =>
                {
                    platformTabs.Width = width; platformTabs.RequestedTheme = theme;
                    window.Root.Content = platformTabs;
                }, () =>
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var container = (ListViewItem)platformTabs.ContainerFromIndex(i);
                        if (container == null) throw new Exception("Platform tab was not visible.");
                        var label = Descendants(container).OfType<TextBlock>().First(t => t.Text == ViewModels.MainVM.Sites[i].Label);
                        var origin = container.TransformToVisual(platformTabs).TransformPoint(new Windows.Foundation.Point());
                        if (origin.X < -1 || origin.X + container.ActualWidth > width + 1 || label.TextTrimming != TextTrimming.None)
                            throw new Exception("Platform label was clipped or ellipsized.");
                    }
                });
        }
        var home = new Views.RecomendPage();
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            var sampleText = new TextBlock { Text = "游戏直播与设置 1280×720" };
            var sampleButton = new Button { Content = "刷新" };
            var sampleInput = new TextBox { Text = "搜索主播" };
            var typography = new StackPanel { RequestedTheme = theme };
            typography.Children.Add(sampleText);
            typography.Children.Add(sampleButton);
            typography.Children.Add(sampleInput);
            await CheckWindow("Native YaHei UI controls in " + theme, () => window.Root.Content = typography, () =>
            {
                foreach (var font in new[] { sampleText.FontFamily, sampleButton.FontFamily, sampleInput.FontFamily })
                    if (font.Source != "Microsoft YaHei UI") throw new Exception("A control did not use the native interface font.");
                if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets/Fonts/ResourceHanRoundedCN-Regular.ttf")) ||
                    !File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets/Fonts/ResourceHanRounded-LICENSE.md")))
                    throw new Exception("Resource Han Rounded font or license was missing.");
            });
        }
        var cards = new Controls.MyAdaptiveGridView
        {
            ItemTemplate = (DataTemplate)App.Current.Resources["RoomItemTemplate"],
            ItemContainerStyle = ((Controls.MyAdaptiveGridView)home.FindName("RoomsGrid")).ItemContainerStyle,
            ItemsSource = new[] { new YolanLive.Core.Models.LiveRoomItem { Title = "游戏直播", UserName = "主播", Cover = "ms-appx:///Assets/YolanLive.png" } }
        };
        Image FindCover(DependencyObject parent)
        {
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index);
                if (child is Image image) return image;
                if (FindCover(child) is Image nested) return nested;
            }
            return null;
        }
        foreach (var width in new[] { 960d, 640d, 480d })
            await CheckWindow("Room cover ratio at width " + width, () =>
            {
                cards.Width = width;
                window.Root.Content = cards;
            }, () =>
            {
                var cover = FindCover(cards.ContainerFromIndex(0));
                var surface = cover == null ? null : (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(cover));
                if (surface == null || surface.ActualHeight <= 0 || Math.Abs(surface.ActualWidth / surface.ActualHeight - 16d / 9) > .02)
                    throw new Exception($"Room cover surface is {surface?.ActualWidth}×{surface?.ActualHeight}.");
            });
        var categoryPage = new Views.CategoryPage();
        var sourceGroups = (ListView)categoryPage.FindName("GroupPicker");
        var sourceGames = (GridView)categoryPage.FindName("GamesGrid");
        var groupItems = new[] { "网游", "手游", "单机游戏", "游戏赛事" }.Select(name => new YolanLive.Core.Models.LiveCategory { Name = name }).ToArray();
        var groupsPreview = new ListView { Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 8), ItemsPanel = sourceGroups.ItemsPanel, ItemTemplate = sourceGroups.ItemTemplate, ItemContainerStyle = sourceGroups.ItemContainerStyle, ItemsSource = groupItems, SelectedItem = groupItems[0] };
        var gamesPreview = new Controls.MyAdaptiveGridView { DesiredWidth = 160, Padding = new Thickness(0), Margin = new Thickness(-8,0,-8,-8), ItemTemplate = sourceGames.ItemTemplate, ItemContainerStyle = sourceGames.ItemContainerStyle, ItemsSource = new[] { new YolanLive.Core.Models.LiveGameCategory { Name = "策略卡牌" }, new YolanLive.Core.Models.LiveGameCategory { Name = "单机游戏" } } };
        var categoryPreview = new Grid { Padding = new Thickness(24) };
        categoryPreview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        categoryPreview.RowDefinitions.Add(new RowDefinition());
        categoryPreview.Children.Add(groupsPreview);
        Grid.SetRow(gamesPreview, 1); categoryPreview.Children.Add(gamesPreview);
        T FindVisual<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index);
                if (child is T match) return match;
                if (FindVisual<T>(child) is T nested) return nested;
            }
            return null;
        }
        System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        await CheckWindow("Room card rounded cover and full-title tooltip", () =>
        {
            cards.ItemsSource = new[] { new YolanLive.Core.Models.LiveRoomItem { Title = "很长的游戏直播标题用于检查省略和全文提示", UserName = "主播", Cover = "ms-appx:///Assets/YolanLive.png" } };
            window.Root.Content = cards;
        }, () =>
        {
            var container = (GridViewItem)cards.ContainerFromIndex(0);
            var descendants = Descendants(container).ToArray();
            var title = descendants.OfType<TextBlock>().Single(t => t.Text == "很长的游戏直播标题用于检查省略和全文提示");
            if (ToolTipService.GetToolTip(title)?.ToString() != title.Text || title.TextTrimming != TextTrimming.CharacterEllipsis ||
                !descendants.OfType<Border>().Any(b => b.CornerRadius.TopLeft == 10 && b.CornerRadius.TopRight == 10 && b.CornerRadius.BottomLeft == 10 && b.CornerRadius.BottomRight == 10))
                throw new Exception("Room cover corners or full-title tooltip are missing.");
        });
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            await CheckWindow("Search badges render with known/unknown status in " + theme, () =>
            {
                cards.RequestedTheme = theme;
                cards.Width = 800;
                cards.ItemsSource = new YolanLive.Core.Models.LiveRoomItem[] {
                    new Models.SearchRoomItem(new() { Title = "开播", UserName = "主播", IsLive = true }, ViewModels.MainVM.Sites[0].LiveSite),
                    new Models.SearchRoomItem(new() { Title = "停播", UserName = "主播", IsLive = false }, ViewModels.MainVM.Sites[1].LiveSite),
                    new Models.SearchRoomItem(new() { Title = "未知", UserName = "主播" }, ViewModels.MainVM.Sites[1].LiveSite) };
                window.Root.Content = cards;
            }, () =>
            {
                for (var i = 0; i < 3; i++)
                {
                    var texts = Descendants((GridViewItem)cards.ContainerFromIndex(i)).OfType<TextBlock>().ToArray();
                    if (!texts.Any(t => t.Text == (i == 0 ? "哔哩哔哩" : "抖音") && t.Visibility == Visibility.Visible) ||
                        (i < 2 && !texts.Any(t => t.Text == (i == 0 ? "直播中" : "未开播") && t.Visibility == Visibility.Visible)) ||
                        (i == 2 && texts.Any(t => t.Text == "直播中" || t.Text == "未开播")))
                        throw new Exception("Search badge/state rendering is incorrect.");
                }
            });
        var shortcutHome = new Views.RecomendPage();
        var pinSection = (StackPanel)shortcutHome.FindName("PinnedGamesSection");
        var pinsPreview = (ListView)shortcutHome.FindName("PinnedGamesList");
        ((StackPanel)((Grid)shortcutHome.Content).Children[0]).Children.Remove(pinSection);
        var pinSite = ViewModels.MainVM.BrowseSite;
        SettingHelper.SetValue(BrowsePreferences.PinsKey(pinSite), new List<PinnedGame>());
        Check("Empty shortcut row is hidden", () =>
        {
            shortcutHome.RefreshPinnedGames();
            if (pinSection.Visibility != Visibility.Collapsed) throw new Exception("Empty shortcuts occupy the homepage.");
        });
        for (var i = 0; i < 6; i++) BrowsePreferences.TogglePin(pinSite, BrowseChecks.Game(pinSite, "preview-" + i, "游戏 " + i));
        foreach (var width in new[] { 960d, 480d, 280d })
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            await CheckWindow("Pinned games wrap and native menus at width " + width + " in " + theme, () =>
            {
                shortcutHome.RefreshPinnedGames();
                pinSection.Width = width; pinSection.RequestedTheme = theme;
                window.Root.Content = pinSection;
            }, () =>
            {
                var list = pinsPreview;
                if (pinSection.Visibility != Visibility.Visible || list.Items.Count != 6) throw new Exception("Pinned shortcuts missing.");
                for (var i = 0; i < 6; i++)
                {
                    var container = (ListViewItem)list.ContainerFromIndex(i);
                    var text = FindVisual<TextBlock>(container);
                    var border = (Border)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(text);
                    var bottom = container.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point(0, container.ActualHeight)).Y;
                    if (container.ContextFlyout is not MenuFlyout menu || ((MenuFlyoutItem)menu.Items[0]).Text != "取消固定" ||
                        border.CornerRadius.TopLeft != 8 || text.Text != "游戏 " + i || ToolTipService.GetToolTip(border)?.ToString() != text.Text ||
                        text.TextAlignment != TextAlignment.Center || bottom > list.ActualHeight + 1)
                        throw new Exception("Shortcut wrap, corner or keyboard context menu is incorrect.");
                }
            });
        SettingHelper.SetValue(BrowsePreferences.PinsKey(pinSite), new List<PinnedGame>());
        ((Grid)categoryPage.Content).Children.Remove(sourceGames);
        await CheckWindow("Game category exposes native pin context menu", () =>
        {
            sourceGames.ItemsSource = new[] { BrowseChecks.Game(pinSite, "context-test") };
            window.Root.Content = sourceGames;
        }, () =>
        {
            if (((GridViewItem)sourceGames.ContainerFromIndex(0)).ContextFlyout is not MenuFlyout menu || menu.Items.Count != 1)
                throw new Exception("Category card has no keyboard/right-click pin menu.");
        });
        await CheckWindow("Category selected surface alignment and centered labels", () => window.Root.Content = categoryPreview, () =>
        {
            var groupContainer = (ListViewItem)groupsPreview.ContainerFromIndex(0);
            var presenter = FindVisual<Microsoft.UI.Xaml.Controls.Primitives.ListViewItemPresenter>(groupContainer);
            var gameContainer = (GridViewItem)gamesPreview.ContainerFromIndex(0);
            var label = Descendants(gameContainer).OfType<TextBlock>().Single(t => t.Text == "策略卡牌");
            var card = (Border)gameContainer.ContentTemplateRoot;
            var left = presenter.TransformToVisual(categoryPreview).TransformPoint(new Windows.Foundation.Point()).X;
            var cardLeft = card.TransformToVisual(categoryPreview).TransformPoint(new Windows.Foundation.Point()).X;
            if (Math.Abs(left - cardLeft) > 1 || presenter.CornerRadius.TopLeft != 8 || card.CornerRadius.TopLeft != 10 ||
                label.TextAlignment != TextAlignment.Center || label.VerticalAlignment != VerticalAlignment.Center)
                throw new Exception($"Category surface mismatch: group left={left}, card left={cardLeft}, group radius={presenter.CornerRadius.TopLeft}, card radius={card.CornerRadius.TopLeft}, text={label.TextAlignment}/{label.VerticalAlignment}.");
        });
        foreach (var groupWidth in new[] { 520d, 280d })
            await CheckWindow("Full game-group labels at width " + groupWidth, () =>
            {
                groupsPreview.Width = groupWidth;
                groupsPreview.HorizontalAlignment = HorizontalAlignment.Left;
            }, () =>
            {
                foreach (var item in groupItems)
                {
                    var container = (ListViewItem)groupsPreview.ContainerFromItem(item);
                    var text = FindVisual<TextBlock>(container);
                    var measured = new TextBlock { Text = item.Name, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight };
                    measured.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (text.Text != item.Name || text.TextTrimming != TextTrimming.None || text.ActualWidth + 1 < measured.DesiredSize.Width)
                        throw new Exception("A game-group label is clipped: " + item.Name);
                    var bottom = container.TransformToVisual(groupsPreview).TransformPoint(new Windows.Foundation.Point(0, container.ActualHeight)).Y;
                    if (bottom > groupsPreview.ActualHeight + 1) throw new Exception("A wrapped game-group label is hidden.");
                }
            });
        var settings = new Views.SettingsPage();
        foreach (var logicalWidth in new[] { 1280d, 960d, 640d, 480d })
            await CheckWindow("Settings layout width " + logicalWidth, () =>
            {
                settings.Width = logicalWidth;
                window.Root.Content = settings;
            }, () =>
            {
                if (((StackPanel)settings.FindName("SettingsCards")).Children.OfType<Border>().Count() != 4 || settings.SettingRows.Count() != 12)
                    throw new Exception("Settings groups or rows are missing.");
                var content = (StackPanel)settings.FindName("SettingsCards");
                var expectedWidth = Math.Min(1040, logicalWidth - 2 * (logicalWidth < 640 ? 16 : 24));
                if (Math.Abs(content.ActualWidth - expectedWidth) > 3)
                    throw new Exception($"Settings content shrank to {content.ActualWidth}, expected {expectedWidth}.");
                foreach (var grid in settings.SettingRows)
                {
                    if (grid.Children.Count < 2 || grid.Visibility == Visibility.Collapsed) continue;
                    if (grid.ActualWidth <= 0) throw new Exception("Settings card was not laid out.");
                    if (grid.ActualWidth < 640 && Grid.GetRow((FrameworkElement)grid.Children[1]) != 1)
                        throw new Exception("Narrow settings action overlaps label.");
                    if (grid.ActualWidth >= 640 && (Grid.GetRow((FrameworkElement)grid.Children[1]) != 0 || Grid.GetColumn((FrameworkElement)grid.Children[1]) != 1))
                        throw new Exception("Wide settings action did not stay beside its label.");
                }
            });
        MainWindow second = null;
        await CheckWindow("Size selection updates all open windows and saved value", () =>
        {
            second = App.OpenWindow(typeof(Views.SettingsPage));
            ((ComboBox)settings.FindName("cbWindowSize")).SelectedItem = "1600×900";
            ((ComboBox)settings.FindName("cbWindowSize")).SelectedItem = "1280×720";
        }, () =>
        {
            if (MainWindow.GetSavedWindowSize() != "1280×720" || window.SelectedWindowSize != "1280×720" || second.SelectedWindowSize != "1280×720")
                throw new Exception("Selection did not update saved value and both windows.");
            CheckNormalSize();
            var expected = MainWindow.FitBounds(MainWindow.GetPresetSize(second.SelectedWindowSize), second.AppWindow.Position,
                DisplayArea.GetFromWindowId(second.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea, second.GetWindowChromeSize());
            if (second.AppWindow.Size.Width != expected.Width || second.AppWindow.Size.Height != expected.Height)
                throw new Exception("Second window did not resize.");
        });
        second?.Close();
        await CheckWindow("New window inherits saved preset", () => second = App.OpenWindow(typeof(Views.SettingsPage)), () =>
        {
            if (second.SelectedWindowSize != "1280×720") throw new Exception("New window ignored saved size.");
        });
        second?.Close();
        SettingHelper.SetValue(SettingHelper.NEW_WINDOW_LIVEROOM, true);
        await CheckWindow("Independent player enters compact overlay", () =>
        {
            second = App.OpenWindow(typeof(Views.SettingsPage));
            second.Root.Content = new Views.LiveRoomPage();
        }, () => second.SetWindowMode(AppWindowPresenterKind.CompactOverlay));
        await CheckWindow("Closing compact player leaves main window unchanged", () =>
        {
            window.SetWindowMode(AppWindowPresenterKind.FullScreen);
            second.Close();
        }, () =>
        {
            if (window.AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen)
                throw new Exception("Closing independent player changed main window mode.");
        });
        window.SetWindowMode(AppWindowPresenterKind.Default);
        window.ApplyWindowSize(originalSize);
        await CheckAsync("Douyin login browser and logout cookie cleanup", async () =>
        {
            var dialog = new Controls.DouyinLoginDialog { XamlRoot = window.Root.XamlRoot };
            if (Controls.DouyinLoginDialog.IsTrusted("https://douyin.com.attacker.invalid/") || !Controls.DouyinLoginDialog.IsTrusted("https://live.douyin.com/"))
                throw new Exception("Login navigation origin check failed.");
            var showing = dialog.ShowAsync().AsTask();
            try
            {
                for (var attempt = 0; attempt < 80 && !dialog.IsPrimaryButtonEnabled; attempt++) await Task.Delay(100);
                if (!dialog.IsPrimaryButtonEnabled) throw new Exception("WebView2 login browser did not initialize.");
                var manager = dialog.Browser.CoreWebView2.CookieManager;
                if (!dialog.Browser.CoreWebView2.IsMuted) throw new Exception("Login browser may output preview audio.");
                manager.AddOrUpdateCookie(manager.CreateCookie("sessionid", "test-only-not-a-real-session", ".douyin.com", "/"));
                await dialog.SaveSessionAsync();
                if (!DouyinAccount.Instance.Logined) throw new Exception("Browser session was not saved.");
            }
            finally { dialog.Hide(); await showing; }
            DouyinAccount.Instance.Logout();
            await Controls.DouyinLoginDialog.ClearCookiesAsync();
        });
        await CheckAsync("Independent Huya and Douyu login protection and logout", () => PlatformChecks.LoginAsync(window));
        var reportDirectory = Directory.CreateDirectory(AppPaths.DataDirectory).FullName;
        File.WriteAllLines(Path.Combine(reportDirectory, "smoke-test.log"), output);
        Environment.ExitCode = failed ? 1 : 0;
    }
}
