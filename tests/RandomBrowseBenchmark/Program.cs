using System.Diagnostics;
using System.Text.Json;
using YolanLive.Core;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;

var site = new TimedSite();
MainVM.Sites.Add(new Site { Name = site.Name, LiveSite = site });
var excluded = new HashSet<string>();
var runs = new List<object>();
for (var index = 0; index < 3; index++)
{
    var watch = Stopwatch.StartNew();
#if PREPARED_PICKER
    var selected = await RandomLive.PickPreparedAsync(CancellationToken.None, excluded);
    var room = selected.Room;
    var detail = selected.GetFreshDetail(site, room.RoomID) ?? await site.GetRoomDetail(room.RoomID, CancellationToken.None);
#else
    var room = await RandomLive.PickAsync(CancellationToken.None, excluded);
    var detail = await site.GetRoomDetail(room.RoomID, CancellationToken.None);
#endif
    if (!detail.Status || !excluded.Add(site.Name + ":" + room.RoomID)) throw new Exception("Invalid or repeated room");
    runs.Add(new { step = index + 1, selection_and_detail_ms = watch.Elapsed.TotalMilliseconds,
        list_requests = site.ListCalls, detail_requests = site.DetailCalls, status_requests = site.StatusCalls });
}
Console.WriteLine(JsonSerializer.Serialize(new { scenario = "Controlled single-platform metadata path, 40ms ready source, 800ms unrelated source, 120ms room detail; no video playback", runs }, new JsonSerializerOptions { WriteIndented = true }));

sealed class TimedSite : BiliBili, ILiveSite
{
    public int ListCalls, DetailCalls, StatusCalls;
    public new Task<List<LiveCategory>> GetCategores(CancellationToken token) => Task.FromResult(new[] { "2", "3", "6" }
        .Select(id => new LiveCategory { ID = id, Children = new() { new LiveSubCategory { ID = "0", ParentID = id, Name = "Game" } } }).ToList());
    public new async Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, CancellationToken token)
    {
        Interlocked.Increment(ref ListCalls);
        await Task.Delay(category.ParentID == "2" ? 40 : 800, token);
        return new LiveCategoryResult { Rooms = category.ParentID == "2" ? Enumerable.Range(1, 8)
            .Select(i => new LiveRoomItem { RoomID = "room-" + i, IsLive = true }).ToList() : new() };
    }
    public new async Task<bool> GetLiveStatus(object id, CancellationToken token)
    { StatusCalls++; return (await GetRoomDetail(id, token)).Status; }
    public new async Task<LiveRoomDetail> GetRoomDetail(object id, CancellationToken token)
    {
        DetailCalls++; await Task.Delay(120, token);
        return new LiveRoomDetail { RoomID = id.ToString(), Status = true };
    }
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
