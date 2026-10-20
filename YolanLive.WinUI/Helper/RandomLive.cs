using YolanLive.Core.Helper;
using YolanLive.Core.Models;
using YolanLive.WinUI.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper;

internal static class RandomLive
{
    internal static async Task<LiveRoomItem> PickAsync(CancellationToken cancellation, System.Collections.Generic.IReadOnlySet<string> excluded = null)
    {
        var platforms = MainVM.Sites.ToArray();
        Random.Shared.Shuffle(platforms);
        Exception lastError = null;
        string lastPlatform = null;
        foreach (var platform in platforms)
        {
            cancellation.ThrowIfCancellationRequested();
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            attempt.CancelAfter(TimeSpan.FromSeconds(12));
            try
            {
                var catalog = await GameCatalog.LoadAsync(platform.LiveSite, attempt.Token);
                var sources = GameBrowse.OverviewSources(catalog);
                var batches = await Task.WhenAll(sources.Select(async source =>
                {
                    try
                    {
                        var result = await source.Site.GetCategoryRooms(source.Category, 1, attempt.Token);
                        return (rooms: result.Rooms, error: result.Error);
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        return (rooms: (System.Collections.Generic.List<LiveRoomItem>)null, error: ex);
                    }
                }));
                attempt.Token.ThrowIfCancellationRequested();
                var error = batches.Select(b => b.error).FirstOrDefault(e => e != null);
                if (error != null) { lastError = error; lastPlatform = platform.Name; }
                var candidates = batches.Where(b => b.rooms != null).SelectMany(b => b.rooms)
                    .Where(r => r.IsLive == true && !string.IsNullOrWhiteSpace(r.RoomID) && excluded?.Contains(platform.LiveSite.Name + ":" + r.RoomID) != true)
                    .DistinctBy(r => r.RoomID).ToArray();
                Random.Shared.Shuffle(candidates);
                foreach (var room in candidates.Take(3))
                {
                    try
                    {
                        if (!await platform.LiveSite.GetLiveStatus(room.RoomID, attempt.Token)) continue;
                        room.Source = platform.LiveSite;
                        return room;
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        lastError = ex; lastPlatform = platform.Name;
                        attempt.Token.ThrowIfCancellationRequested();
                    }
                }
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested)
            {
                lastError = ex is OperationCanceledException ? new TimeoutException("平台请求超时") : ex;
                lastPlatform = platform.Name;
                LogHelper.Log("随便逛逛：" + platform.Name + "暂时不可用", LogType.ERROR, lastError);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        if (lastError != null)
            throw new InvalidOperationException("未能找到可观看的游戏直播：" + RequestErrorMessage.Describe(lastError, lastPlatform));
        return null;
    }
}
