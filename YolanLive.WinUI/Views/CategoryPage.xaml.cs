using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;
namespace YolanLive.WinUI.Views;
public sealed partial class CategoryPage : Page
{
    readonly CategoryVM categoryVM = new();
    public CategoryPage()
    {
        InitializeComponent(); Unloaded += (_, _) => categoryVM.CancelPending(); NavigationCacheMode = NavigationCacheMode.Enabled;
        GroupPicker.SelectionChanged += (_, change) => PlatformTabAnimation.Play(GroupPicker, change);
        PlatformPicker.ItemsSource = MainVM.Sites;
        PlatformPicker.SelectedItem = MainVM.BrowseSite;
        Loaded += (_, _) =>
        {
            if (PlatformPicker.SelectedItem != MainVM.BrowseSite) PlatformPicker.SelectedItem = MainVM.BrowseSite;
            else if (categoryVM.Items.Count == 0) _ = LoadCategoriesAsync();
            else _ = categoryVM.CompletePicturesAsync();
        };
    }
    private async System.Threading.Tasks.Task LoadCategoriesAsync()
    {
        await categoryVM.LoadAsync();
    }
    private async void PlatformPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (PlatformPicker.SelectedItem is not Site site) return;
        PlatformTabAnimation.Play(PlatformPicker, e);
        MainVM.BrowseSite = site;
        await LoadCategoriesAsync();
    }
    private void GamesGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        args.ItemContainer.ContextFlyout = null;
        // Preserve the URL binding when recycling. DecodedImage releases pixels on Unloaded.
        if (args.InRecycleQueue) return;
        if (args.Item is not LiveGameCategory game || game.Sources == null || game.Sources.Count == 0) return;
        var site = MainVM.Sites.Single(s => s.LiveSite == game.Sources[0].Site);
        var action = new MenuFlyoutItem();
        var menu = new MenuFlyout();
        menu.Items.Add(action);
        menu.Opening += (_, _) => action.Text = BrowsePreferences.IsPinned(site, game) ? "取消固定" : "固定到首页";
        action.Click += (_, _) =>
        {
            if (!BrowsePreferences.TogglePin(site, game)) Utils.ShowMessageToast("每个平台最多固定 6 个游戏，请先移除已有入口");
        };
        args.ItemContainer.ContextFlyout = menu;
    }
    private void GridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LiveGameCategory category) MessageCenter.NavigatePage(typeof(CategoryDetailPage), category);
    }
}
