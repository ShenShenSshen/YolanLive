using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YolanLive.Core.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace YolanLive.WinUI;
internal static class CategoryRepro
{
    private static readonly string Report = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayWin", "category-repro.log");
    private static int errors;
    public static void RecordError(Exception exception)
    {
        errors++;
        Environment.ExitCode = 1;
        File.AppendAllText(Report, "ERROR " + exception + Environment.NewLine);
    }
    public static async Task RunAsync(MainWindow window)
    {
        var browserHasSession = false;
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, "START category navigation repro\n");
        window.Activate();
        if (Environment.GetCommandLineArgs().Contains("--use-saved-login"))
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayWin", "settings.json");
            var saved = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(settingsPath));
            foreach (var key in new[] { Helper.SettingHelper.THEME, Helper.SettingHelper.WINDOW_SIZE, Helper.SettingHelper.PANE_DISPLAY_MODE,
                Helper.SettingHelper.BILI_COOKIE, Helper.SettingHelper.BILI_USER_ID, Helper.SettingHelper.DOUYIN_COOKIE, Helper.SettingHelper.DOUYIN_USER_AGENT })
                if (saved[key] != null) Helper.SettingHelper.SetValue(key, saved[key]);
            window.ApplyWindowSize(MainWindow.GetSavedWindowSize());
            window.Root.RequestedTheme = (ElementTheme)Helper.SettingHelper.GetValue(Helper.SettingHelper.THEME, 0);
            await Helper.DouyinAccount.Instance.InitLoginInfo();
            File.AppendAllText(Report, "Saved Douyin session loaded=" + Helper.DouyinAccount.Instance.Logined + "\n");
        }
        if (Environment.GetCommandLineArgs().Contains("--use-browser-login"))
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayWin", "DouyinWebView");
            var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(null, profile, null);
            var controller = await environment.CreateCoreWebView2ControllerAsync(Microsoft.Web.WebView2.Core.CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)window.Handle));
            try
            {
                controller.IsVisible = false;
                controller.CoreWebView2.IsMuted = true;
                var cookies = await controller.CoreWebView2.CookieManager.GetCookiesAsync("https://live.douyin.com/");
                var cookie = string.Join("; ", cookies.GroupBy(c => c.Name).Select(c => c.First()).Select(c => c.Name + "=" + c.Value));
                browserHasSession = Helper.DouyinAccount.HasSession(cookie);
                File.AppendAllText(Report, "Browser Douyin session exists=" + browserHasSession + "\n");
                if (browserHasSession)
                {
                    var site = (YolanLive.Core.Douyin)ViewModels.MainVM.Sites.Single(s => s.LiveSite is YolanLive.Core.Douyin).LiveSite;
                    site.Cookie = cookie;
                    site.UserAgent = controller.CoreWebView2.Settings.UserAgent;
                }
                if (Environment.GetCommandLineArgs().Contains("--inspect-official-browser"))
                {
                    controller.CoreWebView2.WebResourceResponseReceived += async (_, args) =>
                    {
                        var uri = new Uri(args.Request.Uri);
                        if (uri.Host != "live.douyin.com" || !uri.AbsolutePath.Contains("/partition/detail/room/")) return;
                        try
                        {
                            var content = await args.Response.GetContentAsync();
                            using var reader = new StreamReader(content.AsStreamForRead());
                            var text = await reader.ReadToEndAsync();
                            var queryKeys = string.Join(",", uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')[0]));
                            File.AppendAllText(Report, "Official game request: HTTP=" + args.Response.StatusCode + ", bodyLength=" + text.Length + ", queryKeys=" + queryKeys + "\n");
                        }
                        catch (Exception ex) { File.AppendAllText(Report, "Official response read failed: " + ex.GetType().Name + "\n"); }
                    };
                    var loaded = new TaskCompletionSource<bool>();
                    controller.CoreWebView2.NavigationCompleted += (_, args) => loaded.TrySetResult(args.IsSuccess);
                    controller.CoreWebView2.Navigate("https://live.douyin.com/");
                    var success = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    await Task.Delay(5000);
                    var info = await controller.CoreWebView2.ExecuteScriptAsync("JSON.stringify({frontier:typeof window.byted_acrawler,gameLinks:Array.from(document.querySelectorAll('a')).filter(a=>a.textContent.trim()==='游戏').map(a=>new URL(a.href).pathname)})");
                    File.AppendAllText(Report, "Official page loaded=" + success + ", SDK and game links=" + info + "\n");
                    controller.CoreWebView2.Navigate("https://live.douyin.com/categorynew/4_103");
                    await Task.Delay(10000);
                    var gameInfo = await controller.CoreWebView2.ExecuteScriptAsync("JSON.stringify({sdkKeys:Object.keys(window.byted_acrawler||{}),roomLinks:Array.from(document.querySelectorAll('a')).filter(a=>/^https:\\/\\/live\\.douyin\\.com\\/\\d+/.test(a.href)).length})");
                    File.AppendAllText(Report, "Official game page=" + gameInfo + "\n");
                    var sdkInfo = await controller.CoreWebView2.ExecuteScriptAsync("JSON.stringify({signature:window.byted_acrawler.frontierSign.toString().slice(0,800)})");
                    File.AppendAllText(Report, "Official SDK function=" + sdkInfo + "\n");
                    await controller.CoreWebView2.ExecuteScriptAsync("window.__YolanLiveProbe=null;fetch('/webcast/web/partition/detail/room/v2/?aid=6383&app_name=douyin_web&live_id=1&device_platform=web&language=zh-CN&cookie_enabled=true&screen_width=1980&screen_height=1080&browser_language=zh-CN&browser_platform=Win32&browser_name=Edge&browser_version=125.0.0.0&count=15&offset=0&partition=1&partition_type=1&req_from=2').then(r=>r.text()).then(t=>{try{let j=JSON.parse(t);window.__YolanLiveProbe={length:t.length,rooms:j.data?.data?.length,status:j.status_code}}catch{window.__YolanLiveProbe={length:t.length}}}).catch(e=>window.__YolanLiveProbe={error:e.name})");
                    await Task.Delay(5000);
                    var fetchInfo = await controller.CoreWebView2.ExecuteScriptAsync("JSON.stringify(window.__YolanLiveProbe)");
                    File.AppendAllText(Report, "Official SDK fetch=" + fetchInfo + "\n");
                }
            }
            finally { controller.Close(); }
        }
        if (Helper.DouyinAccount.Instance.Logined || browserHasSession || Environment.GetCommandLineArgs().Contains("--live-douyin-api"))
        {
            try
            {
                var dy = ViewModels.MainVM.Sites.Single(s => s.LiveSite is YolanLive.Core.Douyin).LiveSite;
                var categories = await dy.GetCategores().WaitAsync(TimeSpan.FromSeconds(10));
                var games = YolanLive.Core.Helper.GameBrowse.SelectCategories(dy, categories).SelectMany(c => c.Children).Take(3);
                foreach (var game in games)
                {
                    try
                    {
                        var rooms = await dy.GetCategoryRooms(game).WaitAsync(TimeSpan.FromSeconds(20));
                        File.AppendAllText(Report, "Douyin result count=" + rooms.Rooms.Count + ", confirmedLive=" + rooms.Rooms.Count(r => r.IsLive == true) + "\n");
                        if (rooms.Rooms.Count == 0 || rooms.Rooms.Any(r => r.IsLive != true)) throw new InvalidOperationException("没有确认开播的游戏房间");
                        File.AppendAllText(Report, "Signed Douyin " + game.Name + " rooms=" + rooms.Rooms.Count + "\n");
                        if (game.ID == "103,4" && Environment.GetCommandLineArgs().Contains("--check-background-audio"))
                        {
                            var target = ((YolanLive.Core.Douyin)dy).WebRequestAsync.Target;
                            var controllerField = typeof(Helper.DouyinWebRequest).GetField("controller", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var controller = (Microsoft.Web.WebView2.Core.CoreWebView2Controller)controllerField.GetValue(target);
                            File.AppendAllText(Report, "Background browser muted=" + controller.CoreWebView2.IsMuted + ", documentPlayingAudio=" + controller.CoreWebView2.IsDocumentPlayingAudio + "\n");
                            if (!controller.CoreWebView2.IsMuted) throw new InvalidOperationException("未进入直播间的后台浏览器允许输出声音");
                            if (controller.CoreWebView2.IsDocumentPlayingAudio) throw new InvalidOperationException("隐藏页面仍在播放直播，只做了静音");
                            if (await controller.CoreWebView2.ExecuteScriptAsync("Array.from(document.querySelectorAll('video,audio')).some(media=>!media.paused)") != "false")
                                throw new InvalidOperationException("隐藏页面仍有播放中的媒体元素");
                            await controller.CoreWebView2.ExecuteScriptAsync("window.__YolanLiveMediaStatus=null;fetch('/YolanLive-background-media-check.flv').then(()=>window.__YolanLiveMediaStatus='allowed').catch(e=>window.__YolanLiveMediaStatus=e.name)");
                            for (var wait = 0; wait < 30 && await controller.CoreWebView2.ExecuteScriptAsync("window.__YolanLiveMediaStatus") == "null"; wait++) await Task.Delay(100);
                            if (await controller.CoreWebView2.ExecuteScriptAsync("window.__YolanLiveMediaStatus") != "\"NotAllowedError\"") throw new InvalidOperationException("隐藏页面未拦截视频下载");
                            if (await controller.CoreWebView2.ExecuteScriptAsync("(()=>{try{new XMLHttpRequest().open('GET','/YolanLive-background-media-check.flv');return false;}catch(e){return e.name==='NotAllowedError';}})()") != "true") throw new InvalidOperationException("隐藏页面未拦截 XHR 视频下载");
                            File.AppendAllText(Report, "PASS background media stopped and stream downloads blocked\n");
                        }
                        if (game.ID == "103,4")
                        {
                            var next = await dy.GetCategoryRooms(game, 2).WaitAsync(TimeSpan.FromSeconds(20));
                            if (!next.Rooms.Any(r => rooms.Rooms.All(first => first.RoomID != r.RoomID))) throw new InvalidOperationException("分页没有新房间");
                            var detail = await dy.GetRoomDetail(rooms.Rooms[0].RoomID).WaitAsync(TimeSpan.FromSeconds(15));
                            var qualities = await dy.GetPlayQuality(detail);
                            var urls = await dy.GetPlayUrls(detail, qualities.First());
                            if (!detail.Status || urls.Count == 0) throw new InvalidOperationException("房间未返回播放地址");
                            File.AppendAllText(Report, "PASS Douyin pagination and playable room: qualities=" + qualities.Count + ", urls=" + urls.Count + "\n");
                        }
                    }
                    catch (Exception ex) { errors++; File.AppendAllText(Report, "Signed Douyin " + game.Name + " failed: " + ex.Message + "\n"); }
                }
                File.AppendAllText(Report, "Official browser request adapter enabled=" + (((YolanLive.Core.Douyin)dy).WebRequestAsync != null) + "\n");
            }
            catch (Exception ex) { errors++; File.AppendAllText(Report, "Signed Douyin request failed: " + ex.Message + "\n"); }
        }
        window.Root.Navigate(typeof(BaseFramePage));
        window.Activate();
        await Task.Delay(2000);
        var shell = (BaseFramePage)window.Root.Content;
        var main = (MainPage)((Frame)shell.FindName("MainFrame")).Content;
        var navigation = (NavigationView)main.FindName("navigationView");
        var categoryItem = navigation.MenuItems.OfType<NavigationViewItem>().Single(i => (string)i.Tag == "CategoryPage");
        File.AppendAllText(Report, "CLICK category\n");
        navigation.SelectedItem = categoryItem;
        var content = (Frame)main.FindName("frame");
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (content.Content is Views.CategoryPage page)
            {
                var grid = ((Grid)page.Content).Children.OfType<GridView>().First();
                if (grid.Items.Count > 0)
                {
                    File.AppendAllText(Report, "LOADED categories=" + grid.Items.Count + "\n");
                    var picker = (ListView)page.FindName("PlatformPicker");
                    picker.SelectedItem = picker.Items.Single(i => ((ViewModels.Site)i).LiveSite is YolanLive.Core.Douyin);
                    for (var wait = 0; wait < 150 && !grid.Items.Cast<LiveGameCategory>().Any(c => c.Name == "射击游戏"); wait++) await Task.Delay(100);
                    if (!grid.Items.Cast<LiveGameCategory>().Any(c => c.Name == "射击游戏") || grid.Items.Cast<LiveGameCategory>().Any(c => c.Sources.Any(s => s.Site is not YolanLive.Core.Douyin)))
                        throw new InvalidOperationException("抖音游戏分类没有正确恢复");
                    File.AppendAllText(Report, "PASS platform switch: Douyin categories=" + grid.Items.Count + "\n");
                    picker.SelectedItem = picker.Items.Single(i => ((ViewModels.Site)i).LiveSite is YolanLive.Core.BiliBili);
                    for (var wait = 0; wait < 150 && !grid.Items.Cast<LiveGameCategory>().Any(c => c.Name == "英雄联盟"); wait++) await Task.Delay(100);
                    if (grid.Items.Count == 0 || grid.Items.Cast<LiveGameCategory>().Any(c => c.Sources.Any(s => s.Site is not YolanLive.Core.BiliBili))) throw new InvalidOperationException("平台切换后分类混入其他来源");
                    var category = grid.Items.Cast<LiveGameCategory>().FirstOrDefault(c => c.Name == "英雄联盟") ?? (LiveGameCategory)grid.Items[0];
                    File.AppendAllText(Report, "CLICK game " + category.Name + "\n");
                    Helper.MessageCenter.NavigatePage(typeof(Views.CategoryDetailPage), category);
                    await Task.Delay(4000);
                    for (var cycle = 0; cycle < 5; cycle++)
                    {
                        var outer = (Frame)shell.FindName("MainFrame");
                        if (outer.CanGoBack) outer.GoBack();
                        await Task.Delay(100);
                        navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == "RecomendPage");
                        await Task.Delay(100);
                        navigation.SelectedItem = categoryItem;
                        await Task.Delay(100);
                    }
                    await Task.Delay(3000);
                    File.AppendAllText(Report, errors == 0 ? "PASS category navigation\n" : "FAIL UI exceptions=" + errors + "\n");
                    Environment.ExitCode = errors == 0 ? 0 : 1;
                    return;
                }
            }
            await Task.Delay(100);
        }
        File.AppendAllText(Report, "FAIL category did not load\n");
        Environment.ExitCode = 1;
    }
}
