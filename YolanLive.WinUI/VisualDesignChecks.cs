using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace YolanLive.WinUI;

internal static class VisualDesignChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Children(DependencyObject value)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++)
        {
            var child = VisualTreeHelper.GetChild(value, i); yield return child;
            foreach (var descendant in Children(child)) yield return descendant;
        }
    }
    private static double Contrast(Windows.UI.Color a, Windows.UI.Color b)
    {
        double L(Windows.UI.Color c)
        {
            double S(byte v) { var x = v / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
            return .2126 * S(c.R) + .7152 * S(c.G) + .0722 * S(c.B);
        }
        return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
    }
    internal static async Task RunAsync(MainWindow window)
    {
        var report = new List<string>();
        var folder = AppPaths.EnsureDirectory();
        var failed = false;
        async Task Case(string name, Func<Task> check)
        {
            try { await check(); report.Add("PASS " + name); }
            catch (Exception ex) { failed = true; report.Add("FAIL " + name + "\n" + ex); }
            File.WriteAllLines(Path.Combine(folder, "visual-checks.log"), report);
        }
        async Task Show(FrameworkElement element)
        {
            window.Root.Content = element; window.Activate(); await Task.Delay(200); element.UpdateLayout();
        }
        async Task Capture(FrameworkElement element, string name)
        {
            var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            var dir = await StorageFolder.GetFolderFromPathAsync(folder);
            var file = await dir.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
        }
        await Case("shared typography roles and native font sizes", async () =>
        {
            foreach (var spec in new[] { ("BodyTextStyle",14d,22d), ("SecondaryTextStyle",13d,20d), ("NavigationTextStyle",14d,20d), ("SectionTitleStyle",16d,24d), ("PageTitleStyle",22d,30d) })
            {
                var text = new TextBlock { Style = (Style)App.Current.Resources[spec.Item1], Text = "游岚 游戏直播 YolanLive" };
                Check(text.FontSize == spec.Item2 && text.LineHeight == spec.Item3 && text.FontWeight.Weight == 400, "Incorrect text role " + spec.Item1);
                Check(text.RenderTransform is not ScaleTransform, "Text must not be scaled");
            }
            await Task.CompletedTask;
        });
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        await Case("merged browser header, sidebar and native caption clearance " + theme, async () =>
        {
            var chrome = new BaseFramePage();
            var outer = (Frame)chrome.FindName("MainFrame");
            var shell = new MainPage();
            var navigation = (NavigationView)shell.FindName("navigationView");
            var inner = (Frame)shell.FindName("frame");
            var category = new Views.CategoryPage();
            var vm = (CategoryVM)typeof(Views.CategoryPage).GetField("categoryVM", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(category);
            for (var i = 0; i < 24; i++)
            {
                var game = BrowseChecks.Game(MainVM.BrowseSite, "header-" + i, i == 0 ? "三角洲行动" : "游戏 " + i);
                game.Pic = "ms-appx:///Assets/YolanLive.png";
                vm.Items.Add(game);
            }
            var groups = (ListView)category.FindName("GroupPicker");
            var sampleGroups = new[] { new LiveCategory { Name = "网游" }, new LiveCategory { Name = "手游" }, new LiveCategory { Name = "单机游戏" }, new LiveCategory { Name = "游戏赛事" } };
            inner.Content = category; outer.Content = shell;
            chrome.RequestedTheme = theme;
            await Show(chrome);
            inner.Content = category;
            await Task.Delay(250); chrome.RefreshChrome();
            groups.ItemsSource = sampleGroups;
            await Task.Delay(100);
            foreach (var preset in new[] { "960×540", "1280×720" })
            foreach (var expanded in new[] { false, true })
            {
                window.ApplyWindowSize(preset);
                navigation.IsPaneOpen = expanded;
                await Task.Delay(350); chrome.UpdateLayout();
                var bar = (TitleBar)category.FindName("BrowserTitleBar");
                var header = (Grid)category.FindName("BrowseHeader");
                var refresh = (Button)category.FindName("HeaderRefresh");
                var platform = (ListView)category.FindName("PlatformPicker");
                var root = (Grid)category.Content;
                var search = (AutoSuggestBox)category.FindName("HeaderSearch");
                var visibleSearch = search.Visibility == Visibility.Visible;
                if (visibleSearch)
                {
                    var input = Children(search).OfType<TextBox>().FirstOrDefault();
                    Check(navigation.AutoSuggestBox == null && input != null, "Top search duplicated the sidebar input");
                    Check(search.ActualHeight == 38 && input.CornerRadius.TopLeft == 19 && input.TextAlignment == TextAlignment.Left,
                        "Top search must retain its native rounded input and left alignment");
                    var searchRight = search.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(search.ActualWidth, 0)).X;
                    var refreshLeft = refresh.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point()).X;
                    Check(searchRight + 11 <= refreshLeft, "Search and refresh overlap or lose their group spacing");
                }
                else Check(ReferenceEquals(navigation.AutoSuggestBox, shell.FindName("searchBox")), "Narrow layout lost its sidebar search fallback");
                var nativeHeader = Children(bar).OfType<Grid>().FirstOrDefault(g => g.Name == "PART_LayoutRoot");
                Check(nativeHeader != null && nativeHeader.ActualHeight >= header.ActualHeight,
                    "Native titlebar clips the taller browser header");
                Check(navigation.OpenPaneLength == 188, "Expanded sidebar differs from selected layout");
                Check(((Grid)chrome.FindName("TitleBarGrid")).Visibility == Visibility.Collapsed && Grid.GetRow(outer) == 0, "Old blank title row remains");
                Check(root.Padding.Top == 0 && bar.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point()).Y <= 1,
                    "Browser header does not reach the top: padding=" + root.Padding + "; y=" + bar.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point()).Y);
                var rightInset = window.AppWindow.TitleBar.RightInset / chrome.XamlRoot.RasterizationScale;
                var refreshRight = refresh.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(refresh.ActualWidth, 0)).X;
                var nativeContent = Children(bar).OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                var contentRight = nativeContent.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(nativeContent.ActualWidth, 0)).X;
                Check(refreshRight <= contentRight + .5, "Native header clips refresh: preset=" + preset + "; expanded=" + expanded + "; refreshRight=" + refreshRight + "; contentRight=" + contentRight);
                Check(refreshRight <= chrome.ActualWidth - rightInset + 1, "Refresh overlaps system caption buttons");
                var gameGrid = (GridView)category.FindName("GamesGrid");
                var first = gameGrid.ContainerFromIndex(0) as GridViewItem;
                Check(first != null, "No game card in integrated layout");
                var card = (FrameworkElement)first.ContentTemplateRoot;
                Check(Math.Abs(card.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point()).X - header.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point()).X) <= 1, "Header and game cards no longer align");
                var firstY = first.TransformToVisual(gameGrid).TransformPoint(new Windows.Foundation.Point()).Y;
                var row = Enumerable.Range(0, gameGrid.Items.Count).Select(i => gameGrid.ContainerFromIndex(i) as GridViewItem)
                    .Where(item => item != null && Math.Abs(item.TransformToVisual(gameGrid).TransformPoint(new Windows.Foundation.Point()).Y - firstY) < 1).ToArray();
                var rowRight = row.Max(item => item.TransformToVisual(gameGrid).TransformPoint(new Windows.Foundation.Point(item.ActualWidth, 0)).X);
                Check(gameGrid.ActualWidth - rowRight <= 24, "Category grid leaves an unused column: width=" + gameGrid.ActualWidth + "; right=" + rowRight + "; panel=" + gameGrid.ItemsPanelRoot.ActualWidth);
                for (var i = 0; i < 4; i++) Check(platform.ContainerFromIndex(i) != null, "A platform label is hidden");
                Check(refresh.Focus(FocusState.Keyboard), "Header actions lost keyboard access");
                var pointer = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id);
                var pass = pointer.GetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough);
                var caption = pointer.GetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Caption);
                var scale = chrome.XamlRoot.RasterizationScale;
                bool IsInteractive(Windows.Foundation.Point point)
                {
                    bool Contains(Windows.Graphics.RectInt32 r) => point.X * scale >= r.X && point.X * scale < r.X + r.Width && point.Y * scale >= r.Y && point.Y * scale < r.Y + r.Height;
                    return !caption.Any(Contains) || pass.Any(Contains);
                }
                var center = refresh.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(refresh.ActualWidth / 2, refresh.ActualHeight / 2));
                Check(IsInteractive(center), "Native drag region intercepts refresh clicks: caption=" + string.Join(";", caption.Select(r => $"{r.X},{r.Y},{r.Width},{r.Height}")) + "; center=" + center);
                if (visibleSearch)
                {
                    var searchCenter = search.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(search.ActualWidth / 2, search.ActualHeight / 2));
                    Check(IsInteractive(searchCenter) && search.Focus(FocusState.Keyboard), "Top search is intercepted by window dragging or cannot receive keyboard focus");
                }
                if (((Border)category.FindName("PlatformSurface")).Parent == header)
                {
                    var firstTab = (ListViewItem)platform.ContainerFromIndex(0);
                    var tabCenter = firstTab.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(firstTab.ActualWidth / 2, firstTab.ActualHeight / 2));
                    Check(IsInteractive(tabCenter), "Native drag region intercepts platform selection");
                }
                await Capture(chrome, "merged-browser-" + theme + "-" + preset.Replace("×", "x") + (expanded ? "-expanded" : "") + ".png");
            }
            window.Root.Content = null; await Task.Delay(150);
            Check(!((TitleBar)category.FindName("BrowserTitleBar")).AutoRefreshDragRegions, "Inactive cached header still listens for layout changes");
        });
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        foreach (var pageType in new[] { typeof(Views.FavoritePage), typeof(Views.HistoryPage) })
        await Case("consistent top search and action overflow " + pageType.Name + " " + theme, async () =>
        {
            var chrome = new BaseFramePage { RequestedTheme = theme };
            var shell = new MainPage();
            ((Frame)chrome.FindName("MainFrame")).Content = shell;
            var page = (Page)Activator.CreateInstance(pageType);
            ((Frame)shell.FindName("frame")).Content = page;
            await Show(chrome);
            // Initial NavigationView loading selects Home; mount the target after that completes.
            ((Frame)shell.FindName("frame")).Content = page;
            await Task.Delay(200); chrome.RefreshChrome();
            var navigation = (NavigationView)shell.FindName("navigationView");
            foreach (var preset in new[] { "1280×720", "960×540", "640×360" })
            foreach (var expanded in new[] { false, true })
            {
                window.ApplyWindowSize(preset); navigation.IsPaneOpen = expanded;
                await Task.Delay(250); chrome.UpdateLayout(); chrome.RefreshChrome();
                var bar = (TitleBar)page.FindName("BrowserTitleBar");
                var search = (AutoSuggestBox)page.FindName("HeaderSearch");
                var actions = (StackPanel)page.FindName("HeaderActions");
                var native = Children(bar).OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                var actionRight = actions.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(actions.ActualWidth, 0)).X;
                var nativeRight = native.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(native.ActualWidth, 0)).X;
                Check(actionRight <= nativeRight + .5, "Account/history actions are clipped: " + preset);
                if (search.Visibility == Visibility.Visible)
                {
                    var searchRight = search.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point(search.ActualWidth, 0)).X;
                    var actionLeft = actions.TransformToVisual(chrome).TransformPoint(new Windows.Foundation.Point()).X;
                    Check(searchRight + 11 <= actionLeft && navigation.AutoSuggestBox == null && search.ActualHeight == 38,
                        "Top search overlaps actions or duplicates sidebar input: " + preset);
                    Check(search.Focus(FocusState.Keyboard), "Unified search cannot receive keyboard focus");
                }
                else Check(ReferenceEquals(navigation.AutoSuggestBox, shell.FindName("searchBox")), "Narrow page lost its search fallback");
                if (page.FindName("ManageFavorites") is Button manage)
                    Check((manage.Visibility == Visibility.Visible) != (((MenuFlyoutItem)page.FindName("ManageFavoritesOverflow")).Visibility == Visibility.Visible),
                        "Favorite management must have one available entrance");
                if (preset == "960×540" && !expanded) await Capture(chrome, pageType.Name + "-" + theme + "-960x540.png");
            }
            window.Root.Content = null; await Task.Delay(100);
        });
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        foreach (var width in new[] { 280d,480d,640d,960d,1280d })
            await Case("category alignment, wrapped platform labels and 4:3 art " + theme + " width=" + width, async () =>
            {
                var page = new Views.CategoryPage(); var root = (Grid)page.Content;
                root.Width = width; root.HorizontalAlignment = HorizontalAlignment.Left; root.RequestedTheme = theme;
                var games = (GridView)page.FindName("GamesGrid");
                var platform = (ListView)page.FindName("PlatformPicker");
                var header = (Grid)page.FindName("BrowseHeader");
                games.ItemsSource = new[] { BrowseChecks.Game(MainVM.Sites[0], "design-1", "三角洲行动"), BrowseChecks.Game(MainVM.Sites[0], "design-2", "暗区突围：无限与较长名称") };
                page.Content = null; await Show(root);
                Check(root.Padding.Left == (width < 640 ? 16 : 24), "Wrong responsive inset");
                for (var i = 0; i < 4; i++)
                {
                    var container = platform.ContainerFromIndex(i) as ListViewItem;
                    Check(container != null, "Missing platform label");
                    var label = Children(container).OfType<TextBlock>().First(t => t.Text == MainVM.Sites[i].Label);
                    var bounds = label.TransformToVisual(platform).TransformBounds(new Windows.Foundation.Rect(0,0,label.ActualWidth,label.ActualHeight));
                    Check(bounds.Right <= platform.ActualWidth + 1 && bounds.Bottom <= platform.ActualHeight + 1, "Platform label clipped: " + label.Text + "; bounds=" + bounds + "; picker=" + platform.ActualWidth + "x" + platform.ActualHeight);
                    Check(label.LineHeight == 20 && label.Margin.Top >= 0, "Platform baseline uses ad hoc offset");
                    if (container.IsSelected)
                    {
                        var presenter = Children(container).OfType<ListViewItemPresenter>().First();
                        Check(Contrast(((SolidColorBrush)label.Foreground).Color, ((SolidColorBrush)presenter.SelectedBackground).Color) >= 4.5, "Selected platform text contrast too low");
                    }
                }
                var card = (GridViewItem)games.ContainerFromIndex(0);
                var surface = (Border)card.ContentTemplateRoot;
                var image = Children(card).OfType<Image>().Single();
                var art = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(image));
                Check(Math.Abs(art.ActualWidth / art.ActualHeight - 4d / 3) < .02, "Game image slot lost 4:3 ratio");
                Check(image.Stretch == Stretch.Uniform && surface.CornerRadius.TopLeft == 10 && art.CornerRadius.TopLeft == 8, "Game image crop or corner hierarchy incorrect");
                Check(((SolidColorBrush)surface.Background).Color.A == 0, "Category cards still have a permanent panel background");
                var gamePresenter = Children(card).OfType<ListViewItemPresenter>().First();
                Check(((SolidColorBrush)gamePresenter.PointerOverBackground).Color.A > 0 && card.UseSystemFocusVisuals && card.FocusVisualPrimaryThickness.Left == 2,
                    "Category pointer and keyboard feedback is missing");
                Check(((SolidColorBrush)art.Background).Color.A == 0, "Game artwork still has a tinted nested frame");
                Check(card.Focus(FocusState.Keyboard) && card.FocusState == FocusState.Keyboard, "Keyboard focus feedback is missing");
                if (!new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
                    Check(((SolidColorBrush)surface.BorderBrush).Color.A == 0, "Persistent category outline remains");
                Check(Math.Abs(surface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).X - header.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).X) <= 1, "Header and first card are misaligned");
                var text = Children(card).OfType<TextBlock>().First(t => t.Text == "三角洲行动");
                Check(Contrast(((SolidColorBrush)text.Foreground).Color, ((SolidColorBrush)root.Background).Color) >= 4.5, "Body contrast too low");
                if (width == 960) await Capture(root, "category-layout-" + theme + ".png");
            });
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
            await Case("quiet room cards keep rounded video and title details " + theme, async () =>
            {
                var home = new Views.RecomendPage();
                var style = ((GridView)home.FindName("RoomsGrid")).ItemContainerStyle;
                var grid = new Controls.MyAdaptiveGridView { Width = 640, HorizontalAlignment = HorizontalAlignment.Left,
                    RequestedTheme = theme, ItemContainerStyle = style, ItemTemplate = (DataTemplate)App.Current.Resources["RoomItemTemplate"],
                    ItemsSource = new[] { new LiveRoomItem { Title = "用于检查的游戏直播标题", UserName = "测试主播", Cover = "ms-appx:///Assets/YolanLive.png" } } };
                await Show(grid);
                var card = (GridViewItem)grid.ContainerFromIndex(0);
                Check(((SolidColorBrush)card.Background).Color.A == 0, "Room metadata retains an opaque tile background");
                var image = Children(card).OfType<Image>().Single();
                var cover = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(image));
                Check(cover.CornerRadius == new CornerRadius(10) && Math.Abs(cover.ActualWidth / cover.ActualHeight - 16d / 9) < .02, "Video cover lost its rounded shape or aspect ratio");
                var title = Children(card).OfType<TextBlock>().Single(t => t.Text == "用于检查的游戏直播标题");
                Check(ToolTipService.GetToolTip(title)?.ToString() == title.Text && title.TextTrimming == TextTrimming.CharacterEllipsis, "Full title hint is missing");
                Check(card.Focus(FocusState.Keyboard) && card.FocusState == FocusState.Keyboard, "Room card lost native keyboard focus");
            });
        foreach (var width in new[] {480d,640d,960d,1280d})
            await Case("settings centered panel and responsive actions width=" + width, async () =>
            {
                var page = new Views.SettingsPage { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
                await Show(page);
                var content = (StackPanel)page.FindName("SettingsCards");
                Check(Math.Abs(content.ActualWidth - Math.Min(1040, width - 2 * (width < 640 ? 16 : 24))) <= 2, "Settings width drift");
                var title = (TextBlock)page.FindName("PageTitle");
                Check(Math.Abs(title.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).X - content.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).X) <= 1, "Settings heading does not align with its centered panels");
                foreach (var row in page.SettingRows.Where(r => r.Visibility == Visibility.Visible))
                {
                    var action = (FrameworkElement)row.Children[1];
                    Check(Grid.GetRow(action) == (row.ActualWidth < 640 ? 1 : 0), "Settings action failed to reflow");
                }
                if (width == 960) await Capture(page, "settings-layout.png");
            });
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        await Case("retained category loading and in-place retry " + theme, async () =>
        {
            var originalSites = MainVM.Sites; var originalSite = MainVM.BrowseSite;
            var api = new BrowseChecks.CatalogSite();
            var site = new Site { Name = originalSites[0].Name, SiteType = originalSites[0].SiteType, LiveSite = api };
            try
            {
                MainVM.Sites = new() { site }; MainVM.BrowseSite = site;
                var page = new Views.CategoryPage(); var root = (Grid)page.Content;
                root.Width = 480; root.HorizontalAlignment = HorizontalAlignment.Left; root.RequestedTheme = theme;
                var vm = (CategoryVM)typeof(Views.CategoryPage).GetField("categoryVM", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(page);
                await vm.LoadAsync(); await Show(page);
                var count = vm.Items.Count;
                var pending = new TaskCompletionSource<List<LiveCategory>>(); api.Pending = pending.Task;
                var refresh = vm.RefreshAsync(); await Task.Delay(30);
                Check(vm.Items.Count == count && !((Button)page.FindName("HeaderRefresh")).IsEnabled &&
                    ((Border)page.FindName("LoadStatus")).Visibility == Visibility.Collapsed &&
                    ((StackPanel)page.FindName("EmptyState")).Visibility == Visibility.Collapsed,
                    "Normal refresh clears the content or moves it with a status banner");
                pending.SetException(new System.Net.Http.HttpRequestException("Isolated layout failure"));
                await refresh; await Task.Delay(80); page.UpdateLayout();
                var status = (Border)page.FindName("LoadStatus");
                var retry = (Button)page.FindName("InlineRetry");
                Check(status.Visibility == Visibility.Visible && retry.Visibility == Visibility.Visible && retry.Focus(FocusState.Keyboard),
                    "Retained content failure has no reachable inline retry");
                var label = Children(status).OfType<TextBlock>().First();
                Check(Contrast(((SolidColorBrush)label.Foreground).Color, ((SolidColorBrush)status.Background).Color) >= 4.5,
                    "Error feedback contrast is too low");
                Check(status.ActualWidth <= root.ActualWidth - root.Padding.Left - root.Padding.Right + 1 && label.TextWrapping == TextWrapping.Wrap,
                    "Inline error feedback overflows the narrow page");
                await Capture(page, "refresh-feedback-" + theme + ".png");
                api.Pending = null;
                retry.Command.Execute(null); await Task.Delay(100);
                Check(!vm.Loading && !vm.CanRetry && status.Visibility == Visibility.Collapsed && vm.Items.Count > 0,
                    "The visible retry control did not recover its current page");
            }
            finally { window.Root.Content = null; MainVM.Sites = originalSites; MainVM.BrowseSite = originalSite; GameCatalog.Invalidate(api); }
        });
        report.Add("INFO actual rasterization scale=" + window.Root.XamlRoot.RasterizationScale + "; 150/200 percent physical font rendering and OS high contrast remain manual checks.");
        File.WriteAllLines(Path.Combine(folder, "visual-checks.log"), report);
        Environment.ExitCode = failed ? 1 : 0;
    }
}
