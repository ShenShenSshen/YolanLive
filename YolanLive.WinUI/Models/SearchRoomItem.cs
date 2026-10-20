using YolanLive.Core.Interface;
using YolanLive.Core.Models;

namespace YolanLive.WinUI.Models;

internal sealed class SearchRoomItem : LiveRoomItem
{
    internal SearchRoomItem(LiveRoomItem room, ILiveSite site)
    {
        Source = site;
        RoomID = room.RoomID;
        Title = room.Title;
        Cover = room.Cover;
        UserName = room.UserName;
        Online = room.Online;
        IsLive = room.IsLive;
    }
    internal string PlatformLabel => Source.Name.Replace("直播", "");
    internal string StatusLabel => IsLive.HasValue ? IsLive.Value ? "直播中" : "未开播" : null;
}
