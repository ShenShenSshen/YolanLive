using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
namespace YolanLive.WinUI.Views;
public sealed partial class SearchPage : Page
{
    readonly SearchVM searchVM = new();
    public SearchPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Enabled;
        Unloaded += (_, _) => searchVM.CancelPending();
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e); MessageCenter.ChangeTitle("直播间搜索");
        if (e.NavigationMode == NavigationMode.New && e.Parameter is string keyword)
        { searchBox.Text = keyword; await searchVM.SearchAsync(keyword); }
    }
    private void MyAdaptiveGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LiveRoomItem room) MessageCenter.OpenLiveRoom(room.Source, room);
    }
    private async void searchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.QueryText)) { Utils.ShowMessageToast("关键字不能为空"); return; }
        await searchVM.SearchAsync(args.QueryText);
    }
}
