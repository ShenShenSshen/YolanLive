using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using System.Linq;
namespace YolanLive.WinUI.Views;
public sealed partial class CategoryPage : Page
{
    readonly CategoryVM categoryVM = new();
    long? filterTextToken;
    public CategoryPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            GameFilterFlyout.Hide(); categoryVM.CancelPending();
            if (filterTextToken is long token) GameFilter.UnregisterPropertyChangedCallback(AutoSuggestBox.TextProperty, token);
            filterTextToken = null;
        };
        NavigationCacheMode = NavigationCacheMode.Enabled;
        GroupPicker.SelectionChanged += (_, change) =>
        {
            PlatformTabAnimation.Play(GroupPicker, change);
            if (!categoryVM.Loading && change.AddedItems.Count > 0) categoryVM.FilterText = "";
        };
        PlatformPicker.ItemsSource = MainVM.Sites;
        PlatformPicker.SelectedItem = MainVM.BrowseSite;
        Loaded += (_, _) =>
        {
            filterTextToken ??= GameFilter.RegisterPropertyChangedCallback(AutoSuggestBox.TextProperty, (_, _) => UpdateGameFilter());
            UpdateGameFilter();
            if (PlatformPicker.SelectedItem != MainVM.BrowseSite) PlatformPicker.SelectedItem = MainVM.BrowseSite;
            else if (categoryVM.Groups.Count == 0) _ = LoadCategoriesAsync();
            else _ = categoryVM.CompletePicturesAsync();
        };
    }
    private async System.Threading.Tasks.Task LoadCategoriesAsync()
    {
        await categoryVM.LoadAsync();
    }
    private void CategoryTools_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 640;
        if (compact && GameFilterHost.Child != null)
        {
            GameFilterHost.Child = null;
            GameFilterFlyout.Content = GameFilter;
        }
        else if (!compact && GameFilterFlyout.Content != null)
        {
            GameFilterFlyout.Hide();
            GameFilterFlyout.Content = null;
            GameFilterHost.Child = GameFilter;
        }
        GameFilterHost.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactGameFilter.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        GameFilter.Width = compact ? System.Math.Min(280, System.Math.Max(120, args.NewSize.Width - 32))
            : System.Math.Clamp(args.NewSize.Width * .28, 200, 280);
    }
    private void UpdateGameFilter()
    {
        var name = "在" + MainVM.BrowseSite.Label + "找游戏";
        GameFilter.PlaceholderText = name;
        AutomationProperties.SetName(GameFilter, name);
        if (!string.IsNullOrWhiteSpace(GameFilter.Text)) name += "：" + GameFilter.Text;
        ToolTipService.SetToolTip(CompactGameFilter, name);
        AutomationProperties.SetName(CompactGameFilter, name);
        FilterMarker.Visibility = string.IsNullOrWhiteSpace(GameFilter.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void GameFilterFlyout_Opened(object sender, object args) => GameFilter.Focus(FocusState.Programmatic);
    private async void PlatformPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (PlatformPicker.SelectedItem is not Site site) return;
        PlatformTabAnimation.Play(PlatformPicker, e);
        MainVM.BrowseSite = site;
        UpdateGameFilter();
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
            if (args.ItemContainer.ContentTemplateRoot is FrameworkElement root) GameCard.Refresh(root);
        };
        args.ItemContainer.ContextFlyout = menu;
    }
    private void GridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LiveGameCategory category) MessageCenter.NavigatePage(typeof(CategoryDetailPage), category);
    }
}
