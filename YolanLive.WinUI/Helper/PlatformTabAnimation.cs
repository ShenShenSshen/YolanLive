using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Linq;
using Windows.UI.ViewManagement;

namespace YolanLive.WinUI.Helper;

public static class PlatformTabAnimation
{
    // Draw inside item content, retaining the native presenter as the template root.
    // Synchronize on selection/materialization only; no polling or extra animation clock.
    public static readonly DependencyProperty UnderlineProperty = DependencyProperty.RegisterAttached(
        "Underline", typeof(bool), typeof(PlatformTabAnimation), new PropertyMetadata(false, UnderlineChanged));
    public static bool GetUnderline(DependencyObject value) => (bool)value.GetValue(UnderlineProperty);
    public static void SetUnderline(DependencyObject value, bool enabled) => value.SetValue(UnderlineProperty, enabled);
    private static void UnderlineChanged(DependencyObject value, DependencyPropertyChangedEventArgs change)
    {
        if (value is not ListView picker) return;
        picker.Loaded -= PickerLoaded;
        picker.SelectionChanged -= PickerSelectionChanged;
        picker.ContainerContentChanging -= ContainerChanged;
        if (!(bool)change.NewValue) return;
        picker.Loaded += PickerLoaded;
        picker.SelectionChanged += PickerSelectionChanged;
        picker.ContainerContentChanging += ContainerChanged;
    }
    private static void PickerLoaded(object sender, RoutedEventArgs args) => RefreshUnderline((ListView)sender);
    private static void PickerSelectionChanged(object sender, SelectionChangedEventArgs args) => RefreshUnderline((ListView)sender);
    private static void ContainerChanged(ListViewBase sender, ContainerContentChangingEventArgs args)
        => sender.DispatcherQueue.TryEnqueue(() => { if (sender.IsLoaded) RefreshUnderline((ListView)sender); });
    private static void RefreshUnderline(ListView picker)
    {
        foreach (var item in picker.Items)
            if (picker.ContainerFromItem(item) is ListViewItem container &&
                container.ContentTemplateRoot is FrameworkElement root && root.FindName("PlatformUnderline") is Border mark)
                mark.Visibility = container.IsSelected ? Visibility.Visible : Visibility.Collapsed;
    }
    internal static void Play(ListView picker, SelectionChangedEventArgs change)
    {
        if (!picker.IsLoaded) return;
        foreach (var item in change.RemovedItems.Cast<object>().Concat(change.AddedItems.Cast<object>()))
        {
            if (picker.ContainerFromItem(item) is not FrameworkElement container) continue;
            var visual = ElementCompositionPreview.GetElementVisual(container);
            visual.StopAnimation("Opacity");
            visual.Opacity = 1;
            if (!new UISettings().AnimationsEnabled) continue;
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0.85f);
            fade.InsertKeyFrame(1, 1);
            fade.Duration = TimeSpan.FromMilliseconds(160);
            visual.StartAnimation("Opacity", fade);
        }
    }
}
