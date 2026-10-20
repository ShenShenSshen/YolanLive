using YolanLive.Core;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;

var failed = false;
async Task Test(string name, Func<Task> run)
{
    MainVM.Sites.Clear();
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed = true; Console.WriteLine("FAIL " + name + "\n" + ex); }
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
await Test("Ready room is not blocked by unrelated slow overview sources", async () =>
{
    var site = new FixtureSite { GateSlow = true };
    MainVM.Sites.Add(new Site { Name = site.Name, LiveSite = site });
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var pick = RandomLive.PickAsync(stop.Token);
    await site.ListReady.Task.WaitAsync(stop.Token);
    var completed = await Task.WhenAny(pick, Task.Delay(500, stop.Token)) == pick;
    if (completed) await site.ListCancellation.Task.WaitAsync(TimeSpan.FromSeconds(1));
    site.Slow.TrySetResult();
    var room = await pick;
    Check(completed && room?.RoomID.StartsWith("room-") == true, "A valid room waited for unrelated gated responses.");
});
await Test("Next random pick reuses candidates and excludes the visited room", async () =>
{
    var site = new FixtureSite();
    MainVM.Sites.Add(new Site { Name = site.Name, LiveSite = site });
    var first = await RandomLive.PickAsync(CancellationToken.None);
    var requests = site.ListCalls;
    var next = await RandomLive.PickAsync(CancellationToken.None, new HashSet<string> { site.Name + ":" + first.RoomID });
    Check(next != null && next.RoomID != first.RoomID, "Visited room returned again.");
    Check(site.ListCalls == requests, "Next pick unnecessarily fetched the homepage again.");
});
await Test("Selection confirms live status through one reusable detail request", async () =>
{
    var site = new FixtureSite();
    MainVM.Sites.Add(new Site { Name = site.Name, LiveSite = site });
    await RandomLive.PickAsync(CancellationToken.None);
    Check(site.DetailCalls == 1 && site.StatusCalls == 0, "Selection separately checks status instead of preparing room details.");
});
await Test("Random session prepares just one next room without requesting stream URLs", async () =>
{
    var site = new FixtureSite();
    MainVM.Sites.Add(new Site { Name = site.Name, LiveSite = site });
    var first = new LiveRoomItem { RoomID = "room-1", Source = site, IsLive = true };
    var session = new RandomBrowseSession(first);
    try
    {
        await session.PrefetchNextAsync();
        await session.PrefetchNextAsync();
        Check(site.DetailCalls == 1 && site.UrlCalls == 0, "Prefetch did not prepare exactly one room, or requested a video stream.");
        var next = await session.FindAsync(true, CancellationToken.None);
        Check(next?.Room.RoomID != first.RoomID && site.DetailCalls == 1, "Next did not consume the prepared result.");
    }
    finally { session.End(); }
});
await Test("Ending a session cancels in-flight prefetch and cannot return a room later", async () =>
{
    var site = new FixtureSite { HoldDetail = true };
    MainVM.Sites.Add(new Site { Name = site.Name, LiveSite = site });
    var session = new RandomBrowseSession(new LiveRoomItem { RoomID = "first", Source = site });
    var work = session.PrefetchNextAsync();
    await site.DetailStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Check(ReferenceEquals(work, session.PrefetchNextAsync()), "Repeated playback events queued duplicate work.");
    session.End();
    await work.WaitAsync(TimeSpan.FromSeconds(1));
    await site.DetailCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Check(site.CanceledDetails == 1 && site.UrlCalls == 0, "Session end did not stop the underlying request.");
    try { await session.FindAsync(true, CancellationToken.None); throw new Exception("Ended session selected a room."); }
    catch (OperationCanceledException) { }
});
await Test("History skips confirmed offline, preserves unknown failures and reuses forward records", async () =>
{
    var site = new FixtureSite();
    LiveRoomItem Room(string id) => new() { RoomID = id, Source = site };
    var session = new RandomBrowseSession(Room("first"));
    try
    {
        session.Commit(new RandomRoomChoice(Room("second"), 1, true));
        session.Commit(new RandomRoomChoice(Room("third"), 2, true));
        site.Offline = "second";
        var previous = await session.FindAsync(false, CancellationToken.None);
        Check(previous.Room.RoomID == "first" && previous.Prepared.Detail.Status, "Previous did not skip confirmed offline.");
        session.Commit(previous);
        var next = await session.FindAsync(true, CancellationToken.None);
        Check(next.Room.RoomID == "third" && !next.IsNew && site.ListCalls == 0, "Forward did not follow history.");
        session.Commit(next);
        site.Failure = "second";
        try { await session.FindAsync(false, CancellationToken.None); throw new Exception("Unknown result was skipped as offline."); }
        catch (InvalidOperationException) { }
        Check(session.CanGoPrevious, "Failed lookup mutated position.");
    }
    finally { session.End(); }
});
await Test("Canceled history lookup releases navigation even when detail ignores cancellation", async () =>
{
    var site = new FixtureSite { IgnoreDetailCancellation = true };
    var session = new RandomBrowseSession(new LiveRoomItem { RoomID = "first", Source = site });
    session.Commit(new RandomRoomChoice(new LiveRoomItem { RoomID = "second", Source = site }, 1, true));
    using var stop = new CancellationTokenSource();
    var lookup = session.FindAsync(false, stop.Token);
    try
    {
        await site.DetailStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        stop.Cancel();
        try { await lookup.WaitAsync(TimeSpan.FromMilliseconds(500)); throw new Exception("Canceled lookup returned a target."); }
        catch (OperationCanceledException) { }
        Check(session.CanGoPrevious, "Canceled lookup changed the current position.");
    }
    finally
    {
        site.ReleaseDetail.TrySetResult();
        try { await lookup; } catch (OperationCanceledException) { }
        session.End();
    }
});
await Test("Only fresh matching details can be handed to the player", async () =>
{
    var site = new FixtureSite();
    var room = new LiveRoomItem { RoomID = "one", Source = site };
    var selected = await RandomLive.PrepareAsync(room, CancellationToken.None);
    Check(selected.GetFreshDetail(site, "one") != null, "Fresh matching detail was discarded.");
    Check(selected.GetFreshDetail(new FixtureSite(), "one") == null && selected.GetFreshDetail(site, "another") == null, "Details leaked across platform or room.");
    Check((selected with { PreparedAt = Environment.TickCount64 - 16_000 }).GetFreshDetail(site, "one") == null, "Expired details were reused.");
});
await Test("Canceling next also cancels the pending prefetch without changing position", async () =>
{
    var site = new FixtureSite { HoldDetail = true };
    MainVM.Sites.Add(new Site { Name = site.Name, LiveSite = site });
    var session = new RandomBrowseSession(new LiveRoomItem { RoomID = "first", Source = site });
    try
    {
        var work = session.PrefetchNextAsync();
        await site.DetailStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        using var stop = new CancellationTokenSource();
        var next = session.FindAsync(true, stop.Token);
        stop.Cancel();
        try { await next; throw new Exception("Canceled switch returned a result."); } catch (OperationCanceledException) { }
        await work.WaitAsync(TimeSpan.FromSeconds(1));
        await site.DetailCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Check(site.CanceledDetails == 1 && !session.Ended && !session.CanGoPrevious, "Cancel leaked a request or committed a room.");
    }
    finally { session.End(); }
});
await Test("Candidate cache evicts older platforms beyond its four-platform bound", async () =>
{
    var sites = Enumerable.Range(0, 5).Select(_ => new FixtureSite()).ToArray();
    foreach (var site in sites)
    {
        MainVM.Sites = new() { new Site { Name = site.Name, LiveSite = site } };
        await RandomLive.PickAsync(CancellationToken.None);
    }
    var before = sites[0].ListCalls;
    MainVM.Sites = new() { new Site { Name = sites[0].Name, LiveSite = sites[0] } };
    await RandomLive.PickAsync(CancellationToken.None);
    Check(sites[0].ListCalls > before, "Older platform candidates accumulated beyond the cache bound.");
});
if (args.Contains("--expiry"))
    await Test("Candidate cache expires after thirty seconds without polling", async () =>
    {
        var site = new FixtureSite();
        MainVM.Sites = new() { new Site { Name = site.Name, LiveSite = site } };
        await RandomLive.PickAsync(CancellationToken.None);
        var before = site.ListCalls;
        await Task.Delay(31_000);
        await RandomLive.PickAsync(CancellationToken.None);
        Check(site.ListCalls > before, "Expired homepage candidates were reused.");
    });
