using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics;

namespace YolanLive.WinUI;

internal static class UsabilityChecks
{
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    internal static async Task RunAsync(MainWindow window)
    {
        await DatabaseHelper.InitializeDatabase();
        var report = new List<string>();
        var originalPage = window.Root.Content;
        var originalSites = MainVM.Sites;
        async Task Case(string name, Func<Task> run)
        {
            try { await run(); report.Add("PASS " + name); }
            catch (Exception error) { report.Add("FAIL " + name + "\n" + error); }
            File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "usability-checks.log"), report);
        }
        try
        {
            await Case("Existing grouped favorite checks", async () =>
            {
                await FavoriteGroupChecks.RunAsync(window);
                Check(Environment.ExitCode == 0, "Existing favorite group checks failed; see favorite-groups.log.");
            });
            await Case("Local favorite filtering preserves data, references and stable collections", async () =>
            {
                DatabaseHelper.DeleteFavorite();
                var a = new GameBrowseChecks.TestSite("A"); var b = new GameBrowseChecks.TestSite("B") { Live = false };
                MainVM.Sites = new() { new() { Name = "A", LiveSite = a }, new() { Name = "B", LiveSite = b } };
                DatabaseHelper.AddFavorite(new() { SiteName = "A", RoomID = "local-a", UserName = "Alice", Photo = "" });
                DatabaseHelper.AddFavorite(new() { SiteName = "B", RoomID = "local-b", UserName = "Bob", Photo = "" });
                var vm = new FavoriteVM(); await vm.LoadAsync();
                var retained = vm.Items.ToArray(); var resets = 0;
                foreach (var group in vm.Groups) group.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
                await vm.LoadAsync();
                Check(vm.Items.SequenceEqual(retained) && resets == 0, "An unchanged refresh replaced objects or reset groups.");
                vm.FilterText = "bob";
                Check(vm.Groups[1].Single().UserName == "Bob" && vm.Groups[0].Count == 0 && vm.Items.Count == 2 && (await DatabaseHelper.GetFavorites()).Count == 2, "Local filter navigated, deleted data or mismatched the case-insensitive name.");
                vm.FilterText = "not-found"; Check(vm.IsEmpty && vm.EmptyMessage.Contains("匹配"), "Filtered empty state looked like no saved favorites.");
                vm.FilterText = ""; a.Live = false; b.Live = true; await vm.LoadAsync();
                Check(vm.Groups[0].Single().UserName == "Bob" && vm.Items.SequenceEqual(retained) && resets == 0, "A status transition lost identity or reset a collection.");
                MainVM.Sites = originalSites;
            });
            await Case("History dates, platform labels and local filtering", () =>
            {
                var vm = new HistoryVM();
                vm.Items.Add(new() { ID = 1, UserName = "Alice", SiteName = "哔哩哔哩直播", WatchTime = DateTime.Today.AddHours(12) });
                vm.Items.Add(new() { ID = 2, UserName = "Alice", SiteName = "抖音直播", WatchTime = DateTime.Today.AddDays(-1).AddHours(12) });
                vm.Items.Add(new() { ID = 3, UserName = "Older", SiteName = "虎牙", WatchTime = DateTime.Today.AddDays(-4) });
                vm.FilterText = " ";
                Check(vm.Groups.Select(group => group.Count).SequenceEqual(new[] { 1, 1, 1 }) && vm.Items[0].PlatformLabel == "哔哩哔哩" && vm.Items[1].PlatformLabel == "抖音", "History date grouping or platform identity was lost.");
                vm.FilterText = "抖音"; Check(vm.Groups[1].Count == 1 && vm.Groups[0].Count == 0 && vm.Items.Count == 3, "History filter changed saved source items.");
                vm.FilterText = "missing"; Check(vm.IsEmpty && vm.EmptyMessage.Contains("匹配"), "History filter has the wrong empty state.");
                return Task.CompletedTask;
            });
            MainVM.Sites = originalSites;
            await Case("Browsing resizes and maximizes, playback stays fixed, return restores browsing", async () =>
            {
                window.Root.Content = new Page(); window.Activate(); await Task.Delay(100);
                var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
                Check(presenter.IsResizable && presenter.IsMaximizable, "Browsing window remained locked.");
                window.AppWindow.Resize(new SizeInt32(1100, 650)); await Task.Delay(120);
                Check(window.AppWindow.Size.Width == 1100 && window.AppWindow.Size.Height == 650, "Browsing resize snapped back to a playback preset.");
                presenter.Maximize(); await Task.Delay(120);
                Check(presenter.State == OverlappedPresenterState.Maximized, "Browsing maximization was undone.");
                var viewport = new Grid(); window.Root.Content = new Page { Content = viewport }; await Task.Delay(50);
                window.SetPlaybackViewport(viewport); await Task.Delay(150);
                Check(!presenter.IsResizable && !presenter.IsMaximizable && presenter.State == OverlappedPresenterState.Restored, "Playback did not recover fixed window rules.");
                window.SetPlaybackViewport(null); await Task.Delay(150);
                Check(presenter.IsResizable && presenter.IsMaximizable && presenter.State == OverlappedPresenterState.Maximized, "Returning from playback lost browser maximization.");
                presenter.Restore(); await Task.Delay(100);
                Check(window.AppWindow.Size.Width == 1100 && window.AppWindow.Size.Height == 650, "Returning lost the saved browsing bounds.");
            });
            await Case("Fixed top search, local filter and history rows at wide and narrow widths", async () =>
            {
                DatabaseHelper.AddHistory(new() { SiteName = originalSites[0].Name, RoomID = "usability-history", UserName = "History example", Photo = "" });
                var chrome = new BaseFramePage(); var shell = new MainPage();
                ((Frame)chrome.FindName("MainFrame")).Content = shell;
                window.Root.Content = chrome; window.Activate(); await Task.Delay(200);
                foreach (var pageType in new[] { typeof(Views.FavoritePage), typeof(Views.HistoryPage), typeof(Views.SettingsPage) })
                {
                    var frame = (Frame)shell.FindName("frame");
                    frame.Navigate(pageType);
                    var page = (Page)frame.Content;
                    await Task.Delay(150); chrome.RefreshChrome();
                    foreach (var width in new[] { 1200, 640 })
                    {
                        window.AppWindow.Resize(new SizeInt32(width, 720)); await Task.Delay(150); page.UpdateLayout(); chrome.RefreshChrome();
                        var search = (AutoSuggestBox)page.FindName("HeaderSearch"); var compact = (Button)page.FindName("CompactSearch");
                        Check((search.Visibility == Visibility.Visible) != (compact.Visibility == Visibility.Visible) && ((NavigationView)shell.FindName("navigationView")).AutoSuggestBox == null, "Global search vanished, duplicated or moved into the sidebar.");
                        if (compact.Visibility == Visibility.Visible)
                        {
                            PageDesign.OpenSearch(compact); await Task.Delay(40);
                            Check(compact.Flyout is Flyout { Content: AutoSuggestBox }, "Compact search has no usable input.");
                            compact.Flyout.Hide();
                        }
                    }
                    if (page.FindName("LocalFilter") is AutoSuggestBox filter)
                    {
                        var list = page.FindName("FavoriteList") as ListViewBase ?? (ListViewBase)page.FindName("HistoryList");
                        var before = list.Items.Count;
                        Check(before > 0, "No local records before filtering: " + page.GetType().Name);
                        filter.Text = "does-not-match-a-local-record"; await Task.Delay(80);
                        Check(list.Items.Count == 0 && shell.ActivePage == page, "Local filtering changed pages or did not filter.");
                        filter.Text = ""; await Task.Delay(80);
                        Check(list.Items.Count == before, "Clearing the filter failed to restore local records: " + page.GetType().Name + "; before=" + before + "; after=" + list.Items.Count + "; text=" + filter.Text);
                    }
                }
            });
            await Case("Favorite regrouping preserves the visible anchor and keyboard focus", async () =>
            {
                var a = new GameBrowseChecks.TestSite("A"); var b = new GameBrowseChecks.TestSite("B") { Live = false };
                MainVM.Sites = new() { new() { Name = "A", LiveSite = a }, new() { Name = "B", LiveSite = b } };
                try
                {
                    for (var i = 0; i < 40; i++) DatabaseHelper.AddFavorite(new() { SiteName = "A", RoomID = "anchor-" + i, UserName = "Anchor " + i, Photo = "" });
                    window.AppWindow.Resize(new SizeInt32(960, 600));
                    var page = new Views.FavoritePage(); window.Root.Content = page; window.Activate(); await Task.Delay(180);
                    var list = (Controls.MyAdaptiveGridView)page.FindName("FavoriteList");
                    var item = list.Items.Cast<FavoriteItem>().Single(row => row.RoomID == "anchor-20");
                    list.ScrollIntoView(item, ScrollIntoViewAlignment.Leading); await Task.Delay(120); page.UpdateLayout();
                    var card = (GridViewItem)list.ContainerFromItem(item);
                    Check(card.Focus(FocusState.Keyboard), "Could not focus the anchor card.");
                    var y = card.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point()).Y;
                    a.Live = false; b.Live = true;
                    ((Button)page.FindName("HeaderRefresh")).Command.Execute(null);
                    await Task.Delay(220); page.UpdateLayout();
                    var next = list.ContainerFromItem(item) as GridViewItem;
                    Check(next != null && Math.Abs(next.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point()).Y - y) <= 2,
                        "Regrouping moved the visible anchor.");
                    Check(next.FocusState == FocusState.Keyboard, "Regrouping lost keyboard focus.");
                }
                finally
                {
                    window.Root.Content = null;
                    foreach (var item in (await DatabaseHelper.GetFavorites()).Where(item => item.RoomID.StartsWith("anchor-"))) DatabaseHelper.DeleteFavorite(item.ID);
                    MainVM.Sites = originalSites;
                }
            });
            await Case("Update recovery states and links", async () =>
            {
                await UpdateChecks.RecoveryAsync();
                var page = new Views.LiveRoomPage();
                Check(((TextBlock)((Button)page.FindName("WindowSizeButton")).Content).Text.Contains("窗口大小") && ((ToggleMenuFlyoutItem)page.FindName("PlayBtnFullWindow")).Text == "无边框播放", "Player labels still mix video quality with window modes.");
            });
        }
        finally
        {
            MainVM.Sites = originalSites;
            window.SetPlaybackViewport(null);
            window.Root.Content = originalPage;
        }
        Environment.ExitCode = report.Any(line => line.StartsWith("FAIL")) ? 1 : 0;
    }
}
