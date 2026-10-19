using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System;
using Windows.UI.ViewManagement;

namespace PlayWin.WinUI.Helper;

// Shared browsing insets and native integrated headers; player chrome is separate.
public static class PageDesign
{
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
        if (root.FindName("PlatformSurface") is not Border surface ||
            root.FindName("PlatformOverflowHost") is not Border overflow ||
            root.FindName("BrowseHeader") is not Grid header) return;
        var window = App.GetWindowFor(root);
        var captionInset = window?.AppWindow.TitleBar.RightInset / (root.XamlRoot?.RasterizationScale ?? 1) ?? 138;
        var available = Math.Max(0, bar.ActualWidth - captionInset - 56);
        var compact = available < 384;
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
            bar.AutoRefreshDragRegions = false;
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
        var visual = ElementCompositionPreview.GetElementVisual(root);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
        if (!new UISettings().AnimationsEnabled) return;
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0.6f); fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(160);
        visual.StartAnimation("Opacity", fade);
    }
    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        if (((Grid)sender).FindName("BrowserTitleBar") is TitleBar bar)
        { bar.AutoRefreshDragRegions = false; bar.BackRequested -= BackRequested; bar.SizeChanged -= HeaderSizeChanged; }
        var visual = ElementCompositionPreview.GetElementVisual((Grid)sender);
        visual.StopAnimation("Opacity"); visual.Opacity = 1;
    }
}
