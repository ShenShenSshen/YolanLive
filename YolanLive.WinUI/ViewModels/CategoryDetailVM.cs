using YolanLive.Core.Models;
using YolanLive.Core.Interface;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
namespace YolanLive.WinUI.ViewModels;
public class CategoryDetailVM : LiveRoomListVM
{
    private ILiveSite platform;
    public LiveGameCategory Category { get; private set; }
    public async Task LoadCategoryAsync(LiveGameCategory category)
    {
        Reset();
        Category = category;
        platform = null;
        DoPropertyChanged(nameof(Category));
        await LoadAsync();
    }
    internal async Task SelectPlatformAsync(ILiveSite site)
    {
        if (platform == site) return;
        Reset();
        platform = site;
        await LoadAsync();
    }

    protected override string NoRoomsMessage => platform != null && !Category.Sources.Any(source => source.Site == platform)
        ? "该平台目录中未找到这个游戏的对应分区" : base.NoRoomsMessage;

    protected override LiveRoomItem DisplayRoom(LiveRoomItem room) => new Models.SearchRoomItem(room, room.Source, showStatus: false);

    internal List<LiveGameSource> GetActiveSources() =>
        Category?.Sources?.Where(source => platform == null || source.Site == platform).ToList() ?? new();

    protected override Task<List<LiveGameSource>> GetSourcesAsync(CancellationToken cancellationToken) => Task.FromResult(GetActiveSources());
}
