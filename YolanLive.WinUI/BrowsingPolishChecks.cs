using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using YolanLive.Core;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;

namespace YolanLive.WinUI;

internal static class BrowsingPolishChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        if (root == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Children(child)) yield return descendant;
        }
    }
    private static async Task Until(Func<bool> ready, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new Exception(message); await Task.Delay(20); }
    }
    internal static async Task RunAsync(MainWindow window)
    {
        await DatabaseHelper.InitializeDatabase();
        var report = new List<string>();
        var originalSites = MainVM.Sites; var originalSite = MainVM.BrowseSite;
        var originalContent = window.Root.Content;
        async Task Case(string name, Func<Task> run)
        {
            try { await run(); report.Add("PASS " + name); }
            catch (Exception error) { report.Add("FAIL " + name + "\n" + error); }
            File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "browsing-polish.log"), report);
        }
        try
        {
            await Case("Room cover density stays near target width, retains 16:9 and fills each row", async () =>
            {
                window.AppWindow.Resize(new SizeInt32(1320, 900));
                var grid = new Controls.MyAdaptiveGridView
                {
                    DesiredWidth = (double)App.Current.Resources["GridViewDesiredWidth"], HorizontalAlignment = HorizontalAlignment.Left,
                    ItemTemplate = (DataTemplate)App.Current.Resources["RoomItemTemplate"],
                    ItemContainerStyle = (Style)App.Current.Resources["GridViewItemCornerRadiusStyle"],
                    ItemsSource = Enumerable.Range(0, 24).Select(i => new LiveRoomItem { RoomID = i.ToString(), Title = "Room " + i }).ToList()
                };
                window.Root.Content = new Page { Content = grid }; window.Activate();
                foreach (var width in new[] { 520, 600, 800, 960, 1200 })
                {
                    grid.Width = width; await Task.Delay(80); grid.UpdateLayout();
                    var panel = (ItemsWrapGrid)grid.ItemsPanelRoot;
                    Check(panel.ItemWidth >= 240 && panel.ItemWidth <= 340, "Oversized/undersized cover at " + width + ": " + panel.ItemWidth);
                    var count = Math.Round(width / panel.ItemWidth);
                    Check(width - count * panel.ItemWidth >= -.5 && width - count * panel.ItemWidth < count + 1, "A column was lost to rounding.");
                    Check(Math.Abs((panel.ItemHeight - 80) / (panel.ItemWidth - 16) - 9d / 16) < .001, "Cover aspect ratio changed.");
                }
            });
            var catalog = new CatalogSite();
            var site = new Site { Name = catalog.Name, LiveSite = catalog, SiteType = originalSites[0].SiteType };
            MainVM.Sites = new() { site }; MainVM.BrowseSite = site;
            SettingHelper.SetValue(BrowsePreferences.GroupKey(site), "2");
            SettingHelper.SetValue(BrowsePreferences.PinsKey(site), new List<PinnedGame>());
            await Case("Local game filter spans groups without requests and restores group on clear", async () =>
            {
                var vm = new CategoryVM(); await vm.LoadAsync();
                var calls = catalog.Calls;
                vm.SelectedGroup = vm.Groups.Single(g => g.ID == "3");
                vm.FilterText = " ALPHA ";
                Check(vm.Items.Single().Name == "Alpha" && vm.Items[0].Sources.Single().Category.ID == "a" && vm.SelectedGroup.ID == "3", "Filtering lost another group's source or selected group.");
                vm.FilterText = ""; Check(vm.Items.Single().Name == "赛车", "Clearing did not restore the selected group.");
                vm.FilterText = "no-match"; Check(vm.IsEmpty && vm.EmptyMessage.Contains("匹配"), "Filter empty state is misleading.");
                Check(catalog.Calls == calls, "Typing generated catalog requests.");
                await vm.RefreshAsync();
                Check(vm.FilterText == "no-match" && vm.IsEmpty && vm.SelectedGroup.ID == "3" && catalog.Calls == calls + 1, "Refresh lost the filter or failed to force a catalog refresh.");
                vm.CancelPending();
            });
            var page = new Views.CategoryPage();
            var games = (GridView)page.FindName("GamesGrid");
            var filter = (AutoSuggestBox)page.FindName("GameFilter");
            await Case("Category local search reflows and empty filtered results survive cached return", async () =>
            {
                window.Root.Content = page; window.Activate();
                await Until(() => games.Items.Count > 0, "Categories did not load.");
                foreach (var width in new[] { 1280, 640 })
                {
                    window.AppWindow.Resize(new SizeInt32(width, 850)); await Task.Delay(100); page.UpdateLayout();
                    var tools = (Grid)page.FindName("CategoryTools");
                    var compact = (Button)page.FindName("CompactGameFilter");
                    Check((compact.Visibility == Visibility.Visible) == (tools.ActualWidth < 640), "Local search did not compact without adding a navigation row.");
                    if (compact.Visibility == Visibility.Visible)
                    {
                        compact.Flyout.ShowAt(compact); await Task.Delay(80);
                        Check(filter.Focus(FocusState.Keyboard), "Compact game search cannot receive keyboard focus.");
                    }
                    Check(filter.ActualWidth > 100 && filter.ActualWidth <= tools.ActualWidth, "Local game search was clipped.");
                    Check(Grid.GetRow(compact) == 0, "Local search added an unnecessary row.");
                    filter.Text = "Alpha"; await Task.Delay(30);
                    Check(games.Items.Count == 1 && ((LiveGameCategory)games.Items[0]).Name == "Alpha", "Compact search lost its existing live filter binding.");
                    Check(((FrameworkElement)page.FindName("FilterMarker")).Visibility == Visibility.Visible, "Active compact filter is not indicated: width=" + width + "; input=" + filter.Text + "; name=" + Microsoft.UI.Xaml.Automation.AutomationProperties.GetName((Button)page.FindName("CompactGameFilter")));
                    compact.Flyout.Hide();
                }
                filter.Text = "no-match"; await Task.Delay(50);
                Check(games.Items.Count == 0, "Native text binding did not filter the view.");
                var calls = catalog.Calls;
                window.Root.Content = new Page(); await Task.Delay(30);
                window.Root.Content = page; await Task.Delay(80);
                Check(filter.Text == "no-match" && games.Items.Count == 0 && calls == catalog.Calls, "Returning to an empty local filter reloaded/reset categories.");
                filter.Text = ""; await Until(() => games.Items.Count > 0, "Clearing search did not restore cards.");
            });
            await Case("Game groups size to their names, wrap, retain single selection and show themed feedback", async () =>
            {
                filter.Text = "";
                var picker = (ListView)page.FindName("GroupPicker");
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                {
                    page.RequestedTheme = theme;
                    window.AppWindow.Resize(new SizeInt32(960, 650)); await Task.Delay(100); page.UpdateLayout();
                    var first = (ListViewItem)picker.ContainerFromIndex(0);
                    var second = (ListViewItem)picker.ContainerFromIndex(1);
                    Check(second.ActualWidth >= first.ActualWidth + 20, "Short and long group labels still occupy equal-width slots.");
                    var calls = catalog.Calls;
                    var peer = FrameworkElementAutomationPeer.CreatePeerForElement(picker);
                    var itemPeer = peer.GetChildren().FirstOrDefault(p => p.GetName() == "赛车竞速");
                    var selection = itemPeer?.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider;
                    Check(selection != null, "Game group has no SelectionItem automation pattern through its owner: " + string.Join(",", peer.GetChildren().Select(p => p.GetType().Name + ":" + p.GetName())));
                    selection.Select();
                    await Task.Delay(180);
                    Check(second.IsSelected && !first.IsSelected && second.Focus(FocusState.Keyboard), "Native single selection or keyboard focus was lost.");
                    Check(games.Items.Count == 1 && ((LiveGameCategory)games.Items[0]).Name == "赛车" && catalog.Calls == calls, "Group selection lost its directory or generated a request.");
                    var background = (Border)Children(second).First(e => e is Border b && b.Name == "GroupRoot");
                    Check(((SolidColorBrush)background.Background).Color.A > 0 && background.CornerRadius.TopLeft == 8, "Selected group has no rounded visible feedback.");
                    picker.Width = first.ActualWidth + 12;
                    await Task.Delay(80); page.UpdateLayout();
                    Check(second.TransformToVisual(picker).TransformPoint(new Windows.Foundation.Point()).Y > first.TransformToVisual(picker).TransformPoint(new Windows.Foundation.Point()).Y, "Group labels did not wrap when space ran out.");
                    picker.ClearValue(FrameworkElement.WidthProperty);
                }
                page.RequestedTheme = ElementTheme.Default;
                picker.SelectedIndex = 0;
            });
            await Case("Pin action supports card keyboard focus, invoke, persistent marker and recycling", async () =>
            {
                games.ScrollIntoView(games.Items[0]); page.UpdateLayout();
                await Until(() => games.ContainerFromIndex(0) is GridViewItem, "Card container missing.");
                var card = (GridViewItem)games.ContainerFromIndex(0);
                var root = (FrameworkElement)card.ContentTemplateRoot;
                var pin = Children(root).OfType<Button>().Single(b => b.Name == "PinGame");
                Check(card.ContextFlyout is MenuFlyout, "The keyboard context menu was removed.");
                Check(card.Focus(FocusState.Keyboard), "Card could not receive keyboard focus.");
                await Task.Delay(40);
                Check(pin.Opacity == 1 && pin.IsEnabled, "Keyboard-focused card has no visible pin action.");
                var game = (LiveGameCategory)card.Content;
                var invoke = (IInvokeProvider)new ButtonAutomationPeer(pin).GetPattern(PatternInterface.Invoke);
                invoke.Invoke(); await Task.Delay(40);
                Check(BrowsePreferences.IsPinned(site, game), "Pin invocation did not save the game.");
                filter.Focus(FocusState.Programmatic); await Task.Delay(30);
                Check(pin.Opacity == 1, "Pinned marker vanished when focus left the card.");
                window.Root.Content = new Page(); await Task.Delay(30);
                window.Root.Content = page; await Task.Delay(70); page.UpdateLayout();
                card = (GridViewItem)games.ContainerFromItem(game);
                pin = Children(card).OfType<Button>().Single(b => b.Name == "PinGame");
                Check(pin.Opacity == 1 && ToolTipService.GetToolTip(pin).ToString().Contains("取消"), "Returned card lost pinned state.");
                ((IInvokeProvider)new ButtonAutomationPeer(pin).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(30);
                Check(!BrowsePreferences.IsPinned(site, game) && BrowsePreferences.ReadPins(site).Count == 0, "Unpin invocation failed or handlers ran twice.");
            });
            await Case("Game art keeps full aspect, 12 DIP safe area and a theme-readable backing", async () =>
            {
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                {
                    page.RequestedTheme = theme; await Task.Delay(60); page.UpdateLayout();
                    var card = (GridViewItem)games.ContainerFromIndex(0);
                    var image = Children(card).OfType<Image>().Single(i => i.Name == "GameImage");
                    var art = (Border)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(image));
                    Check(image.Stretch == Stretch.Uniform && image.Margin == new Thickness(12), "Game art was cropped or lost consistent padding.");
                    Check(art.CornerRadius.TopLeft == 8 && ((SolidColorBrush)art.Background).Color.A == 255, "Transparent artwork has no readable backing.");
                    Check(image.ActualWidth > 20 && image.ActualHeight > 20, "Artwork safe area consumed the image.");
                }
            });
            await Case("Cached content returns without replaying its page entrance", async () =>
            {
                var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
                var content = new Border(); Grid.SetRow(content, 1); root.Children.Add(content); PageDesign.SetInsets(root, true);
                var cached = new Page { Content = root };
                window.Root.Content = cached; await Task.Delay(220);
                window.Root.Content = new Page(); await Task.Delay(30);
                window.Root.Content = cached;
                await Until(() => root.IsLoaded, "Cached root did not reload.");
                Check(ElementCompositionPreview.GetElementVisual(content).Opacity > .99, "Cached return replayed a content fade.");
            });
            await Case("Loading stages follow real requests, cancel clears and stale responses cannot relaunch", async () =>
            {
                var pending = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
                var fixture = new RandomBrowseChecks.FixtureSite { PendingUrls = pending.Task };
                var vm = new LiveRoomVM(); var stages = new List<string>(); var plays = 0;
                vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LiveRoomVM.LoadingStage)) stages.Add(vm.LoadingStage); };
                vm.ChangedPlayUrl += (_, _) => plays++;
                using var cancel = new CancellationTokenSource();
                var loading = vm.LoadDataAsync(fixture, "loading-stages", cancel.Token);
                await fixture.UrlStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Check(stages.Take(3).SequenceEqual(new[] { "正在获取直播间…", "正在读取画质…", "正在获取播放线路…" }) && vm.LoadingStage.Contains("线路") && vm.Loading, "Displayed stages did not follow the requests.");
                cancel.Cancel(); await loading;
                Check(vm.LoadingStage == "" && !vm.Loading && plays == 0, "Cancellation left a stale loading stage.");
                pending.SetResult(new() { fixture.StreamUrl }); await Task.Delay(30);
                Check(plays == 0 && vm.LoadingStage == "", "Late stream response changed the cancelled room.");
                fixture.PendingUrls = null; await vm.LoadDataAsync(fixture, "ready-stages");
                Check(plays == 1 && vm.LoadingStage == "", "Successful room did not hand off to media loading.");
                vm.CancelPendingPlayback();
            });
        }
        finally
        {
            window.Root.Content = originalContent;
            MainVM.Sites = originalSites; MainVM.BrowseSite = originalSite;
        }
        Environment.ExitCode = report.Any(line => line.StartsWith("FAIL")) ? 1 : 0;
    }
    private sealed class CatalogSite : BiliBili, ILiveSite
    {
        internal int Calls;
        public new Task<List<LiveCategory>> GetCategores(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(new List<LiveCategory>
            {
                new() { ID = "2", Name = "网游", Children = new() { new() { ID = "a", ParentID = "2", Name = "Alpha", Pic = "ms-appx:///Assets/YolanLive.png" } } },
                new() { ID = "3", Name = "赛车竞速", Children = new() { new() { ID = "b", ParentID = "3", Name = "赛车", Pic = "ms-appx:///Assets/YolanLive.png" } } }
            });
        }
    }
}
