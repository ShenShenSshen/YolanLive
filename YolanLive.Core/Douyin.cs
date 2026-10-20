using YolanLive.Core.Helper;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Web;
using System.Xml.Linq;

namespace YolanLive.Core
{
    public class Douyin : ILiveSite
    {
        public string Name => "抖音直播";

        private const string USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0";
        private const string REFERER = "https://live.douyin.com";
        private const string AUTHORITY = "live.douyin.com";

        Dictionary<string, string> headers = new Dictionary<string, string>
        {
            { "User-Agent", USER_AGENT },
            { "Referer", REFERER },
            { "Authority", AUTHORITY }
        };

        private string cookie = "";
        private string anonymousCookie = "";
        public string Cookie { get => cookie; set { cookie = value ?? ""; anonymousCookie = ""; ClearLiveStatusCache(); } }
        private readonly object liveStatusGate = new object();
        private readonly Dictionary<string, (DateTime expires, bool live)> liveStatusCache = new Dictionary<string, (DateTime, bool)>();
        public void ClearLiveStatusCache() { lock (liveStatusGate) liveStatusCache.Clear(); }
        public string UserAgent { get; set; } = USER_AGENT;
        private async Task<Dictionary<string, string>> GetRequestHeaders(CancellationToken cancellationToken = default)
        {
            var requestHeaders = new Dictionary<string, string>(headers) { ["User-Agent"] = UserAgent };
            if (!string.IsNullOrEmpty(Cookie)) requestHeaders["Cookie"] = Cookie;
            else
            {
                if (string.IsNullOrEmpty(anonymousCookie))
                {
                    try
                    {
                        using var resp = await HttpUtil.Head("https://live.douyin.com", requestHeaders, cancellationToken: cancellationToken);
                        if (resp.Headers.TryGetValues("Set-Cookie", out var values))
                            anonymousCookie = values.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith("ttwid=")) ?? "";
                    }
                    catch (Exception error) when (!(error is OperationCanceledException)) { }
                }
                if (!string.IsNullOrEmpty(anonymousCookie)) requestHeaders["Cookie"] = anonymousCookie;
            }
            return requestHeaders;
        }

        public Task<List<LiveCategory>> GetCategores() => GetCategores(CancellationToken.None);
        public async Task<List<LiveCategory>> GetCategores(CancellationToken cancellationToken)
        {
            List<LiveCategory> categories = new List<LiveCategory>();
            var resp = await HttpUtil.GetString("https://live.douyin.com/", await GetRequestHeaders(cancellationToken: cancellationToken), cancellationToken: cancellationToken);

            Regex regex = new Regex("\\{\\\\\"pathname\\\\\":\\\\\"\\/\\\\\",\\\\\"categoryData.*?\\]\\\\n", RegexOptions.Singleline);
            Match match = regex.Match(resp);
            string renderData = match.Success ? match.Groups[0].Value : "";
            if (string.IsNullOrEmpty(renderData))
            {
                throw new Exception("无法读取分类数据");
            }
            renderData = renderData.Trim().Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("]\\n", "");
            // 解析JSON数据
            var renderDataJson = ParseEmbeddedCategoryData(renderData);
            foreach (var item in renderDataJson["categoryData"])
            {
                List<LiveSubCategory> subs = new List<LiveSubCategory>();
                var id = $"{item["partition"]["id_str"]},{item["partition"]["type"]}";
                foreach (var subItem in item["sub_partition"])
                {
                    var subCategory = new LiveSubCategory()
                    {
                        ID = $"{subItem["partition"]["id_str"]},{subItem["partition"]["type"]}",
                        Name = subItem["partition"]["title"].ToString(),
                        ParentID = id,
                        Pic = "",
                    };
                    subs.Add(subCategory);
                }
                var category = new LiveCategory()
                {
                    Children = subs,
                    ID = id,
                    Name = item["partition"]["title"].ToString(),
                };
                subs.Insert(0, new LiveSubCategory() { ID = category.ID, Name = category.Name, ParentID = category.ID, Pic = "" });
                categories.Add(category);
            }
            return categories;
        }

