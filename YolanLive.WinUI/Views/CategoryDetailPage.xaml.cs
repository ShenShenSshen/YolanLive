using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using System.Threading;
namespace YolanLive.WinUI.Views;
public sealed partial class CategoryDetailPage : Page
{
    readonly CategoryDetailVM categoryDetailVM = new();
    readonly Site[] platforms = MainVM.Sites.ToArray();
    private bool selectingCategory;
    private CancellationTokenSource randomPick;
    public CategoryDetailPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Enabled;
        GamePlatforms.ItemsSource = new[] { "全部平台" }.Concat(platforms.Select(site => site.Label)).ToArray();

        Unloaded += (_, _) => CancelPending();
        _ = new PageStatusRefresh(this, () => categoryDetailVM.HasLoaded ? categoryDetailVM.HideStoppedAsync() : categoryDetailVM.LoadAsync(), CancelPending);
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.NavigationMode == NavigationMode.New && e.Parameter is LiveGameCategory category)
        {
            selectingCategory = true;
            GamePlatforms.SelectedIndex = 0;
            selectingCategory = false;
            MessageCenter.ChangeTitle(category.Name);
            var load = categoryDetailVM.LoadCategoryAsync(category);
            UpdateRandomButton();
            await load;
        }
        else if (categoryDetailVM.Category != null)
        {
            MessageCenter.ChangeTitle(categoryDetailVM.Category.Name);
            UpdateRandomButton();
            if (categoryDetailVM.HasLoaded) await categoryDetailVM.HideStoppedAsync();
            else await categoryDetailVM.LoadAsync();
        }
    }
    private async void GamePlatforms_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (selectingCategory || GamePlatforms.SelectedIndex < 0 || categoryDetailVM.Category == null) return;
        PlatformTabAnimation.Play(GamePlatforms, e);
        var index = GamePlatforms.SelectedIndex;
        CancelRandomPick();
        var load = categoryDetailVM.SelectPlatformAsync(index == 0 ? null : platforms[index - 1].LiveSite);
        UpdateRandomButton();
        await load;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        CancelRandomPick();
        base.OnNavigatedFrom(e);
    }

    private void CancelPending()
    {
        categoryDetailVM.CancelPending();
        CancelRandomPick();
    }

    private void CancelRandomPick()
    {
        var operation = randomPick;
        randomPick = null;
        operation?.Cancel();
        UpdateRandomButton();
    }

    private void UpdateRandomButton()
    {
        GameRandomButton.IsEnabled = randomPick == null && categoryDetailVM.GetActiveSources().Count > 0;
        GameRandomButton.Content = randomPick == null ? "随便逛逛" : "正在挑选…";
    }

    private async void GameRandomButton_Click(object sender, RoutedEventArgs e)
    {
        if (randomPick != null) return;
        var scope = categoryDetailVM.GetActiveSources();
        if (scope.Count == 0) return;
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        randomPick = operation;
        UpdateRandomButton();
        try
        {
            var selected = await RandomLive.PickPreparedAsync(operation.Token, gameScope: scope);
            if (!IsLoaded || randomPick != operation || operation.IsCancellationRequested) return;
            if (selected == null)
            {
                Utils.ShowMessageToast("当前范围暂无可观看的直播，请稍后再试");
                return;
            }
            MessageCenter.OpenLiveRoom(selected.Room.Source, selected.Room, new RandomBrowseSession(selected.Room, selected, scope));
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded && randomPick == operation) Utils.ShowMessageToast("挑选超时，请稍后再试");
        }
        catch (Exception error)
        {
            if (IsLoaded && randomPick == operation) Utils.ShowMessageToast(error.Message);
        }
        finally
        {
            if (randomPick == operation)
            {
                randomPick = null;
                UpdateRandomButton();
            }
        }
    }

    private void MyAdaptiveGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LiveRoomItem room) return;
        CancelRandomPick();
        MessageCenter.OpenLiveRoom(room.Source, room);
    }
}
