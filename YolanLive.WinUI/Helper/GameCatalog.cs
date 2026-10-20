using YolanLive.Core.Helper;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace YolanLive.WinUI.Helper;
internal static class GameCatalog
{
    private sealed class Entry
    {
        internal List<LiveCategory> Value;
        internal DateTime Expires, Used;
        internal Pending Pending;
    }
    private sealed class Pending
    {
        internal readonly CancellationTokenSource Cancellation = new(TimeSpan.FromSeconds(20));
        internal Task<List<LiveCategory>> Task;
        internal int Waiters;
        internal bool Completed;
    }
    private static readonly object Gate = new();
    private static readonly Dictionary<ILiveSite, Entry> Entries = new();
    private static long hits, loads;
    internal static long Hits => Interlocked.Read(ref hits);
    internal static long Loads => Interlocked.Read(ref loads);
    internal static void Invalidate(ILiveSite site) { lock (Gate) if (Entries.TryGetValue(site, out var entry)) entry.Expires = DateTime.MinValue; }
    public static Task<List<LiveGameSource>> LoadAsync(ILiveSite site) => LoadAsync(site, CancellationToken.None);
    public static async Task<List<LiveGameSource>> LoadAsync(ILiveSite site, CancellationToken cancellationToken)
        => (await GetCategoriesAsync(site, cancellationToken)).SelectMany(c => c.Children).Select(c => new LiveGameSource { Site = site, Category = c }).ToList();
    internal static async Task<List<LiveCategory>> GetCategoriesAsync(ILiveSite site, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(site); cancellationToken.ThrowIfCancellationRequested();
        Pending pending;
        lock (Gate)
        {
            if (!Entries.TryGetValue(site, out var entry))
            {
                if (Entries.Count == 4)
                {
                    var oldest = Entries.OrderBy(e => e.Value.Used).First();
                    Entries.Remove(oldest.Key);
                }
                Entries[site] = entry = new Entry();
            }
            entry.Used = DateTime.UtcNow;
            if (!forceRefresh && entry.Value != null && entry.Expires > DateTime.UtcNow) { Interlocked.Increment(ref hits); return entry.Value; }
            pending = entry.Pending;
            if (pending == null || pending.Cancellation.IsCancellationRequested)
            {
                entry.Pending = pending = new Pending();
                pending.Task = FetchAsync(site, entry, pending);
            }
            else Interlocked.Increment(ref hits);
            pending.Waiters++;
        }
        try { return await pending.Task.WaitAsync(cancellationToken); }
        finally
        {
            lock (Gate)
                if (--pending.Waiters == 0 && !pending.Completed) pending.Cancellation.Cancel();
        }
    }
    private static async Task<List<LiveCategory>> FetchAsync(ILiveSite site, Entry entry, Pending pending)
    {
        try
        {
            Interlocked.Increment(ref loads);
            var value = GameBrowse.SelectCategories(site, await site.GetCategores(pending.Cancellation.Token));
            pending.Cancellation.Token.ThrowIfCancellationRequested();
            if (value.Count == 0 || !value.Any(c => c.Children.Count > 0)) throw new InvalidOperationException("暂无可用游戏分类");
            lock (Gate) { entry.Value = value; entry.Expires = DateTime.UtcNow.AddMinutes(10); }
            return value;
        }
        finally
        {
            lock (Gate) { pending.Completed = true; if (entry.Pending == pending) entry.Pending = null; }
            pending.Cancellation.Dispose();
        }
    }
}