        internal static JObject ParseEmbeddedCategoryData(string renderData)
        {
            // The embedded script record can contain a trailing array delimiter after the JSON object.
            using (var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(renderData)))
            {
                return JObject.Load(reader);
            }
        }

        public Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page = 1) => GetCategoryRooms(category, page, CancellationToken.None);
        public async Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, CancellationToken cancellationToken)
        {
            var ids = category.ID.Split(',');
            var partitionId = ids[0];
            var partitionType = ids[1];
            var reqParams = new Dictionary<string, string> {
                    {"aid","6383"},
                    {"app_name","douyin_web" },
                    {"live_id", "1"},
                    {"device_platform","web" },
                    { "language", "zh-CN"},
                    { "enter_from", "link_share"},
                    { "cookie_enabled", "true"},
                    { "screen_width", "1980"},
                    { "screen_height", "1080"},
                    { "browser_language", "zh-CN"},
                    { "browser_platform", "Win32"},
                    { "browser_name", "Edge"},
                    { "browser_version", "125.0.0.0"},
                    {"browser_online", "true"},
                    { "count","15" },
                    { "offset", ((page - 1) * 15).ToString()},
                    {"partition",partitionId},
                    {"partition_type",partitionType},
                    {"req_from","2" }
                };
            var url = $"https://live.douyin.com/webcast/web/partition/detail/room/v2/?{Utils.BuildQueryString(reqParams)}";

            var resp = await GetWebResponse(url, cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(resp)) throw new InvalidOperationException("抖音未返回直播列表，请稍后刷新");
            var json = JObject.Parse(resp);
            LiveRequestException.EnsureSuccess(json, "status_code");
            if (!(json["data"]?["data"] is JArray)) throw new InvalidOperationException("抖音直播列表暂不可用，请登录或稍后刷新");
            var hasMore = (json["data"]["data"] as JArray).Count >= 15;
            var items = new List<LiveRoomItem>();
            foreach (var item in json["data"]["data"])
            {
                var roomItem = new LiveRoomItem()
                {
                    RoomID = item["web_rid"].ToString(),
                    IsLive = ReadListLiveStatus(item["room"]["status"]),
                    Title = item["room"]["title"].ToString(),
                    Cover = item["room"]["cover"]["url_list"][0].ToString(),
                    UserName = item["room"]["owner"]["nickname"].ToString(),
                    Online = item["room"]["room_view_stats"]?["display_value"]?.ToObject<int>() ?? 0,
                };
                items.Add(roomItem);
            }
            var statusError = await ConfirmLiveStatus(items, cancellationToken: cancellationToken);
            return new LiveCategoryResult()
            {
                HasMore = hasMore,
                Error = statusError,
                Rooms = items
            };
        }
        public Task<LiveCategoryResult> GetRecommendRooms(int page = 1) => GetRecommendRooms(page, CancellationToken.None);
        public async Task<LiveCategoryResult> GetRecommendRooms(int page, CancellationToken cancellationToken)
        {
            var reqParams = new Dictionary<string, string> {
                    {"aid","6383"},
                    {"app_name","douyin_web" },
                    {"live_id", "1"},
                    {"device_platform","web" },
                    { "language", "zh-CN"},
                    { "enter_from", "link_share"},
                    { "cookie_enabled", "true"},
                    { "screen_width", "1980"},
                    { "screen_height", "1080"},
                    { "browser_language", "zh-CN"},
                    { "browser_platform", "Win32"},
                    { "browser_name", "Edge"},
                    { "browser_version", "125.0.0.0"},
                    {"browser_online", "true"},
                    { "count","15" },
                    { "offset", ((page - 1) * 15).ToString()},
                    {"partition","720" },
                    {"partition_type","1"},
                    {"req_from","2" }
                };
            var url = $"https://live.douyin.com/webcast/web/partition/detail/room/v2/?{Utils.BuildQueryString(reqParams)}";

            var resp = await GetWebResponse(url, cancellationToken: cancellationToken);

            var json = JObject.Parse(resp);
            LiveRequestException.EnsureSuccess(json, "status_code");
            var hasMore = (json["data"]["data"] as JArray).Count >= 15;
            var items = new List<LiveRoomItem>();
            foreach (var item in json["data"]["data"])
            {
                var roomItem = new LiveRoomItem()
                {
                    RoomID = item["web_rid"].ToString(),
                    IsLive = ReadListLiveStatus(item["room"]["status"]),
                    Title = item["room"]["title"].ToString(),
                    Cover = item["room"]["cover"]["url_list"][0].ToString(),
                    UserName = item["room"]["owner"]["nickname"].ToString(),
                    Online = item["room"]["room_view_stats"]?["display_value"]?.ToObject<int>() ?? 0,
                };
                items.Add(roomItem);
            }
            var statusError = await ConfirmLiveStatus(items, cancellationToken: cancellationToken);
            return new LiveCategoryResult()
            {
                HasMore = hasMore,
                Error = statusError,
                Rooms = items
            };
        }
        public Task<LiveRoomDetail> GetRoomDetail(object roomId) => GetRoomDetail(roomId, CancellationToken.None);
        public async Task<LiveRoomDetail> GetRoomDetail(object roomId, CancellationToken cancellationToken)
        {
            // 有两种roomId，一种是webRid，一种是roomId
            // roomId是一次性的，用户每次重新开播都会生成一个新的roomId
            // roomId一般长度为19位，例如：7376429659866598196
            // webRid是固定的，用户每次开播都是同一个webRid
            // webRid一般长度为11-12位，例如：416144012050
            // 这里简单进行判断，如果roomId长度小于15，则认为是webRid
            if (roomId.ToString().Length <= 16)
            {
                var webRid = roomId as string;
                return await GetRoomDetailByWebRid(webRid, cancellationToken: cancellationToken);
            }

            return await GetRoomDetailByRoomID(roomId as string, cancellationToken: cancellationToken);
        }
        /// <summary>
        /// 通过RoomId获取直播间详情
        /// </summary>
        /// <param name="roomId">
        /// roomId是一次性的，用户每次重新开播都会生成一个新的roomId。
        /// roomId一般长度为19位，例如：7376429659866598196
        /// </param>
        /// <returns></returns>
        private async Task<LiveRoomDetail> GetRoomDetailByRoomID(string roomId, CancellationToken cancellationToken = default)
        {
            var roomData = await GetRoomDataByRoomID(roomId, cancellationToken: cancellationToken);
            // 通过房间信息获取WebRid
            var webRid = roomData["data"]["room"]["owner"]["web_rid"].ToString();
            var room = roomData["data"]["room"];
            var owner = room["owner"];
            var status = room["status"].ToObject<int>();
            // roomId是一次性的，用户每次重新开播都会生成一个新的roomId
            // 所以如果roomId对应的直播间状态不是直播中，就通过webRid获取直播间信息
            if (status == 4)
            {
                var result = await GetRoomDetailByWebRid(webRid, cancellationToken: cancellationToken);
                return result;
            }
            var roomStatus = ReadRoomLiveStatus(room["status"]);
            return new LiveRoomDetail()
            {
                RoomID = webRid,
                Title = room["title"].ToString(),
                Cover = roomStatus ? room["cover"]["url_list"][0].ToString() : "",
                UserName = owner["nickname"].ToString(),
                UserAvatar = owner["avatar_thumb"]["url_list"][0].ToString(),
                Online = roomStatus
                  ? (room["room_view_stats"]?["display_value"]?.ToObject<int>() ?? 0)
                  : 0,
                Status = roomStatus,
                Url = $"https://live.douyin.com/{webRid}",
                Introduction = owner?["signature"]?.ToString() ?? "",
                Notice = "",
                Data = roomStatus ? room["stream_url"] : null,
            };

        }

        /// <summary>
        /// 通过webRid获取直播间详情
        /// </summary>
        /// <param name="webRid">
        /// webRid是固定的，用户每次开播都是同一个webRid
        /// webRid一般长度为11-12位，例如：416144012050
        /// </param>
        /// <returns></returns>
        private async Task<LiveRoomDetail> GetRoomDetailByWebRid(string webRid, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await GetRoomDetailByWebRidApi(webRid, cancellationToken: cancellationToken);
                return result;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                if (ex is LiveRequestException failure && failure.Kind == LiveFailureKind.Authentication) throw;
                try { return await GetRoomDetailByWebRidHtml(webRid, cancellationToken: cancellationToken); }
                catch (Exception fallback) when (!(fallback is OperationCanceledException)) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw(); throw; }
            }
        }

        private async Task<LiveRoomDetail> GetRoomDetailByWebRidApi(string webRid, CancellationToken cancellationToken = default)
        {
            // 读取房间信息
            //var data = await _getRoomDataByApi(webRid);
            var data = await GetRoomDataApi(webRid, cancellationToken: cancellationToken);
            var roomData = data["data"][0];
            var userData = data["user"];



            var owner = roomData["owner"];

            var roomStatus = ReadRoomLiveStatus(roomData["status"]);

            return new LiveRoomDetail()
            {
                RoomID = webRid,
                Title = roomData["title"].ToString(),
                Cover = roomStatus ? roomData["cover"]["url_list"][0].ToString() : "",
                UserName = roomStatus
                    ? owner["nickname"].ToString()
                    : userData["nickname"].ToString(),
                UserAvatar = roomStatus
                    ? owner["avatar_thumb"]["url_list"][0].ToString()
                    : userData["avatar_thumb"]["url_list"][0].ToString(),
                Online = roomStatus
                    ? (roomData["room_view_stats"]?["display_value"]?.ToObject<int>() ?? 0)
                    : 0,
                Status = roomStatus,
                Url = $"https://live.douyin.com/{webRid}",
                Introduction = owner?["signature"]?.ToString() ?? "",
                Notice = "",
                Data = roomStatus ? roomData["stream_url"] : null,
            };

        }

        private async Task<LiveRoomDetail> GetRoomDetailByWebRidHtml(string webRid, CancellationToken cancellationToken = default)
        {
            var roomData = await GetRoomDataHtml(webRid, cancellationToken: cancellationToken);

            var room = roomData["roomStore"]["roomInfo"]["room"];
            var owner = room["owner"];
            var anchor = roomData["roomStore"]["roomInfo"]["anchor"];
            var roomStatus = ReadRoomLiveStatus(room["status"]);

            return new LiveRoomDetail()
            {
                RoomID = webRid,
                Title = room["title"].ToString(),
                Cover = roomStatus ? room["cover"]["url_list"][0].ToString() : "",
                UserName = roomStatus
                    ? owner["nickname"].ToString()
                    : anchor["nickname"].ToString(),
                UserAvatar = roomStatus
                    ? owner["avatar_thumb"]["url_list"][0].ToString()
                    : anchor["avatar_thumb"]["url_list"][0].ToString(),
                Online = roomStatus
                    ? (room["room_view_stats"]?["display_value"]?.ToObject<int>() ?? 0)
                    : 0,
                Status = roomStatus,
                Url = $"https://live.douyin.com/{webRid}",
                Introduction = owner?["signature"]?.ToString() ?? "",
                Notice = "",
                Data = roomStatus ? room["stream_url"] : null,
            };
        }
        /// <summary>
        ///  进入直播间前需要先获取cookie
        /// </summary>
        /// <param name="webRid">直播间RID</param>
        /// <returns></returns>
        private async Task<string> GetWebCookie(string webRid, CancellationToken cancellationToken = default)
        {
            using var resp = await HttpUtil.Head($"https://live.douyin.com/{webRid}",
                headers: await GetRequestHeaders(cancellationToken: cancellationToken)
            , cancellationToken: cancellationToken);
            var dyCookie = Cookie;
            foreach (var item in resp.Headers.GetValues("Set-Cookie"))
            {
                var cookie = item.Split(';')[0];
                if (cookie.Contains("ttwid") || cookie.Contains("__ac_nonce") || cookie.Contains("msToken"))
                {
                    if (!dyCookie.Split(';').Any(c => c.Trim().StartsWith(cookie.Split('=')[0] + "="))) dyCookie += $";{cookie}";
                }
            }
            return dyCookie;
        }

        private async Task<JToken> GetRoomDataHtml(string webRid, CancellationToken cancellationToken = default)
        {
            var dyCookie = await GetWebCookie(webRid, cancellationToken: cancellationToken);
            var resp = await HttpUtil.GetString($"https://live.douyin.com/{webRid}",
                headers: new Dictionary<string, string>
                {
                    { "User-Agent", UserAgent },
                    { "Referer", REFERER },
                    { "Authority", AUTHORITY },
                    { "Cookie", dyCookie }
                }
            , cancellationToken: cancellationToken);
            Regex regex = new Regex("\\{\\\\\"state\\\\\":\\{\\\\\"appStore.*?\\]\\\\n", RegexOptions.Singleline);
            Match match = regex.Match(resp);
            string json = match.Success ? match.Groups[0].Value : "";
            if (string.IsNullOrEmpty(json))
            {
                throw new Exception("无法读取直播间数据");
            }
            json = json.Trim().Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("]\\n", "");
            return JObject.Parse(json)["state"];
        }

        private async Task<JToken> GetRoomDataApi(string webRid, CancellationToken cancellationToken = default)
        {
            var reqParams = new Dictionary<string, string> {
                    {"aid","6383" },
                    {"app_name","douyin_web" },
                    {"live_id","1" },
                    {"device_platform","web" },
                    {"enter_from","web_live" },
                    {"web_rid",webRid },
                    {"room_id_str","" },
                    {"enter_source","" },
                    {"Room-Enter-User-Login-Ab","0" },
                    {"is_need_double_stream","false" },
                    {"cookie_enabled","true" },
                    {"screen_width","1980" },
                    {"screen_height","1080" },
                    {"browser_language","zh-CN" },
                    {"browser_platform","Win32" },
                    {"browser_name","Edge" },
                    {"browser_version","125.0.0.0" },
                };
            var url = $"https://live.douyin.com/webcast/room/web/enter/?{Utils.BuildQueryString(reqParams)}";

            var resp = await GetWebResponse(url, cancellationToken: cancellationToken);


           
            var response = JObject.Parse(resp);
            LiveRequestException.EnsureSuccess(response, "status_code");
            return response["data"];
        }

        private async Task<JToken> GetRoomDataByRoomID(string roomId, CancellationToken cancellationToken = default)
        {
            var resp = await HttpUtil.GetString($"https://webcast.amemv.com/webcast/room/reflow/info/",
                headers: await GetRequestHeaders(cancellationToken: cancellationToken),
                queryParameters: new Dictionary<string, string>
                {
                    {"type_id","0" },
                    {"live_id","1" },
                    {"room_id",roomId },
                    {"sec_user_id","" },
                    {"version_code","99.99.99" },
                    {"app_id","6383" },
                }
            , cancellationToken: cancellationToken);
            return JObject.Parse(resp);
        }

        public Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail roomDetail) => GetPlayQuality(roomDetail, CancellationToken.None);
        public Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail roomDetail, CancellationToken cancellationToken)
        {
            List<LivePlayQuality> qualities = new List<LivePlayQuality>();
            if (roomDetail.Data == null)
            {
                return Task.FromResult(qualities);
            }
            var data = roomDetail.Data as JToken;
            var qulityList = data["live_core_sdk_data"]["pull_data"]["options"]["qualities"];
            var streamData = data["live_core_sdk_data"]["pull_data"]["stream_data"].ToString();

            if (!streamData.StartsWith("{"))
            {
                var flvList = (data["flv_pull_url"] as JToken).Values().Select(c => c.ToString()).ToList();
                var hlsList = (data["hls_pull_url_map"] as JToken).Values().Select(c => c.ToString()).ToList();
                foreach (var quality in qulityList)
                {
                    int level = quality["level"].ToObject<int>();
                    List<String> urls = new List<string>();
                    var flvIndex = flvList.Count - level;
                    if (flvIndex >= 0 && flvIndex < flvList.Count)
                    {
                        urls.Add(flvList[flvIndex]);
                    }
                    var hlsIndex = hlsList.Count - level;
                    if (hlsIndex >= 0 && hlsIndex < hlsList.Count)
                    {
                        urls.Add(hlsList[hlsIndex]);
                    }
                    var qualityItem = new LivePlayQuality()
                    {
                        Quality = quality["name"].ToString(),
                        Sort = level,
                        Data = urls,
                    };
                    if (urls.Count > 0)
                    {
                        qualities.Add(qualityItem);
                    }
                }
            }
            else
            {
                var qualityData = JObject.Parse(streamData)["data"] as JObject;
                foreach (var quality in qulityList)
                {
                    List<string> urls = new List<string>();

                    var flvUrl =
                        qualityData[quality["sdk_key"].ToString()]?["main"]?["flv"]?.ToString();

                    if (flvUrl != null && flvUrl.Length > 0)
                    {
                        urls.Add(flvUrl);
                    }
                    var hlsUrl =
                        qualityData[quality["sdk_key"].ToString()]?["main"]?["hls"]?.ToString();
                    if (hlsUrl != null && hlsUrl.Length > 0)
                    {
                        urls.Add(hlsUrl);
                    }
                    var qualityItem = new LivePlayQuality()
                    {
                        Quality = quality["name"].ToString(),
                        Sort = quality["level"].ToObject<int>(),
                        Data = urls,
                    };
                    if (urls.Count > 0)
                    {
                        qualities.Add(qualityItem);
                    }
                }
            }
            // var qualityData = json.decode(
            //     detail.data["live_core_sdk_data"]["pull_data"]["stream_data"])["data"];

            //qualities.sort((a, b) => b.sort.compareTo(a.sort));
            qualities = qualities.OrderByDescending(q => q.Sort).ToList();
            return Task.FromResult(qualities);
        }

        public Task<List<string>> GetPlayUrls(LiveRoomDetail roomDetail, LivePlayQuality qn) => GetPlayUrls(roomDetail, qn, CancellationToken.None);
        public Task<List<string>> GetPlayUrls(LiveRoomDetail roomDetail, LivePlayQuality qn, CancellationToken cancellationToken)
        {
            return Task.FromResult(qn.Data as List<string>);
        }

        public Task<LiveSearchResult> Search(string keyword, int page = 1) => Search(keyword, page, CancellationToken.None);
        public async Task<LiveSearchResult> Search(string keyword, int page, CancellationToken cancellationToken)
        {
            var query = new Dictionary<string, string>
            {
                { "device_platform", "webapp" },
                { "aid", "6383" },
                { "channel", "channel_pc_web" },
                { "search_channel", "aweme_live" },
                { "keyword", keyword },  // 动态值
                { "search_source", "switch_tab" },
                { "query_correct_type", "1" },
                { "is_filter_search", "0" },
                { "from_group_id", "" },
                { "offset", ((page - 1) * 10).ToString() },  // 动态计算值
                { "count", "10" },
                { "pc_client_type", "1" },
                { "version_code", "170400" },
                { "version_name", "17.4.0" },
                { "cookie_enabled", "true" },
                { "screen_width", "1980" },
                { "screen_height", "1080" },
                { "browser_language", "zh-CN" },
                { "browser_platform", "Win32" },
                { "browser_name", "Edge" },
                { "browser_version", "125.0.0.0" },
                { "browser_online", "true" },
                { "engine_name", "Blink" },
                { "engine_version", "125.0.0.0" },
                { "os_name", "Windows" },
                { "os_version", "10" },
                { "cpu_core_num", "12" },
                { "device_memory", "8" },
                { "platform", "PC" },
                { "downlink", "10" },
                { "effective_type", "4g" },
                { "round_trip_time", "100" },
                { "webid", "7382872326016435738" }
            };

            var requestUrl = $"https://www.douyin.com/aweme/v1/web/live/search/?{Utils.BuildQueryString(query)}";
            var cookie = (await GetRequestHeaders(cancellationToken: cancellationToken))["Cookie"];
            var headers = new Dictionary<string, string>
            {
                { "accept", "application/json, text/plain, */*" },
                { "accept-language", "zh-CN,zh;q=0.9,en;q=0.8" },
                { "cookie", cookie },
                { "priority", "u=1, i" },
                { "referer", $"https://www.douyin.com/search/{Uri.EscapeUriString(keyword)}?type=live" },
                { "sec-ch-ua", "\"Microsoft Edge\";v=\"125\", \"Chromium\";v=\"125\", \"Not.A/Brand\";v=\"24\"" },
                { "sec-ch-ua-mobile", "?0" },
                { "sec-ch-ua-platform", "\"Windows\"" },
                { "sec-fetch-dest", "empty" },
                { "sec-fetch-mode", "cors" },
                { "sec-fetch-site", "same-origin" },
                { "user-agent", USER_AGENT }
            };
            var resp = await HttpUtil.GetString(requestUrl, headers, cancellationToken: cancellationToken);
            var json = JObject.Parse(resp);
            var items = new List<LiveRoomItem>();
            foreach (var item in json["data"])
            {
                var itemData = JObject.Parse(item["lives"]["rawdata"].ToString());
                var roomItem = new LiveRoomItem()
                {
                    IsLive = ReadSearchLiveStatus(itemData["status"]),
                    RoomID = itemData["owner"]["web_rid"].ToString(),
                    Title = itemData["title"].ToString(),
                    Cover = itemData["cover"]["url_list"][0].ToString(),
                    UserName = itemData["owner"]["nickname"].ToString(),
                    Online = itemData["stats"]["total_user"].ToObject<int>(),
                };
                items.Add(roomItem);
            }
            return new LiveSearchResult()
            {
                HasMore = items.Count >= 10,
                Rooms = items
            };
        }
        private static bool? ReadSearchLiveStatus(JToken status)
        {
            if (!int.TryParse(status?.ToString(), out var value)) return null;
            return value == 2 ? true : value == 4 ? false : (bool?)null;
        }
        public Task<bool> GetLiveStatus(object roomId) => GetLiveStatus(roomId, CancellationToken.None);
        public async Task<bool> GetLiveStatus(object roomId, CancellationToken cancellationToken)
        {
            var key = roomId.ToString();
            lock (liveStatusGate)
                if (liveStatusCache.TryGetValue(key, out var saved) && saved.expires > DateTime.UtcNow) return saved.live;
            var result = await GetRoomDetail(roomId: roomId, cancellationToken: cancellationToken);
            lock (liveStatusGate)
            {
                foreach (var expired in liveStatusCache.Where(entry => entry.Value.expires <= DateTime.UtcNow).Select(entry => entry.Key).ToArray()) liveStatusCache.Remove(expired);
                liveStatusCache[key] = (DateTime.UtcNow.AddSeconds(30), result.Status);
            }
            return result.Status;

        }

        private static bool? ReadListLiveStatus(JToken status)
        {
            var value = status?.ToObject<int>() ?? 0;
            return value == 0 ? (bool?)null : value == 2;
        }
        private static bool ReadRoomLiveStatus(JToken status)
        {
            var value = status?.ToObject<int>() ?? 0;
            if (value == 2) return true;
            if (value == 4) return false;
            throw new LiveRequestException(LiveFailureKind.Platform, "平台未确认当前开播状态");
        }
        private async Task<Exception> ConfirmLiveStatus(List<LiveRoomItem> items, CancellationToken cancellationToken = default)
        {
            Exception error = null;
            // Partition feeds use status=0 for unspecified state; query room details before hiding them.
            await Task.WhenAll(items.Where(r => r.IsLive == null).Select(async room =>
            {
                try { room.IsLive = await GetLiveStatus(room.RoomID, cancellationToken: cancellationToken); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { room.IsLive = null; System.Threading.Interlocked.CompareExchange(ref error, ex, null); }
            }));
            return error;
        }
        public Func<string, Task<string>> WebRequestAsync { get; set; }
        public Func<string, CancellationToken, Task<string>> CancellableWebRequestAsync { get; set; }
        private async Task<string> GetWebResponse(string url, CancellationToken cancellationToken = default)
        {
            if (CancellableWebRequestAsync != null) return await CancellableWebRequestAsync(url, cancellationToken);
            if (WebRequestAsync != null) return await WebRequestAsync(url);
            throw new InvalidOperationException("抖音接口需要官网浏览器签名，请使用桌面版");
        }
    }
}
