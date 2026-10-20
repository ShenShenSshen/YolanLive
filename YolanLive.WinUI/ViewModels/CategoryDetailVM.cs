using YolanLive.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
namespace YolanLive.WinUI.ViewModels;
public class CategoryDetailVM : LiveRoomListVM
{
    public LiveGameCategory Category { get; private set; }
    public async Task LoadCategoryAsync(LiveGameCategory category)
    {
        Reset();
        Category = category;
        DoPropertyChanged(nameof(Category));
        await LoadAsync();
    }
    protected override Task<List<LiveGameSource>> GetSourcesAsync(CancellationToken cancellationToken) => Task.FromResult(Category.Sources);
}
