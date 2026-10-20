using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Microsoft.Web.WebView2.Core;
using YolanLive.Core;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace YolanLive.WinUI;
internal static class PerformanceRegressionChecks
{
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    internal static async Task RunAsync(MainWindow window)
    {
        var report = new List<string>();
        var site = new CatalogSite();
        using (var first = new CancellationTokenSource())
        {
            var a = GameCatalog.GetCategoriesAsync(site, first.Token);
            var b = GameCatalog.GetCategoriesAsync(site, CancellationToken.None);
            first.Cancel();
            try { await a; throw new Exception("Catalog waiter ignored cancellation"); } catch (OperationCanceledException) { }
            Check((await b).Count == 1 && site.Calls == 1, "Canceling one catalog waiter canceled or duplicated the shared request");
        }
        await GameCatalog.GetCategoriesAsync(site, CancellationToken.None);
        Check(site.Calls == 1, "Catalog cache did not hit");
        await GameCatalog.GetCategoriesAsync(site, CancellationToken.None, true);
        Check(site.Calls == 2, "Manual refresh did not bypass cache");
        site.Fail = true;
        try { await GameCatalog.GetCategoriesAsync(site, CancellationToken.None, true); throw new Exception("Refresh failure not surfaced"); } catch (InvalidOperationException) { }
        Check((await GameCatalog.GetCategoriesAsync(site, CancellationToken.None)).Count == 1, "Failure overwrote successful cache");
        site.Fail = false; site.Hold = true;
        using (var last = new CancellationTokenSource(30))
        {
            try { await GameCatalog.GetCategoriesAsync(site, last.Token, true); throw new Exception("Last waiter ignored cancellation"); } catch (OperationCanceledException) { }
            await Task.Delay(50); Check(site.Canceled, "Last waiter did not cancel the underlying catalog request");
        }
        site.Hold = false;
        await GameCatalog.GetCategoriesAsync(site, CancellationToken.None, true);
        report.Add("PASS directory single-flight, independent cancellation, cache hit, force refresh, failed-refresh retention and last-waiter cancellation");
        var avatar = new PersonPicture { Width = 48, Height = 48 };
        DecodedImage.SetUrl(avatar, "ms-appx:///Assets/YolanLive.png");
        var image = new Image { Width = 300, Height = 170 };
        DecodedImage.SetUrl(image, "ms-appx:///Assets/YolanLive.png");
        var content = new StackPanel(); content.Children.Add(image); content.Children.Add(avatar);
        var page = new Page { Content = content };
        window.Root.Content = page; window.Activate(); await Task.Delay(300);
        var scale = image.XamlRoot.RasterizationScale;
        Check(image.Source is BitmapImage bitmap && bitmap.DecodePixelWidth >= image.ActualWidth*scale && bitmap.DecodePixelWidth < image.ActualWidth*scale+32, "Cover decode width ignored actual pixels/DPI: actual=" + image.ActualWidth + ", scale=" + scale + ", loaded=" + image.IsLoaded + ", source=" + image.Source?.GetType().Name + ", decode=" + (image.Source as BitmapImage)?.DecodePixelWidth);
        Check(avatar.ProfilePicture is BitmapImage avatarBitmap && avatarBitmap.DecodePixelWidth >=48*scale, "Avatar decode size ignored DPI");
        image.Width=160; await Task.Delay(150);
        Check(((BitmapImage)image.Source).DecodePixelWidth <= 160*scale+32, "Resizing retained oversized decode");
        int refreshes=0, cancellations=0;
        var status = new PageStatusRefresh(page, () => { refreshes++; return Task.CompletedTask; }, () => cancellations++);
        // Subscribe before the next load, like production pages.
        await Task.Delay(150);
        var presenter=(OverlappedPresenter)window.AppWindow.Presenter;
        presenter.Minimize(); await Task.Delay(200); await status.RefreshAsync();
        Check(refreshes==0 && cancellations>0, "Minimized window continued status work");
        presenter.Restore(); await Task.Delay(200);
        Check(refreshes==1, "Restored window did not refresh status");
        window.Root.Content=null; await Task.Delay(100);
        Check(image.Source==null && avatar.ProfilePicture==null, "Unloaded thumbnails retained their image references");
        report.Add("PASS pixel/DPI decode, resize, recycle, minimize pause and restore refresh");
        var environment=await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(AppPaths.EnsureDirectory(),"BrowserIdleTest"),null);
        CoreWebView2Controller controller=await environment.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)window.Handle));
        controller.IsVisible=false; controller.CoreWebView2.IsMuted=true;
        var navigated = new TaskCompletionSource<bool>();
        controller.CoreWebView2.NavigationCompleted += (_, e) => navigated.TrySetResult(e.IsSuccess);
        controller.CoreWebView2.NavigateToString("<html><body>isolated idle test</body></html>");
        Check(await navigated.Task.WaitAsync(TimeSpan.FromSeconds(10)), "Isolated idle document failed to load");
        var gate=new SemaphoreSlim(1);bool closed=false;
        var policy=new BrowserIdlePolicy(gate,()=>controller,()=>{closed=true;controller.Close();controller=null;});
        try
        {
            using(var activity=policy.Begin()) {await policy.CheckAsync(300);Check(!closed,"Idle cleanup closed active/queued work");}
            await policy.CheckAsync(60); await Task.Delay(100); Check(controller.CoreWebView2.IsSuspended,"Idle browser did not suspend: result=" + policy.LastSuspendResult + ", failure=" + policy.LastSuspendFailure);
            controller.CoreWebView2.Resume();Check(!controller.CoreWebView2.IsSuspended,"Browser did not resume");
            await policy.CheckAsync(300);Check(closed && controller==null,"Idle browser did not close");
        }
        finally {policy.Stop();controller?.Close();gate.Dispose();}
        report.Add("PASS real WebView2 suspend/resume, busy guard and idle close");
        await PerformanceChecks.FavoriteAsync();report.Add("PASS follow/unfollow regression");
        await RandomNavigationAsync(); report.Add("PASS random previous/next, offline skip, unknown preservation and cancellation");
        await GameBrowseChecks.RunAsync();
        BrowseChecks.Pins(); await BrowseChecks.MemoryAsync(); BrowseChecks.SearchStatuses(); await BrowseChecks.SearchAsync();
        report.Add("PASS browse pagination, status filtering, saved preferences, pins and search isolation");
        File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(),"performance-checks.log"),report);
    }
    private static async Task RandomNavigationAsync()
    {
        var site = new CatalogSite();
        LiveRoomItem Room(string id) => new() { RoomID = id, Source = site };
        var session = new RandomBrowseSession(Room("first"));
        Check(!session.CanGoPrevious, "First random room allowed previous");
        session.Commit(new RandomRoomChoice(Room("second"), 1, true));
        session.Commit(new RandomRoomChoice(Room("third"), 2, true));
        site.Offline = "second";
        var previous = await session.FindAsync(false, CancellationToken.None);
        Check(previous?.Room.RoomID == "first", "Random previous did not skip confirmed offline"); session.Commit(previous);
        var next = await session.FindAsync(true, CancellationToken.None);
        Check(next?.Room.RoomID == "third" && !next.IsNew, "Random next did not reuse history"); session.Commit(next);
        site.FailStatus = true;
        try { await session.FindAsync(false, CancellationToken.None); throw new Exception("Unknown status was treated as offline"); } catch (InvalidOperationException) { }
        Check(session.CanGoPrevious, "Failed lookup changed random position");
        site.FailStatus = false; site.Hold = true;
        using var cancel = new CancellationTokenSource(30);
        try { await session.FindAsync(false, cancel.Token); throw new Exception("Random status ignored cancellation"); } catch (OperationCanceledException) { }
        await site.StatusCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Check(site.Canceled, "Random cancellation did not reach status source");
        session.End(); Check(session.Ended && !session.CanGoPrevious, "Random session did not end");
    }
    internal static async Task PlaybackAsync(MainWindow window)
    {
        var report = new List<string>();
        var douyin = (YolanLive.Core.Douyin)ViewModels.MainVM.Sites.Find(s => s.SiteType == LiveSite.Douyin).LiveSite;
        if (douyin.WebRequestAsync == null) { var browser = new DouyinWebRequest(); douyin.WebRequestAsync = browser.GetAsync; douyin.CancellableWebRequestAsync = browser.GetAsync; }
        foreach (var platform in ViewModels.MainVM.Sites)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--controls-only") >= 0) break;
            var stage = "home";
            try
            {
                using var request = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var rooms = await platform.LiveSite.GetRecommendRooms(1, request.Token);
                var candidate = rooms.Rooms.Find(r => r.IsLive == true) ?? throw new InvalidOperationException("No visitor live candidates");
                stage = "room";
                var detail = await platform.LiveSite.GetRoomDetail(candidate.RoomID, request.Token);
                if (!detail.Status) throw new InvalidOperationException("Candidate stopped");
                stage = "quality";
                var qualities = await platform.LiveSite.GetPlayQuality(detail, request.Token);
                stage = "urls";
                var urls = await platform.LiveSite.GetPlayUrls(detail, qualities[0], request.Token);
                stage = "decode";
                bool decoded = false;
                foreach (var url in urls.GetRange(0, Math.Min(2, urls.Count)))
                {
                    try { await PlatformChecks.DecodeAsync(window, url, detail.Url); decoded = true; break; }
                    catch { }
                }
                if (!decoded) throw new InvalidOperationException("Visitor stream did not decode");
                report.Add("PASS " + platform.Label + " visitor data/quality/stream decode");
            }
            catch (Exception error) { report.Add("FAIL " + platform.Label + " " + stage + " " + error.GetType().Name); }
        }
        File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "playback-regression.log"), report);
        await WatchingChecks.WindowPlacementAsync(); report.Add("PASS window placement and mode restore");
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--standalone-player") >= 0) SettingHelper.SetValue(SettingHelper.NEW_WINDOW_LIVEROOM, true);
        var page = new Views.LiveRoomPage(); window.Root.Content = page; window.AppWindow.Show(); window.Activate(); await Task.Delay(300);
        await WatchingChecks.PlayerControlsAsync(window, page);
        window.SetWindowMode(AppWindowPresenterKind.CompactOverlay); await Task.Delay(100);
        Check(window.IsMiniMode && window.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true }, "Borderless topmost mini window did not open");
        window.SetWindowMode(AppWindowPresenterKind.Default); await Task.Delay(100);
        Check(!window.IsMiniMode && window.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: false }, "Mini window did not restore");
        window.Root.Content = null;
        report.Add("PASS player inputs, fullscreen, mini-window and restore");
        File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "playback-regression.log"), report);
        if (report.Exists(line => line.StartsWith("FAIL"))) Environment.ExitCode = 1;
    }
    private sealed class CatalogSite : BiliBili, ILiveSite
    {
        internal int Calls;internal bool Fail,Hold,Canceled;
        internal string Offline; internal bool FailStatus;
        internal readonly TaskCompletionSource StatusCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public new async Task<LiveRoomDetail> GetRoomDetail(object id, CancellationToken cancellationToken)
            => new() { RoomID = id.ToString(), Status = await GetLiveStatus(id, cancellationToken) };
        public new async Task<bool> GetLiveStatus(object id, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Hold ? Timeout.Infinite : 1, cancellationToken); }
            catch (OperationCanceledException) { Canceled = true; StatusCanceled.TrySetResult(); throw; }
            if (FailStatus) throw new InvalidOperationException("Controlled status failure");
            return id?.ToString() != Offline;
        }
        public new Task<List<LiveCategory>> GetCategores()=>GetCategores(CancellationToken.None);
        public new async Task<List<LiveCategory>> GetCategores(CancellationToken cancellationToken)
        {
            Calls++;
            try {await Task.Delay(Hold?Timeout.Infinite:100,cancellationToken);}catch(OperationCanceledException){Canceled=true;throw;}
            if(Fail)throw new InvalidOperationException("Controlled catalog failure");
            return new(){new(){ID="2",Name="游戏",Children=new(){new(){ID="game",ParentID="2",Name="测试游戏"}}}};
        }
    }
}
