using YolanLive.Core;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Playback;
using FFmpegInteropX;

namespace YolanLive.WinUI;

internal static class RandomBrowseChecks
{
    internal static async Task RunAsync(MainWindow window)
    {
        var report = new List<string>();
        var failed = false;
        var original = MainVM.Sites;
        async Task Test(string name, Func<Task> run)
        {
            try { await run(); report.Add("PASS " + name); }
            catch (Exception ex) { failed = true; report.Add("FAIL " + name + "\n" + ex); }
        }
        try
        {
            await DatabaseHelper.InitializeDatabase();
            await Test("Game scope bypasses the homepage pool and remains fixed across prefetch and history", async () =>
            {
                var site = new FixtureSite { SiteName = "Scoped platform", GameRooms = new[] { "game-1", "game-2", "game-3" } };
                var other = new FixtureSite { SiteName = "Other platform", GameRooms = new[] { "other-1" } };
                MainVM.Sites = new() { new() { LiveSite = site, Name = site.SiteName } };
                var warm = await RandomLive.PickPreparedAsync(CancellationToken.None);
                Check(warm.Room.RoomID == "next-room", "The ordinary homepage fixture did not warm its own pool.");
                MainVM.Sites.Add(new() { LiveSite = other, Name = other.SiteName });
                var scope = new List<LiveGameSource> { new() { Site = site, Category = new() { ID = "chosen-game", ParentID = "2" } } };
                var first = await RandomLive.PickPreparedAsync(CancellationToken.None, gameScope: scope);
                Check(first.Room.Source == site && site.GameRooms.Contains(first.Room.RoomID), "First scoped pick came from the homepage pool.");
                var session = new RandomBrowseSession(first.Room, first, scope);
                try
                {
                    scope[0] = new() { Site = other, Category = new() { ID = "different-game", ParentID = "2" } };
                    await session.PrefetchNextAsync();
                    var next = await session.FindAsync(true, CancellationToken.None);
                    Check(next.Room.Source == site && next.Room.RoomID != first.Room.RoomID && next.Prepared != null,
                        "Prefetch escaped the original filter or repeated the current room.");
                    session.Commit(next);

                    var back = await session.FindAsync(false, CancellationToken.None);
                    Check(back.Room.RoomID == first.Room.RoomID && !back.IsNew, "Previous did not use the current session history.");
                    session.Commit(back);
                    var forward = await session.FindAsync(true, CancellationToken.None);
                    Check(forward.Room.RoomID == next.Room.RoomID && !forward.IsNew, "Next did not follow history after going back.");
                    session.Commit(forward);

                    var last = await session.FindAsync(true, CancellationToken.None);
                    Check(last.IsNew && last.Room.Source == site && last.Room.RoomID != next.Room.RoomID &&
                          last.Room.RoomID != first.Room.RoomID, "A fresh next escaped the scope or repeated a room.");
                    session.Commit(last);
                    Check(await session.FindAsync(true, CancellationToken.None) == null, "An exhausted game fell back to other rooms.");
                    Check(other.RequestedPartitions.Count == 0 && site.RequestedPartitions.Skip(1).All(id => id == "chosen-game"),
                        "Scoped browsing queried an unrelated platform or partition.");
                }
                finally { session.End(); }
                var home = await RandomLive.PickPreparedAsync(CancellationToken.None, excluded: new HashSet<string> { other.SiteName + ":next-room" });
                Check(home?.Room.RoomID == "next-room", "Scoped candidates polluted the homepage pool.");
            });
            await Test("All-platform game scope includes only matching partitions and survives one platform failure", async () =>
            {
                var first = new FixtureSite { SiteName = "Failing game platform", FailGameRooms = true };
                var second = new FixtureSite { SiteName = "Working game platform", GameRooms = new[] { "same-game" } };
                var unrelated = new FixtureSite { SiteName = "Unmapped platform" };
                MainVM.Sites = new[] { first, second, unrelated }.Select(site => new Site { LiveSite = site, Name = site.SiteName }).ToList();
                var scope = new LiveGameSource[]
                {
                    new() { Site = first, Category = new() { ID = "first-id", ParentID = "2" } },
                    new() { Site = second, Category = new() { ID = "second-id", ParentID = "2" } }
                };
                var picked = await RandomLive.PickPreparedAsync(CancellationToken.None, gameScope: scope);
                Check(picked?.Room.Source == second && second.RequestedPartitions.SequenceEqual(new[] { "second-id" }) &&
                    first.RequestedPartitions.All(id => id == "first-id") && unrelated.RequestedPartitions.Count == 0,
                    "Selecting all platforms discarded the working game partition or queried an unmapped platform.");
                Check(await RandomLive.PickPreparedAsync(CancellationToken.None, gameScope: Array.Empty<LiveGameSource>()) == null,
                    "An empty game scope fell back to homepage results.");
            });
            await Test("Prepared selection reaches the real player view model without a second detail request", async () =>
            {
                var site = new FixtureSite();
                MainVM.Sites = new() { new Site { LiveSite = site, Name = site.Name } };
                var room = new LiveRoomItem { RoomID = "fixture-room", Source = site };
                var selected = await RandomLive.PrepareAsync(room, CancellationToken.None);
                var vm = new LiveRoomVM();
                var address = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.ChangedPlayUrl += (_, url) => address.TrySetResult(url);
                try
                {
                    await vm.LoadDataAsync(site, room.RoomID, CancellationToken.None, selected);
                    await address.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Check(site.DetailCalls == 1 && site.QualityCalls == 1 && site.UrlCalls == 1,
                        "Expected one detail, quality and URL call; got " + site.DetailCalls + "," + site.QualityCalls + "," + site.UrlCalls);
                    Check(vm.RoomID == room.RoomID && vm.Title == "Isolated game live", "Prepared room data did not populate the player.");
                }
                finally { vm.CancelPendingPlayback(); }
            });
            await Test("Ordinary opens and expired snapshots still fetch current details", async () =>
            {
                var site = new FixtureSite();
                MainVM.Sites = new() { new Site { LiveSite = site, Name = site.Name } };
                var room = new LiveRoomItem { RoomID = "fixture-room", Source = site };
                var selected = await RandomLive.PrepareAsync(room, CancellationToken.None);
                var vm = new LiveRoomVM();
                try
                {
                    await vm.LoadDataAsync(site, room.RoomID, prepared: selected with { PreparedAt = Environment.TickCount64 - 16_000 });
                    Check(site.DetailCalls == 2, "Expired snapshot prevented a fresh detail request.");
                    await vm.LoadDataAsync(site, room.RoomID);
                    Check(site.DetailCalls == 3, "Ordinary open reused random-room data.");
                }
                finally { vm.CancelPendingPlayback(); }
            });
            await Test("Canceled prepared navigation cannot start quality or stream requests", async () =>
            {
                var site = new FixtureSite();
                MainVM.Sites = new() { new Site { LiveSite = site, Name = site.Name } };
                var room = new LiveRoomItem { RoomID = "fixture-room", Source = site };
                var selected = await RandomLive.PrepareAsync(room, CancellationToken.None);
                var vm = new LiveRoomVM();
                using var stop = new CancellationTokenSource(); stop.Cancel();
                try
                {
                    await vm.LoadDataAsync(site, room.RoomID, stop.Token, selected);
                    Check(site.QualityCalls == 0 && site.UrlCalls == 0, "Canceled prepared navigation still started playback requests.");
                }
                finally { vm.CancelPendingPlayback(); }
            });
            await Test("Real player page starts one metadata prefetch after playing and cancels it on unload", async () =>
            {
                var path = await PlayerPerformanceChecks.MediaAsync(Path.Combine(AppPaths.EnsureDirectory(), "random-prefetch-720p.mp4"));
                using var server = new PlayerPerformanceChecks.LocalMediaServer(path);
                var site = new FixtureSite { StreamUrl = server.Url, HoldNext = true };
                MainVM.Sites = new() { new Site { LiveSite = site, Name = site.Name, Logo = "ms-appx:///Assets/Placeholder/Placeholder1x1.png" } };
                var room = new LiveRoomItem { RoomID = "fixture-room", Source = site };
                var prepared = await RandomLive.PrepareAsync(room, CancellationToken.None);
                var session = new RandomBrowseSession(room, prepared);
                try
                {
                    window.Activate();
                    for (var wait = 0; wait < 40 && !window.Root.IsLoaded; wait++) await Task.Delay(25);
                    Check(window.Root.IsLoaded, "Playback host was not loaded.");
                    window.Root.Navigate(typeof(Views.LiveRoomPage), new Models.PageArgs { Site = site, Data = room, RandomSession = session });
                    try { await site.NextStarted.Task.WaitAsync(TimeSpan.FromSeconds(6)); }
                    catch (TimeoutException error)
                    {
                        var page = (Views.LiveRoomPage)window.Root.Content;
                        var view = (MediaPlayerElement)page.FindName("player");
                        throw new InvalidOperationException("Prefetch did not start: loaded=" + page.IsLoaded + ", source=" + (view.MediaPlayer?.Source != null) + ", state=" + view.MediaPlayer?.PlaybackSession.PlaybackState + ", width=" + view.MediaPlayer?.PlaybackSession.NaturalVideoWidth + ", url_calls=" + site.UrlCalls, error);
                    }
                    Check(site.DetailCalls == 2 && site.QualityCalls == 1 && site.UrlCalls == 1,
                        "Playing did not start exactly one metadata-only prefetch.");
                    window.Root.Content = null;
                    await site.NextCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    Check(session.Ended, "Leaving the actual page did not end its random session.");
                }
                finally { window.Root.Content = null; session.End(); await Task.Delay(150); }
            });
            await Test("Previous and next recover after real player room switches", async () =>
            {
                var path = await PlayerPerformanceChecks.MediaAsync(Path.Combine(AppPaths.EnsureDirectory(), "random-prefetch-720p.mp4"));
                using var server = new PlayerPerformanceChecks.LocalMediaServer(path);
                var site = new FixtureSite { StreamUrl = server.Url };
                MainVM.Sites = new() { new Site { LiveSite = site, Name = site.Name, Logo = "ms-appx:///Assets/Placeholder/Placeholder1x1.png" } };
                var room = new LiveRoomItem { RoomID = "fixture-room", Source = site };
                var session = new RandomBrowseSession(room, await RandomLive.PrepareAsync(room, CancellationToken.None));
                try
                {
                    window.Root.Navigate(typeof(Views.LiveRoomPage), new Models.PageArgs { Site = site, Data = room, RandomSession = session });
                    var page = (Views.LiveRoomPage)window.Root.Content;
                    var previous = (Button)page.FindName("RandomPrevious");
                    var next = (Button)page.FindName("RandomNext");
                    await Task.Delay(300);
                    Check(next.IsEnabled && !previous.IsEnabled, "Initial next is unavailable, or previous incorrectly enabled.");
                    void Click(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                        new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
                            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                    Click(next);
                    for (var i = 0; i < 100 && (!session.CanGoPrevious || !next.IsEnabled); i++) await Task.Delay(30);
                    Check(session.CanGoPrevious && next.IsEnabled && previous.IsEnabled,
                        "After next completed, previous/next remained disabled.");
                    Click(previous);
                    for (var i = 0; i < 100 && (session.CanGoPrevious || !next.IsEnabled); i++) await Task.Delay(30);
                    Check(!session.CanGoPrevious && !previous.IsEnabled && next.IsEnabled,
                        "After previous completed, next did not recover.");
                    Click(next);
                    for (var i = 0; i < 100 && (!session.CanGoPrevious || !next.IsEnabled); i++) await Task.Delay(30);
                    Check(session.CanGoPrevious && previous.IsEnabled && next.IsEnabled,
                        "Returning through forward history left navigation disabled.");
                }
                finally { window.Root.Content = null; session.End(); await Task.Delay(150); }
            });
            await Test("Switch deadline restores buttons even when the platform ignores cancellation", async () =>
            {
                var path = await PlayerPerformanceChecks.MediaAsync(Path.Combine(AppPaths.EnsureDirectory(), "random-prefetch-720p.mp4"));
                using var server = new PlayerPerformanceChecks.LocalMediaServer(path);
                var site = new FixtureSite { StreamUrl = server.Url, HoldNextQuality = true };
                MainVM.Sites = new() { new Site { LiveSite = site, Name = site.Name, Logo = "ms-appx:///Assets/Placeholder/Placeholder1x1.png" } };
                var room = new LiveRoomItem { RoomID = "fixture-room", Source = site };
                var session = new RandomBrowseSession(room, await RandomLive.PrepareAsync(room, CancellationToken.None));
                try
                {
                    window.Root.Navigate(typeof(Views.LiveRoomPage), new Models.PageArgs { Site = site, Data = room, RandomSession = session });
                    var page = (Views.LiveRoomPage)window.Root.Content;
                    var previous = (Button)page.FindName("RandomPrevious");
                    var next = (Button)page.FindName("RandomNext");
                    await Task.Delay(200);
                    ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(next)
                        .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                    await site.NextQualityStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                    Check(!next.IsEnabled && !previous.IsEnabled, "Switch did not prevent overlapping operations.");
                    await site.NextQualityCanceled.Task.WaitAsync(TimeSpan.FromSeconds(62));
                    await Task.Delay(200);
                    Check(next.IsEnabled && previous.IsEnabled, "Platform received cancellation, but both buttons stayed locked beyond the deadline.");
                    var urlCalls = site.UrlCalls;
                    site.ReleaseQuality.TrySetResult();
                    await Task.Delay(200);
                    Check(site.UrlCalls == urlCalls, "Late platform response started an expired stream.");
                }
                finally { site.ReleaseQuality.TrySetResult(); window.Root.Content = null; session.End(); await Task.Delay(150); }
            });
        }
        finally { MainVM.Sites = original; }
        File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "random-browse-checks.log"), report);
        Environment.ExitCode = failed ? 1 : 0;
    }
    internal static async Task LiveAsync(MainWindow window)
    {
        var report = new List<string>();
        var original = MainVM.Sites;
        var douyin = (Douyin)original.First(s => s.LiveSite is Douyin).LiveSite;
        if (douyin.CancellableWebRequestAsync == null)
        {
            var browser = new DouyinWebRequest();
            douyin.WebRequestAsync = browser.GetAsync;
            douyin.CancellableWebRequestAsync = browser.GetAsync;
        }
        var failed = false;
        try
        {
            foreach (var platform in original)
            {
                var watch = Stopwatch.StartNew();
                var stage = "selection including details";
                try
                {
                    MainVM.Sites = new() { platform };
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var selected = await RandomLive.PickPreparedAsync(timeout.Token);
                    Check(selected != null, "No candidate");
                    var pickMs = watch.ElapsedMilliseconds;
                    stage = "quality"; watch.Restart();
                    var qualities = await platform.LiveSite.GetPlayQuality(selected.Detail, timeout.Token);
                    Check(qualities?.Count > 0, "No quality");
                    var qualityMs = watch.ElapsedMilliseconds;
                    stage = "stream address"; watch.Restart();
                    var quality = LiveRoomVM.GetPreferredQuality(platform.LiveSite, qualities);
                    var urls = await platform.LiveSite.GetPlayUrls(selected.Detail, quality, timeout.Token);
                    Check(urls?.Count > 0, "No stream address");
                    var addressMs = watch.ElapsedMilliseconds;
                    stage = "decode readiness"; watch.Restart();
                    Exception lineError = null;
                    var decoded = false;
                    foreach (var (url, index) in urls.Take(3).Select((url, index) => (url, index)))
                    {
                        try
                        {
                            await DecodeReadyAsync(window, url, selected.Detail.Url ?? "", timeout.Token);
                            report.Add("PASS " + platform.Name + " decoded_line=" + (index + 1) + " available_lines=" + urls.Count);
                            decoded = true; break;
                        }
                        catch (Exception error)
                        {
                            lineError = error;
                            report.Add("FAIL " + platform.Name + " line=" + (index + 1) + " hresult=" + error.HResult.ToString("X8") + " " + await ProbeStreamAsync(url, selected.Detail.Url, timeout.Token));
                        }
                    }
                    if (!decoded) throw lineError ?? new InvalidOperationException("No line decoded");
                    report.Add("PASS " + platform.Name + " selection_with_details_ms=" + pickMs + " quality_ms=" + qualityMs +
                        " address_ms=" + addressMs + " decode_ready_ms=" + watch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    failed = true;
                    report.Add("FAIL " + platform.Name + " stage=" + stage + " elapsed_ms=" + watch.ElapsedMilliseconds + " error=" + ex.GetType().Name + " inner=" + ex.InnerException?.GetType().Name);
                }
                File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "random-browse-live.log"), report);
            }
        }
        finally { MainVM.Sites = original; }
        report.Add("INFO isolated guest profiles only; decode readiness means Playing with video dimensions, not measured visible first pixels; no baseline live-stream comparison.");
        File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "random-browse-live.log"), report);
        Environment.ExitCode = failed ? 1 : 0;
    }
    private static async Task DecodeReadyAsync(MainWindow window, string url, string referer, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var config = new MediaSourceConfig();
        config.FFmpegOptions.Add("rw_timeout", "15000000");
        config.FFmpegOptions.Add("referer", referer);
        if (referer.Contains("bilibili.com", StringComparison.OrdinalIgnoreCase))
            config.FFmpegOptions.Add("user_agent", "Mozilla/5.0 BiliDroid/1.12.0 (bbcallen@gmail.com)");
        using var source = await FFmpegMediaSource.CreateFromUriAsync(url, config).AsTask(timeout.Token);
        using var player = new MediaPlayer { IsMuted = true, AutoPlay = true };
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decodingEnded = false;
        void Failed(MediaPlayer sender, MediaPlayerFailedEventArgs e) => ready.TrySetException(new InvalidOperationException("Decoder " + e.Error));
        void StateChanged(MediaPlaybackSession session, object args)
        {
            if (!decodingEnded && session.PlaybackState == MediaPlaybackState.Playing && session.NaturalVideoWidth > 0) ready.TrySetResult();
        }
        player.MediaFailed += Failed;
        player.PlaybackSession.PlaybackStateChanged += StateChanged;
        var view = new MediaPlayerElement { AreTransportControlsEnabled = false };
        view.SetMediaPlayer(player); window.Root.Content = view; window.Activate();
        try { player.Source = source.CreateMediaPlaybackItem(); await ready.Task.WaitAsync(timeout.Token); }
        finally
        {
            decodingEnded = true;
            player.PlaybackSession.PlaybackStateChanged -= StateChanged;
            player.MediaFailed -= Failed;
            player.Pause(); player.Source = null; view.SetMediaPlayer(null); window.Root.Content = null;
        }
    }
    private static async Task<string> ProbeStreamAsync(string url, string referer, CancellationToken cancellation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var client = new System.Net.Http.HttpClient();
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            if (!string.IsNullOrEmpty(referer)) request.Headers.Referrer = new Uri(referer);
            using var response = await client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return "http=" + (int)response.StatusCode + " content_type=" + response.Content.Headers.ContentType?.MediaType;
        }
        catch (Exception error) { return "http_probe=" + error.GetType().Name; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal sealed class FixtureSite : BiliBili, ILiveSite
    {
        internal int DetailCalls, QualityCalls, UrlCalls;
        internal string SiteName = "哔哩哔哩直播";
        string ILiveSite.Name => SiteName;
        internal string[] GameRooms = Array.Empty<string>();
        internal bool FailGameRooms;
        internal readonly List<string> RequestedPartitions = new();
        internal string StreamUrl = "https://example.invalid/not-downloaded.flv";
        internal bool HoldNext, HoldNextQuality;
        internal Task<List<string>> PendingUrls;
        internal readonly TaskCompletionSource UrlStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource NextQualityStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource NextQualityCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseQuality = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource NextStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource NextCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public new Task<List<LiveCategory>> GetCategores(CancellationToken cancellationToken)
            => Task.FromResult(new List<LiveCategory> { new() { ID = "2", Children = new() { new() { ID = "0", ParentID = "2", Name = "Game" } } } });
        public new Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedPartitions.Add(category.ID);
            if (category.ID != "0" && FailGameRooms) throw new InvalidOperationException("Isolated partition failure");
            var ids = category.ID == "0" ? new[] { "next-room" } : GameRooms;
            return Task.FromResult(new LiveCategoryResult { Rooms = ids.Select(id => new LiveRoomItem { RoomID = id, IsLive = true }).ToList() });
        }
        public new async Task<LiveRoomDetail> GetRoomDetail(object roomId, CancellationToken cancellationToken)
        {
            DetailCalls++;
            if (HoldNext && roomId.ToString() == "next-room")
            {
                NextStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { NextCanceled.TrySetResult(); throw; }
            }
            return new LiveRoomDetail { RoomID = roomId.ToString(), Title = "Isolated game live", UserName = "Fixture", Status = true };
        }
        public new async Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail detail, CancellationToken cancellationToken)
        {
            QualityCalls++;
            if (HoldNextQuality && detail.RoomID == "next-room")
            {
                using var canceled = cancellationToken.Register(() => NextQualityCanceled.TrySetResult());
                NextQualityStarted.TrySetResult();
                await ReleaseQuality.Task;
            }
            return new List<LivePlayQuality> { new() { Quality = "fixture" } };
        }
        public new Task<List<string>> GetPlayUrls(LiveRoomDetail detail, LivePlayQuality quality, CancellationToken cancellationToken)
        { UrlCalls++; UrlStarted.TrySetResult(); return PendingUrls ?? Task.FromResult(new List<string> { StreamUrl }); }
    }
}