Environment.ExitCode = failed ? 1 : 0;

sealed class FixtureSite : BiliBili, ILiveSite
{
    public bool GateSlow, HoldDetail, IgnoreDetailCancellation;
    public readonly TaskCompletionSource ReleaseDetail = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource DetailCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Offline, Failure;
    public int ListCalls, DetailCalls, StatusCalls, CanceledLists, CanceledDetails, UrlCalls;
    public readonly TaskCompletionSource ListReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource Slow = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource DetailStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource ListCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public new Task<List<LiveCategory>> GetCategores(CancellationToken token) => Task.FromResult(new[] { "2", "3", "6" }
        .Select(id => new LiveCategory { ID = id, Children = new() { new LiveSubCategory { ID = "0", ParentID = id, Name = "Game" } } }).ToList());
    public new async Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, CancellationToken token)
    {
        Interlocked.Increment(ref ListCalls);
        if (category.ParentID != "2")
        {
            if (GateSlow)
                try { await Slow.Task.WaitAsync(token); }
                catch (OperationCanceledException) { if (Interlocked.Increment(ref CanceledLists) == 2) ListCancellation.TrySetResult(); throw; }
            return new LiveCategoryResult { Rooms = new() };
        }
        ListReady.TrySetResult();
        return new LiveCategoryResult { Rooms = Enumerable.Range(1, 8).Select(i => new LiveRoomItem { RoomID = "room-" + i, IsLive = true }).ToList() };
    }
    public new Task<bool> GetLiveStatus(object id, CancellationToken token) { StatusCalls++; return Task.FromResult(true); }
    public new async Task<LiveRoomDetail> GetRoomDetail(object id, CancellationToken token)
    {
        DetailCalls++;
        DetailStarted.TrySetResult();
        if (IgnoreDetailCancellation) await ReleaseDetail.Task;
        if (HoldDetail)
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { CanceledDetails++; DetailCanceled.TrySetResult(); throw; }
        if (id.ToString() == Failure) throw new HttpRequestException("Controlled network failure");
        return new LiveRoomDetail { RoomID = id.ToString(), Status = id.ToString() != Offline };
    }
    public new Task<List<string>> GetPlayUrls(LiveRoomDetail detail, LivePlayQuality quality, CancellationToken token)
    { UrlCalls++; throw new Exception("Prefetch must not request streams."); }
}
namespace YolanLive.WinUI.ViewModels
{
    internal static class MainVM { internal static List<Site> Sites = new(); }
    internal class Site { public string Name; public ILiveSite LiveSite; }
}
namespace YolanLive.WinUI.Helper
{
    internal enum LogType { ERROR }
    internal static class LogHelper { public static void Log(string message, LogType type, Exception ex) { } }
    internal static class RequestErrorMessage { public static string Describe(Exception ex, string platform) => ex.Message; }
}
