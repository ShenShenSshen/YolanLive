using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Controls;
public sealed partial class FavoriteManagementDialog : ContentDialog
{
    private readonly ObservableCollection<FavoriteItem> items = new();
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock status = new() { Text = "显示所有已保存的关注，包括停播主播。", TextWrapping = TextWrapping.Wrap };
    public FavoriteManagementDialog()
    {
        Title = "管理全部关注";
        PrimaryButtonText = "取消所选关注";
        CloseButtonText = "关闭";
        IsPrimaryButtonEnabled = false;
        list.ItemsSource = items;
        list.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Padding='8' Spacing='4'><TextBlock Text='{Binding UserName}' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding SiteName}' Foreground='{ThemeResource TextFillColorSecondaryBrush}' FontSize='12'/></StackPanel></DataTemplate>");
        list.SelectionChanged += (_, _) => IsPrimaryButtonEnabled = list.SelectedItem != null;
        var panel = new Grid { MinWidth = 240, MaxHeight = 460, RowDefinitions = { new() { Height = GridLength.Auto }, new() } };
        panel.Children.Add(status);
        Grid.SetRow(list, 1);
        panel.Children.Add(list);
        Content = panel;
        Opened += async (_, _) => await LoadAsync();
        PrimaryButtonClick += (_, args) => { args.Cancel = true; RemoveSelected(); };
    }
    internal async Task LoadAsync()
    {
        try
        {
            items.Clear();
            foreach (var item in await DatabaseHelper.GetFavorites()) items.Add(item);
            status.Text = items.Count == 0 ? "还没有关注主播" : "显示所有已保存的关注，包括停播主播。";
        }
        catch (System.Exception) { status.Text = "关注读取失败，请关闭后重试"; }
    }
    internal void RemoveSelected()
    {
        if (list.SelectedItem is not FavoriteItem item) return;
        try
        {
            DatabaseHelper.DeleteFavorite(item.ID);
            items.Remove(item);
            status.Text = items.Count == 0 ? "还没有关注主播" : "已取消关注：" + item.UserName;
            MessageCenter.UpdateFavorite();
        }
        catch (System.Exception) { status.Text = "取消关注失败，请重试"; }
    }
}
