using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.ViewModels;
public abstract class LiveRoomListVM : BaseViewModel
{
    private const int MaxCachedRooms = 300;
    public ObservableCollection<LiveRoomItem> Items { get; } = new();
    private List<LiveGameSource> sources;
    private readonly Dictionary<LiveGameSource, int> pages = new();
    private readonly HashSet<LiveGameSource> finished = new();
    private int generation;
    private CancellationTokenSource requests = new();
    private bool checkingStatus;
    public bool HasLoaded { get; private set; }
    private string emptyMessage = "暂无已开播的直播";
    public string EmptyMessage { get => emptyMessage; private set { emptyMessage = value; DoPropertyChanged(nameof(EmptyMessage)); } }
    protected virtual bool LiveOnly => true;
    protected virtual string PlatformName => "直播平台";
    protected abstract Task<List<LiveGameSource>> GetSourcesAsync(CancellationToken cancellationToken);
    protected virtual Task<LiveCategoryResult> FetchAsync(LiveGameSource source, int page, CancellationToken cancellationToken) => source.Site.GetCategoryRooms(source.Category, page, cancellationToken);
    public Task LoadAsync() => LoadCoreAsync(false);
    public virtual async Task RefreshAsync()
    {
        if (Loading) return;
        foreach (var site in MainVM.Sites.Select(s => s.LiveSite).OfType<YolanLive.Core.Douyin>()) site.ClearLiveStatusCache();
        CancelPending();
        await LoadCoreAsync(true);
    }
    private async Task LoadCoreAsync(bool replace)
    {
        if (Loading || (!replace && Items.Count >= MaxCachedRooms)) return;
        var current = generation;
        var cancellation = requests.Token;
        IsEmpty = Items.Count == 0;
        CanRetry = false;
        StatusMessage = Items.Count > 0 ? replace ? "正在刷新…" : "正在加载更多…" : "";
        Loading = true;
        try
        {
            var activeSources = replace ? null : sources;
            var activePages = replace ? new Dictionary<LiveGameSource, int>() : pages;
            var activeFinished = replace ? new HashSet<LiveGameSource>() : finished;
            if (activeSources == null)
            {
                var loaded = await GetSourcesAsync(cancellation);
                if (current != generation) return;
                activeSources = loaded;
                if (!replace) sources = loaded;
            }
            if (current != generation) return;
            var batches = await Task.WhenAll(activeSources.Where(s => !activeFinished.Contains(s)).Select(async source =>
            {
                try
                {
                    var page = activePages.TryGetValue(source, out var next) ? next : 1;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var result = await FetchAsync(source, page, timeout.Token);
                    return (source, page, result, error: (Exception)null);
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested) { return (source, page: 0, result: (LiveCategoryResult)null, error: ex); }
            }));
            if (current != generation) return;
            var rooms = new List<LiveRoomItem>();
            foreach (var batch in batches)
            {
                if (batch.error != null)
                {
                    activeFinished.Add(batch.source);
                    LogHelper.Log(batch.source.Site.Name + "直播列表读取失败", LogType.ERROR, batch.error);
                    continue;
                }
                activePages[batch.source] = batch.page + 1;
                if (!batch.result.HasMore) activeFinished.Add(batch.source);
                foreach (var room in batch.result.Rooms)
                {
                    if (LiveOnly && room.IsLive != true) continue;
                    room.Source = batch.source.Site;
                    rooms.Add(room);
                }
            }
            var failedBatch = batches.FirstOrDefault(b => b.error != null || b.result?.Error != null);
            var error = failedBatch.error ?? failedBatch.result?.Error;
            var unknownOnly = LiveOnly && rooms.Count == 0 && batches.Any(b => b.result?.Rooms.Any(r => r.IsLive == null) == true);
            EmptyMessage = error != null ? RequestErrorMessage.Report(error, failedBatch.source.Site.Name) :
                unknownOnly ? "部分开播状态查询失败，请稍后重试" : LiveOnly ? "当前没有已开播的直播" : "没有找到相关直播";
            var failed = error != null || unknownOnly;
            CanRetry = failed;
            if (replace && failed)
            {
                if (LiveOnly)
                {
                    var stopped = batches.SelectMany(b => (b.result?.Rooms ?? new List<LiveRoomItem>())
                        .Where(r => r.IsLive == false).Select(r => b.source.Site.Name + ":" + r.RoomID)).ToHashSet();
                    foreach (var room in Items.Where(r => stopped.Contains(r.Source.Name + ":" + r.RoomID)).ToArray()) Items.Remove(room);
                }
                CanLoadMore = false;
                StatusMessage = Items.Count > 0 ? "刷新失败，保留上次内容。" + EmptyMessage : "";
                return;
            }
            if (replace)
            {
                sources = activeSources;
                pages.Clear(); foreach (var entry in activePages) pages[entry.Key] = entry.Value;
                finished.Clear(); foreach (var source in activeFinished) finished.Add(source);
                Items.Clear();
            }
            var keys = new HashSet<string>(Items.Select(r => r.Source.Name + ":" + r.RoomID));
            foreach (var room in rooms.OrderByDescending(r => r.Online))
            {
                if (Items.Count >= MaxCachedRooms) break;
                if (keys.Add(room.Source.Name + ":" + room.RoomID)) Items.Add(room);
            }
            CanLoadMore = Items.Count < MaxCachedRooms && sources.Any(s => !finished.Contains(s));
            if (Items.Count >= MaxCachedRooms)
                Utils.ShowMessageToast("已加载 300 个直播间，可刷新列表或缩小分类、搜索范围");
            StatusMessage = failed && Items.Count > 0 ? EmptyMessage : "";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (current == generation)
            {
                CanLoadMore = false;
                EmptyMessage = RequestErrorMessage.Report(ex, PlatformName); CanRetry = true;
                StatusMessage = Items.Count > 0 ? "加载失败，保留上次内容。" + EmptyMessage : "";
                LogHelper.Log("直播列表读取失败", LogType.ERROR, ex);
            }
        }
        finally
        {
            if (current == generation) { IsEmpty = Items.Count == 0; Loading = false; HasLoaded = true; }
        }
    }
    public async Task HideStoppedAsync()
    {
        if (!LiveOnly || checkingStatus || Loading) return;
        checkingStatus = true;
        var current = generation;
        var cancellation = requests.Token;
        try
        {
            using var limit = new SemaphoreSlim(4);
            var checks = await Task.WhenAll(Items.ToArray().Select(async room =>
            {
                await limit.WaitAsync(cancellation);
                if (current != generation) { limit.Release(); return (room, live: (bool?)true, error: (Exception)null); }
                try { using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(10)); return (room, live: (bool?)await room.Source.GetLiveStatus(room.RoomID, timeout.Token), error: (Exception)null); }
                catch (Exception ex) when (!cancellation.IsCancellationRequested) { return (room, live: (bool?)null, error: ex); }
                finally { limit.Release(); }
            }));
            if (current != generation) return;
            foreach (var check in checks.Where(c => c.live == false)) Items.Remove(check.room);
            if (checks.Length > 0)
            {
                var failed = checks.FirstOrDefault(c => c.error != null);
                EmptyMessage = failed.error != null ? RequestErrorMessage.Report(failed.error, failed.room.Source.Name) : "当前没有已开播的直播";
            }
            IsEmpty = Items.Count == 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { checkingStatus = false; }
    }
    public void Reset()
    {
        CancelPending();
        sources = null;
        pages.Clear();
        finished.Clear();
        Items.Clear();
        CanLoadMore = false;
        IsEmpty = false;
        HasLoaded = false;
        CanRetry = false; StatusMessage = "";
    }
    public void CancelPending() { generation++; requests.Cancel(); requests.Dispose(); requests = new(); if (Loading) StatusMessage = ""; Loading = false; }
    public override async void Refresh() => await RefreshAsync();
    public override async void LoadMore() { if (CanLoadMore) await LoadAsync(); }
}
