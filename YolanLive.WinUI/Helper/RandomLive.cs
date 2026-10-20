using YolanLive.Core.Helper;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper;

internal sealed record RandomRoomSelection(LiveRoomItem Room, LiveRoomDetail Detail, long PreparedAt)
{
    internal bool IsFresh => Environment.TickCount64 - PreparedAt is >= 0 and <= 15_000;
    internal LiveRoomDetail GetFreshDetail(ILiveSite site, object roomId) => IsFresh &&
        ReferenceEquals(Room.Source, site) && Room.RoomID == roomId?.ToString() ? Detail : null;
}

internal static class RandomLive
{
    private sealed class CandidatePool
    {
        internal DateTime Expires, Used;
        internal LiveRoomItem[] Rooms;
    }
    private static readonly object CacheGate = new();
    private static readonly Dictionary<ILiveSite, CandidatePool> Pools = new();
    private static LiveRoomItem[] ReadCandidates(ILiveSite site)
    {
        lock (CacheGate)
        {
            if (!Pools.TryGetValue(site, out var pool)) return Array.Empty<LiveRoomItem>();
            if (pool.Expires <= DateTime.UtcNow) { Pools.Remove(site); return Array.Empty<LiveRoomItem>(); }
            pool.Used = DateTime.UtcNow;
            return pool.Rooms;
        }
    }
    private static void SaveCandidates(ILiveSite site, IEnumerable<LiveRoomItem> rooms)
    {
        var available = rooms.Where(r => r.IsLive == true && !string.IsNullOrWhiteSpace(r.RoomID)).DistinctBy(r => r.RoomID).Take(24).ToArray();
        if (available.Length == 0) return;
        lock (CacheGate)
        {
            if (!Pools.ContainsKey(site) && Pools.Count == 4) Pools.Remove(Pools.OrderBy(p => p.Value.Used).First().Key);
            Pools[site] = new CandidatePool { Rooms = available, Used = DateTime.UtcNow, Expires = DateTime.UtcNow.AddSeconds(30) };
        }
    }
    private static void RemoveCandidate(ILiveSite site, string roomId)
    {
        lock (CacheGate)
            if (Pools.TryGetValue(site, out var pool)) pool.Rooms = pool.Rooms.Where(r => r.RoomID != roomId).ToArray();
    }
    internal static async Task<RandomRoomSelection> PrepareAsync(LiveRoomItem room, CancellationToken cancellation)
    {
        var detail = await room.Source.GetRoomDetail(room.RoomID, cancellation).WaitAsync(cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (detail == null) throw new InvalidOperationException("房间详情为空，请稍后重试");
        return detail.Status ? new RandomRoomSelection(room, detail, Environment.TickCount64) : null;
    }
    internal static async Task<LiveRoomItem> PickAsync(CancellationToken cancellation, IReadOnlySet<string> excluded = null)
        => (await PickPreparedAsync(cancellation, excluded))?.Room;

    internal static async Task<RandomRoomSelection> PickPreparedAsync(CancellationToken cancellation, IReadOnlySet<string> excluded = null)
    {
        // Shuffle platforms before consulting their pools: cache hits must not favor one platform.
        var platforms = MainVM.Sites.ToArray();
        Random.Shared.Shuffle(platforms);
        Exception lastError = null;
        string lastPlatform = null;
        foreach (var platform in platforms)
        {
            cancellation.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            attempt.CancelAfter(TimeSpan.FromSeconds(12));
            var tried = new HashSet<string>();
            async Task<RandomRoomSelection> ChooseAsync(IEnumerable<LiveRoomItem> rooms)
            {
                var candidates = rooms.Where(r => r.IsLive == true && !string.IsNullOrWhiteSpace(r.RoomID) &&
                    excluded?.Contains(platform.LiveSite.Name + ":" + r.RoomID) != true && !tried.Contains(r.RoomID)).DistinctBy(r => r.RoomID).ToArray();
                Random.Shared.Shuffle(candidates);
                foreach (var room in candidates.Take(3 - tried.Count))
                {
                    tried.Add(room.RoomID);
                    try
                    {
                        room.Source = platform.LiveSite;
                        var prepared = await PrepareAsync(room, attempt.Token);
                        if (prepared != null) return prepared;
                        RemoveCandidate(platform.LiveSite, room.RoomID);
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        lastError = ex; lastPlatform = platform.Name;
                        attempt.Token.ThrowIfCancellationRequested();
                    }
                }
                return null;
            }
            try
            {
                var cached = await ChooseAsync(ReadCandidates(platform.LiveSite));
                if (cached != null) return cached;
                if (tried.Count == 3) continue;
                var catalog = await GameCatalog.LoadAsync(platform.LiveSite, attempt.Token);
                var sources = GameBrowse.OverviewSources(catalog);
                var batches = sources.Select(async source =>
                {
                    try { return await source.Site.GetCategoryRooms(source.Category, 1, attempt.Token); }
                    catch (Exception ex) { return new LiveCategoryResult { Error = ex }; }
                }).ToList();
                while (batches.Count > 0 && tried.Count < 3)
                {
                    var completed = await Task.WhenAny(batches).WaitAsync(attempt.Token);
                    batches.Remove(completed);
                    var batch = await completed;
                    attempt.Token.ThrowIfCancellationRequested();
                    if (batch.Error != null) { lastError = batch.Error; lastPlatform = platform.Name; }
                    if (batch.Rooms == null) continue;
                    SaveCandidates(platform.LiveSite, batch.Rooms);
                    var chosen = await ChooseAsync(batch.Rooms);
                    if (chosen != null) return chosen;
                }
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested)
            {
                lastError = ex is OperationCanceledException ? new TimeoutException("平台请求超时") : ex;
                lastPlatform = platform.Name;
                LogHelper.Log("随便逛逛：" + platform.Name + "暂时不可用", LogType.ERROR, lastError);
            }
            finally { attempt.Cancel(); }
        }
        cancellation.ThrowIfCancellationRequested();
        if (lastError != null) throw new InvalidOperationException("未能找到可观看的游戏直播：" + RequestErrorMessage.Describe(lastError, lastPlatform), lastError);
        return null;
    }
}
