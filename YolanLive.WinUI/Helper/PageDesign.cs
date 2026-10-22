using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System;
using System.Linq;
using Windows.UI.ViewManagement;

namespace YolanLive.WinUI.Helper;

// Shared browsing insets and native integrated headers; player chrome is separate.
public static class PageDesign
{
    public static readonly DependencyProperty SearchProperty = DependencyProperty.RegisterAttached(
        "Search", typeof(bool), typeof(PageDesign), new PropertyMetadata(false, OnSearchChanged));
    public static bool GetSearch(DependencyObject value) => (bool)value.GetValue(SearchProperty);
    public static void SetSearch(DependencyObject value, bool enabled) => value.SetValue(SearchProperty, enabled);
    private static void OnSearchChanged(DependencyObject value, DependencyPropertyChangedEventArgs change)
    {
        if (value is not AutoSuggestBox search) return;
        search.QuerySubmitted -= SearchSubmitted;
        if ((bool)change.NewValue) search.QuerySubmitted += SearchSubmitted;
    }
    private static async void SearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var shell = GetShell(sender);
        if (shell != null) await shell.SubmitSearchAsync(args.QueryText);
    }
    public static readonly DependencyProperty InsetsProperty = DependencyProperty.RegisterAttached(
        "Insets", typeof(bool), typeof(PageDesign), new PropertyMetadata(false, OnInsetsChanged));
    public static bool GetInsets(DependencyObject value) => (bool)value.GetValue(InsetsProperty);
    public static void SetInsets(DependencyObject value, bool enabled) => value.SetValue(InsetsProperty, enabled);
    private static void OnInsetsChanged(DependencyObject value, DependencyPropertyChangedEventArgs change)
    {
        if (value is not Grid root) return;
        root.Loaded -= Loaded; root.Unloaded -= Unloaded; root.SizeChanged -= SizeChanged;
        if (!(bool)change.NewValue) return;
        root.Loaded += Loaded; root.Unloaded += Unloaded; root.SizeChanged += SizeChanged;
        ApplyInset(root);
    }
    private static Page GetPage(DependencyObject child)
    {
        for (var current = child; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is Page page) return page;
        return null;
    }
    private static bool UsesMergedHeader(Page page)
    {
        for (DependencyObject current = page; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is MainPage shell && shell.IsTopNavigation) return false;
            if (current is BaseFramePage) return true;
        }
        return false;
    }
    private static MainPage GetShell(DependencyObject child)
    {
        for (var current = child; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is MainPage shell) return shell;
        return null;
    }
    private static void ApplyInset(Grid root)
    {
        var inset = root.ActualWidth < 640 ? 16 : 24;
        var merged = UsesMergedHeader(GetPage(root));
        root.Padding = new Thickness(inset, merged ? 0 : inset, inset, inset);
        if (root.FindName("SettingsCards") is FrameworkElement settings && root.FindName("PageTitle") is TextBlock title)
            title.Margin = new Thickness(Math.Max(0, (root.ActualWidth - 2 * inset - settings.MaxWidth) / 2), 0, 0, 0);
        if (root.FindName("BrowserTitleBar") is TitleBar bar)
        {
            bar.Margin = new Thickness(0, bar.Margin.Top, merged ? -inset : 0, bar.Margin.Bottom);
            UpdatePlatformLayout(root, bar);
        }
    }
    private static void UpdatePlatformLayout(Grid root, TitleBar bar)
    {
        if (root.FindName("PlatformSurface") == null &&
            root.FindName("BrowseHeader") is Grid plainHeader &&
            root.FindName("HeaderSearch") is AutoSuggestBox plainSearch &&
            root.FindName("HeaderActions") is StackPanel actions)
        {
            var owner = App.GetWindowFor(root);
            var inset = owner?.AppWindow.TitleBar.RightInset / (root.XamlRoot?.RasterizationScale ?? 1) ?? 138;
            var drag = (double)App.Current.Resources["TitleBarMinDragRegionWidth"];
            var innerWidth = Math.Max(0, bar.ActualWidth - inset - drag - 8);
            var manage = root.FindName("ManageFavorites") as Button;
            if (manage != null)
            {
                var direct = innerWidth >= 348;
                manage.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
                ((MenuFlyoutItem)root.FindName("ManageFavoritesOverflow")).Visibility = direct ? Visibility.Collapsed : Visibility.Visible;
            }
            // Loading may hide a button; reserve its slot to keep the search position stable.
            var actionItems = actions.Children.OfType<FrameworkElement>().Where(c => c != manage || manage.Visibility == Visibility.Visible).ToArray();
            var actionWidth = actionItems.Sum(c => c.Width) + Math.Max(0, actionItems.Length - 1) * actions.Spacing;
            var availableWidth = innerWidth - actionWidth - 3 * plainHeader.ColumnSpacing - 112;
            var plainShell = GetShell(root);
            var show = availableWidth >= 180 && plainShell?.IsTopNavigation != true;
            plainSearch.Width = Math.Clamp(availableWidth, 180, 320);
            plainSearch.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (root.IsLoaded && plainShell?.ActivePage == GetPage(root)) plainShell.SetHeaderSearch(show ? plainSearch : null);
            return;
        }
        if (root.FindName("PlatformSurface") is not Border surface ||
            root.FindName("PlatformOverflowHost") is not Border overflow ||
            root.FindName("BrowseHeader") is not Grid header) return;
        var window = App.GetWindowFor(root);
        var captionInset = window?.AppWindow.TitleBar.RightInset / (root.XamlRoot?.RasterizationScale ?? 1) ?? 138;
        // Budget every header column, including the native drag strip, before sizing search.
        var refresh = (Button)root.FindName("HeaderRefresh");
        var dragWidth = (double)App.Current.Resources["TitleBarMinDragRegionWidth"];
        var available = Math.Max(0, bar.ActualWidth - captionInset - dragWidth - refresh.Width - 3 * header.ColumnSpacing - 8);
        var search = root.FindName("HeaderSearch") as AutoSuggestBox;
        var shell = GetShell(root);
        var showSearch = search != null && available >= 80 + 180 && shell?.IsTopNavigation != true;
        var compact = available < (showSearch ? 392 + 180 : 392);
        if (search != null)
        {
            search.Visibility = showSearch ? Visibility.Visible : Visibility.Collapsed;
            search.Width = Math.Clamp(available - (compact ? 80 : 392), 180, 320);
            if (root.IsLoaded && shell != null && shell.ActivePage == GetPage(root)) shell.SetHeaderSearch(showSearch ? search : null);
        }
        if (compact && surface.Parent == header)
        {
            header.Children.Remove(surface); overflow.Child = surface;
        }
        else if (!compact && surface.Parent == overflow)
        {
            overflow.Child = null; header.Children.Insert(0, surface);
        }
        overflow.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)root.FindName("CompactPageTitle")).Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }
    private static void HeaderSizeChanged(object sender, SizeChangedEventArgs args)
    {
        for (DependencyObject current = (TitleBar)sender; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is Grid root && GetInsets(root))
            {
                UpdatePlatformLayout(root, (TitleBar)sender);
                break;
            }
    }
    internal static void ActivateHeader(Page page)
    {
        if (page?.Content is not Grid root || root.FindName("BrowserTitleBar") is not TitleBar bar || !UsesMergedHeader(page)) return;
        ApplyInset(root);
        var window = App.GetWindowFor(page);
        if (window == null) return;
        bar.IsBackButtonVisible = page is Views.SearchPage or Views.CategoryDetailPage;
        bar.IsBackButtonEnabled = page.Frame?.CanGoBack == true;
        bar.BackRequested -= BackRequested;
        bar.BackRequested += BackRequested;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        bar.AutoRefreshDragRegions = true;
        window.SetTitleBar(bar);
        bar.RecomputeDragRegions();
    }
    internal static void DeactivateHeader(Page page)
    {
        if (page?.Content is Grid root && root.FindName("BrowserTitleBar") is TitleBar bar)
        {
            bar.AutoRefreshDragRegions = false;
            if (root.FindName("HeaderSearch") is AutoSuggestBox search) GetShell(root)?.ClearHeaderSearch(search);
        }
    }
    private static void BackRequested(TitleBar sender, object args)
    {
        var page = GetPage(sender);
        if (page?.Frame?.CanGoBack == true) page.Frame.GoBack();
    }
    private static void SizeChanged(object sender, SizeChangedEventArgs args) => ApplyInset((Grid)sender);
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        var root = (Grid)sender; ApplyInset(root);
        if (root.FindName("BrowserTitleBar") is TitleBar bar)
        {
            bar.SizeChanged -= HeaderSizeChanged; bar.SizeChanged += HeaderSizeChanged;
        }
        ActivateHeader(GetPage(root));
        // Keep native header/search stable while page content enters.
        foreach (var child in root.Children)
            if (child is FrameworkElement content && Grid.GetRow(content) > 0)
                InteractionMotion.Reveal(content, 160);
    }
    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        if (((Grid)sender).FindName("BrowserTitleBar") is TitleBar bar)
        { bar.AutoRefreshDragRegions = false; bar.BackRequested -= BackRequested; bar.SizeChanged -= HeaderSizeChanged; }
        if (((Grid)sender).FindName("HeaderSearch") is AutoSuggestBox search) GetShell((Grid)sender)?.ClearHeaderSearch(search);
        foreach (var child in ((Grid)sender).Children)
            if (child is FrameworkElement content && Grid.GetRow(content) > 0)
                InteractionMotion.Reset(content);
    }
}
