using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YolanLive.WinUI.Controls;

// Keep native keyboard, popup and accessibility behavior; only tighten the arrow slot.
public sealed partial class PlayerOptionComboBox : ComboBox
{
    public PlayerOptionComboBox() => DefaultStyleKey = typeof(ComboBox);
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("LayoutRoot") is Grid layout && layout.ColumnDefinitions.Count == 2)
            layout.ColumnDefinitions[1].Width = new GridLength(24);
        if (GetTemplateChild("DropDownGlyph") is FrameworkElement glyph)
        {
            glyph.MinHeight = 0;
            glyph.Width = glyph.Height = 10;
            glyph.Margin = new Thickness(0, 0, 6, 0);
        }
    }
}
