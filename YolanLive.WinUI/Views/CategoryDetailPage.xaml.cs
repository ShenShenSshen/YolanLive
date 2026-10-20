using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
namespace YolanLive.WinUI.Views;
public sealed partial class CategoryDetailPage : Page
{
    readonly CategoryDetailVM categoryDetailVM = new();
    public CategoryDetailPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Enabled;

        Unloaded += (_, _) => {  categoryDetailVM.CancelPending(); };
        _ = new PageStatusRefresh(this, () => categoryDetailVM.HasLoaded ? categoryDetailVM.HideStoppedAsync() : categoryDetailVM.LoadAsync(), categoryDetailVM.CancelPending);
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.NavigationMode == NavigationMode.New && e.Parameter is LiveGameCategory category)
            await categoryDetailVM.LoadCategoryAsync(category);
        else if (categoryDetailVM.Category != null) await categoryDetailVM.HideStoppedAsync();
        if (categoryDetailVM.Category != null) MessageCenter.ChangeTitle(categoryDetailVM.Category.Name);
    }
    private void MyAdaptiveGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LiveRoomItem room) MessageCenter.OpenLiveRoom(room.Source, room);
    }
}
