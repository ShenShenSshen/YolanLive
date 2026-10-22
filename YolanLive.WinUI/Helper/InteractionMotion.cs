using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.UI.ViewManagement;

namespace YolanLive.WinUI.Helper;

// One-shot composition feedback; no timers or media preview instances.
public static class InteractionMotion
{
    public static readonly DependencyProperty RoomCardProperty = DependencyProperty.RegisterAttached(
        "RoomCard", typeof(bool), typeof(InteractionMotion), new PropertyMetadata(false, CardChanged));
    public static bool GetRoomCard(DependencyObject value) => (bool)value.GetValue(RoomCardProperty);
    public static void SetRoomCard(DependencyObject value, bool enabled) => value.SetValue(RoomCardProperty, enabled);
    private static void CardChanged(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not FrameworkElement root) return;
        root.Loaded -= CardLoaded; root.Unloaded -= CardUnloaded;
        if ((bool)args.NewValue) { root.Loaded += CardLoaded; root.Unloaded += CardUnloaded; }
        else CardUnloaded(root, null);
    }
    private static void CardLoaded(object sender, RoutedEventArgs args)
    {
        var root = (FrameworkElement)sender;
        root.PointerEntered -= Entered; root.PointerEntered += Entered;
        root.PointerExited -= Exited; root.PointerExited += Exited;
        root.PointerCanceled -= Exited; root.PointerCanceled += Exited;
        if (root.FindName("RoomHover") is FrameworkElement overlay)
        {
            overlay.Opacity = 1;
            Reset(overlay, 0);
        }
    }
    private static void CardUnloaded(object sender, RoutedEventArgs args)
    {
        var root = (FrameworkElement)sender;
        root.PointerEntered -= Entered; root.PointerExited -= Exited; root.PointerCanceled -= Exited;
        if (root.FindName("RoomHover") is FrameworkElement overlay) Reset(overlay, 0);
    }
    private static void Entered(object sender, PointerRoutedEventArgs args) => Hover((FrameworkElement)sender, 1);
    private static void Exited(object sender, PointerRoutedEventArgs args) => Hover((FrameworkElement)sender, 0);
    private static void Hover(FrameworkElement root, float target)
    {
        if (root.FindName("RoomHover") is FrameworkElement overlay)
        {
            var visual = ElementCompositionPreview.GetElementVisual(overlay);
            Animate(overlay, visual.Opacity, target, 120);
        }
    }
    internal static void Reveal(FrameworkElement element, int milliseconds) => Animate(element, .65f, 1, milliseconds);
    internal static void Reset(FrameworkElement element, float opacity = 1)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity"); visual.Opacity = opacity;
    }
    private static void Animate(FrameworkElement element, float from, float to, int milliseconds)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity"); visual.Opacity = to;
        if (!new UISettings().AnimationsEnabled) return;
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, from); fade.InsertKeyFrame(1, to);
        fade.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation("Opacity", fade);
    }
}
