using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using YolanLive.Core;
using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace YolanLive.WinUI;

// Runs the actual navigation, room-loading, FFmpeg and window paths with an external
// platform substitute and the same local H.264 file. Reflection observes timer/cursor diagnostics only.
internal static class PlayerPerformanceChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task UntilAsync(Func<bool> ready, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(message); await Task.Delay(50); }
    }
    internal static async Task<string> MediaAsync(string path = null)
    {
        path ??= Environment.GetEnvironmentVariable("YOLANLIVE_BENCH_MEDIA");
        Check(!string.IsNullOrWhiteSpace(path) && Path.GetFullPath(path).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Benchmark media must stay under the isolated D-drive temp root");
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path));
        var file = await folder.CreateFileAsync(Path.GetFileName(path), CreationCollisionOption.ReplaceExisting);
        var composition = new MediaComposition();
        foreach (var color in new[] { Microsoft.UI.Colors.SteelBlue, Microsoft.UI.Colors.SeaGreen, Microsoft.UI.Colors.DarkSlateBlue })
            composition.Clips.Add(MediaClip.CreateFromColor(color, TimeSpan.FromSeconds(Environment.GetCommandLineArgs().Contains("--player-recovery-check") ? 15 : 3)));
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.FrameRate.Numerator = 30; profile.Video.FrameRate.Denominator = 1;
        Check(await composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, profile) == TranscodeFailureReason.None, "Failed to generate local H.264 fixture");
        return path;
    }
    private static void Click(Control control)
    {
        if (control is ToggleMenuFlyoutItem toggle) new ToggleMenuFlyoutItemAutomationPeer(toggle).Toggle();
        else if (control is MenuFlyoutItem menu) new MenuFlyoutItemAutomationPeer(menu).Invoke();
        else new ButtonAutomationPeer((Button)control).Invoke();
    }
    private static MediaPlayer Media(Views.LiveRoomPage page) => ((MediaPlayerElement)page.FindName("player")).MediaPlayer;
    private static Control Button(Views.LiveRoomPage page, string name) => (Control)page.FindName(name);
    private sealed class Observation
    {
        internal int ControlTicks, HoverTicks, CursorChanges;
        internal Observation(Views.LiveRoomPage page)
        {
            var cursor = typeof(UIElement).GetProperty("ProtectedCursor", Private);
            var previous = cursor.GetValue(page);
            ((DispatcherTimer)page.GetType().GetField("controlTimer", Private).GetValue(page)).Tick += (_, _) => ControlTicks++;
            ((DispatcherTimer)page.GetType().GetField("miniHoverTimer", Private).GetValue(page)).Tick += (_, _) =>
            {
                HoverTicks++;
                var current = cursor.GetValue(page);
                if (!ReferenceEquals(current, previous)) CursorChanges++;
                previous = current;
            };
        }
    }
    internal static async Task RunAsync(MainWindow window)
    {
        var results = new List<object>(); var failures = new List<string>();
        var originalSite = MainVM.Sites[0].LiveSite;
        var quick = Environment.GetCommandLineArgs().Contains("--player-quick");
        var seconds = quick ? 3 : 60;
        var process = Process.GetCurrentProcess();
        void Save() => File.WriteAllText(Path.Combine(AppPaths.EnsureDirectory(), "player-performance.json"), JsonConvert.SerializeObject(new { secondsPerPhase = seconds, input = "local 1280x720 H.264 30fps solid-color sequence, automatic decoder, muted", results, failures }, Formatting.Indented));
        try
        {
            await DatabaseHelper.InitializeDatabase();
            using var server = new LocalMediaServer(await MediaAsync());
            var site = new LocalSite(server.Url); MainVM.Sites[0].LiveSite = site;
            var first = new LiveRoomItem { RoomID = "local-first", Source = site, IsLive = true };
            var second = new LiveRoomItem { RoomID = "local-second", Source = site, IsLive = true };
            var session = new RandomBrowseSession(first);
            session.Commit(new RandomRoomChoice(second, 1, true)); session.Commit(new RandomRoomChoice(first, 0, false));
            window.ApplyWindowSize("1280×720"); window.AppWindow.Move(new Windows.Graphics.PointInt32(40, 40));
            var shell = new BaseFramePage(); window.Root.Content = shell; window.Activate();
            await UntilAsync(() => shell.IsLoaded, "Playback shell did not load");
            var frame = (Frame)shell.FindName("MainFrame");
            frame.Navigate(typeof(Views.LiveRoomPage), new PageArgs { Site = site, Data = first, RandomSession = session });
            var page = (Views.LiveRoomPage)frame.Content;
            await UntilAsync(() => Media(page)?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing && Media(page).PlaybackSession.NaturalVideoWidth == 1280, "Actual FFmpeg video did not begin playback");
            Media(page).IsLoopingEnabled = true;
            var observation = new Observation(page);
            if (Environment.GetCommandLineArgs().Contains("--player-recovery-check"))
            {
                using var broken = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(server.Url.Replace("/720p30.mp4", "/unavailable")));
                var beforeFailure = Media(page).Source;
                Media(page).Source = broken;
                await UntilAsync(() => !ReferenceEquals(beforeFailure, Media(page).Source) && Media(page).Source is Windows.Media.Playback.MediaPlaybackItem && Media(page).PlaybackSession.PlaybackState == MediaPlaybackState.Playing, "Native media failure did not recover through the existing player");
                var beforeRecovery = observation.ControlTicks;
                ((OverlappedPresenter)window.AppWindow.Presenter).Minimize();
                await Task.Delay(3000);
                Check(observation.ControlTicks > beforeRecovery, "Minimizing lost the healthy recovery-budget deadline");
                Check(observation.HoverTicks == 0, "Recovery in an ordinary minimized window started hover polling");
                await Task.Delay(31000);
                var settled = observation.ControlTicks; await Task.Delay(3000);
                Check(observation.ControlTicks == settled, "Recovery tracking did not stop after thirty healthy seconds");
                Check(Media(page).PlaybackSession.PlaybackState == MediaPlaybackState.Playing, "Minimized recovery stopped video playback");
                ((OverlappedPresenter)window.AppWindow.Presenter).Restore();
                results.Add(new { phase = "minimized-recovery-deadline", passed = true, recoveryTicks = settled - beforeRecovery, hoverTicks = observation.HoverTicks }); Save(); return;
            }
            async Task PhaseAsync(string name)
            {
                if (Environment.GetCommandLineArgs().Contains("--player-lifecycle-only")) return;
                await Task.Delay(500);
                var ticks = observation.ControlTicks; var hover = observation.HoverTicks; var cursors = observation.CursorChanges;
                process.Refresh(); var cpu = process.TotalProcessorTime; var allocations = GC.GetTotalAllocatedBytes();
                var samples = new List<object>(); var started = Stopwatch.StartNew();
                while (started.Elapsed.TotalSeconds < seconds)
                {
                    await Task.Delay(1000); process.Refresh();
                    samples.Add(new { atSeconds = Math.Round(started.Elapsed.TotalSeconds, 1), privateMB = Math.Round(process.PrivateMemorySize64 / 1048576d, 2), workingMB = Math.Round(process.WorkingSet64 / 1048576d, 2), threads = process.Threads.Count, handles = process.HandleCount });
                }
                var control = observation.ControlTicks - ticks; var polls = observation.HoverTicks - hover; var changes = observation.CursorChanges - cursors;
                results.Add(new { phase = name, elapsedSeconds = started.Elapsed.TotalSeconds, cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, allocatedBytes = GC.GetTotalAllocatedBytes() - allocations, controlTicks = control, hoverTicks = polls, cursorChanges = changes, playing = Media(page).PlaybackSession.PlaybackState == MediaPlaybackState.Playing, width = Media(page).PlaybackSession.NaturalVideoWidth, height = Media(page).PlaybackSession.NaturalVideoHeight, samples });
                if (Media(page).PlaybackSession.PlaybackState != MediaPlaybackState.Playing || Media(page).PlaybackSession.NaturalVideoWidth != 1280 || Media(page).PlaybackSession.NaturalVideoHeight != 720)
                    failures.Add(name + ": video stopped or lost its decoded dimensions");
                if (name == "ordinary" && control != 0) failures.Add("Ordinary playback keeps waking the toolbar timer without a pending hide or recovery");
                if (name == "minimized" && (control != 0 || polls != 0)) failures.Add("Minimized playback keeps polling invisible controls");
                if ((name == "mini" || name == "window-full") && changes > 1) failures.Add("Unchanged external toolbar repeatedly replaces its cursor");
                Save();
            }
            await PhaseAsync("ordinary");
            Click(Button(page, "PlayBtnFullWindow")); await Task.Delay(300); await PhaseAsync("window-full");
            Click(Button(page, "PlayBtnExitFullWindow")); await Task.Delay(300);
            window.SetWindowMode(AppWindowPresenterKind.CompactOverlay); await PhaseAsync("mini");
            var mini = (OverlappedPresenter)window.AppWindow.Presenter; mini.Minimize(); await Task.Delay(300); await PhaseAsync("minimized");
            mini.Restore(); window.SetWindowMode(AppWindowPresenterKind.Default); await Task.Delay(300);
            Click(Button(page, "PlayBtnFullScreen")); await Task.Delay(300); await PhaseAsync("full-screen");
            Click(Button(page, "PlayBtnExitFullScreen")); await Task.Delay(300);
            var latencies = new List<double>();
            for (var index = 0; index < (quick ? 4 : 24); index++)
            {
                var previous = Media(page).Source; var watch = Stopwatch.StartNew();
                Click(Button(page, index % 2 == 0 ? "RandomNext" : "RandomPrevious"));
                await UntilAsync(() => !ReferenceEquals(previous, Media(page).Source) && Media(page).PlaybackSession.PlaybackState == MediaPlaybackState.Playing && Button(page, "RandomNext").IsEnabled, "Random room switch did not complete through the actual player");
                Media(page).IsLoopingEnabled = true; latencies.Add(watch.Elapsed.TotalMilliseconds);
            }
            results.Add(new { phase = "same-player-room-switches", count = latencies.Count, milliseconds = latencies }); Save();
            var released = new List<WeakReference> { new(page) };
            frame.Navigate(typeof(Page)); await UntilAsync(() => !page.IsLoaded, "Old playback page did not unload"); page = null;
            for (var index = 0; index < (quick ? 3 : 12); index++)
                released.Add(await OpenAndLeaveAsync(frame, site, first));
            frame.BackStack.Clear(); await Task.Delay(1500);
            process.Refresh(); var beforeCollection = process.PrivateMemorySize64 / 1048576d;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Task.Delay(500);
            process.Refresh();
            results.Add(new { phase = "page-release", openedPages = released.Count, survivingPagesAfterDiagnosticGC = released.Count(r => r.IsAlive), privateBeforeDiagnosticGCMB = beforeCollection, privateAfterDiagnosticGCMB = process.PrivateMemorySize64 / 1048576d });
            if (released.Any(r => r.IsAlive)) failures.Add("A departed playback page survived diagnostic collection after navigation settled");
            Save();
            if (failures.Count > 0 && Environment.GetCommandLineArgs().Contains("--assert-player-budget")) throw new InvalidOperationException(string.Join("; ", failures));
        }
        finally { window.Root.Content = null; MainVM.Sites[0].LiveSite = originalSite; Save(); }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> OpenAndLeaveAsync(Frame frame, LocalSite site, LiveRoomItem room)
    {
        frame.Navigate(typeof(Views.LiveRoomPage), new PageArgs { Site = site, Data = room });
        var page = (Views.LiveRoomPage)frame.Content;
        try { await UntilAsync(() => Media(page)?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing, "Repeated room did not play"); }
        catch (Exception error)
        {
            throw new InvalidOperationException("Repeated room did not play: loaded=" + page.IsLoaded + ", state=" + Media(page)?.PlaybackSession.PlaybackState + ", source=" + (Media(page)?.Source != null) + ", closed=" + page.GetType().GetField("closingWindow", Private).GetValue(page) + ", hasArgs=" + (page.GetType().GetField("pageArgs", Private).GetValue(page) != null) + ", text=" + ((TextBlock)page.FindName("PlayerLoadText")).Text, error);
        }
        var reference = new WeakReference(page); frame.Navigate(typeof(Page)); await UntilAsync(() => !page.IsLoaded, "Departed playback page remained loaded"); return reference;
    }
    internal sealed class LocalMediaServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly string path;
        internal string Url { get; }
        internal LocalMediaServer(string path)
        {
            this.path = path; listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/720p30.mp4";
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
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var stream = client.GetStream(); var headerBytes = new byte[8192]; var count = 0;
                while (count < headerBytes.Length)
                {
                    var read = await stream.ReadAsync(headerBytes.AsMemory(count), timeout.Token).ConfigureAwait(false);
                    if (read == 0) return; count += read;
                    if (Encoding.ASCII.GetString(headerBytes, 0, count).Contains("\r\n\r\n")) break;
                }
                var request = Encoding.ASCII.GetString(headerBytes, 0, count);
                if (request.Contains(" /unavailable "))
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token).ConfigureAwait(false); return;
                }
                using var file = File.OpenRead(path); long start = 0, end = file.Length - 1;
                var range = request.Split("\r\n").FirstOrDefault(line => line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase));
                if (range != null)
                {
                    var parts = range[13..].Split('-');
                    if (long.TryParse(parts[0], out var from)) start = Math.Clamp(from, 0, end);
                    if (parts.Length > 1 && long.TryParse(parts[1], out var to)) end = Math.Clamp(to, start, end);
                }
                var response = (range == null ? "HTTP/1.1 200 OK" : "HTTP/1.1 206 Partial Content") + "\r\nContent-Type: video/mp4\r\nAccept-Ranges: bytes\r\nConnection: close\r\nContent-Length: " + (end - start + 1) + "\r\n";
                if (range != null) response += "Content-Range: bytes " + start + "-" + end + "/" + file.Length + "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response + "\r\n"), timeout.Token).ConfigureAwait(false);
                if (request.StartsWith("HEAD ")) return;
                file.Position = start; var buffer = new byte[65536]; var remaining = end - start + 1;
                while (remaining > 0)
                {
                    var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), timeout.Token).ConfigureAwait(false);
                    if (read == 0) return;
                    await stream.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false); remaining -= read;
                }
            }
            catch (Exception error) when (error is IOException || error is OperationCanceledException || error is SocketException) { }
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); }
    }
    private sealed class LocalSite(string path) : BiliBili, ILiveSite
    {
        public new Task<LiveRoomDetail> GetRoomDetail(object id) => GetRoomDetail(id, CancellationToken.None);
        public new Task<LiveRoomDetail> GetRoomDetail(object id, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(new LiveRoomDetail { RoomID = id.ToString(), Title = "隔离播放测量", UserName = "本地测试媒体", Status = true }); }
        public new Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail detail) => GetPlayQuality(detail, CancellationToken.None);
        public new Task<List<LivePlayQuality>> GetPlayQuality(LiveRoomDetail detail, CancellationToken cancellation) => Task.FromResult(new List<LivePlayQuality> { new() { Quality = "720p", Data = path } });
        public new Task<List<string>> GetPlayUrls(LiveRoomDetail detail, LivePlayQuality quality) => GetPlayUrls(detail, quality, CancellationToken.None);
        public new Task<List<string>> GetPlayUrls(LiveRoomDetail detail, LivePlayQuality quality, CancellationToken cancellation) => Task.FromResult(new List<string> { path });
        public new Task<bool> GetLiveStatus(object id) => GetLiveStatus(id, CancellationToken.None);
        public new Task<bool> GetLiveStatus(object id, CancellationToken cancellation) => Task.FromResult(true);
    }
}
