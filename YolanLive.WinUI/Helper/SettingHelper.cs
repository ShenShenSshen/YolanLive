using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading;
namespace YolanLive.WinUI.Helper
{
    public class SettingHelper
    {
        private static readonly object Gate = new();
        private static readonly string FilePath = Path.Combine(AppPaths.EnsureDirectory(), "settings.json");
        private static readonly Dictionary<string, JToken> Values = File.Exists(FilePath)
            ? JsonConvert.DeserializeObject<Dictionary<string, JToken>>(File.ReadAllText(FilePath)) ?? new()
            : new();
        private static bool dirty;
        private static long saveDue;
        private static readonly Timer SaveTimer = new(_ =>
        {
            try
            {
                lock (Gate)
                {
                    if (!dirty) return;
                    var remaining = saveDue - Environment.TickCount64;
                    if (remaining > 0) { SaveTimer.Change((int)remaining, Timeout.Infinite); return; }
                    Flush();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LogHelper.Log("设置保存失败，将在下次保存时重试", LogType.ERROR, ex); }
        }, null, Timeout.Infinite, Timeout.Infinite);
        public static T GetValue<T>(string key, T fallback)
        {
            lock (Gate) return Values.TryGetValue(key, out var value) && value.Type != JTokenType.Null ? value.ToObject<T>() : fallback;
        }
        public static void SetValue<T>(string key, T value, bool defer = false)
        {
            lock (Gate)
            {
                var next = value == null ? JValue.CreateNull() : JToken.FromObject(value);
                if (Values.TryGetValue(key, out var previous) && JToken.DeepEquals(previous, next)) return;
                Values[key] = next;
                dirty = true;
                if (defer)
                {
                    saveDue = Environment.TickCount64 + 500;
                    SaveTimer.Change(500, Timeout.Infinite);
                }
                else Flush();
            }
        }
        public static void Flush()
        {
            lock (Gate)
            {
                SaveTimer.Change(Timeout.Infinite, Timeout.Infinite);
                if (!dirty) return;
                File.WriteAllText(FilePath + ".tmp", JsonConvert.SerializeObject(Values));
                File.Move(FilePath + ".tmp", FilePath, true);
                dirty = false;
            }
        }
        /// <summary>
        /// 主题,0为默认，1为浅色，2为深色
        /// </summary>
        public const string THEME = "theme";
        public const string WINDOW_SIZE = "WindowSize";
        public const string DOUYIN_COOKIE = "DouyinProtectedCookie";
        public const string DOUYIN_USER_AGENT = "DouyinUserAgent";
        public const string PREFERRED_QUALITY = "PreferredQuality:";
        public const string BILI_LOGIN_EXPIRED = "BiliLoginExpired";
        public const string DOUYIN_LOGIN_EXPIRED = "DouyinLoginExpired";

        /// <summary>
        /// 新窗口打开直播间
        /// </summary>
        public const string NEW_WINDOW_LIVEROOM = "newWindowLiveRoom";
        /// <summary>
        /// 鼠标功能键返回、关闭页面
        /// </summary>
        public const string MOUSE_BACK = "MouseBack";

        /// <summary>
        /// 视频解码
        /// </summary>
        public const string VIDEO_DECODER = "VideoDecoder";

        /// <summary>
        /// 音量
        /// </summary>
        public const string PLAYER_VOLUME = "PlayerVolume";
        /// <summary>
        /// 亮度
        /// </summary>
        public const string PLAYER_BRIGHTNESS = "PlayeBrightness";

        /// <summary>
        /// 哔哩哔哩Cookie
        /// </summary>
        public const string BILI_COOKIE = "BiliCookie";

        /// <summary>
        /// 哔哩哔哩用户ID
        /// </summary>
        public const string BILI_USER_ID = "BiliUserId";

        /// <summary>
        /// NavigationView导航栏显示模式
        /// </summary>
        public const string PANE_DISPLAY_MODE = "PaneDisplayMode";

        /// <summary>
        /// 忽略哔哩哔哩登录提醒
        /// </summary>
        public const string IGNORE_BILI_LOGIN_TIP = "IgnoreBiliLoginTip";
    }
}
