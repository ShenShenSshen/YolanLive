using YolanLive.Core.Helper;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.Core.Protocol;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using Tup;
namespace YolanLive.Core;
public sealed class Huya : ILiveSite
{
    public string Name => "虎牙直播";
    public string Cookie { get; set; } = "";
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0.0.0 Safari/537.36";
    public Func<string, string, Task<string>> RequestAsync { get; set; }
    public Func<string, string, CancellationToken, Task<string>> CancellableRequestAsync { get; set; }
    private Task<string> Request(string url, CancellationToken cancellationToken = default) => CancellableRequestAsync?.Invoke(url, null, cancellationToken) ?? RequestAsync?.Invoke(url, null) ?? PlatformHttp.Send(url, null, Cookie, url.Contains("m.huya.com/") ? "Mozilla/5.0 (Linux; Android 11) AppleWebKit/537.36 Chrome/131.0.0.0 Mobile Safari/537.36" : UserAgent, "https://www.huya.com/", cancellationToken);
    private static JObject Json(string text) { var j = JObject.Parse(text); if ((int?)j["status"] != 200) throw new LiveRequestException(LiveFailureKind.Platform, "虎牙接口暂不可用"); return j; }
    public Task<List<LiveCategory>> GetCategores() => GetCategores(CancellationToken.None);
    public async Task<List<LiveCategory>> GetCategores(CancellationToken cancellationToken)
    {
        var groups = new[] { new LiveCategory { ID = "1", Name = "网游" }, new LiveCategory { ID = "2", Name = "单机" }, new LiveCategory { ID = "3", Name = "手游" } };
        foreach (var g in groups) g.Children = Json(await Request("https://live.cdn.huya.com/liveconfig/game/bussLive?bussType=" + g.ID, cancellationToken: cancellationToken))["data"].Select(c => new LiveSubCategory { ID = (string)c["gid"], ParentID = g.ID, Name = (string)c["gameFullName"], Pic = "https://huyaimg.msstatic.com/cdnimage/game/" + c["gid"] + "-MS.jpg" }).ToList();
        groups[0].Children.Insert(0, new LiveSubCategory { ID = "0", ParentID = "1", Name = "全部游戏" });
        return groups.ToList();
    }
    public Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page = 1) => GetCategoryRooms(category, page, CancellationToken.None);
    public async Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, CancellationToken cancellationToken)
    {
        var data = Json(await Request("https://www.huya.com/cache.php?m=LiveList&do=getLiveListByPage&tagAll=0&gameId=" + PlatformHttp.Room(category.ID) + "&page=" + page, cancellationToken: cancellationToken))["data"];
        return new LiveCategoryResult { HasMore = (int?)data["page"] < (int?)data["totalPage"], Rooms = data["datas"].Where(r => category.ID != "0" || new[] { "1", "2", "3" }.Contains((string)r["bussType"])).Select(r => new LiveRoomItem { RoomID = (string)r["profileRoom"], Title = (string)r["introduction"] ?? (string)r["roomName"], Cover = (string)r["screenshot"], UserName = (string)r["nick"], Online = PlatformHttp.Hot(r["totalCount"]), IsLive = true }).ToList() };
    }
    public Task<LiveCategoryResult> GetRecommendRooms(int page = 1) => GetRecommendRooms(page, CancellationToken.None);
    public Task<LiveCategoryResult> GetRecommendRooms(int page, CancellationToken cancellationToken) => GetCategoryRooms(new LiveSubCategory { ID = "0", ParentID = "1" }, page, cancellationToken: cancellationToken);
    public Task<LiveSearchResult> Search(string keyword, int page = 1) => Search(keyword, page, CancellationToken.None);
    public async Task<LiveSearchResult> Search(string keyword, int page, CancellationToken cancellationToken)
    {
        var j = JObject.Parse(await Request("https://search.cdn.huya.com/?m=Search&do=getSearchContent&q=" + Uri.EscapeDataString(keyword) + "&uid=0&v=4&typ=-5&livestate=0&rows=20&start=" + (page - 1) * 20, cancellationToken: cancellationToken));
        var data = j["response"]?["3"] ?? throw new LiveRequestException(LiveFailureKind.Platform, "虎牙搜索暂不可用");
        return new LiveSearchResult { HasMore = (int?)data["numFound"] > page * 20, Rooms = (data["docs"] as JArray ?? new JArray()).Select(r => new LiveRoomItem { RoomID = (string)r["room_id"], Title = (string)r["game_roomName"], Cover = (string)r["game_screenshot"], UserName = (string)r["game_nick"], Online = PlatformHttp.Hot(r["game_total_count"]), IsLive = r["live_state"] != null ? (int?)r["live_state"] == 1 : (bool?)null }).ToList() };
    }
    internal static JObject ParseRoom(string html)
    {
        var match = Regex.Match(html, @"window\.HNF_GLOBAL_INIT\s*=\s*([\s\S]*?)</script>");
        if (!match.Success) throw new LiveRequestException(LiveFailureKind.Platform, "虎牙房间信息不可用");
        return JObject.Parse(match.Groups[1].Value.Trim().TrimEnd(';'));
    }
    private async Task<JObject> RoomInfo(object id, CancellationToken cancellationToken = default)
    {
        var room = ParseRoom(await Request("https://m.huya.com/" + PlatformHttp.Room(id), cancellationToken: cancellationToken));
        if (room["roomInfo"]?["eLiveStatus"] == null) throw new LiveRequestException(LiveFailureKind.Platform, "虎牙未返回开播状态");
        return room;
    }
    public Task<LiveRoomDetail> GetRoomDetail(object roomId) => GetRoomDetail(roomId, CancellationToken.None);
    public async Task<LiveRoomDetail> GetRoomDetail(object roomId, CancellationToken cancellationToken)
    {
        var data = await RoomInfo(roomId, cancellationToken: cancellationToken); var live = data["roomInfo"]?["tLiveInfo"];
        if (live == null) throw new LiveRequestException(LiveFailureKind.Platform, "虎牙房间不存在");
        return new LiveRoomDetail { RoomID = (string)live["lProfileRoom"] ?? roomId.ToString(), Title = (string)live["sIntroduction"] ?? (string)live["sRoomName"], UserName = (string)live["sNick"], UserAvatar = (string)live["sAvatar180"], Cover = (string)live["sScreenshot"], Online = PlatformHttp.Hot(live["lTotalCount"]), Introduction = "", Status = (int?)data["roomInfo"]["eLiveStatus"] == 2, Url = "https://www.huya.com/" + roomId, Data = live["tLiveStreamInfo"] };
    }
    public Task<bool> GetLiveStatus(object roomId) => GetLiveStatus(roomId, CancellationToken.None);
    public async Task<bool> GetLiveStatus(object roomId, CancellationToken cancellationToken) => (int?)(await RoomInfo(roomId, cancellationToken: cancellationToken))["roomInfo"]?["eLiveStatus"] == 2;
    public Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail room) => GetPlayQuality(room, CancellationToken.None);
    public Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail room, CancellationToken cancellationToken)
    {
        var data = (JToken)room.Data;
        var list = data["vBitRateInfo"]?["value"] as JArray ?? new JArray();
        var result = list.Select(q => new LivePlayQuality { Quality = (string)q["sDisplayName"], Data = (int)q["iBitRate"], Sort = (int)q["iBitRate"] }).ToList();
        if (result.Count == 0) result.Add(new LivePlayQuality { Quality = "原画", Data = 0 });
        return Task.FromResult(result);
    }
    public Task<List<string>> GetPlayUrls(LiveRoomDetail room, LivePlayQuality quality) => GetPlayUrls(room, quality, CancellationToken.None);
    public async Task<List<string>> GetPlayUrls(LiveRoomDetail room, LivePlayQuality quality, CancellationToken cancellationToken)
    {
        var lines = ((JToken)room.Data)["vStreamInfo"]?["value"] as JArray ?? new JArray();
        var urls = new List<string>(); Exception error = null;
        foreach (var line in lines.OrderBy(l => ((string)l["sFlvUrl"] ?? "").Contains("al.flv")).Take(4)) try
        {
            if (string.IsNullOrEmpty((string)line["sFlvUrl"])) continue;
            var req = new HYGetCdnTokenReq { stream_name = (string)line["sStreamName"], cdn_type = (string)line["sCdnType"], presenter_uid = (long?)line["lPresenterUid"] ?? 0 };
            var packet = new TarsUniPacket { ServantName = "liveui", FuncName = "getCdnTokenInfo" }; packet.setTarsVersion(Const.PACKET_TYPE_TUP3); packet.setTarsPacketType(Const.PACKET_TYPE_TARSNORMAL); packet.Put("tReq", req);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://wup.huya.com/");
            if (!string.IsNullOrEmpty(Cookie)) request.Headers.TryAddWithoutValidation("Cookie", Cookie);
            request.Content = new ByteArrayContent(packet.Encode()); request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/x-wup");
            using var response = await HttpTransport.SendAsync(request, cancellationToken, redirect: false); response.EnsureSuccessStatusCode();
            var result = new TarsUniPacket(); result.Decode(await response.Content.ReadAsByteArrayAsync());
            if (result.Get("", 0) != 0) throw new LiveRequestException(LiveFailureKind.Platform, "虎牙播放令牌请求失败");
            var token = result.Get("tRsp", new HYGetCdnTokenResp());
            if (string.IsNullOrEmpty(token.stream_name) || string.IsNullOrEmpty(token.flv_anti_code)) throw new LiveRequestException(LiveFailureKind.Platform, "虎牙未返回播放令牌");
            urls.Add(((string)line["sFlvUrl"]).Replace("http:", "https:") + "/" + token.stream_name + ".flv?" + token.flv_anti_code + "&codec=264&ratio=" + Convert.ToInt32(quality.Data));
        }
        catch (Exception ex) when (!(ex is OperationCanceledException)) { error = ex; }
        if (urls.Count == 0) throw error ?? new LiveRequestException(LiveFailureKind.Platform, "虎牙暂未提供播放地址");
        return urls.Distinct().ToList();
    }
}
