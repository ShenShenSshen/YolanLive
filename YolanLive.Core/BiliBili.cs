using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using YolanLive.Core.Helper;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using System.Web;

namespace YolanLive.Core
{

    public class BiliBili : ILiveSite
    {
        public string Name => "哔哩哔哩直播";
        private static JObject ParseResponse(string payload)
        {
            var response = JObject.Parse(payload);
            LiveRequestException.EnsureSuccess(response, "code", true);
            return response;
        }
        /// <summary>
        /// 哔哩哔哩Cookie
        /// </summary>
        public string Cookie { get; set; }


        private string buvid3 = "";
        private string buvid4 = "";
        private async Task<Dictionary<string, string>> GetRequestHeader(CancellationToken cancellationToken = default)
        {

            if (string.IsNullOrEmpty(buvid3))
            {
                var buvid = await GetBuvid(cancellationToken: cancellationToken);
                buvid3 = buvid.Item1;
                buvid4 = buvid.Item2;
            }

            var headers = new Dictionary<string, string>()
            {
                {"user-agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0" },
                {"referer","https://live.bilibili.com/" },
                { "cookie",GetCookie()}
            };

            return headers;
        }

        private string GetCookie()
        {
            var _cookie = "";
            if (string.IsNullOrEmpty(Cookie))
            {
                _cookie = $"buvid3={buvid3};buvid4={buvid4};";
            }
            else
            {
                _cookie = Cookie.Contains("buvid3") ? Cookie : $"{Cookie};buvid3={buvid3};buvid4={buvid4}";
            }
            return _cookie;
        }

