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
    private int generation;
    private CancellationTokenSource requests = new();
    private string emptyMessage = "暂无可用游戏分类";
    public string EmptyMessage { get => emptyMessage; private set { emptyMessage = value; DoPropertyChanged(nameof(EmptyMessage)); } }
    private YolanLive.Core.Interface.ILiveSite loadedSite;
    private List<LiveCategory> loadedCategories;
    private int pictureGeneration = -1;
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
    public async Task LoadAsync()
    {
        CancelPending();
        var current = generation;
        var cancellation = requests.Token;
        var site = MainVM.BrowseSite.LiveSite;
        Loading = true;
        EmptyMessage = "暂无可用游戏分类";
        SelectedGroup = null;
        loadedCategories = null;
        loadedSite = null;
        Items.Clear();
        Groups.Clear();
        try
        {
            var categories = await GameCatalog.GetCategoriesAsync(site, cancellation);
            if (current != generation) return;
            loadedSite = site;
            loadedCategories = categories;
            foreach (var category in categories) Groups.Add(category);
            SelectedGroup = BrowsePreferences.RestoreGroup(MainVM.Sites.Single(s => s.LiveSite == site), Groups);
            _ = CompletePicturesAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (current == generation) { EmptyMessage = RequestErrorMessage.Report(ex, site.Name); HandleError(ex, EmptyMessage); } }
        finally { if (current == generation) { IsEmpty = Items.Count == 0; Loading = false; } }
    }
    public void ShowGroup(LiveCategory group)
    {
        Items.Clear();
        if (group != null)
            foreach (var category in GameBrowse.MergeCategories(group.Children.Select(c => new LiveGameSource { Site = loadedSite, Category = c })))
            {
                category.Pic = string.IsNullOrWhiteSpace(category.Pic) ? null :
                    category.Pic.StartsWith("//") ? "https:" + category.Pic : category.Pic;
                Items.Add(category);
            }
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
    public void CancelPending() { generation++; requests.Cancel(); requests.Dispose(); requests = new(); Loading = false; }
    public override async void Refresh() { GameCatalog.Invalidate(MainVM.BrowseSite.LiveSite); await LoadAsync(); }
}
