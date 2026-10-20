using YolanLive.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Helper;

internal sealed record RandomRoomChoice(LiveRoomItem Room, int Index, bool IsNew, RandomRoomSelection Prepared = null);

internal sealed class RandomBrowseSession
{
    private readonly List<LiveRoomItem> rooms;
    private readonly object prefetchGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource prefetchOperation;
    private Task<RandomRoomSelection> prefetched;
    private RandomRoomSelection initialSelection;
    private int position;
    internal bool Ended { get; private set; }
    internal bool CanGoPrevious => !Ended && position > 0;

    internal RandomBrowseSession(LiveRoomItem first, RandomRoomSelection prepared = null)
    { rooms = new() { first }; initialSelection = prepared; }

    internal RandomRoomSelection TakeInitialSelection()
    { var selected = initialSelection; initialSelection = null; return selected; }

    // Called once playback is running; completed work is kept until consumed or navigation changes.
    internal Task PrefetchNextAsync()
    {
        lock (prefetchGate)
        {
            if (Ended || position != rooms.Count - 1 || rooms.Any(r => r.Source == null)) return Task.CompletedTask;
            if (prefetched != null) return prefetched;
            var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            operation.CancelAfter(TimeSpan.FromSeconds(60));
            prefetchOperation = operation;
            prefetched = PrepareNextAsync(operation, rooms.Select(r => r.Source.Name + ":" + r.RoomID).ToHashSet());
            return prefetched;
        }
    }
    private async Task<RandomRoomSelection> PrepareNextAsync(CancellationTokenSource operation, HashSet<string> excluded)
    {
        try { return await RandomLive.PickPreparedAsync(operation.Token, excluded); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            // Speculative failure must not disturb the room currently playing or change login state.
            LogHelper.Log("随便逛逛：下一间资料预取未完成（" + ex.GetType().Name + "）", LogType.ERROR, null);
            return null;
        }
        finally
        {
            lock (prefetchGate)
            {
                if (prefetchOperation == operation) prefetchOperation = null;
                operation.Dispose();
            }
        }
    }
    private void CancelPrefetch()
    {
        lock (prefetchGate)
        {
            var operation = prefetchOperation;
            prefetchOperation = null;
            prefetched = null;
            operation?.Cancel();
        }
    }
    internal async Task<RandomRoomChoice> FindAsync(bool forward, CancellationToken cancellation)
    {
        if (Ended) throw new OperationCanceledException(cancellation);
        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
        lookup.CancelAfter(TimeSpan.FromSeconds(60));
        var token = lookup.Token;
        try
        {
            if (!forward || position < rooms.Count - 1) CancelPrefetch();
            var step = forward ? 1 : -1;
            for (var index = position + step; index >= 0 && index < rooms.Count; index += step)
            {
                token.ThrowIfCancellationRequested();
                var room = rooms[index];
                RandomRoomSelection selected;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    selected = await RandomLive.PrepareAsync(room, timeout.Token);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                { throw new InvalidOperationException(RequestErrorMessage.Describe(ex, room.Source.Name), ex); }
                token.ThrowIfCancellationRequested();
                if (selected != null) return new RandomRoomChoice(room, index, false, selected);
            }
            if (!forward) return null;
            Task<RandomRoomSelection> pending;
            lock (prefetchGate) pending = prefetched;
            var next = pending == null ? null : await pending.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (next != null && !next.IsFresh)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                next = await RandomLive.PrepareAsync(next.Room, timeout.Token);
            }
            next ??= await RandomLive.PickPreparedAsync(token, rooms.Select(r => r.Source.Name + ":" + r.RoomID).ToHashSet());
            token.ThrowIfCancellationRequested();
            return next == null ? null : new RandomRoomChoice(next.Room, rooms.Count, true, next);
        }
        catch (OperationCanceledException) { CancelPrefetch(); throw; }
    }
    internal void Commit(RandomRoomChoice choice)
    {
        if (Ended) throw new InvalidOperationException("本轮随机浏览已结束");
        CancelPrefetch();
        if (choice.IsNew) rooms.Add(choice.Room);
        position = choice.Index;
    }
    internal void End()
    {
        lock (prefetchGate)
        {
            if (Ended) return;
            Ended = true;
            lifetime.Cancel();
            CancelPrefetch();
            lifetime.Dispose();
            initialSelection = null;
            rooms.Clear();
        }
    }
}