        public Task<List<LiveCategory>> GetCategores() => GetCategores(CancellationToken.None);
        public async Task<List<LiveCategory>> GetCategores(CancellationToken cancellationToken)
        {
            List<LiveCategory> categories = new List<LiveCategory>();
            var result = await HttpUtil.GetString("https://api.live.bilibili.com/room/v1/Area/getList?need_entrance=1&parent_id=0", headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            var obj = ParseResponse(result);
            foreach (var item in obj["data"])
            {
                List<LiveSubCategory> subs = new List<LiveSubCategory>();
                foreach (var subItem in item["list"])
                {
                    subs.Add(new LiveSubCategory()
                    {
                        Pic = subItem["pic"].ToString() + "@320w.png",
                        ID = subItem["id"].ToString(),
                        ParentID = subItem["parent_id"].ToString(),
                        Name = subItem["name"].ToString(),
                    });
                }

                categories.Add(new LiveCategory()
                {
                    Children = subs,
                    ID = item["id"].ToString(),
                    Name = item["name"].ToString(),
                });
            }
            return categories;
        }
        public Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page = 1) => GetCategoryRooms(category, page, CancellationToken.None);
        public async Task<LiveCategoryResult> GetCategoryRooms(LiveSubCategory category, int page, CancellationToken cancellationToken)
        {
            LiveCategoryResult categoryResult = new LiveCategoryResult()
            {
                Rooms = new List<LiveRoomItem>(),

            };
            var url = $"https://api.live.bilibili.com/room/v3/area/getRoomList";
            var query = $"platform=android&parent_area_id={category.ParentID}&area_id={category.ID}&sort_type=online&page_size=30&page={page}";
            var result = await HttpUtil.GetString($"{url}?{query}", headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);

            var obj = ParseResponse(result);
            if (obj["data"]?["list"] == null)
                throw new InvalidOperationException($"B站分类接口返回错误：{obj["code"]} {obj["message"]}");
            categoryResult.HasMore = obj["data"]["count"].ToInt32() > page * 30;
            foreach (var item in obj["data"]["list"])
            {
                var parent = item["area_v2_parent_id"]?.ToString() ?? item["parent_id"]?.ToString();
                var area = item["area_v2_id"]?.ToString() ?? item["area_id"]?.ToString();
                if (parent != category.ParentID || (category.ID != "0" && area != category.ID)) continue;
                categoryResult.Rooms.Add(new LiveRoomItem()
                {
                    IsLive = item["live_status"] == null || item["live_status"].ToObject<int>() == 1,
                    Cover = item["cover"].ToString() + "@300w.jpg",
                    Online = item["online"].ToInt32(),
                    RoomID = item["roomid"].ToString(),
                    Title = item["title"].ToString(),
                    UserName = item["uname"].ToString(),
                });
            }
            return categoryResult;
        }
        public Task<LiveCategoryResult> GetRecommendRooms(int page = 1) => GetRecommendRooms(page, CancellationToken.None);
        public async Task<LiveCategoryResult> GetRecommendRooms(int page, CancellationToken cancellationToken)
        {
            LiveCategoryResult categoryResult = new LiveCategoryResult()
            {
                Rooms = new List<LiveRoomItem>(),

            };
            var url = $"https://api.live.bilibili.com/xlive/web-interface/v1/second/getListByArea";
            var query = $"platform=web&sort=online&page_size=30&page={page}";
            query = await GetWbiSign(query, cancellationToken: cancellationToken);
            var result = await HttpUtil.GetString($"{url}?{query}", headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            var obj = ParseResponse(result);
            categoryResult.HasMore = ((JArray)obj["data"]["list"]).Count > 0;
            foreach (var item in obj["data"]["list"])
            {
                categoryResult.Rooms.Add(new LiveRoomItem()
                {
                    IsLive = item["live_status"] == null || item["live_status"].ToObject<int>() == 1,
                    Cover = item["cover"].ToString() + "@300w.jpg",
                    Online = item["online"].ToInt32(),
                    RoomID = item["roomid"].ToString(),
                    Title = item["title"].ToString(),
                    UserName = item["uname"].ToString(),
                });
            }
            return categoryResult;
        }
        public Task<LiveRoomDetail> GetRoomDetail(object roomId) => GetRoomDetail(roomId, CancellationToken.None);
        public async Task<LiveRoomDetail> GetRoomDetail(object roomId, CancellationToken cancellationToken)
        {
            var url = "https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByRoom";
            var query = $"room_id={roomId}";
            query = await GetWbiSign(query, cancellationToken: cancellationToken);
            var result = await HttpUtil.GetString($"{url}?{query}", headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            var obj = ParseResponse(result);

            return new LiveRoomDetail()
            {
                Cover = obj["data"]["room_info"]["cover"].ToString(),
                Online = obj["data"]["room_info"]["online"].ToInt32(),
                RoomID = obj["data"]["room_info"]["room_id"].ToString(),
                Title = obj["data"]["room_info"]["title"].ToString(),
                UserName = obj["data"]["anchor_info"]["base_info"]["uname"].ToString(),
                Introduction = obj["data"]["room_info"]["description"].ToString(),
                UserAvatar = obj["data"]["anchor_info"]["base_info"]["face"].ToString() + "@100w.jpg",
                Notice = "",
                Status = obj["data"]["room_info"]["live_status"].ToInt32() == 1,
                Url = "https://live.bilibili.com/" + roomId
            };
        }
        public Task<LiveSearchResult> Search(string keyword, int page = 1) => Search(keyword, page, CancellationToken.None);
        public async Task<LiveSearchResult> Search(string keyword, int page, CancellationToken cancellationToken)
        {
            LiveSearchResult searchResult = new LiveSearchResult()
            {
                Rooms = new List<LiveRoomItem>(),

            };
            var result = await HttpUtil.GetString($"https://api.bilibili.com/x/web-interface/search/type?context=&search_type=live&cover_type=user_cover&page={page}&order=&keyword={Uri.EscapeDataString(keyword)}&category_id=&__refresh__=true&_extra=&highlight=0&single_column=0", headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            var obj = ParseResponse(result);

            foreach (var item in obj["data"]["result"]["live_room"])
            {
                searchResult.Rooms.Add(new LiveRoomItem()
                {
                    Cover = "https:" + item["cover"].ToString() + "@300w.jpg",
                    IsLive = ReadSearchLiveStatus(item["live_status"]),
                    Online = item["online"].ToInt32(),
                    RoomID = item["roomid"].ToString(),
                    Title = Regex.Replace(item["title"].ToString(), @"<em.*?/em>", ""),
                    UserName = item["uname"].ToString(),
                });
            }
            searchResult.HasMore = searchResult.Rooms.Count > 0;
            return searchResult;
        }
        private static bool? ReadSearchLiveStatus(JToken status)
        {
            if (!int.TryParse(status?.ToString(), out var value)) return null;
            return value == 1 ? true : value == 0 || value == 2 ? false : (bool?)null;
        }
        public Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail roomDetail) => GetPlayQuality(roomDetail, CancellationToken.None);
        public async Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail roomDetail, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Cookie))
            {
                return await GetPlayQualityOld(roomDetail.RoomID, cancellationToken: cancellationToken);
            }
            else
            {
                return await GetPlayQualityNew(roomDetail.RoomID, cancellationToken: cancellationToken);
            }
        }
        /// <summary>
        /// 新的获取清晰度方式，需要登录
        /// </summary>
        /// <param name="roomID"></param>
        /// <returns></returns>
        private async Task<List<LivePlayQuality>> GetPlayQualityNew(string roomID, CancellationToken cancellationToken = default)
        {

            List<LivePlayQuality> qualities = new List<LivePlayQuality>();
            var result = await HttpUtil.GetString($"https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo",
                headers: await GetRequestHeader(cancellationToken: cancellationToken),
                queryParameters: new Dictionary<string, string>() {
                    { "room_id", roomID } ,
                    { "protocol", "0,1" },
                    { "format", "0,1,2"},
                    { "codec","0,1"},
                    { "platform", "web"}
                }
            , cancellationToken: cancellationToken);
            var obj = ParseResponse(result);
            var qualitiesMap = new Dictionary<int, string>();
            foreach (var item in obj["data"]["playurl_info"]["playurl"]["g_qn_desc"])
            {
                qualitiesMap[item["qn"].ToObject<int>()] =
                    item["desc"].ToString();
            }
            foreach (var item in obj["data"]["playurl_info"]["playurl"]["stream"][0]["format"][0]["codec"][0]["accept_qn"])
            {
                var qualityItem = new LivePlayQuality()
                {
                    Quality = qualitiesMap[item.ToObject<int>()] ?? "未知清晰度",
                    Data = item,
                };
                qualities.Add(qualityItem);
            }
            return qualities;
        }

