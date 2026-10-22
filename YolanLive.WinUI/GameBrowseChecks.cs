using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace YolanLive.WinUI;
internal static class GameBrowseChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task RefreshFeedbackAsync()
    {
        var site = new TestSite("刷新测试");
        var vm = new TestList(new() { new() { Site = site, Category = new() { ID = "game" } } });
        await vm.LoadAsync();
        var retained = vm.Items.Single();
        var pending = new TaskCompletionSource<LiveCategoryResult>();
        site.Pending = pending.Task;
        var refresh = vm.RefreshAsync();
        Check(vm.Loading && !vm.CanRefresh && vm.Items.Single() == retained && !vm.ShowEmptyStatus,
            "Refreshing cleared or disabled the retained content");
        var calls = site.Calls;
        vm.Refresh(); await vm.RefreshAsync();
        Check(site.Calls == calls, "Repeated refresh submitted another request");
        pending.SetException(new System.Net.Http.HttpRequestException("Isolated refresh failure"));
        await refresh;
        Check(vm.Items.Single() == retained && vm.CanRetry && vm.ShowInlineStatus && vm.StatusMessage.Contains("保留"),
            "Failed refresh lost the old content or its retry feedback");
        site.Pending = null;
        await vm.RefreshAsync();
        Check(vm.Items.Count == 1 && vm.Items[0] != retained && !vm.CanRetry && !vm.ShowInlineStatus,
            "Retry appended instead of replacing, or left an error visible");
        retained = vm.Items.Single();
        pending = new(); site.Pending = pending.Task;
        refresh = vm.RefreshAsync(); vm.CancelPending();
        pending.SetResult(new() { Rooms = new() { new() { RoomID = "stale", IsLive = true } } });
        await refresh;
        Check(vm.Items.Single() == retained && !vm.Loading && !vm.CanRetry, "Cancelled refresh committed stale content or reported an error");
        site.Pending = null; site.Empty = true;
        await vm.RefreshAsync();
        Check(vm.IsEmpty && vm.ShowEmptyStatus && !vm.CanRetry && vm.Items.Count == 0, "Successful empty result looked like a failed request");
        site.Empty = false;
        for (var i = 0; i < 300; i++) vm.Items.Add(new() { RoomID = "cap-" + i, Source = site, IsLive = true });
        await vm.RefreshAsync();
        Check(vm.Items.Count == 1 && vm.Items[0].RoomID == "same", "Room cap prevented a fresh replacement");
    }
    public static async Task RunAsync()
    {
        var a = new TestSite("A");
        var b = new TestSite("B");
        var catalogSite = new TestCatalogSite();
        var selectedSources = await GameCatalog.LoadAsync(catalogSite);
        Check(selectedSources.Count == 1 && selectedSources.All(source => source.Site == catalogSite), "Game catalog did not restrict results to the selected platform.");
        catalogSite.Fail = true; GameCatalog.Invalidate(catalogSite);
        try { await GameCatalog.LoadAsync(catalogSite); throw new Exception("Catalog swallowed a platform failure."); }
        catch (InvalidOperationException ex) when (ex.Message == "Test category unavailable") { }
        var sources = new List<LiveGameSource> { new() { Site = a, Category = new() { ID = "game" } }, new() { Site = b, Category = new() { ID = "game" } } };
        var vm = new TestList(sources);
        await vm.LoadAsync();
        Check(vm.Items.Count == 2 && vm.Items.Select(r => r.Source.Name).Distinct().Count() == 2, "Cross-platform room IDs were collapsed or offline room was shown.");
        Check(vm.Items.All(r => r.IsLive == true), "Unknown/offline rooms were shown.");
        await vm.LoadAsync();
        Check(vm.Items.Count == 4 && a.Pages.SequenceEqual(new[] { 1, 2 }) && b.Pages.SequenceEqual(new[] { 1, 2 }), "Independent pagination failed.");
        var beforeReturn = vm.Items.ToArray();
        vm.CancelPending();
        Check(vm.HasLoaded, "Leaving a loaded list invalidated the retained browse state.");
        await vm.HideStoppedAsync();
        Check(vm.Items.SequenceEqual(beforeReturn) && a.Pages.Count == 2 && b.Pages.Count == 2, "Returning replaced the list or restarted pagination.");
        a.Live = false;
        await vm.HideStoppedAsync();
        Check(vm.Items.Count == 2 && vm.Items.All(r => r.Source == b), "Stopped rooms remained visible.");
        a.Fail = true;
        vm.Reset();
        await vm.LoadAsync();
        Check(vm.Items.Count == 1 && vm.Items[0].Source == b, "One failed platform suppressed the working platform.");
        b.Fail = true;
        vm.Reset();
        await vm.LoadAsync();
        Check(vm.IsEmpty && vm.EmptyMessage.Contains("失败"), "Failed loading looked like an empty live list.");
        a.Fail = b.Fail = false;
        a.Empty = b.Empty = true;
        vm.Reset();
        await vm.LoadAsync();
        Check(vm.IsEmpty && !vm.EmptyMessage.Contains("失败"), "A successful empty list looked like a network error.");
        a.Empty = b.Empty = false;
        var switching = new SwitchingList(sources[0], sources[1]);
        var oldLoad = switching.LoadAsync();
        switching.Reset();
        await switching.LoadAsync();
        switching.Release();
        await oldLoad;
        Check(switching.Items.Count == 1 && switching.Items[0].Source == b && !switching.Loading, "A stale response overwrote the new query.");

        var original = MainVM.Sites;
        try
        {
            MainVM.Sites = new() { new() { Name = "A", LiveSite = a }, new() { Name = "B", LiveSite = b } };
            a.Fail = false;
            await DatabaseHelper.InitializeDatabase();
            DatabaseHelper.AddFavorite(new FavoriteItem { SiteName = "A", RoomID = "same", UserName = "offline", Photo = "" });
            DatabaseHelper.AddFavorite(new FavoriteItem { SiteName = "B", RoomID = "same", UserName = "live", Photo = "" });
            var favorites = new FavoriteVM();
            await favorites.LoadAsync();
            Check(favorites.Items.Count == 1 && favorites.Items[0].SiteName == "B", "Stopped favorite was not hidden.");
            Check((await DatabaseHelper.GetFavorites()).Count == 2, "Hidden favorite was deleted.");
            a.Live = true;
            await favorites.LoadAsync();
            Check(favorites.Items.Count == 2, "Favorite did not reappear when live again.");
            a.StatusFail = b.StatusFail = true;
            await favorites.LoadAsync();
            Check(favorites.Items.Count == 2 && favorites.Items.All(item => !item.LiveStatus) && favorites.CanRetry && favorites.EmptyMessage.Contains("失败") && (await DatabaseHelper.GetFavorites()).Count == 2, "Failed favorite queries lost retained items, guessed live status, or changed saved records.");
            await CheckQualityPreferencesAsync();
        }
        finally { MainVM.Sites = original; }
    }
    private static async Task CheckQualityPreferencesAsync()
    {
        var bili = new TestSite("哔哩哔哩直播");
        var douyin = new TestSite("抖音直播");
        douyin.QualityOptions[1].Quality = "流畅";
        var first = new LiveRoomVM();
        await first.LoadDataAsync(bili, "quality-test");
        first.CurrentQuality = bili.QualityOptions[1];
        var second = new LiveRoomVM();
        await second.LoadDataAsync(douyin, "quality-test");
        Check(second.CurrentQuality == douyin.QualityOptions[0], "Bilibili quality preference leaked into Douyin.");
        second.CurrentQuality = douyin.QualityOptions[1];
        Check(LiveRoomVM.GetPreferredQuality(douyin, douyin.QualityOptions) == douyin.QualityOptions[1], "Douyin quality selection was not saved.");
        var returning = new LiveRoomVM();
        await returning.LoadDataAsync(bili, "quality-test");
        Check(returning.CurrentQuality == bili.QualityOptions[1], "Preferred quality was not restored in a new room model.");
        bili.QualityOptions.RemoveAt(1);
        await returning.LoadDataAsync(bili, "quality-test");
        Check(returning.CurrentQuality == bili.QualityOptions[0] && SettingHelper.GetValue(SettingHelper.PREFERRED_QUALITY + bili.Name, "") == "高清", "Fallback overwrote the saved quality preference.");
    }
    private class TestList : LiveRoomListVM
    {
        readonly List<LiveGameSource> sources;
        public TestList(List<LiveGameSource> sources) { this.sources = sources; }
        protected override Task<List<LiveGameSource>> GetSourcesAsync(System.Threading.CancellationToken cancellationToken) => Task.FromResult(sources);
    }
    private class SwitchingList : LiveRoomListVM
    {
        readonly LiveGameSource oldSource, newSource;
        readonly TaskCompletionSource<List<LiveGameSource>> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls;
        public SwitchingList(LiveGameSource oldSource, LiveGameSource newSource) { this.oldSource = oldSource; this.newSource = newSource; }
        protected override Task<List<LiveGameSource>> GetSourcesAsync(System.Threading.CancellationToken cancellationToken) => calls++ == 0 ? pending.Task : Task.FromResult(new List<LiveGameSource> { newSource });
        public void Release() => pending.SetResult(new() { oldSource });
    }
    private class TestCatalogSite : YolanLive.Core.BiliBili, ILiveSite
    {
        public bool Fail;
        public new Task<List<LiveCategory>> GetCategores(System.Threading.CancellationToken cancellationToken) => GetCategores().WaitAsync(cancellationToken);
        public new Task<List<LiveCategory>> GetCategores() => Fail
            ? Task.FromException<List<LiveCategory>>(new InvalidOperationException("Test category unavailable"))
            : Task.FromResult(new List<LiveCategory> { new() { ID = "2", Name = "网游", Children = new() { new() { ID = "game", Name = "Test game" } } } });
    }
    private class TestSite : ILiveSite
    {
        public string Name { get; }
        public bool Live = true;
        public bool Fail;
        public bool Empty;
        public bool StatusFail;
        public Task<LiveCategoryResult> Pending;
        public int Calls;
        public List<LivePlayQuality> QualityOptions { get; } = new() { new() { Quality = "原画" }, new() { Quality = "高清" } };
        public List<int> Pages { get; } = new();
        public TestSite(string name) { Name = name; }
        public Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, System.Threading.CancellationToken cancellationToken) => GetCategoryRooms(category, page).WaitAsync(cancellationToken);
        public Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page = 1)
        {
            Calls++;
            if (Pending != null) return Pending;
            if (Fail) throw new Exception("Test endpoint unavailable");
            Pages.Add(page);
            if (Empty) return Task.FromResult(new LiveCategoryResult { HasMore = false, Rooms = new() });
            var room = new LiveRoomItem { RoomID = page == 1 ? "same" : "next", IsLive = true };
            return Task.FromResult(new LiveCategoryResult { HasMore = page < 2, Rooms = new() { room, room, new() { RoomID = "offline", IsLive = false }, new() { RoomID = "unknown" } } });
        }
        public Task<bool> GetLiveStatus(object roomId, System.Threading.CancellationToken cancellationToken) => GetLiveStatus(roomId).WaitAsync(cancellationToken);
        public Task<bool> GetLiveStatus(object roomId) => StatusFail ? Task.FromException<bool>(new Exception("Test status unavailable")) : Task.FromResult(Live);
        public Task<List<LiveCategory>> GetCategores(System.Threading.CancellationToken cancellationToken) => GetCategores().WaitAsync(cancellationToken);
        public Task<List<LiveCategory>> GetCategores() => throw new NotSupportedException();
        public Task<LiveSearchResult> Search(string keyword, int page, System.Threading.CancellationToken cancellationToken) => Search(keyword, page).WaitAsync(cancellationToken);
        public Task<LiveSearchResult> Search(string keyword, int page = 1) => throw new NotSupportedException();
        public Task<LiveCategoryResult> GetRecommendRooms(int page, System.Threading.CancellationToken cancellationToken) => GetRecommendRooms(page).WaitAsync(cancellationToken);
        public Task<LiveCategoryResult> GetRecommendRooms(int page = 1) => throw new NotSupportedException();
        public Task<LiveRoomDetail> GetRoomDetail(object roomId, System.Threading.CancellationToken cancellationToken) => GetRoomDetail(roomId).WaitAsync(cancellationToken);
        public Task<LiveRoomDetail> GetRoomDetail(object roomId) => Task.FromResult(new LiveRoomDetail { RoomID = roomId.ToString(), Title = "Test", UserName = "Test", Status = true });
        public Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail roomDetail, System.Threading.CancellationToken cancellationToken) => GetPlayQuality(roomDetail).WaitAsync(cancellationToken);
        public Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail roomDetail) => Task.FromResult(QualityOptions);
        public Task<List<string>> GetPlayUrls(LiveRoomDetail roomDetail, LivePlayQuality qn, System.Threading.CancellationToken cancellationToken) => GetPlayUrls(roomDetail, qn).WaitAsync(cancellationToken);
        public Task<List<string>> GetPlayUrls(LiveRoomDetail roomDetail, LivePlayQuality qn) => Task.FromResult(new List<string> { "https://example.invalid/test-stream" });
    }
}
