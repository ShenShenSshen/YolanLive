using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using System.Runtime.CompilerServices;
using YolanLive.Core.Models;
using YolanLive.WinUI.ViewModels;

namespace YolanLive.WinUI.Helper;

// The template owns its action; virtualized cards attach only while loaded.
public static class GameCard
{
    public static readonly DependencyProperty PinningProperty = DependencyProperty.RegisterAttached(
        "Pinning", typeof(bool), typeof(GameCard), new PropertyMetadata(false, Changed));
    public static bool GetPinning(DependencyObject value) => (bool)value.GetValue(PinningProperty);
    public static void SetPinning(DependencyObject value, bool enabled) => value.SetValue(PinningProperty, enabled);
    private static readonly ConditionalWeakTable<FrameworkElement, ActionState> actions = new();
    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is not FrameworkElement root) return;
        root.Loaded -= Loaded; root.Unloaded -= Unloaded;
        Unloaded(root, null);
        if ((bool)args.NewValue) { root.Loaded += Loaded; root.Unloaded += Unloaded; }
    }
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        var root = (FrameworkElement)sender;
        if (actions.TryGetValue(root, out _)) return;
        if (root.FindName("PinGame") is Button button) actions.Add(root, new(root, button));
    }
    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        var root = (FrameworkElement)sender;
        if (actions.TryGetValue(root, out var state)) { state.Detach(); actions.Remove(root); }
    }
    internal static void Refresh(FrameworkElement root)
    { if (actions.TryGetValue(root, out var state)) state.Update(); }

    private sealed class ActionState
    {
        private readonly FrameworkElement root;
        private readonly Button button;
        private readonly FrameworkElement host;
        private bool hovering;
        internal ActionState(FrameworkElement root, Button button)
        {
            this.root = root; this.button = button;
            host = root;
            for (var parent = VisualTreeHelper.GetParent(root); parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is GridViewItem item) { host = item; break; }
            host.PointerEntered += Entered; host.PointerExited += Exited; host.PointerCanceled += Exited;
            host.GotFocus += FocusChanged; host.LostFocus += FocusChanged; root.DataContextChanged += DataChanged;
            button.Click += Click;
            Update();
        }
        private void Entered(object sender, PointerRoutedEventArgs args) { hovering = true; Update(); }
        private void Exited(object sender, PointerRoutedEventArgs args) { hovering = false; Update(); }
        private void FocusChanged(object sender, RoutedEventArgs args)
        { root.DispatcherQueue.TryEnqueue(() => { if (root.IsLoaded) Update(); }); }
        private void DataChanged(FrameworkElement sender, DataContextChangedEventArgs args) => Update();
        private Site Site(LiveGameCategory game) => MainVM.Sites.FirstOrDefault(s => game?.Sources?.FirstOrDefault()?.Site == s.LiveSite);
        internal void Update()
        {
            var game = root.DataContext as LiveGameCategory;
            var site = Site(game);
            var pinned = site != null && BrowsePreferences.IsPinned(site, game);
            var focus = root.XamlRoot == null ? null : FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            while (focus != null && focus != host) focus = VisualTreeHelper.GetParent(focus);
            // Keep the button in the tab sequence even when hidden from the pointer.
            button.Opacity = site != null && (pinned || hovering || focus == host) ? 1 : 0;
            button.IsHitTestVisible = button.Opacity > 0;
            button.IsEnabled = site != null;
            var label = pinned ? "取消固定" : "固定到首页";
            ToolTipService.SetToolTip(button, label);
            AutomationProperties.SetName(button, label + "：" + game?.Name);
            if (button.Content is SymbolIcon icon) icon.Symbol = pinned ? Symbol.UnPin : Symbol.Pin;
        }
        private void Click(object sender, RoutedEventArgs args)
        {
            if (root.DataContext is not LiveGameCategory game || Site(game) is not Site site) return;
            if (!BrowsePreferences.TogglePin(site, game)) Utils.ShowMessageToast("每个平台最多固定 6 个游戏，请先移除已有入口");
            Update();
        }
        internal void Detach()
        {
            host.PointerEntered -= Entered; host.PointerExited -= Exited; host.PointerCanceled -= Exited;
            host.GotFocus -= FocusChanged; host.LostFocus -= FocusChanged; root.DataContextChanged -= DataChanged;
            button.Click -= Click;
        }
    }
}
