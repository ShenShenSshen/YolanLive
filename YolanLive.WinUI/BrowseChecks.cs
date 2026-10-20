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
            foreach (var picker in new[] { (Microsoft.UI.Xaml.Controls.ListView)home.FindName("PlatformPicker"), (Microsoft.UI.Xaml.Controls.ListView)page.FindName("PlatformPicker") })
                Check(picker.SelectedItem == picker.Items[1], "Saved platform was not selected before the page loaded.");
            ((Microsoft.UI.Xaml.Controls.ListView)home.FindName("PlatformPicker")).SelectedItem = ((Microsoft.UI.Xaml.Controls.ListView)home.FindName("PlatformPicker")).Items[0];
            ((Microsoft.UI.Xaml.Controls.ListView)page.FindName("PlatformPicker")).SelectedItem = ((Microsoft.UI.Xaml.Controls.ListView)page.FindName("PlatformPicker")).Items[0];
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
    private sealed class CatalogSite : YolanLive.Core.BiliBili, ILiveSite
    {
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
