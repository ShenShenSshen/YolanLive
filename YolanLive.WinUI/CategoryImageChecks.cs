using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using YolanLive.Core;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
namespace YolanLive.WinUI;
internal static class CategoryImageChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task RunAsync(MainWindow window)
    {
        var report = new List<string>();
        var originalSites = MainVM.Sites; var originalSite = MainVM.BrowseSite;
        using var server = new ImageServer(await CreatePictureAsync());
        try
        {
            var pending = new Image { Width = 100, Height = 100 };
            window.Root.Content = pending;
            window.Activate();
            await UntilAsync(() => pending.IsLoaded, "Picture fixture did not load");
            DecodedImage.SetUrl(pending, server.Url);
            Check(DecodedImage.GetHasPicture(pending), "A valid image source still shows the missing-picture placeholder while loading");
            window.Root.Content = null;
            var dy = new ControlledDouyin();
            var donor = new ControlledBili(token => Task.FromResult(DonorGroups(server.Url)));
            SetSites(dy, donor);
            var page = new Views.CategoryPage();
            window.Root.Content = page;
            ((Microsoft.UI.Windowing.OverlappedPresenter)window.AppWindow.Presenter).IsAlwaysOnTop = true;
            window.AppWindow.Show(); window.Activate();
            var grid = (GridView)page.FindName("GamesGrid");
            await UntilAsync(() => grid.Items.OfType<LiveGameCategory>().Any(g => g.Pic == server.Url), "Same-name artwork did not reach the category card");
            var game = grid.Items.OfType<LiveGameCategory>().Single();
            Check(game.Sources.Single().Site == dy && game.Sources.Single().Category.ID == "1010014,1" && game.Sources.Single().Category.ParentID == "103,4", "Artwork changed the Douyin navigation source");
            page.UpdateLayout(); grid.ScrollIntoView(game); page.UpdateLayout();
            Image image = null;
            await UntilAsync(() =>
            {
                var container = grid.ContainerFromItem(game) as GridViewItem;
                image = FindImage(container);
                return image?.Source is BitmapImage bitmap && bitmap.PixelWidth > 0;
            }, "Category artwork did not receive image dimensions");
            Check(image.Opacity == 1 && image.Visibility == Visibility.Visible, "Decoded artwork remains hidden");
            var renderedRoot = ((GridViewItem)grid.ContainerFromItem(game)).ContentTemplateRoot as FrameworkElement;
            await AssertPictureRenderedAsync(renderedRoot, image);
            await UntilAsync(() => DecodedImage.GetHasPicture(image), "Valid picture source remained marked missing after rendered pixel assertion");
            await UntilAsync(() => image.Parent is Grid host && host.Children[0].Visibility == Visibility.Collapsed,
                "Decoded artwork still shows its placeholder");
            Check(donor.Calls == 1 && server.Requests > 0, "The isolated artwork source was not exercised");
            report.Add("PASS actual category card: same-name fallback, original Douyin navigation, HTTP image decode and rendered pixels");
            window.Root.Content = null;
            await UntilAsync(() => image.Source == null && !DecodedImage.GetHasPicture(image), "Unloaded image retained decoded source/readiness");
            window.Root.Content = page;
            await UntilAsync(() => image.Source is BitmapImage bitmap && bitmap.PixelWidth > 0, "Returning page did not reload its artwork");
            await AssertPictureRenderedAsync(renderedRoot, image);
            await UntilAsync(() => DecodedImage.GetHasPicture(image), "Returning image remained marked missing");
            Check(donor.Calls == 1, "Returning page repeated the artwork directory request");
            report.Add("PASS page return: released image reloads and renders; successful donor directory reused");
            window.Root.Content = null;
            await Task.Delay(50);
            await CheckCachedNavigationAsync(window, server.Url, report);
            await CheckCancelledCompletionAsync(server.Url, report);
            await CheckDonorFailureAsync(server.Url, report);
            var blank = new Image { Width = 100, Height = 100 };
            window.Root.Content = blank;
            await UntilAsync(() => blank.IsLoaded, "Blank-picture control did not load");
            var rejectedBlank = false;
            try { await AssertPictureRenderedAsync(blank, blank, 250); }
            catch (Exception error) when (error.Message.StartsWith("Category shows a placeholder/blank", StringComparison.Ordinal)) { rejectedBlank = true; }
            Check(rejectedBlank, "Rendered-pixel validation accepted a permanently blank image");
            report.Add("PASS negative control: permanently blank artwork is rejected by the rendered-pixel deadline");
        }
        finally
        {
            window.Root.Content = null; await Task.Delay(50);
            MainVM.Sites = originalSites; MainVM.BrowseSite = originalSite;
            File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "category-image-checks.log"), report);
        }
    }
    private static async Task CheckCachedNavigationAsync(MainWindow window, string picture, List<string> report)
    {
        // Exceed the virtualized panel cache even when all seven columns fit.
        const int categoryCount = 240;
        var donor = new ControlledBili(_ => Task.FromResult(new List<LiveCategory>
        {
            new() { ID = "2", Name = "网游", Children = Enumerable.Range(0, categoryCount).Select(i =>
                new LiveSubCategory { ID = "return-" + i, ParentID = "2", Name = "返回游戏 " + i.ToString("D2"), Pic = picture }).ToList() }
        }));
        SetSites(new ControlledDouyin(), donor);
        MainVM.BrowseSite = MainVM.Sites[0];
        var frame = new Frame();
        window.Root.Content = frame;
        Check(frame.Navigate(typeof(Views.CategoryPage)), "Category navigation failed");
        await UntilAsync(() => frame.Content is Views.CategoryPage current && current.IsLoaded, "Navigated category did not load");
        var page = (Views.CategoryPage)frame.Content;
        var grid = (GridView)page.FindName("GamesGrid");
        await UntilAsync(() => grid.Items.Count == categoryCount, "Navigation fixture did not load its game names");
        var first = grid.Items.OfType<LiveGameCategory>().First();
        async Task CheckFirstPictureAsync()
        {
            grid.ScrollIntoView(first);
            page.UpdateLayout();
            Image image = null;
            await UntilAsync(() =>
            {
                image = FindImage(grid.ContainerFromItem(first) as GridViewItem);
                return image?.Source is BitmapImage bitmap && bitmap.PixelWidth > 0;
            }, "Cached category kept its game names but did not restore the game artwork");
            await AssertPictureRenderedAsync((FrameworkElement)((GridViewItem)grid.ContainerFromItem(first)).ContentTemplateRoot, image);
            Check(DecodedImage.GetHasPicture(image), "Returned game still shows its missing-picture placeholder");
        }
        var recycled = 0;
        void Recycled(ListViewBase sender, ContainerContentChangingEventArgs args) { if (args.InRecycleQueue) recycled++; }
        grid.ContainerContentChanging += Recycled;
        try
        {
            await CheckFirstPictureAsync();
            foreach (var scrollAway in new[] { false, true })
            {
                if (scrollAway)
                {
                    var last = grid.Items.OfType<LiveGameCategory>().Last();
                    grid.ScrollIntoView(last);
                    await UntilAsync(() => grid.ContainerFromItem(last) != null && recycled > 0, "Scrolling did not exercise actual container recycling");
                }
                Check(frame.Navigate(typeof(Page)), "Navigation away failed");
                await UntilAsync(() => !page.IsLoaded, "Category did not unload during navigation");
                Check(frame.Navigate(typeof(Views.CategoryPage)), "Navigation back failed");
                await UntilAsync(() => page.IsLoaded, "Cached category did not load on return");
                Check(ReferenceEquals(frame.Content, page) && grid.Items.Count == categoryCount, "Cached category or game names changed on return");
                await CheckFirstPictureAsync();
                report.Add(scrollAway ? "PASS recycled category navigation: game artwork restores after scrolling, leaving and returning" :
                    "PASS cached Frame navigation: game names and rendered artwork survive leaving and returning");
            }
        }
        finally
        {
            grid.ContainerContentChanging -= Recycled;
            window.Root.Content = null;
            await Task.Delay(50);
        }
    }
    private static async Task CheckCancelledCompletionAsync(string picture, List<string> report)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var donor = new ControlledBili(async token =>
        {
            if (++attempts == 1)
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            }
            return DonorGroups(picture);
        });
        SetSites(new ControlledDouyin(), donor);
        var vm = new CategoryVM();
        await vm.LoadAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Check(string.IsNullOrEmpty(vm.Items.Single().Pic), "Pending artwork unexpectedly replaced placeholder");
        vm.CancelPending();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Check(string.IsNullOrEmpty(vm.Items.Single().Pic), "Cancelled completion altered the departed page");
        await vm.CompletePicturesAsync();
        Check(vm.Items.Single().Pic == picture && donor.Calls == 2, "Returning page did not retry cancelled artwork completion");
        vm.CancelPending();
        report.Add("PASS cancellation/return: underlying donor request cancelled, no stale artwork, return retries successfully");
    }
    private static async Task CheckDonorFailureAsync(string picture, List<string> report)
    {
        var failing = new ControlledBili(_ => Task.FromException<List<LiveCategory>>(new IOException("controlled source unavailable")));
        var fallback = new ControlledBili(_ => Task.FromResult(DonorGroups(picture)));
        SetSites(new ControlledDouyin(), failing, fallback);
        var vm = new CategoryVM();
        await vm.LoadAsync();
        await UntilAsync(() => vm.Items.SingleOrDefault()?.Pic == picture, "A failed donor prevented fallback to the next source");
        Check(failing.Calls == 1 && fallback.Calls == 1 && vm.Items.Single().Sources.Single().Category.ID == "1010014,1",
            "Fallback repeated requests or changed the original partition");
        vm.CancelPending();
        report.Add("PASS donor failure: next platform supplies matching artwork, Douyin partition unchanged");
    }
    private static void SetSites(ControlledDouyin dy, ControlledBili donor, ControlledBili other = null)
    {
        other ??= new ControlledBili(_ => Task.FromResult(new List<LiveCategory> { new() { ID = "2", Name = "网游", Children = new() { new() { ID = "other", Name = "其他游戏" } } } }));
        MainVM.Sites = new()
        {
            new Site { SiteType = LiveSite.Bilibili, Name = "哔哩哔哩直播", LiveSite = donor },
            new Site { SiteType = LiveSite.Douyin, Name = "抖音直播", LiveSite = dy },
            new Site { SiteType = LiveSite.Huya, Name = "虎牙直播", LiveSite = other },
            new Site { SiteType = LiveSite.Douyu, Name = "斗鱼直播", LiveSite = other }
        };
        MainVM.BrowseSite = MainVM.Sites[1];
    }
    private static List<LiveCategory> DonorGroups(string picture) => new()
    {
        new() { ID = "2", Name = "网游", Children = new() { new() { ID = "86", ParentID = "2", Name = "英雄联盟", Pic = picture } } }
    };
    private static async Task UntilAsync(Func<bool> predicate, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new Exception(message);
            await Task.Delay(25);
        }
    }
    private static Image FindImage(DependencyObject root)
    {
        if (root == null) return null;
        if (root is Image image && image.Name == "GameImage") return image;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindImage(VisualTreeHelper.GetChild(root, index));
            if (found != null) return found;
        }
        return null;
    }
    private static async Task AssertPictureRenderedAsync(FrameworkElement root, Image image, int timeoutMilliseconds = 8000)
    {
        // Bitmap dimensions can precede a rendered frame. Sample real pixels with a
        // fresh capture until ready; a missing/wrong picture still fails at the deadline.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        byte[] sample = Array.Empty<byte>();
        do
        {
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(root);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0 && root.ActualWidth > 0 && root.ActualHeight > 0)
            {
                var point = image.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(image.ActualWidth / 2, image.ActualHeight / 2));
                var x = Math.Clamp((int)(point.X * bitmap.PixelWidth / root.ActualWidth), 0, bitmap.PixelWidth - 1);
                var y = Math.Clamp((int)(point.Y * bitmap.PixelHeight / root.ActualHeight), 0, bitmap.PixelHeight - 1);
                var offset = (y * bitmap.PixelWidth + x) * 4;
                sample = pixels.Skip(offset).Take(4).ToArray();
                if (sample.Length == 4 && Math.Abs(sample[0] - 41) <= 10 && Math.Abs(sample[1] - 139) <= 10 && Math.Abs(sample[2] - 233) <= 10 && sample[3] >= 240)
                    return;
            }
            await Task.Delay(25);
        } while (DateTime.UtcNow < deadline);
        throw new Exception("Category shows a placeholder/blank instead of the decoded picture: BGRA=" + string.Join(",", sample));
    }
    private static async Task<byte[]> CreatePictureAsync()
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[16 * 16 * 4];
        for (var index = 0; index < pixels.Length; index += 4) { pixels[index] = 41; pixels[index + 1] = 139; pixels[index + 2] = 233; pixels[index + 3] = 255; }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 16, 16, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size); var bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes); return bytes;
    }
    private sealed class ControlledDouyin : Douyin, ILiveSite
    {
        public new Task<List<LiveCategory>> GetCategores() => GetCategores(CancellationToken.None);
        public new Task<List<LiveCategory>> GetCategores(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new List<LiveCategory> { new() { ID = "103,4", Name = "游戏", Children = new() { new() { ID = "1010014,1", ParentID = "103,4", Name = "英雄联盟" } } } });
        }
    }
    private sealed class ControlledBili : BiliBili, ILiveSite
    {
        private readonly Func<CancellationToken, Task<List<LiveCategory>>> get;
        internal int Calls;
        internal ControlledBili(Func<CancellationToken, Task<List<LiveCategory>>> get) => this.get = get;
        public new Task<List<LiveCategory>> GetCategores() => GetCategores(CancellationToken.None);
        public new Task<List<LiveCategory>> GetCategores(CancellationToken cancellationToken) { Calls++; return get(cancellationToken); }
    }
    private sealed class ImageServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly byte[] picture;
        internal int Requests;
        internal string Url { get; }
        internal ImageServer(byte[] picture)
        {
            this.picture = picture; listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/cover.png";
            _ = AcceptAsync();
        }
        private async Task AcceptAsync()
        {
            try { while (!stop.IsCancellationRequested) _ = ServeAsync(await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false)); }
            catch (OperationCanceledException) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            try
            {
                var stream = client.GetStream(); var buffer = new byte[8192]; var count = 0;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                while (count < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token).ConfigureAwait(false);
                    if (read == 0) return;
                    count += read;
                    if (Encoding.ASCII.GetString(buffer, 0, count).Contains("\r\n\r\n")) break;
                }
                Interlocked.Increment(ref Requests);
                var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nCache-Control: no-store\r\nContent-Length: " + picture.Length + "\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, timeout.Token).ConfigureAwait(false);
                await stream.WriteAsync(picture, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException || error is OperationCanceledException || error is SocketException) { }
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); stop.Dispose(); }
    }
}
