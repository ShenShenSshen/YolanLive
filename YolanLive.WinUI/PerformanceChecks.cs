using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using YolanLive.Core.Helper;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
namespace YolanLive.WinUI;
internal static class PerformanceChecks
{
    internal static async Task FavoriteAsync()
    {
        await DatabaseHelper.InitializeDatabase();
        var vm = new LiveRoomVM { RoomID = "performance-favorite", Name = "隔离测试", Photo = "" };
        typeof(LiveRoomVM).GetField("Site", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(vm, MainVM.Sites[0].LiveSite);
        vm.AddFavoriteCommand.Execute(null);
        if (!vm.IsFavorite || DatabaseHelper.CheckFavorite(vm.RoomID, MainVM.Sites[0].Name) == null) throw new Exception("Adding favorite failed");
        vm.RemoveFavoriteCommand.Execute(null);
        if (vm.IsFavorite || DatabaseHelper.CheckFavorite(vm.RoomID, MainVM.Sites[0].Name) != null) throw new Exception("Cannot unfollow immediately after following");
        vm.AddFavoriteCommand.Execute(null); vm.RemoveFavoriteCommand.Execute(null);
        if (DatabaseHelper.CheckFavorite(vm.RoomID, MainVM.Sites[0].Name) != null) throw new Exception("Repeated favorite toggle failed");
        File.WriteAllText(Path.Combine(AppPaths.EnsureDirectory(), "favorite-check.txt"), "PASS follow-unfollow and repeat");
    }
    internal static async Task RunAsync(MainWindow window)
    {
        var douyin = (YolanLive.Core.Douyin)MainVM.Sites.First(s => s.SiteType == LiveSite.Douyin).LiveSite;
        douyin.WebRequestAsync ??= new DouyinWebRequest().GetAsync;
        await DatabaseHelper.InitializeDatabase();
        var loaded = new TaskCompletionSource<bool>();
        window.Root.Loaded += (_, _) => loaded.TrySetResult(true);
        window.Root.Navigate(typeof(MainPage)); window.Activate();
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var startup = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        var errors = new List<object>();
        var samples = new List<object>();
        var results = new List<object>();
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        var tracked = new Dictionary<int, double>();
        double completedCpu = 0;
        void Sample()
        {
            var ids = Descendants(Environment.ProcessId);
            double mainMemory = 0, childMemory = 0, working = 0, cpu = completedCpu;
            int threads = 0, count = 0;
            foreach (var id in tracked.Keys.Concat(ids).Distinct().ToArray())
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    if (process.HasExited) throw new InvalidOperationException();
                    var milliseconds = process.TotalProcessorTime.TotalMilliseconds;
                    tracked[id] = milliseconds; cpu += milliseconds;
                    if (id == Environment.ProcessId) mainMemory = process.PrivateMemorySize64 / 1048576d;
                    else childMemory += process.PrivateMemorySize64 / 1048576d;
                    working += process.WorkingSet64 / 1048576d; threads += process.Threads.Count; count++;
                }
                catch
                {
                    if (tracked.Remove(id, out var previous)) { completedCpu += previous; cpu += previous; }
                }
            }
            samples.Add(new { seconds = Math.Round(watch.Elapsed.TotalSeconds, 1), mainPrivateMB = Math.Round(mainMemory, 2), childPrivateMB = Math.Round(childMemory, 2), workingSetMB = Math.Round(working, 2), cpuMs = Math.Round(cpu, 1), threads, processes = count, requests = RequestMetrics.Requests, activeRequests = RequestMetrics.Active });
        }
        Sample(); timer.Tick += (_, _) => Sample();
        double activeEnd = 0;
        if (!Environment.GetCommandLineArgs().Contains("--performance-startup"))
        {
            timer.Start();
            var main = (MainPage)window.Root.Content;
            var frame = (Frame)typeof(MainPage).GetField("frame", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(main);
            for (int cycle = 0; cycle < 2 && watch.Elapsed.TotalSeconds < 180; cycle++)
            foreach (var platform in MainVM.Sites)
            {
                if (watch.Elapsed.TotalSeconds >= 180) break;
                MainVM.BrowseSite = platform;
                foreach (var page in new[] { typeof(Views.RecomendPage), typeof(Views.CategoryPage) })
                {
                    try
                    {
                        frame.Navigate(page);
                        await Task.Delay(400);
                        var modelName = page == typeof(Views.RecomendPage) ? "recomendVM" : "categoryVM";
                        var model = (BaseViewModel)page.GetField(modelName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(frame.Content);
                        var deadline = DateTime.UtcNow.AddSeconds(20);
                        while (model.Loading && DateTime.UtcNow < deadline) await Task.Delay(100);
                        if (model.Loading) throw new TimeoutException();
                        var count = page == typeof(Views.RecomendPage) ? ((RecomendVM)model).Items.Count : ((CategoryVM)model).Items.Count;
                        results.Add(new { platform = platform.Label, page = page.Name, cycle, items = count, empty = model.IsEmpty });
                    }
                    catch (Exception error) { errors.Add(new { platform = platform.Label, page = page.Name, type = error.GetType().Name }); }
                }
                if (cycle == 0 && platform.SiteType == LiveSite.Douyu)
                {
                    try
                    {
                        var rooms = await platform.LiveSite.GetRecommendRooms().WaitAsync(TimeSpan.FromSeconds(15));
                        var room = await platform.LiveSite.GetRoomDetail(rooms.Rooms.First().RoomID).WaitAsync(TimeSpan.FromSeconds(15));
                        await platform.LiveSite.GetPlayQuality(room).WaitAsync(TimeSpan.FromSeconds(25));
                        results.Add(new { platform = platform.Label, page = "Signature", cycle, items = 1, empty = false });
                    }
                    catch (Exception error) { errors.Add(new { platform = platform.Label, page = "Signature", type = error.GetType().Name }); }
                }
            }
            frame.Content = new TextBlock { Text = "隔离性能对照：等待后台浏览器空闲回收", Margin = new Thickness(24) };
            activeEnd = watch.Elapsed.TotalSeconds;
            var remaining = Math.Max(0, 600 - watch.Elapsed.TotalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(remaining));
            timer.Stop(); Sample();
        }
        var report = new { catalogHits = GameCatalog.Hits, catalogLoads = GameCatalog.Loads, startupMs = Math.Round(startup, 1), durationSeconds = watch.Elapsed.TotalSeconds, activeEndSeconds = activeEnd, dataDirectory = AppPaths.DataDirectory, requests = RequestMetrics.Requests, peakRequests = RequestMetrics.Peak, results, errors, samples };
        File.WriteAllText(Path.Combine(AppPaths.EnsureDirectory(), "performance.json"), JsonConvert.SerializeObject(report, Formatting.Indented));
    }
    private static HashSet<int> Descendants(int root)
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        var ids = new HashSet<int> { root };
        if (snapshot == new IntPtr(-1)) return ids;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            var parents = new Dictionary<int, int>();
            if (Process32First(snapshot, ref entry)) do { parents[(int)entry.Id] = (int)entry.Parent; } while (Process32Next(snapshot, ref entry));
            bool changed;
            do { changed = false; foreach (var pair in parents) if (ids.Contains(pair.Value)) changed |= ids.Add(pair.Key); } while (changed);
            return ids;
        }
        finally { CloseHandle(snapshot); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id; public UIntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
