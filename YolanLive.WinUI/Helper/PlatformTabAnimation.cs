using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Linq;
using Windows.UI.ViewManagement;

namespace YolanLive.WinUI.Helper;

internal static class PlatformTabAnimation
{
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