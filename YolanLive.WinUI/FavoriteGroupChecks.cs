using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace YolanLive.WinUI;

internal static class FavoriteGroupChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task RunAsync(MainWindow window)
    {
        var report = new List<string>();
        var original = MainVM.Sites;
        var previousPage = window.Root.Content;
        async Task Case(string name, Func<Task> run)
        {
            try { await run(); report.Add("PASS " + name); }
            catch (Exception error) { report.Add("FAIL " + name + "\n" + error); }
            File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "favorite-groups.log"), report);
        }
        await DatabaseHelper.InitializeDatabase();
        try
        {
            await Case("Existing browsing, favorite grouping and quality preferences", GameBrowseChecks.RunAsync);
            var live = new GameBrowseChecks.TestSite("A");
            var offline = new GameBrowseChecks.TestSite("B") { Live = false };
            var unknown = new GameBrowseChecks.TestSite("C") { StatusFail = true };
            MainVM.Sites = new() { new() { Name = "A", LiveSite = live }, new() { Name = "B", LiveSite = offline }, new() { Name = "C", LiveSite = unknown } };
            DatabaseHelper.AddFavorite(new FavoriteItem { SiteName = "C", RoomID = "unknown-status", UserName = "待确认主播", Photo = "" });
            var vm = new FavoriteVM();
            await Case("First visit retains live, offline and failed-status follows", async () =>
            {
                await vm.LoadAsync();
                Check(vm.Items.Count == 3 && vm.Groups.Select(group => group.Count).SequenceEqual(new[] { 1, 1, 1 }), "A saved follow vanished or entered the wrong status group.");
                Check(vm.Groups[1].Single().IsOffline && vm.Groups[2].Single().IsStatusUnknown && !vm.Groups[2].Single().IsOffline && vm.CanRetry,
                    "Unknown status was treated as offline or lost its retry.");
                // Persist while supported, then remove the platform to reproduce a legacy saved record.
                MainVM.Sites.Add(new() { Name = "不存在的平台", LiveSite = unknown });
                var unavailable = MainVM.Sites.Last();
                try { DatabaseHelper.AddFavorite(new FavoriteItem { SiteName = "不存在的平台", RoomID = "unavailable-site", UserName = "旧关注", Photo = "" }); }
                finally { MainVM.Sites.Remove(unavailable); }
                await vm.LoadAsync();
                var unsupported = vm.Items.Single(item => item.RoomID == "unavailable-site");
                Check(unsupported.IsStatusUnknown && vm.Groups[2].Contains(unsupported), "A follow with an unavailable platform disappeared or looked offline.");
                vm.RemoveItem(unsupported);
            });
            await Case("Native grouped cards in light, dark, wide and narrow windows", async () =>
            {
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                foreach (var size in new[] { "1280×720", "640×360" })
                {
                    var page = new Views.FavoritePage { RequestedTheme = theme };
                    window.ApplyWindowSize(size);
                    window.Root.Content = page;
                    window.Activate();
                    await Task.Delay(180);
                    var list = (Controls.MyAdaptiveGridView)page.FindName("FavoriteList");
                    await WaitUntil(() => list.Items.Count == 3 && ((Button)page.FindName("HeaderRefresh")).IsEnabled);
                    Check(list.GroupStyle.Count == 1 && list.GroupStyle[0].HidesIfEmpty && list.ItemsPanelRoot is ItemsWrapGrid, "Grouping disabled native item virtualization or left empty headers visible.");
                    foreach (var item in list.Items.Cast<FavoriteItem>().ToArray())
                    {
                        list.ScrollIntoView(item); await Task.Delay(80); page.UpdateLayout();
                        var card = list.ContainerFromItem(item) as GridViewItem;
                        Check(card != null && card.ActualWidth > 0 && card.ActualWidth <= list.ActualWidth + 1, "A grouped card overflowed the narrow viewport.");
                        var text = Children(card).OfType<TextBlock>().Where(block => block.ActualWidth > 0 && block.Visibility == Visibility.Visible).ToArray();
                        var expected = item.LiveStatus ? "直播中" : item.IsOffline ? "未开播" : "状态待确认";
                        Check(text.Any(block => block.Text == expected), "The current status label was not rendered.");
                        Check(card.Focus(FocusState.Keyboard), "Grouped card lost keyboard focus.");
                    }
                    window.Root.Content = null;
                }
            });
            await Case("Refresh moves follows between groups and cancellation rejects late results", async () =>
            {
                live.Live = false; offline.Live = true; unknown.StatusFail = false;
                await vm.LoadAsync();
                Check(vm.Groups.Select(group => group.Count).SequenceEqual(new[] { 2, 1, 0 }) && vm.Groups[0].Header == "直播中（2）" && !vm.CanRetry,
                    "Refresh kept a stale group, count, or failure state.");
                var retained = vm.Items.ToArray();
                var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                live.PendingStatus = pending.Task;
                var loading = vm.LoadAsync();
                Check(vm.Items.SequenceEqual(retained), "Refreshing cleared the visible follows.");
                vm.CancelPending();
                await loading.WaitAsync(TimeSpan.FromSeconds(1));
                pending.SetResult(true); live.PendingStatus = null;
                await Task.Delay(50);
                Check(vm.Items.SequenceEqual(retained) && vm.Groups[1].Single().SiteName == "A" && !vm.Loading, "A cancelled result changed the groups or left the page busy.");
            });
            await Case("Removing an offline follow updates its group and database", async () =>
            {
                var item = vm.Groups[1].Single();
                vm.RemoveItem(item);
                Check(vm.Groups[1].Count == 0 && vm.Groups[1].Header == "未开播（0）" && !vm.Items.Contains(item) &&
                    !(await DatabaseHelper.GetFavorites()).Any(saved => saved.ID == item.ID), "Removing an offline follow left a stale row or count.");
            });
        }
        finally
        {
            window.Root.Content = previousPage;
            MainVM.Sites = original;
        }
        Environment.ExitCode = report.Any(line => line.StartsWith("FAIL")) ? 1 : 0;
    }
    private static async Task WaitUntil(Func<bool> ready)
    {
        for (var i = 0; i < 100 && !ready(); i++) await Task.Delay(20);
        Check(ready(), "The grouped page did not become ready.");
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
}
