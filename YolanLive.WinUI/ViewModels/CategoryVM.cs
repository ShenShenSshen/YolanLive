using YolanLive.Core.Helper;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
namespace YolanLive.WinUI.ViewModels;
public class CategoryVM : BaseViewModel
{
    public ObservableCollection<LiveGameCategory> Items { get; } = new();
    public ObservableCollection<LiveCategory> Groups { get; } = new();
    private readonly bool combined;
    private readonly Dictionary<Site, List<LiveCategory>> directories = new();
    private List<LiveGameCategory> directoryGames = new();
    private Dictionary<string, HashSet<string>> directoryGroups = new();
    internal CategoryVM(bool combined = false) => this.combined = combined;
    internal bool HasDirectory => Groups.Count > 0 && (directoryGames.Count > 0 || Items.Count > 0);
    private int generation;
    private CancellationTokenSource requests = new();
    private string emptyMessage = "暂无可用游戏分类";
    public string EmptyMessage { get => emptyMessage; private set { emptyMessage = value; DoPropertyChanged(nameof(EmptyMessage)); } }
    private YolanLive.Core.Interface.ILiveSite loadedSite;
    private List<LiveCategory> loadedCategories;
    private int pictureGeneration = -1;
    private string filterText = "";
    public string FilterText
    {
        get => filterText;
        set
        {
            if (filterText == value) return;
            filterText = value;
            ShowGroup(selectedGroup);
            DoPropertyChanged(nameof(FilterText));
        }
    }
    private LiveCategory selectedGroup;
    public LiveCategory SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (selectedGroup == value) return;
            selectedGroup = value;
            ShowGroup(value);
            if (value != null && loadedSite != null)
                SettingHelper.SetValue(BrowsePreferences.GroupKey(MainVM.Sites.Single(s => s.LiveSite == loadedSite)), value.ID);
            DoPropertyChanged(nameof(SelectedGroup));
        }
    }
    public Task LoadAsync() => combined ? LoadDirectoryAsync(false) : LoadCoreAsync(false);
    public Task RefreshAsync()
    {
        if (Loading) return Task.CompletedTask;
        if (combined) return LoadDirectoryAsync(true);
        GameCatalog.Invalidate(MainVM.BrowseSite.LiveSite);
        return LoadCoreAsync(true);
    }

    private async Task LoadDirectoryAsync(bool refresh)
    {
        if (Loading) return;
        CancelPending();
        var current = generation;
        var cancellation = requests.Token;
        var selectedId = SelectedGroup?.ID;
        var sites = MainVM.Sites.ToArray();
        CanRetry = false;
        StatusMessage = refresh && Items.Count > 0 ? "正在刷新…" : "";
        IsEmpty = Items.Count == 0;
        Loading = true;
        try
        {
            var batches = await Task.WhenAll(sites.Select(async site =>
            {
                try
                {
                    var categories = await GameCatalog.GetCategoriesAsync(site.LiveSite, cancellation, refresh);
                    return (site, categories, error: (Exception)null);
                }
                catch (Exception error) when (!cancellation.IsCancellationRequested)
                {
                    return (site, categories: (List<LiveCategory>)null, error);
                }
            }));
            if (current != generation) return;

            foreach (var batch in batches)
            {
                if (batch.categories != null) directories[batch.site] = batch.categories;
                else LogHelper.Log(batch.site.Label + "游戏目录读取失败", LogType.DEBUG, batch.error);
            }
            var entries = sites.Where(directories.ContainsKey).SelectMany(site =>
                directories[site].SelectMany(group => group.Children
                    .Where(game => game.ID != "0" && !string.IsNullOrWhiteSpace(game.Name))
                    .Select(game => (group: DirectoryGroup(site.SiteType, group.ID),
                        source: new LiveGameSource { Site = site.LiveSite, Category = game })))).ToArray();
            directoryGames = GameBrowse.MergeCategories(entries.Select(e => e.source));
            foreach (var game in directoryGames)
                if (game.Pic?.StartsWith("//", StringComparison.Ordinal) == true) game.Pic = "https:" + game.Pic;
            directoryGroups = entries.GroupBy(e => e.group).ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.source.Category.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase));

            Groups.Clear();
            Groups.Add(new LiveCategory { ID = "all", Name = "全部" });
            foreach (var name in new[] { "网游", "手游", "单机", "游戏赛事", "赛车", "其他" })
                if (directoryGroups.ContainsKey(name)) Groups.Add(new LiveCategory { ID = name, Name = name });
            SelectedGroup = Groups.FirstOrDefault(group => group.ID == selectedId) ?? Groups[0];
            ShowGroup(SelectedGroup);

            var failed = batches.Where(batch => batch.error != null).Select(batch => batch.site.Label).ToArray();
            var hasFailures = failed.Length > 0;
            CanRetry = hasFailures;
            var message = "部分平台目录暂不可用，可刷新重试：" + string.Join("、", failed);
            if (hasFailures && directoryGames.Count == 0) EmptyMessage = "游戏目录暂不可用，请刷新重试";
            StatusMessage = hasFailures && Items.Count > 0 ? message : "";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (current == generation)
            {
                IsEmpty = Items.Count == 0;
                Loading = false;
            }
        }
    }

    private static string DirectoryGroup(LiveSite site, string id) => (site, id) switch
    {
        (LiveSite.Bilibili, "2") or (LiveSite.Huya, "1") or (LiveSite.Douyu, "1") => "网游",
        (LiveSite.Bilibili, "3") or (LiveSite.Huya, "3") or (LiveSite.Douyu, "9") => "手游",
        (LiveSite.Bilibili, "6") or (LiveSite.Huya, "2") or (LiveSite.Douyu, "15") => "单机",
        (LiveSite.Bilibili, "13") => "游戏赛事",
        (LiveSite.Douyu, "22") => "赛车",
        _ => "其他"
    };

    private async Task LoadCoreAsync(bool preserve)
    {
        CancelPending();
        var current = generation;
        var cancellation = requests.Token;
        var site = MainVM.BrowseSite.LiveSite;
        var retain = preserve && loadedSite == site;
        CanRetry = false;
        StatusMessage = retain && Items.Count > 0 ? "正在刷新…" : "";
        Loading = true;
        EmptyMessage = "暂无可用游戏分类";
        if (!retain)
        {
            FilterText = "";
            SelectedGroup = null;
            loadedCategories = null;
            loadedSite = null;
            Items.Clear(); Groups.Clear();
        }
        IsEmpty = Items.Count == 0;
        try
        {
            var categories = await GameCatalog.GetCategoriesAsync(site, cancellation);
            if (current != generation) return;
            SelectedGroup = null;
            Items.Clear(); Groups.Clear();
            loadedSite = site;
            loadedCategories = categories;
            foreach (var category in categories) Groups.Add(category);
            SelectedGroup = BrowsePreferences.RestoreGroup(MainVM.Sites.Single(s => s.LiveSite == site), Groups);
            StatusMessage = "";
            _ = CompletePicturesAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (current == generation)
            {
                EmptyMessage = RequestErrorMessage.Report(ex, site.Name); CanRetry = true;
                StatusMessage = Items.Count > 0 ? "刷新失败，保留上次内容。" + EmptyMessage : "";
                LogHelper.Log("分类读取失败", LogType.ERROR, ex);
            }
        }
        finally { if (current == generation) { IsEmpty = Items.Count == 0; Loading = false; } }
    }
    public void ShowGroup(LiveCategory group)
    {
        if (combined)
        {
            directoryGroups.TryGetValue(group?.ID ?? "all", out var names);
            var matches = directoryGames.Where(game => (names == null || names.Contains(game.Name)) && ListProjection.Matches(FilterText, game.Name));
            ListProjection.Sync(Items, matches);
            EmptyMessage = string.IsNullOrWhiteSpace(FilterText) ? "暂无可用游戏分类" : "没有找到匹配的游戏，试试其他名称";
            IsEmpty = Items.Count == 0;
            return;
        }
        // Filter the loaded platform directory without another request. Clearing
        // the filter restores the selected group and its original navigation IDs.
        var filtering = !string.IsNullOrWhiteSpace(FilterText);
        var children = filtering
            ? loadedCategories?.SelectMany(c => c.Children) ?? Enumerable.Empty<LiveSubCategory>()
            : group?.Children ?? Enumerable.Empty<LiveSubCategory>();
        var games = GameBrowse.MergeCategories(children.Select(c => new LiveGameSource { Site = loadedSite, Category = c }))
            .Where(c => ListProjection.Matches(FilterText, c.Name)).ToList();
        foreach (var category in games)
            category.Pic = string.IsNullOrWhiteSpace(category.Pic) ? null :
                category.Pic.StartsWith("//") ? "https:" + category.Pic : category.Pic;
        ListProjection.Sync(Items, games);
        EmptyMessage = filtering ? "没有找到匹配的游戏，试试其他名称" : "暂无可用游戏分类";
        IsEmpty = Items.Count == 0;
    }
    public async Task CompletePicturesAsync()
    {
        if (loadedSite is not YolanLive.Core.Douyin || loadedCategories == null || pictureGeneration == generation || !NeedsPictures(loadedCategories)) return;
        var current = generation;
        var categories = loadedCategories;
        var cancellation = requests.Token;
        pictureGeneration = current;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            budget.CancelAfter(TimeSpan.FromSeconds(10));
            foreach (var source in MainVM.Sites.Where(s => s.SiteType != LiveSite.Douyin))
            {
                if (current != generation || budget.IsCancellationRequested || !NeedsPictures(categories)) return;
                List<LiveCategory> donor;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    donor = await GameCatalog.GetCategoriesAsync(source.LiveSite, timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (budget.IsCancellationRequested) return;
                    continue;
                }
                catch (Exception error)
                {
                    LogHelper.Log("抖音分类补图来源暂不可用：" + source.Label + " " + error.GetType().Name, LogType.DEBUG);
                    continue;
                }
                if (current != generation || cancellation.IsCancellationRequested) return;
                if (!GameBrowse.FillMissingCategoryPictures(categories, donor)) continue;
                for (var index = 0; index < Items.Count; index++)
                {
                    var game = Items[index];
                    if (!string.IsNullOrWhiteSpace(game.Pic)) continue;
                    var picture = game.Sources.Select(s => s.Category.Pic).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                    if (picture == null) continue;
                    // Replace only this visible card so the collection notifies its image binding.
                    Items[index] = new LiveGameCategory { Name = game.Name, Pic = picture, Sources = game.Sources };
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { LogHelper.Log("抖音分类补图未完成：" + error.GetType().Name, LogType.DEBUG); }
        finally { if (pictureGeneration == current) pictureGeneration = -1; }
    }
    private static bool NeedsPictures(IEnumerable<LiveCategory> categories) =>
        categories.SelectMany(c => c.Children).Any(c => string.IsNullOrWhiteSpace(c.Pic));
    public void CancelPending() { generation++; requests.Cancel(); requests.Dispose(); requests = new(); if (Loading) StatusMessage = ""; Loading = false; }
    public override async void Refresh() => await RefreshAsync();
}
