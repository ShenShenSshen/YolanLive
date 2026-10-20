using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
namespace YolanLive.WinUI.Views;
public sealed partial class RecomendPage : Page
{
    readonly RecomendVM recomendVM = new();
    private int shortcutGeneration;
    public RecomendPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Enabled;
        PlatformPicker.ItemsSource = MainVM.Sites;
        PlatformPicker.SelectedItem = MainVM.BrowseSite;
        Loaded += async (_, _) =>
        {
            RefreshPinnedGames();
            if (PlatformPicker.SelectedItem != MainVM.BrowseSite) PlatformPicker.SelectedItem = MainVM.BrowseSite;
            else if (!recomendVM.HasLoaded) await recomendVM.LoadAsync();
            else await recomendVM.HideStoppedAsync();

        };
        Unloaded += (_, _) => { shortcutGeneration++;  recomendVM.CancelPending(); };
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
    internal void RefreshPinnedGames()
    {
        var pins = BrowsePreferences.ReadPins(MainVM.BrowseSite);
        PinnedGamesList.ItemsSource = pins;
        PinnedGamesSection.Visibility = pins.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
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
