using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace YolanLive.WinUI.Helper;

internal sealed class ListViewportAnchor(ListViewBase view)
{
    private object anchor, focusedItem;
    private DependencyObject focusedElement;
    private double anchorY, offset;
    private bool keyboardFocus;
    private int generation;
    internal void Capture()
    {
        generation++;
        if (!view.IsLoaded) return;
        var scroll = Children(view).OfType<ScrollViewer>().FirstOrDefault();
        offset = scroll?.VerticalOffset ?? 0;
        var index = view.ItemsPanelRoot switch { ItemsWrapGrid wrap => wrap.FirstVisibleIndex, ItemsStackPanel stack => stack.FirstVisibleIndex, _ => 0 };
        anchor = view.Items.Count > 0 ? view.Items[Math.Clamp(index, 0, view.Items.Count - 1)] : null;
        var container = anchor != null ? view.ContainerFromItem(anchor) as FrameworkElement : null;
        if (container == null && offset == 0) anchor = null;
        anchorY = container?.TransformToVisual(view).TransformPoint(new Windows.Foundation.Point()).Y ?? 0;
        focusedElement = FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;
        keyboardFocus = false; focusedItem = null;
        for (var current = focusedElement; current != null && current != view; current = VisualTreeHelper.GetParent(current))
            if (current is SelectorItem item) { focusedItem = item.Content; keyboardFocus = item.FocusState == FocusState.Keyboard; break; }
    }
    internal void Restore()
    {
        var current = generation;
        view.DispatcherQueue.TryEnqueue(() =>
        {
            if (!view.IsLoaded || current != generation) return;
            var retained = anchor != null && view.Items.Contains(anchor);
            var item = retained ? anchor : view.Items.FirstOrDefault();
            if (item == null) return;
            view.UpdateLayout();
            if (view.ContainerFromItem(item) == null) view.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
            view.DispatcherQueue.TryEnqueue(() =>
            {
                if (!view.IsLoaded || current != generation) return;
                view.UpdateLayout();
                var scroll = Children(view).OfType<ScrollViewer>().FirstOrDefault();
                var card = view.ContainerFromItem(item) as FrameworkElement;
                var y = card?.TransformToVisual(view).TransformPoint(new Windows.Foundation.Point()).Y;
                if (scroll != null) scroll.ChangeView(null, retained ? Math.Max(0, y.HasValue ? scroll.VerticalOffset + y.Value - anchorY : offset) : 0, null, true);
                var focus = FocusManager.GetFocusedElement(view.XamlRoot);
                if (keyboardFocus && (focus == null || ReferenceEquals(focus, focusedElement) || ReferenceEquals(focus, view)) && focusedItem != null)
                    (view.ContainerFromItem(focusedItem) as Control)?.Focus(FocusState.Keyboard);
            });
        });
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
}
