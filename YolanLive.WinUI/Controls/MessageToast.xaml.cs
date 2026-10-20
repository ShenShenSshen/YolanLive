using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Threading.Tasks;
namespace YolanLive.WinUI.Controls;
public sealed partial class MessageToast : UserControl
{
    private readonly string message;
    private readonly TimeSpan duration;
    public MessageToast(string content, TimeSpan showTime)
    {
        InitializeComponent();
        message = content;
        duration = showTime;
    }
    public async void Show()
    {
        var window = App.ActiveWindow;
        if (window?.Root.XamlRoot == null) return;
        RequestedTheme = window.Root.ActualTheme;
        tbNotify.Text = message;
        var popup = new Popup { XamlRoot = window.Root.XamlRoot, Child = this };
        Width = window.Root.ActualWidth;
        Height = window.Root.ActualHeight;
        popup.IsOpen = true;
        await Task.Delay(duration);
        popup.IsOpen = false;
        popup.Child = null;
    }
}
