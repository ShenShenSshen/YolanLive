using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Windows.Input;
namespace YolanLive.WinUI.Controls;
public partial class MyAdaptiveGridView : GridView
{
    public ICommand LoadMoreCommand { get; set; }
    public bool CanLoadMore { get; set; }
    public double LoadMoreBottomOffset { get; set; } = 100;
    public static readonly DependencyProperty DataLoadingProperty = DependencyProperty.Register(nameof(DataLoading), typeof(bool), typeof(MyAdaptiveGridView), new PropertyMetadata(true, OnLoadingChanged));
    public bool DataLoading { get => (bool)GetValue(DataLoadingProperty); set => SetValue(DataLoadingProperty, value); }
    public static readonly DependencyProperty DesiredWidthProperty = DependencyProperty.Register(nameof(DesiredWidth), typeof(double), typeof(MyAdaptiveGridView), new PropertyMetadata(280d, OnSizeSettingChanged));
    public double DesiredWidth { get => (double)GetValue(DesiredWidthProperty); set => SetValue(DesiredWidthProperty, value); }
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(MyAdaptiveGridView), new PropertyMetadata(196d, OnSizeSettingChanged));
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    private ScrollViewer scrollViewer;
    private double savedOffset;
    public MyAdaptiveGridView()
    {
        DefaultStyleKey = typeof(GridView);
        SizeChanged += (_, _) => UpdateItemSize();
        Loaded += (_, _) =>
        {
            UpdateItemSize();
            DispatcherQueue.TryEnqueue(() => { if (IsLoaded && Items.Count > 0) scrollViewer?.ChangeView(null, savedOffset, null, true); });
        };
        Unloaded += (_, _) => savedOffset = scrollViewer?.VerticalOffset ?? 0;
    }
    protected override void OnApplyTemplate()
    {
        if (scrollViewer != null) scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
        base.OnApplyTemplate();
        scrollViewer = GetTemplateChild("ScrollViewer") as ScrollViewer;
        if (scrollViewer != null) scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
        UpdateItemSize();
    }
    private static void OnSizeSettingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((MyAdaptiveGridView)sender).UpdateItemSize();
    private static void OnLoadingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (MyAdaptiveGridView)sender;
        if (!view.DataLoading && view.CanLoadMore && view.scrollViewer?.ScrollableHeight == 0) view.LoadMoreCommand?.Execute(null);
    }
    private void UpdateItemSize()
    {
        if (ItemsPanelRoot is not ItemsWrapGrid panel || ActualWidth <= 0) return;
        var available = Math.Max(1, ActualWidth - Padding.Left - Padding.Right);
        var columns = Math.Max(1, Math.Floor(available / Math.Max(1, DesiredWidth)));
        // Round down to physical pixels so accumulated cell rounding cannot wrap
        // the final card onto the next row and leave an entire column empty.
        var scale = XamlRoot?.RasterizationScale ?? 1;
        panel.ItemWidth = Math.Max(1, Math.Floor(available / columns * scale) / scale);
        // Each cell includes 8 DIP on each side. Text stays at its native size.
        panel.ItemHeight = ItemTemplate == (DataTemplate)App.Current.Resources["RoomItemTemplate"]
            ? Math.Max(1, panel.ItemWidth - 16) * 9 / 16 + 80
            : ItemTemplate == (DataTemplate)App.Current.Resources["GameItemTemplate"]
                ? Math.Max(1, panel.ItemWidth - 34) * 3 / 4 + 82 : ItemHeight;
    }
    private void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs args)
    {
        if (!DataLoading && CanLoadMore && scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - LoadMoreBottomOffset)
            LoadMoreCommand?.Execute(null);
    }
}