        /// <summary>
        /// 旧的获取清晰度方式，部分直播看不了
        /// </summary>
        /// <param name="roomID"></param>
        /// <returns></returns>
        private async Task<List<LivePlayQuality>> GetPlayQualityOld(string roomID, CancellationToken cancellationToken = default)
        {

            List<LivePlayQuality> qualities = new List<LivePlayQuality>();
            var result = await HttpUtil.GetString($"https://api.live.bilibili.com/room/v1/Room/playUrl?cid={roomID}&qn=&platform=web", headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            var obj = ParseResponse(result);
            foreach (var item in obj["data"]["quality_description"])
            {
                qualities.Add(new LivePlayQuality()
                {
                    Quality = item["desc"].ToString(),
                    Data = item["qn"].ToInt32(),
                });
            }
            return qualities;
        }

        public Task<List<string>> GetPlayUrls(LiveRoomDetail roomDetail, LivePlayQuality qn) => GetPlayUrls(roomDetail, qn, CancellationToken.None);
        public async Task<List<string>> GetPlayUrls(LiveRoomDetail roomDetail, LivePlayQuality qn, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Cookie))
            {
                return await GetPlayUrlsOld(roomDetail.RoomID, qn.Data, cancellationToken: cancellationToken);
            }
            else
            {
                return await GetPlayUrlsNew(roomDetail.RoomID, qn.Data, cancellationToken: cancellationToken);
            }
        }
        private async Task<List<string>> GetPlayUrlsNew(string roomID, object qn, CancellationToken cancellationToken = default)
        {
            List<string> urls = new List<string>();
            List<LivePlayQuality> qualities = new List<LivePlayQuality>();
            var result = await HttpUtil.GetString($"https://api.live.bilibili.com/xlive/web-room/v2/index/getRoomPlayInfo",
                headers: await GetRequestHeader(cancellationToken: cancellationToken),
                queryParameters: new Dictionary<string, string>() {
                    { "room_id", roomID } ,
                    { "protocol", "0,1" },
                    { "format", "0,2"},
                    { "codec","0"},
                    { "platform", "web" },
                    { "qn",qn.ToString()}
                }
            , cancellationToken: cancellationToken);
            var obj = ParseResponse(result);
            var streamList = obj["data"]["playurl_info"]["playurl"]["stream"];
            foreach (var streamItem in streamList)
            {
                var formatList = streamItem["format"];
                foreach (var formatItem in formatList)
                {
                    var codecList = formatItem["codec"];
                    foreach (var codecItem in codecList)
                    {
                        var urlList = codecItem["url_info"];
                        var baseUrl = codecItem["base_url"].ToString();
                        foreach (var urlItem in urlList)
                        {
                            urls.Add(
                              $"{urlItem["host"].ToString()}{baseUrl.ToString()}{urlItem["extra"].ToString()}"
                            );
                        }
                    }
                }
            }

            // 对链接进行排序，包含mcdn的在后
            urls = urls.OrderBy(x => x.Contains("mcdn")).ToList();

            return urls;
        }
        /// <summary>
        /// 旧的获取播放地址方式，部分直播看不了
        /// </summary>
        /// <param name="roomID"></param>
        /// <param name="qn"></param>
        /// <returns></returns>
        private async Task<List<string>> GetPlayUrlsOld(string roomID, object qn, CancellationToken cancellationToken = default)
        {
            List<string> urls = new List<string>();
            var result = await HttpUtil.GetString($"https://api.live.bilibili.com/room/v1/Room/playUrl?cid={roomID}&qn={qn}&platform=web", headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            var obj = ParseResponse(result);
            foreach (var item in obj["data"]["durl"])
            {
                urls.Add(item["url"].ToString());
            }
            return urls;
        }

        public Task<bool> GetLiveStatus(object roomId) => GetLiveStatus(roomId, CancellationToken.None);
        public async Task<bool> GetLiveStatus(object roomId, CancellationToken cancellationToken)
        {
            var resp = await HttpUtil.GetString($"https://api.live.bilibili.com/room/v1/Room/get_info?room_id={roomId}", headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            var obj = ParseResponse(resp);
            return obj["data"]["live_status"].ToObject<int>() == 1;
        }

        private string _accessId;
        private string _imgKey;
        private string _subKey;
        private int[] mixinKeyEncTab = new int[] {
            46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49,
            33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13, 37, 48, 7, 16, 24, 55, 40,
            61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11,
            36, 20, 34, 44, 52
        };
        private async Task<(string, string)> GetWbiKeys(CancellationToken cancellationToken = default)
        {
            if (_imgKey != null && _subKey != null)
            {
                return (_imgKey, _subKey);
            }
            // 获取最新的 img_key 和 sub_key
            var response = await HttpUtil.GetString(
                "https://api.bilibili.com/x/web-interface/nav",
                headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            var obj = ParseResponse(response);

            var imgUrl = obj["data"]["wbi_img"]["img_url"].ToString();
            var subUrl = obj["data"]["wbi_img"]["sub_url"].ToString();
            var imgKey = imgUrl.Substring(imgUrl.LastIndexOf('/') + 1).Split('.')[0];
            var subKey = subUrl.Substring(subUrl.LastIndexOf('/') + 1).Split('.')[0];

            _imgKey = imgKey;
            _subKey = subKey;

            return (imgKey, subKey);
        }

        private string GetMixinKey(string origin)
        {
            // 对 imgKey 和 subKey 进行字符顺序打乱编码
            return mixinKeyEncTab.Aggregate("", (s, i) => s + origin[i]).Substring(0, 32);
        }

        public Task<string> GetWbiSign(string url) => GetWbiSign(url, CancellationToken.None);
        public async Task<string> GetWbiSign(string url, CancellationToken cancellationToken)
        {
            var (imgKey, subKey) = await GetWbiKeys(cancellationToken: cancellationToken);

            // 为请求参数进行 wbi 签名
            var mixinKey = GetMixinKey(imgKey + subKey);
            var currentTime = (long)Math.Round(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);

            var queryString = HttpUtility.ParseQueryString(url);

            var queryParams = queryString.Cast<string>().ToDictionary(k => k, v => queryString[v]);
            queryParams["wts"] = currentTime + ""; // 添加 wts 字段
            queryParams = queryParams.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value); // 按照 key 重排参数
                                                                                                  // 过滤 value 中的 "!'()*" 字符
            queryParams = queryParams.ToDictionary(x => x.Key, x => string.Join("", x.Value.ToString().Where(c => "!'()*".Contains(c) == false)));

            var query = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

            var wbi_sign = Utils.ToMD5($"{query}{mixinKey}");

            return $"{query}&w_rid={wbi_sign}";
        }

        private async Task<(string, string)> GetBuvid(CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await HttpUtil.GetString($"https://api.bilibili.com/x/frontend/finger/spi",
                    headers: string.IsNullOrEmpty(Cookie) ? null : new Dictionary<string, string>
                    {
                        { "cookie", Cookie }
                    }
                  , cancellationToken: cancellationToken);
                var obj = ParseResponse(result);

                return (obj["data"]["b_3"].ToString(), obj["data"]["b_4"].ToString());
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                return ("", "");
            }
        }

        private async Task<string> GetAssessId(CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrEmpty(_accessId))
            {
                return _accessId;
            }

            var response = await HttpUtil.GetString(
                "https://live.bilibili.com/lol",
                headers: await GetRequestHeader(cancellationToken: cancellationToken), cancellationToken: cancellationToken);
            // 通过正则表达式"access_id":"(.*?)"提取
            var match = Regex.Match(response, "\"access_id\":\"(.*?)\"");
            if (match.Success)
            {
                _accessId = match.Groups[1].Value;
                return _accessId;
            }
            else
            {
                throw new Exception("无法获取 access_id");
            }
        }
    }


}
