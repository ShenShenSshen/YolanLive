using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace YolanLive.WinUI;

internal static class BrowseChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static LiveGameCategory Game(Site site, string id, string name = "测试游戏") => new()
    {
        Name = name,
        Sources = new() { new() { Site = site.LiveSite, Category = new() { ID = id, ParentID = "2", Name = name } } }
    };

    internal static void Pins()
    {
        var bili = MainVM.Sites[0]; var douyin = MainVM.Sites[1];
        foreach (var site in MainVM.Sites) SettingHelper.SetValue(BrowsePreferences.PinsKey(site), new List<PinnedGame>());
        try
        {
            for (var i = 0; i < 6; i++) Check(BrowsePreferences.TogglePin(bili, Game(bili, "game-" + i)), "Pin was not added.");
            var pins = BrowsePreferences.ReadPins(bili);
            Check(pins.Count == 6 && pins.Select(p => p.Sources[0].ID).SequenceEqual(Enumerable.Range(0, 6).Select(i => "game-" + i)), "Pin count or insertion order changed.");
            Check(!BrowsePreferences.TogglePin(bili, Game(bili, "seventh")) && BrowsePreferences.ReadPins(bili).Count == 6, "Seventh pin replaced a saved game.");
            Check(BrowsePreferences.TogglePin(douyin, Game(douyin, "game-0")) && BrowsePreferences.ReadPins(douyin).Count == 1, "Platform pins were not isolated.");
            Check(BrowsePreferences.ResolvePin(bili, pins[0], Game(douyin, "game-0").Sources) == null, "Shortcut crossed platforms.");
            var renamed = Game(bili, "game-0", "平台更新后的名称");
            Check(BrowsePreferences.ResolvePin(bili, pins[0], renamed.Sources)?.Name == renamed.Name, "Pin used a stale name instead of its ID.");
            Check(BrowsePreferences.ResolvePin(bili, pins[0], Game(bili, "different").Sources) == null && BrowsePreferences.ReadPins(bili).Count == 6, "Missing category redirected or deleted the saved pin.");
            Check(BrowsePreferences.TogglePin(bili, Game(bili, "game-0")) && BrowsePreferences.ReadPins(bili).Count == 5, "Toggle did not remove the existing pin.");
            BrowsePreferences.RemovePin(bili, BrowsePreferences.ReadPins(bili)[0]);
            Check(BrowsePreferences.ReadPins(bili).Count == 4 && BrowsePreferences.ReadPins(douyin).Count == 1, "Removing a pin affected the other platform.");
            SettingHelper.SetValue(BrowsePreferences.PinsKey(bili), new[] { pins[0], pins[0], new PinnedGame(), null });
            Check(BrowsePreferences.ReadPins(bili).Count == 1, "Duplicate or malformed saved pins were loaded.");
            var partial = new PinnedGame { Name = "多分区", Sources = new() { pins[0].Sources[0], pins[1].Sources[0] } };
            Check(BrowsePreferences.ResolvePin(bili, partial, renamed.Sources) == null, "Partially removed category opened a different subset.");
        }
        finally { foreach (var site in MainVM.Sites) SettingHelper.SetValue(BrowsePreferences.PinsKey(site), new List<PinnedGame>()); }
    }

    internal static async Task MemoryAsync()
    {
        var originalSites = MainVM.Sites; var originalSite = MainVM.BrowseSite;
        var api = new CatalogSite();
        var site = new Site { SiteType = originalSites[0].SiteType, LiveSite = api, Name = originalSites[0].Name };
        try
        {
            SettingHelper.SetValue(BrowsePreferences.PlatformKey, "invalid-platform");
            Check(BrowsePreferences.ReadPlatform() == originalSites[0], "Invalid platform did not fall back.");
            MainVM.BrowseSite = originalSites[1];
            Check(BrowsePreferences.ReadPlatform() == originalSites[1], "Platform selection was not saved.");
            var home = new Views.RecomendPage();
            var page = new Views.CategoryPage();
            var picker = (Microsoft.UI.Xaml.Controls.ListView)home.FindName("PlatformPicker");
            Check(picker.SelectedItem == picker.Items[1], "Saved homepage platform was not selected before the page loaded.");
            Check(page.FindName("PlatformPicker") == null, "Game directory should start with games instead of a platform picker.");
            picker.SelectedItem = picker.Items[0];
            Check(MainVM.BrowseSite == originalSites[1], "Initial XAML selection overwrote the saved platform.");
            MainVM.Sites = new() { site, originalSites[1] };
            MainVM.BrowseSite = site;
            SettingHelper.SetValue(BrowsePreferences.GroupKey(site), "3");
            SettingHelper.SetValue(BrowsePreferences.GroupKey(originalSites[1]), "103,4");
            var vm = new CategoryVM();
            await vm.LoadAsync();
            Check(vm.SelectedGroup?.ID == "3", "Saved group was not restored.");
            vm.SelectedGroup = vm.Groups[0];
            var pending = new TaskCompletionSource<List<LiveCategory>>();
            api.Pending = pending.Task;
            GameCatalog.Invalidate(api);
            var reload = vm.LoadAsync();
            Check(vm.SelectedGroup == null && SettingHelper.GetValue(BrowsePreferences.GroupKey(site), "") == "2", "Temporary clear overwrote the saved group.");
            pending.SetResult(api.Groups);
            await reload;
            Check(vm.SelectedGroup?.ID == "2", "Refresh did not restore the selected group.");
            api.Pending = Task.FromException<List<LiveCategory>>(new System.Net.Http.HttpRequestException("Isolated test"));
            GameCatalog.Invalidate(api);
            await vm.LoadAsync();
            Check(SettingHelper.GetValue(BrowsePreferences.GroupKey(site), "") == "2", "Network failure overwrote the selected group.");
            api.Pending = null;
            SettingHelper.SetValue(BrowsePreferences.GroupKey(site), "removed");
            await vm.LoadAsync();
            Check(vm.SelectedGroup?.ID == "2" && SettingHelper.GetValue(BrowsePreferences.GroupKey(originalSites[1]), "") == "103,4", "Missing group fallback affected another platform.");
        }
        finally { MainVM.Sites = originalSites; MainVM.BrowseSite = originalSite; }
    }

    internal static async Task HomeCategoryAsync(MainWindow window)
    {
        var oldSites = MainVM.Sites;
        var oldSite = MainVM.BrowseSite;
        var primary = new CatalogSite { PreviewName = "测试平台甲" };
        var secondary = new CatalogSite { PreviewName = "测试平台乙" };
        secondary.Groups[0].Children[0].ID = "same-game-second-partition";
        secondary.Groups[0].Children[0].Pic = "ms-appx:///Assets/YolanLive.png";
        secondary.Groups.RemoveAt(1);
        var first = new Site { SiteType = LiveSite.Bilibili, Name = primary.PreviewName, LiveSite = primary };
        var second = new Site { SiteType = LiveSite.Douyin, Name = secondary.PreviewName, LiveSite = secondary };
        var report = System.IO.Path.Combine(AppPaths.EnsureDirectory(), "home-category-check.log");
        void Pass(string text) => File.AppendAllText(report, "PASS " + text + Environment.NewLine);
        async Task Until(Func<bool> ready)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (ready()) return;
                await Task.Delay(30);
            }
            throw new TimeoutException("Home/category fixture did not reach the expected UI state.");
        }

        try
        {
            MainVM.Sites = new() { first, second };
            MainVM.BrowseSite = first;
            await DatabaseHelper.InitializeDatabase();
            DatabaseHelper.AddHistory(new HistoryItem { SiteName = first.Name, RoomID = "recent", UserName = "测试主播", Photo = "" });
            var home = new Views.RecomendPage();
            var button = (Microsoft.UI.Xaml.Controls.Button)home.FindName("ResumeButton");
            window.Root.Content = home;
            window.Activate();
            await Until(() => button.Visibility == Microsoft.UI.Xaml.Visibility.Visible);
            Check(((Microsoft.UI.Xaml.Controls.TextBlock)home.FindName("RecentTitle")).Text == "测试直播间", "Resume did not use real room metadata.");
            var recent = (await DatabaseHelper.GetHistory(1)).Single();
            Check(recent.RoomID == "recent" && primary.LastDetailId == "recent", "Resume did not look up the latest stored room.");
            Pass("latest history and confirmed live room show the actual resume card");

            window.Root.Content = new Microsoft.UI.Xaml.Controls.Page();
            await Until(() => !home.IsLoaded);
            primary.PreviewLive = false;
            window.Root.Content = home;
            await Until(() => home.IsLoaded && primary.DetailCalls >= 2);
            Check(button.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed, "Stopped history room remained visible.");
            Pass("offline room hides the card");

            var directory = new Views.CategoryPage();
            var grid = (Microsoft.UI.Xaml.Controls.GridView)directory.FindName("GamesGrid");
            window.Root.Content = directory;
            await Until(() => grid.Items.Count == 2);
            var localSearch = (Microsoft.UI.Xaml.Controls.AutoSuggestBox)directory.FindName("GameFilter");
            localSearch.Text = "测试游戏";
            await Until(() => grid.Items.Count == 1);
            var game = (LiveGameCategory)grid.Items[0];
            Check(game.Sources.Count == 2 && game.Pic == secondary.Groups[0].Children[0].Pic, "Merged game lost one platform or its available artwork.");
            Pass("game-first directory merges exact matches, preserves both IDs and searches locally");

            var frame = new Microsoft.UI.Xaml.Controls.Frame();
            window.Root.Content = frame;
            Check(frame.Navigate(typeof(Views.CategoryDetailPage), game), "Game detail navigation failed.");
            var detail = (Views.CategoryDetailPage)frame.Content;
            var platforms = (Microsoft.UI.Xaml.Controls.ListView)detail.FindName("GamePlatforms");
            var list = ((Microsoft.UI.Xaml.Controls.Grid)detail.Content).Children.OfType<Microsoft.UI.Xaml.Controls.GridView>().Single();
            await Until(() => list.Items.Count == 2);
            platforms.SelectedIndex = 2;
            await Until(() => list.Items.Count == 1 && ((LiveRoomItem)list.Items[0]).Source == secondary);
            Check(primary.LastPartition == "game" && secondary.LastPartition == "same-game-second-partition" &&
                ((SearchRoomItem)list.Items[0]).PlatformLabel == secondary.PreviewName, "Platform filtering changed its partition or lost the platform badge.");
            Pass("actual native platform picker switches rooms and keeps platform badges and original IDs");


            var random = (Microsoft.UI.Xaml.Controls.Button)detail.FindName("GameRandomButton");
            var navigation = new TaskCompletionSource<PageArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Capture(Type type, object data) { if (type == typeof(Views.LiveRoomPage)) navigation.TrySetResult((PageArgs)data); }
            void InvokeRandom() => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(random)
                    .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            MessageCenter.NavigatePageEvent += Capture;
            try
            {
                secondary.PendingRooms = new TaskCompletionSource<LiveCategoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                InvokeRandom();
                await Until(() => !random.IsEnabled);
                platforms.SelectedIndex = 1;
                await Until(() => list.Items.Count == 1 && ((LiveRoomItem)list.Items[0]).Source == primary && random.IsEnabled);
                secondary.PendingRooms.SetResult(new LiveCategoryResult { Rooms = new() { new() { RoomID = "stale", IsLive = true } } });
                secondary.PendingRooms = null;
                await Task.Delay(75);
                Check(!navigation.Task.IsCompleted, "A canceled platform pick opened a stale room.");

                platforms.SelectedIndex = 2;
                await Until(() => list.Items.Count == 1 && ((LiveRoomItem)list.Items[0]).Source == secondary);
                InvokeRandom();
                var opened = await navigation.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Check(opened.Site == secondary && opened.RandomSession != null &&
                    ((LiveRoomItem)opened.Data).RoomID == "shared-id" && secondary.LastPartition == "same-game-second-partition",
                    "Native game random button did not pass the selected game and platform to the player.");
                opened.RandomSession.End();
            }
            finally { MessageCenter.NavigatePageEvent -= Capture; }
            Pass("native game random button cancels stale picks and opens the selected platform with a random session");

            window.Root.Content = directory;
            await Until(() => directory.IsLoaded && grid.Items.Count == 1);
            Check(localSearch.Text == "测试游戏" && ReferenceEquals(game, grid.Items[0]), "Reopening the directory lost the filter or reloaded its cards.");
            Pass("returning preserves the game search and the card instance");
            Environment.ExitCode = 0;
        }
        finally
        {
            window.Root.Content = null;
            MainVM.Sites = oldSites;
            MainVM.BrowseSite = oldSite;
        }
    }

    internal static async Task RefreshFeedbackAsync()
    {
        var oldSites = MainVM.Sites; var oldSite = MainVM.BrowseSite;
        var api = new CatalogSite();
        var site = new Site { SiteType = oldSites[0].SiteType, LiveSite = api, Name = oldSites[0].Name };
        try
        {
            MainVM.Sites = new() { site }; MainVM.BrowseSite = site;
            var vm = new CategoryVM(); await vm.LoadAsync();
            var retained = vm.Items.ToArray(); var selected = vm.SelectedGroup;
            var pending = new TaskCompletionSource<List<LiveCategory>>(); api.Pending = pending.Task;
            var refresh = vm.RefreshAsync();
            Check(vm.Items.SequenceEqual(retained) && vm.SelectedGroup == selected && vm.Loading && !vm.CanRefresh,
                "Category refresh cleared the current group before a response");
            vm.Refresh();
            pending.SetException(new System.Net.Http.HttpRequestException("Isolated category refresh failure"));
            await refresh;
            Check(vm.Items.SequenceEqual(retained) && vm.SelectedGroup == selected && vm.CanRetry && vm.ShowInlineStatus,
                "Category error lost retained content or retry feedback");
            api.Pending = null;
            await vm.RefreshAsync();
            Check(vm.Items.Count > 0 && !vm.CanRetry && !vm.ShowInlineStatus && vm.SelectedGroup.ID == selected.ID,
                "Retry did not restore the selected category cleanly");
            api.Pending = Task.FromResult(new List<LiveCategory>());
            await vm.RefreshAsync();
            Check(vm.Items.Count > 0 && vm.CanRetry && vm.SelectedGroup.ID == selected.ID,
                "An unavailable platform directory discarded the retained category");
            api.Pending = Task.FromResult(new List<LiveCategory>
            {
                new() { ID = selected.ID, Name = selected.Name, Children = new() },
                new() { ID = "3", Name = "手游", Children = new() { new() { ID = "mobile", ParentID = "3", Name = "测试手游" } } }
            });
            await vm.RefreshAsync();
            Check(vm.Items.Count == 0 && vm.IsEmpty && !vm.CanRetry && vm.ShowEmptyStatus,
                "A valid empty game group was confused with a request error");
        }
        finally { MainVM.Sites = oldSites; MainVM.BrowseSite = oldSite; GameCatalog.Invalidate(api); }
    }

    internal static void SearchStatuses()
    {
        foreach (var type in new[] { typeof(YolanLive.Core.BiliBili), typeof(YolanLive.Core.Douyin) })
        {
            var parse = type.GetMethod("ReadSearchLiveStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            bool? Parse(Newtonsoft.Json.Linq.JToken token) => (bool?)parse.Invoke(null, new object[] { token });
            Check(Parse(null) == null && Parse(Newtonsoft.Json.Linq.JValue.CreateNull()) == null &&
                Parse(new Newtonsoft.Json.Linq.JValue("invalid")) == null && Parse(new Newtonsoft.Json.Linq.JValue(999)) == null,
                "Unknown raw search state was labeled as live/offline.");
            if (type == typeof(YolanLive.Core.BiliBili))
                Check(Parse(new Newtonsoft.Json.Linq.JValue(1)) == true && Parse(new Newtonsoft.Json.Linq.JValue(0)) == false && Parse(new Newtonsoft.Json.Linq.JValue(2)) == false, "Bilibili live/replay codes were misread.");
            else
                Check(Parse(new Newtonsoft.Json.Linq.JValue(2)) == true && Parse(new Newtonsoft.Json.Linq.JValue(4)) == false && Parse(new Newtonsoft.Json.Linq.JValue(0)) == null, "Douyin live/offline codes were misread.");
        }
    }

    internal static async Task SearchAsync()
    {
        var original = MainVM.Sites;
        var bili = new CatalogSite(); var douyin = new SearchDouyin();
        try
        {
            MainVM.Sites = new() { new() { LiveSite = bili }, new() { LiveSite = douyin } };
            var vm = new SearchVM(); await vm.SearchAsync("测试");
            Check(vm.Items.Count == 6 && vm.Items.All(r => r is SearchRoomItem), "Search metadata lost a result.");
            foreach (var room in vm.Items.Cast<SearchRoomItem>())
            {
                Check(room.PlatformLabel == (room.Source == bili ? "哔哩哔哩" : "抖音"), "Search platform differs from playback source.");
                Check(room.StatusLabel == (room.IsLive.HasValue ? room.IsLive.Value ? "直播中" : "未开播" : null), "Unknown live state was inferred.");
                Check(room.RoomID != null && room.Title == "测试标题" && room.UserName == "测试主播" && room.Cover == "test-cover" && room.Online == 42, "Search wrapper lost room data.");
            }
            Check(bili.SearchCalls == 1 && douyin.SearchCalls == 1 && bili.StatusCalls == 0 && douyin.StatusCalls == 0, "Search badges requested extra status queries.");
        }
        finally { MainVM.Sites = original; }
    }

    internal static async Task RestartAsync()
    {
        var original = MainVM.BrowseSite; var site = MainVM.Sites[1];
        var pins = BrowsePreferences.ReadPins(site);
        try
        {
            MainVM.BrowseSite = site;
            SettingHelper.SetValue(BrowsePreferences.GroupKey(site), "103,4");
            SettingHelper.SetValue(BrowsePreferences.PinsKey(site), new[] { new PinnedGame { Name = "重启测试", Sources = new() { new() { ParentID = "103,4", ID = "restart-game" } } } });
            var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--smoke-test"); start.ArgumentList.Add("--browse-preferences-check");
            start.Environment["YOLANLIVE_SMOKE_PROFILE"] = AppPaths.DataDirectory;
            using var process = Process.Start(start);
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
            catch { if (!process.HasExited) process.Kill(); throw; }
            Check(process.ExitCode == 0 && File.ReadAllText(Path.Combine(AppPaths.DataDirectory, "browse-restore-check.txt")) == "PASS", "A fresh process did not restore browse preferences.");
        }
        finally { SettingHelper.SetValue(BrowsePreferences.PinsKey(site), pins); MainVM.BrowseSite = original; }
    }
    internal static void VerifyRestart()
    {
        var site = MainVM.BrowseSite;
        Check(site == MainVM.Sites[1] && SettingHelper.GetValue(BrowsePreferences.GroupKey(site), "") == "103,4", "Platform/group not restored at startup.");
        var pins = BrowsePreferences.ReadPins(site);
        Check(pins.Count == 1 && pins[0].Name == "重启测试" && pins[0].Sources[0].ID == "restart-game", "Pins not restored at startup.");
        File.WriteAllText(Path.Combine(AppPaths.DataDirectory, "browse-restore-check.txt"), "PASS");
    }

    private static Task<LiveSearchResult> SearchResults() => Task.FromResult(new LiveSearchResult { HasMore = false,
        Rooms = new bool?[] { true, false, null }.Select((live, index) => new LiveRoomItem { RoomID = index.ToString(), Title = "测试标题", UserName = "测试主播", Cover = "test-cover", Online = 42, IsLive = live }).ToList() });
    internal sealed class CatalogSite : YolanLive.Core.BiliBili, ILiveSite
    {
        internal string PreviewName { get; init; } = "哔哩哔哩直播";
        string ILiveSite.Name => PreviewName;
        internal bool PreviewLive = true;
        internal string LastDetailId, LastPartition;
        internal int DetailCalls;
        internal TaskCompletionSource<LiveCategoryResult> PendingRooms;
        public new Task<LiveRoomDetail> GetRoomDetail(object id, System.Threading.CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            LastDetailId = id.ToString();
            DetailCalls++;
            return Task.FromResult(new LiveRoomDetail { RoomID = id.ToString(), Status = PreviewLive, Title = "测试直播间", UserName = "测试主播" });
        }
        public new Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, System.Threading.CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            LastPartition = category.ID;
            return PendingRooms?.Task ?? Task.FromResult(new LiveCategoryResult { Rooms = new() { new LiveRoomItem { RoomID = "shared-id", IsLive = true } } });
        }
        internal Task<List<LiveCategory>> Pending;
        internal int SearchCalls, StatusCalls;
        internal List<LiveCategory> Groups = new() { new() { ID = "2", Name = "网游", Children = new() { new() { ID = "game", ParentID = "2", Name = "测试游戏" } } }, new() { ID = "3", Name = "手游", Children = new() { new() { ID = "mobile", ParentID = "3", Name = "测试手游" } } } };
        public new Task<List<LiveCategory>> GetCategores(System.Threading.CancellationToken cancellationToken) => GetCategores().WaitAsync(cancellationToken);
        public new Task<List<LiveCategory>> GetCategores() => Pending ?? Task.FromResult(Groups);
        public new Task<LiveSearchResult> Search(string keyword, int page, System.Threading.CancellationToken cancellationToken) => Search(keyword, page).WaitAsync(cancellationToken);
        public new Task<LiveSearchResult> Search(string keyword, int page = 1) { SearchCalls++; return SearchResults(); }
        public new Task<bool> GetLiveStatus(object roomId, System.Threading.CancellationToken cancellationToken) => GetLiveStatus(roomId).WaitAsync(cancellationToken);
        public new Task<bool> GetLiveStatus(object roomId) { StatusCalls++; throw new Exception("No extra status requests allowed."); }
    }
    private sealed class SearchDouyin : YolanLive.Core.Douyin, ILiveSite
    {
        internal int SearchCalls, StatusCalls;
        public new Task<LiveSearchResult> Search(string keyword, int page, System.Threading.CancellationToken cancellationToken) => Search(keyword, page).WaitAsync(cancellationToken);
        public new Task<LiveSearchResult> Search(string keyword, int page = 1) { SearchCalls++; return SearchResults(); }
        public new Task<bool> GetLiveStatus(object roomId, System.Threading.CancellationToken cancellationToken) => GetLiveStatus(roomId).WaitAsync(cancellationToken);
        public new Task<bool> GetLiveStatus(object roomId) { StatusCalls++; throw new Exception("No extra status requests allowed."); }
    }
}
