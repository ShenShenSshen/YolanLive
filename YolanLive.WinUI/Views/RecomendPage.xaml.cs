using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Automation;
namespace YolanLive.WinUI.Views;
public sealed partial class RecomendPage : Page
{
    readonly RecomendVM recomendVM = new();
    private int shortcutGeneration;
    private CancellationTokenSource recentRequest;
    private CancellationTokenSource pinArtworkRequest;
    private LiveRoomItem recentRoom;
    public RecomendPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Enabled;
        PlatformPicker.ItemsSource = MainVM.Sites;
        PlatformPicker.SelectedItem = MainVM.BrowseSite;
        Loaded += async (_, _) =>
        {
            RefreshPinnedGames();
            _ = RefreshRecentAsync();
            if (PlatformPicker.SelectedItem != MainVM.BrowseSite) PlatformPicker.SelectedItem = MainVM.BrowseSite;
            else if (!recomendVM.HasLoaded) await recomendVM.LoadAsync();
            else await recomendVM.HideStoppedAsync();

        };
        Unloaded += (_, _) => { shortcutGeneration++; recentRequest?.Cancel(); pinArtworkRequest?.Cancel(); recomendVM.CancelPending(); };
        _ = new PageStatusRefresh(this, () => recomendVM.HasLoaded ? recomendVM.HideStoppedAsync() : recomendVM.LoadAsync(), recomendVM.CancelPending);
    }
    private async void PlatformPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (PlatformPicker.SelectedItem is not Site site) return;
        PlatformTabAnimation.Play(PlatformPicker, e);
        MainVM.BrowseSite = site;
        shortcutGeneration++;
        RefreshPinnedGames();
        recomendVM.Reset();
        await recomendVM.LoadAsync();
    }

    private void QuickBrowse_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateQuickBrowse();

    private void UpdateQuickBrowse()
    {
        var hasRecent = ResumeButton.Visibility == Visibility.Visible;
        var hasPins = PinnedGamesSection.Visibility == Visibility.Visible;
        QuickBrowse.Visibility = hasRecent || hasPins ? Visibility.Visible : Visibility.Collapsed;
        var sideBySide = hasRecent && hasPins && QuickBrowse.ActualWidth >= 620;
        QuickBrowse.ColumnSpacing = sideBySide ? 20 : 0;
        PinsColumn.Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(PinnedGamesSection, sideBySide ? 1 : 0);
        Grid.SetRow(PinnedGamesSection, hasRecent && !sideBySide ? 1 : 0);
    }

    private async Task RefreshRecentAsync()
    {
        recentRequest?.Cancel();
        using var request = recentRequest = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        ResumeButton.Visibility = Visibility.Collapsed;
        UpdateQuickBrowse();
        try
        {
            var item = (await DatabaseHelper.GetHistory(1)).FirstOrDefault();
            var site = MainVM.Sites.FirstOrDefault(candidate => candidate.Name == item?.SiteName);
            if (site == null || request.IsCancellationRequested || string.IsNullOrWhiteSpace(item.RoomID)) return;
            var room = await site.LiveSite.GetRoomDetail(item.RoomID, request.Token).WaitAsync(request.Token);
            if (request.IsCancellationRequested || room?.Status != true || room.IsRecord) return;

            recentRoom = new LiveRoomItem { RoomID = item.RoomID, Source = site.LiveSite };
            RecentTitle.Text = string.IsNullOrWhiteSpace(room.Title) ? item.UserName : room.Title;
            RecentMeta.Text = "直播中　" + site.Label + " / " + (room.UserName ?? item.UserName);
            DecodedImage.SetUrl(RecentCover, string.IsNullOrWhiteSpace(room.Cover) ? (room.UserAvatar ?? item.Photo) : room.Cover);
            AutomationProperties.SetName(ResumeButton, "接着看：" + RecentTitle.Text);
            ResumeButton.Visibility = Visibility.Visible;
            UpdateQuickBrowse();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) { LogHelper.Log("接着看状态读取失败", LogType.DEBUG, error); }
        finally { if (recentRequest == request) recentRequest = null; }
    }

    private async void ResumeRefresh_Click(object sender, RoutedEventArgs args) => await RefreshRecentAsync();

    private void ResumeButton_Click(object sender, RoutedEventArgs args)
    {
        if (recentRoom != null) MessageCenter.OpenLiveRoom(recentRoom.Source, recentRoom);
    }


    private async Task LoadPinnedArtworkAsync(Site site, System.Collections.Generic.List<PinnedGame> pins)
    {
        pinArtworkRequest?.Cancel();
        if (pins.Count == 0) return;
        using var request = pinArtworkRequest = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var catalog = await GameCatalog.LoadAsync(site.LiveSite, request.Token);
            if (request.IsCancellationRequested || !IsLoaded || site != MainVM.BrowseSite || PinnedGamesList.ItemsSource != pins) return;
            foreach (var pin in pins)
            {
                var picture = BrowsePreferences.ResolvePin(site, pin, catalog)?.Pic;
                pin.Picture = picture?.StartsWith("//", StringComparison.Ordinal) == true ? "https:" + picture : picture;
            }
            PinnedGamesList.ItemsSource = pins.ToArray();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) { LogHelper.Log("常看游戏图片读取失败", LogType.DEBUG, error); }
        finally { if (pinArtworkRequest == request) pinArtworkRequest = null; }
    }

    internal void RefreshPinnedGames()
    {
        var site = MainVM.BrowseSite;
        var pins = BrowsePreferences.ReadPins(site);
        PinnedGamesList.ItemsSource = pins;
        PinnedGamesSection.Visibility = pins.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateQuickBrowse();
        if (IsLoaded) _ = LoadPinnedArtworkAsync(site, pins);
    }
    private void PinnedGamesList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        args.ItemContainer.ContextFlyout = null;
        if (args.InRecycleQueue || args.Item is not PinnedGame pin) return;
        var site = MainVM.BrowseSite;
        var menu = new MenuFlyout();
        var remove = new MenuFlyoutItem { Text = "取消固定" };
        remove.Click += (_, _) => { BrowsePreferences.RemovePin(site, pin); RefreshPinnedGames(); };
        menu.Items.Add(remove);
        args.ItemContainer.ContextFlyout = menu;
    }
    private async void PinnedGamesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PinnedGame pin) return;
        var current = ++shortcutGeneration;
        var site = MainVM.BrowseSite;
        try
        {
            var catalog = await GameCatalog.LoadAsync(site.LiveSite);
            if (!IsLoaded || current != shortcutGeneration || MainVM.BrowseSite != site) return;
            var game = BrowsePreferences.ResolvePin(site, pin, catalog);
            if (game == null) Utils.ShowMessageToast("这个游戏分类已不可用，请在游戏分类中重新选择并固定");
            else MessageCenter.NavigatePage(typeof(CategoryDetailPage), game);
        }
        catch (Exception ex)
        {
            if (IsLoaded && current == shortcutGeneration) Utils.ShowMessageToast(RequestErrorMessage.Report(ex, site.Name));
        }
    }
    private void MyAdaptiveGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LiveRoomItem room) MessageCenter.OpenLiveRoom(room.Source, room);
    }
}
