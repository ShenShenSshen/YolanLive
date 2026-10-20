using YolanLive.Core;
using YolanLive.Core.Helper;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Controls;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FFmpegInteropX;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Playback;
namespace YolanLive.WinUI;
internal static class PlatformChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task StructureAsync()
    {
        Check(MainVM.Sites.Select(s => s.SiteType).SequenceEqual(new[] { LiveSite.Bilibili, LiveSite.Douyin, LiveSite.Huya, LiveSite.Douyu }), "Platform order or identifiers changed.");
        foreach (var platform in MainVM.Sites.Skip(2))
        {
            var categories = new[] { new LiveCategory { ID = "1", Name = "游戏", Children = new() { new() { ID = "0", ParentID = "1", Name = "全部游戏" } } }, new LiveCategory { ID = "2", Name = "娱乐", Children = new() } };
            if (platform.SiteType == LiveSite.Huya) categories[1].ID = "8";
            var selected = GameBrowse.SelectCategories(platform.LiveSite, categories);
            Check(selected.Count == 1 && GameBrowse.OverviewSources(selected.SelectMany(g => g.Children.Select(c => new LiveGameSource { Site = platform.LiveSite, Category = c }))).Count == 1, "Game-only filtering or overview source failed.");
            var room = new SearchRoomItem(new LiveRoomItem { RoomID = "123", IsLive = null }, platform.LiveSite);
            Check(room.PlatformLabel == platform.Label && room.StatusLabel == null, "Search platform badge or unknown state was incorrect.");
            var original = MainVM.BrowseSite;
            MainVM.BrowseSite = platform;
            Check(BrowsePreferences.ReadPlatform() == platform, "New platform selection did not persist.");
            MainVM.BrowseSite = original;
            SettingHelper.SetValue(BrowsePreferences.PinsKey(platform), new List<PinnedGame>());
            for (int i = 0; i < 6; i++) Check(BrowsePreferences.TogglePin(platform, BrowseChecks.Game(platform, "pin-" + i)), "New platform pin failed.");
            Check(!BrowsePreferences.TogglePin(platform, BrowseChecks.Game(platform, "seventh")) && BrowsePreferences.ReadPins(platform).Count == 6, "New platform pin limit failed.");
            SettingHelper.SetValue(BrowsePreferences.PinsKey(platform), new List<PinnedGame>());
        }
        Check((await SiteParser.ParseUrl("https://www.huya.com/123?from=share")).Item1 == LiveSite.Huya && (await SiteParser.ParseUrl("https://www.douyu.com/456")).Item1 == LiveSite.Douyu, "New room URLs were not parsed.");
        Check((await SiteParser.ParseUrl("https://www.huya.com.attacker.invalid/123")).Item1 == LiveSite.Unknown, "Foreign host was accepted as a room URL.");
        var huya = new Huya { RequestAsync = (_, _) => Task.FromResult("{\"status\":200,\"data\":{\"page\":1,\"totalPage\":2,\"datas\":[{\"profileRoom\":\"123\",\"bussType\":1},{\"profileRoom\":\"456\",\"bussType\":8}]}}") };
        var hRooms = await huya.GetRecommendRooms();
        Check(hRooms.HasMore && hRooms.Rooms.Count == 1 && hRooms.Rooms[0].IsLive == true, "Huya page/game/live parsing failed.");
        var douyu = new Douyu { RequestAsync = (_, _) => Task.FromResult("{\"error\":0,\"data\":{\"pgcnt\":2,\"rl\":[{\"type\":1,\"rid\":\"123\",\"cid1\":1},{\"type\":1,\"rid\":\"456\",\"cid1\":2},{\"type\":2,\"rid\":\"789\",\"cid1\":1}]}}") };
        var dRooms = await douyu.GetRecommendRooms();
        Check(dRooms.HasMore && dRooms.Rooms.Count == 1 && dRooms.Rooms[0].IsLive == true, "Douyu page/game/live parsing failed.");
    }
    internal static async Task MissingStatusAsync()
    {
        ILiveSite[] sites = { new Huya { RequestAsync = (_, _) => Task.FromResult("<script>window.HNF_GLOBAL_INIT={\"roomInfo\":{}};</script>") }, new Douyu { RequestAsync = (_, _) => Task.FromResult("{\"error\":0,\"room\":{}}") } };
        foreach (var site in sites)
        {
            try { await site.GetLiveStatus("123"); throw new Exception("Unknown status was treated as stopped."); }
            catch (LiveRequestException error) when (error.Kind == LiveFailureKind.Platform) { }
        }
    }
    internal static async Task LoginAsync(MainWindow window)
    {
        foreach (var platform in new[] { LiveSite.Huya, LiveSite.Douyu })
        {
            Check(WebLoginDialog.IsTrusted(platform, WebLoginDialog.Home(platform)) && !WebLoginDialog.IsTrusted(platform, "https://www." + platform.ToString().ToLowerInvariant() + ".com.attacker.invalid/"), "Login domain validation failed.");
            var dialog = new WebLoginDialog(platform) { XamlRoot = window.Root.XamlRoot };
            var showing = dialog.ShowAsync().AsTask();
            try
            {
                for(int i = 0; i < 100 && !dialog.IsPrimaryButtonEnabled; i++) await Task.Delay(100);
                Check(dialog.IsPrimaryButtonEnabled && dialog.Browser.CoreWebView2.IsMuted, "Independent login browser failed or is audible.");
                var manager = dialog.Browser.CoreWebView2.CookieManager;
                var domain = platform == LiveSite.Huya ? ".huya.com" : ".douyu.com";
                foreach(var name in platform == LiveSite.Huya ? new[] { "udb_uid", "udb_biztoken" } : new[] { "acf_uid", "acf_auth" }) manager.AddOrUpdateCookie(manager.CreateCookie(name, "123-test-only-not-a-real-session", domain, "/"));
                await dialog.SaveSessionAsync();
                Check(WebAccount.For(platform).Logined && !SettingHelper.GetValue(WebAccount.For(platform).Key + "Cookie", "").Contains("test-only"), "Session was not protected.");
                await WebAccount.For(platform).InitAsync();
                Check(WebAccount.For(platform).Logined && WebAccount.For(platform).StatusMessage.Contains("尚未"), "Saved session was not restored or falsely verified.");
            }
            finally { dialog.Hide(); await showing; }
        }
        Check(WebLoginDialog.Profile(LiveSite.Huya) != WebLoginDialog.Profile(LiveSite.Douyu), "Browser profiles are shared.");
        WebAccount.For(LiveSite.Huya).MarkExpired();
        Check(!WebAccount.For(LiveSite.Huya).Logined && WebAccount.For(LiveSite.Douyu).Logined, "One account expiration affected another platform.");
        var account = WebAccount.For(LiveSite.Douyu);
        RequestErrorMessage.Report(new LiveRequestException(LiveFailureKind.Network, "test"), "斗鱼直播");
        Check(account.Logined && account.Saved, "Network failure deleted login.");
        WebAccount.For(LiveSite.Huya).Logout();
        await WebLoginDialog.ClearCookiesAsync(LiveSite.Huya);
        Check(account.Logined, "One logout affected another platform.");
        account.Logout(); await WebLoginDialog.ClearCookiesAsync(LiveSite.Douyu);
    }
    internal static async Task LiveAsync(MainWindow window)
    {
        var report = new List<string>(); var failed = false;
        foreach (var site in MainVM.Sites.Select(s => s.LiveSite).Where(s => s is Huya || s is Douyu))
        {
            try
            {
                var categories = GameBrowse.SelectCategories(site, await site.GetCategores());
                Check(categories.Count > 0 && categories.All(c => c.Children.Count > 0), "No game categories.");
                var home = await site.GetRecommendRooms();
                Check(home.Rooms.Count > 0 && home.Rooms.All(r => r.IsLive == true), "Game homepage returned no live rooms.");
                report.Add("PASS " + site.Name + " game homepage rooms=" + home.Rooms.Count);
                var category = categories.SelectMany(c => c.Children).First(c => c.ID != "0");
                var rooms = await site.GetCategoryRooms(category);
                Check(rooms.Rooms.Count > 0, "No game rooms.");
                var room = await site.GetRoomDetail(rooms.Rooms[0].RoomID);
                Check(room.Status, "Selected public room stopped.");
                report.Add("PASS " + site.Name + " game categories=" + categories.Count + " rooms=" + rooms.Rooms.Count + " room=" + room.RoomID);
                var search = await site.Search(room.UserName);
                Check(search.Rooms.Count > 0, "Search returned no rooms.");
                report.Add("PASS " + site.Name + " public search rooms=" + search.Rooms.Count);
                var qualities = await site.GetPlayQuality(room);
                Check(qualities.Count > 0, "No qualities.");
                foreach (var quality in qualities.Take(2))
                {
                    var urls = await site.GetPlayUrls(room, quality);
                    Check(urls.Count > 0, "No stream URLs.");
                    int played = 0; Exception last = null;
                    foreach (var url in urls.Take(2))
                    {
                        try { await DecodeAsync(window, url, room.Url); played++; }
                        catch (Exception ex) { last = ex; }
                    }
                    if (played == 0) throw last ?? new Exception("No playable lines.");
                    report.Add("PASS " + site.Name + " quality=" + quality.Quality + " decodedLines=" + played + " availableLines=" + urls.Count);
                }
            }
            catch (Exception ex) { failed = true; report.Add("FAIL " + site.Name + " " + ex.GetType().Name + " " + System.Text.RegularExpressions.Regex.Replace(ex.Message, @"https?://\S+", "[URL]")); }
        }
        File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "platform-live.log"), report);
        Environment.ExitCode = failed ? 1 : 0;
    }
    internal static async Task DecodeAsync(MainWindow window, string url, string referer)
    {
        var config = new MediaSourceConfig(); config.FFmpegOptions.Add("rw_timeout", "10000000"); config.FFmpegOptions.Add("referer", referer);
        if (referer.Contains("bilibili.com", StringComparison.OrdinalIgnoreCase)) config.FFmpegOptions.Add("user_agent", "Mozilla/5.0 BiliDroid/1.12.0 (bbcallen@gmail.com)");
        using var source = await FFmpegMediaSource.CreateFromUriAsync(url, config).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
        using var player = new MediaPlayer { IsMuted = true, AutoPlay = true };
        var opened = new TaskCompletionSource<bool>();
        player.MediaOpened += (_, _) => opened.TrySetResult(true);
        player.MediaFailed += (_, e) => opened.TrySetException(new Exception("Decoder: " + e.Error));
        var element = new MediaPlayerElement { AreTransportControlsEnabled = false }; element.SetMediaPlayer(player); window.Root.Content = element;
        player.Source = source.CreateMediaPlaybackItem(); window.Activate();
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(1800);
        Check(player.PlaybackSession.NaturalVideoWidth > 0 && player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing, "Stream did not decode video.");
        player.Pause(); player.Source = null; element.SetMediaPlayer(null); window.Root.Content = null;
    }
}
