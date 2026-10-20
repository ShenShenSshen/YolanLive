using YolanLive.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
namespace YolanLive.WinUI.ViewModels;
public class SearchVM : LiveRoomListVM
{
    private string keyword;
    protected override bool LiveOnly => false;
    public async Task SearchAsync(string value)
    {
        Reset();
        keyword = value.Trim();
        await LoadAsync();
    }
    protected override Task<List<LiveGameSource>> GetSourcesAsync(CancellationToken cancellationToken) => Task.FromResult(MainVM.Sites.Select(s => new LiveGameSource { Site = s.LiveSite }).ToList());
    protected override async Task<LiveCategoryResult> FetchAsync(LiveGameSource source, int page, CancellationToken cancellationToken)
    {
        var result = await source.Site.Search(keyword, page, cancellationToken);
        return new LiveCategoryResult { HasMore = result.HasMore, Rooms = result.Rooms.Select(room => (LiveRoomItem)new Models.SearchRoomItem(room, source.Site)).ToList() };
    }
}
