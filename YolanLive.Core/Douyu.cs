using YolanLive.Core.Helper;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
namespace YolanLive.Core;
public sealed class Douyu : ILiveSite
{
    public string Name => "斗鱼直播";
    public string Cookie { get; set; } = "";
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36";
    public Func<string, string, Task<string>> RequestAsync { get; set; }
    public Func<string, string, CancellationToken, Task<string>> CancellableRequestAsync { get; set; }
    public Func<string, string, Task<string>> SignAsync { get; set; }
    public Func<string, string, CancellationToken, Task<string>> CancellableSignAsync { get; set; }
    private Task<string> Request(string url, string body = null, CancellationToken cancellationToken = default) => CancellableRequestAsync?.Invoke(url, body, cancellationToken) ?? RequestAsync?.Invoke(url, body) ?? PlatformHttp.Send(url, body, Cookie, UserAgent, "https://www.douyu.com/", cancellationToken);
    public Task<List<LiveCategory>> GetCategores() => GetCategores(CancellationToken.None);
    public async Task<List<LiveCategory>> GetCategores(CancellationToken cancellationToken)
    {
        var data = PlatformHttp.Json(await Request("https://m.douyu.com/api/cate/list", cancellationToken: cancellationToken))["data"];
        var children = (JArray)data["cate2Info"];
        var result = data["cate1Info"].Select(group => new LiveCategory { ID = (string)group["cate1Id"], Name = (string)group["cate1Name"], Children = children.Where(c => (string)c["cate1Id"] == (string)group["cate1Id"]).Select(c => new LiveSubCategory { ID = (string)c["cate2Id"], ParentID = (string)c["cate1Id"], Name = (string)c["cate2Name"], Pic = (string)c["icon"] }).ToList() }).ToList();
        result.First(g => g.ID == "1").Children.Insert(0, new LiveSubCategory { ID = "0", ParentID = "1", Name = "全部游戏" });
        return result;
    }
    public Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page = 1) => GetCategoryRooms(category, page, CancellationToken.None);
    public async Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, CancellationToken cancellationToken)
    {
        var data = PlatformHttp.Json(await Request(category.ID == "0" ? "https://www.douyu.com/japi/weblist/apinc/allpage/6/" + page : "https://www.douyu.com/gapi/rkc/directory/mixList/2_" + PlatformHttp.Room(category.ID) + "/" + page, cancellationToken: cancellationToken))["data"];
        return new LiveCategoryResult { HasMore = page < (int?)data["pgcnt"], Rooms = data["rl"].Where(r => (int?)r["type"] == 1 && (category.ID != "0" || new[] { "1", "15", "9", "22" }.Contains((string)r["cid1"]))).Select(r => new LiveRoomItem { RoomID = (string)r["rid"], Title = (string)r["rn"], UserName = (string)r["nn"], Cover = (string)r["rs16"], Online = PlatformHttp.Hot(r["ol"]), IsLive = true }).ToList() };
    }
    public Task<LiveCategoryResult> GetRecommendRooms(int page = 1) => GetRecommendRooms(page, CancellationToken.None);
    public Task<LiveCategoryResult> GetRecommendRooms(int page, CancellationToken cancellationToken) => GetCategoryRooms(new LiveSubCategory { ID = "0", ParentID = "1" }, page, cancellationToken: cancellationToken);
    public Task<LiveSearchResult> Search(string keyword, int page = 1) => Search(keyword, page, CancellationToken.None);
    public async Task<LiveSearchResult> Search(string keyword, int page, CancellationToken cancellationToken)
    {
        var data = PlatformHttp.Json(await Request("https://www.douyu.com/japi/search/api/searchShow?kw=" + Uri.EscapeDataString(keyword) + "&page=" + page + "&pageSize=20", cancellationToken: cancellationToken))["data"];
        var rooms = (data["relateShow"] as JArray ?? new JArray()).Select(r => new LiveRoomItem { RoomID = (string)r["rid"], Title = (string)r["roomName"], UserName = (string)r["nickName"], Cover = (string)r["roomSrc"], Online = PlatformHttp.Hot(r["hot"]), IsLive = SearchStatus(r) }).ToList();
        return new LiveSearchResult { Rooms = rooms, HasMore = rooms.Count >= 20 };
    }
    internal static bool? SearchStatus(JToken room) => room["isLive"] != null ? (int?)room["isLive"] == 1 : room["show_status"] != null ? (int?)room["show_status"] == 1 : (bool?)null;
    private async Task<JToken> RoomInfo(object id, CancellationToken cancellationToken = default)
    {
        var room = PlatformHttp.Json(await Request("https://www.douyu.com/betard/" + PlatformHttp.Room(id), cancellationToken: cancellationToken))["room"];
        if (room?["show_status"] == null) throw new LiveRequestException(LiveFailureKind.Platform, "斗鱼未返回开播状态");
        return room;
    }
    public Task<LiveRoomDetail> GetRoomDetail(object roomId) => GetRoomDetail(roomId, CancellationToken.None);
    public async Task<LiveRoomDetail> GetRoomDetail(object roomId, CancellationToken cancellationToken)
    {
        var r = await RoomInfo(roomId, cancellationToken: cancellationToken);
        return new LiveRoomDetail { RoomID = (string)r["room_id"], Title = (string)r["room_name"], UserName = (string)r["owner_name"], UserAvatar = (string)r["owner_avatar"], Cover = (string)r["room_pic"], Online = PlatformHttp.Hot(r["room_biz_all"]?["hot"]), Introduction = (string)r["show_details"], Status = (int?)r["show_status"] == 1 && (int?)r["videoLoop"] != 1, IsRecord = (int?)r["videoLoop"] == 1, Url = "https://www.douyu.com/" + (string)r["room_id"] };
    }
    public Task<bool> GetLiveStatus(object roomId) => GetLiveStatus(roomId, CancellationToken.None);
    public async Task<bool> GetLiveStatus(object roomId, CancellationToken cancellationToken) { var r = await RoomInfo(roomId, cancellationToken: cancellationToken); return (int?)r["show_status"] == 1 && (int?)r["videoLoop"] != 1; }
    private async Task<JToken> Play(LiveRoomDetail room, int rate, string cdn, CancellationToken cancellationToken = default)
    {
        if (SignAsync == null && CancellableSignAsync == null) throw new LiveRequestException(LiveFailureKind.Platform, "斗鱼签名浏览器尚未就绪");
        var data = PlatformHttp.Json(await Request("https://www.douyu.com/swf_api/homeH5Enc?rids=" + PlatformHttp.Room(room.RoomID), cancellationToken: cancellationToken))["data"];
        var script = (string)data["room" + room.RoomID];
        if (string.IsNullOrWhiteSpace(script) || script.Length > 1000000) throw new LiveRequestException(LiveFailureKind.Platform, "斗鱼签名脚本不可用");
        var args = await (CancellableSignAsync?.Invoke(room.RoomID, script, cancellationToken) ?? SignAsync(room.RoomID, script));
        return PlatformHttp.Json(await Request("https://www.douyu.com/lapi/live/getH5Play/" + room.RoomID, args + "&cdn=" + Uri.EscapeDataString(cdn) + "&rate=" + rate, cancellationToken: cancellationToken))["data"];
    }
    public Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail room) => GetPlayQuality(room, CancellationToken.None);
    public async Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail room, CancellationToken cancellationToken)
    {
        var data = await Play(room, 0, "", cancellationToken: cancellationToken);
        room.Data = data;
        return (data["multirates"] as JArray ?? new JArray()).Select(q => new LivePlayQuality { Quality = (string)q["name"], Data = (int)q["rate"], Sort = (int)q["rate"] }).ToList();
    }
    public Task<List<string>> GetPlayUrls(LiveRoomDetail room, LivePlayQuality quality) => GetPlayUrls(room, quality, CancellationToken.None);
    public async Task<List<string>> GetPlayUrls(LiveRoomDetail room, LivePlayQuality quality, CancellationToken cancellationToken)
    {
        var metadata = room.Data as JToken ?? await Play(room, 0, "", cancellationToken: cancellationToken);
        var cdns = (metadata["cdnsWithName"] as JArray ?? new JArray()).Select(c => (string)c["cdn"]).Distinct().OrderBy(c => c.StartsWith("scdn")).Take(4).ToList();
        if (cdns.Count == 0) cdns.Add("");
        var urls = new List<string>(); Exception error = null;
        foreach (var cdn in cdns) try
        {
            var data = await Play(room, Convert.ToInt32(quality.Data), cdn, cancellationToken: cancellationToken);
            var url = (string)data["rtmp_url"] + "/" + (string)data["rtmp_live"];
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")) urls.Add(url);
        }
        catch (LiveRequestException ex) when (ex.Kind == LiveFailureKind.Authentication) { throw; }
        catch (Exception ex) when (!(ex is OperationCanceledException)) { error = ex; }
        if (urls.Count == 0) throw error ?? new LiveRequestException(LiveFailureKind.Platform, "斗鱼暂未提供播放地址");
        return urls.Distinct().ToList();
    }
}
