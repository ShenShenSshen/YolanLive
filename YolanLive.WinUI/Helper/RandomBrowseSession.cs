using YolanLive.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper;

internal sealed record RandomRoomChoice(LiveRoomItem Room, int Index, bool IsNew);

internal sealed class RandomBrowseSession
{
    private readonly List<LiveRoomItem> rooms;
    private int position;
    internal bool Ended { get; private set; }
    internal bool CanGoPrevious => !Ended && position > 0;

    internal RandomBrowseSession(LiveRoomItem first) => rooms = new() { first };

    internal async Task<RandomRoomChoice> FindAsync(bool forward, CancellationToken cancellation)
    {
        var step = forward ? 1 : -1;
        for (var index = position + step; index >= 0 && index < rooms.Count; index += step)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Ended) throw new OperationCanceledException(cancellation);
            var room = rooms[index];
            bool live;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                live = await room.Source.GetLiveStatus(room.RoomID, timeout.Token);
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested)
            {
                throw new InvalidOperationException(RequestErrorMessage.Describe(ex, room.Source.Name), ex);
            }
            cancellation.ThrowIfCancellationRequested();
            if (Ended) throw new OperationCanceledException(cancellation);
            if (live) return new RandomRoomChoice(room, index, false);
        }
        if (!forward || Ended) return null;
        var excluded = rooms.Select(r => r.Source.Name + ":" + r.RoomID).ToHashSet();
        var next = await RandomLive.PickAsync(cancellation, excluded);
        cancellation.ThrowIfCancellationRequested();
        if (Ended) throw new OperationCanceledException(cancellation);
        return next == null ? null : new RandomRoomChoice(next, rooms.Count, true);
    }

    internal void Commit(RandomRoomChoice choice)
    {
        if (Ended) throw new InvalidOperationException("本轮随机浏览已结束");
        if (choice.IsNew) rooms.Add(choice.Room);
        position = choice.Index;
    }

    internal void End() { Ended = true; rooms.Clear(); }
}
