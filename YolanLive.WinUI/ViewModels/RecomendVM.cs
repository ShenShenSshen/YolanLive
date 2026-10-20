using YolanLive.Core.Helper;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
namespace YolanLive.WinUI.ViewModels;
public class RecomendVM : LiveRoomListVM
{
    protected override string PlatformName => MainVM.BrowseSite.Name;
    protected override async Task<List<LiveGameSource>> GetSourcesAsync(CancellationToken cancellationToken) => GameBrowse.OverviewSources(await GameCatalog.LoadAsync(MainVM.BrowseSite.LiveSite, cancellationToken));
    public override void Refresh() { GameCatalog.Invalidate(MainVM.BrowseSite.LiveSite); base.Refresh(); }
}
