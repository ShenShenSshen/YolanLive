using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Playback;
using Windows.Storage;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;

namespace YolanLive.WinUI;

// Explicit smoke-mode documentation capture: public guest data, only this app's client area.
internal static class ReadmeScreenshots
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(MainWindow window)
    {
        var folder = AppPaths.EnsureDirectory();
        var log = Path.Combine(folder, "readme-screenshots.log");
        void Note(string value) => File.AppendAllText(log, value + Environment.NewLine);
        await DatabaseHelper.InitializeDatabase();
        SettingHelper.SetValue(SettingHelper.THEME, (int)ElementTheme.Light);
        MainVM.BrowseSite = MainVM.Sites.Single(s => s.SiteType == LiveSite.Huya);
        window.Root.RequestedTheme = ElementTheme.Light;
        window.ApplyWindowSize("1280×720");
        window.Root.Navigate(typeof(BaseFramePage));
        window.Activate();
        await Task.Delay(500);
        var shell = (BaseFramePage)window.Root.Content;
        var mainFrame = (Frame)shell.FindName("MainFrame");
        var main = (MainPage)mainFrame.Content;
        var navigation = (NavigationView)main.FindName("navigationView");
        var frame = (Frame)main.FindName("frame");
        await WaitAsync(() => frame.Content is Views.RecomendPage, "home navigation");
        var home = (Views.RecomendPage)frame.Content;
        var homeVm = (RecomendVM)typeof(Views.RecomendPage).GetField("recomendVM", Fields).GetValue(home);
        await WaitAsync(() => !homeVm.Loading && homeVm.Items.Count >= 8, "guest home data");
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1280, 540));
        await Task.Delay(4500);
        await CaptureAsync(window, folder, "home.png");
        Note("PASS home: current UI, guest Huya rooms=" + homeVm.Items.Count);
        var rooms = homeVm.Items.Take(8).ToList();
        window.ApplyWindowSize("1280×720");
        navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().Single(i => (string)i.Tag == "CategoryPage");
        await WaitAsync(() => frame.Content is Views.CategoryPage, "category navigation");
        var category = (Views.CategoryPage)frame.Content;
        var categoryVm = (CategoryVM)typeof(Views.CategoryPage).GetField("categoryVM", Fields).GetValue(category);
        await WaitAsync(() => !categoryVm.Loading && categoryVm.Items.Count >= 12, "guest category data");
        await Task.Delay(4500);
        await CaptureAsync(window, folder, "categories.png");
        Note("PASS categories: current UI, guest Huya games=" + categoryVm.Items.Count);
        foreach (var room in rooms)
        {
            mainFrame.Navigate(typeof(Views.LiveRoomPage), new PageArgs { Site = MainVM.BrowseSite.LiveSite, Data = room });
            var page = (Views.LiveRoomPage)mainFrame.Content;
            var player = (MediaPlayerElement)page.FindName("player");
            try
            {
                await WaitAsync(() => player.MediaPlayer?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing && player.MediaPlayer.PlaybackSession.NaturalVideoWidth > 0, "guest playback", 30);
                await Task.Delay(1800);
                await CaptureAsync(window, folder, "player.png");
                Note("PASS player: current UI, real guest stream, platform=" + MainVM.BrowseSite.Label);
                return;
            }
            catch (TimeoutException) { Note("INFO guest candidate did not play within 30 seconds"); }
        }
        throw new InvalidOperationException("No guest stream available for an honest player screenshot.");
    }
    private static async Task WaitAsync(Func<bool> ready, string stage, int seconds = 45)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!ready())
        {
            if (DateTime.UtcNow >= until) throw new TimeoutException("Screenshot capture timed out: " + stage);
            await Task.Delay(100);
        }
    }
    private static async Task CaptureAsync(MainWindow window, string folder, string name)
    {
        window.Activate();
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(window.Root.XamlRoot) is Control focused)
            focused.Focus(FocusState.Pointer);
        await Task.Delay(250);
        if (!GetClientRect(window.Handle, out var rect)) throw new InvalidOperationException("Cannot measure the app window.");
        var width = rect.Right; var height = rect.Bottom;
        var dc = GetDC(window.Handle);
        var memory = CreateCompatibleDC(dc);
        var bitmap = CreateCompatibleBitmap(dc, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(window.Handle, memory, 3)) throw new InvalidOperationException("App capture failed.");
            SelectObject(memory, old);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var pixels = new byte[checked(width * height * 4)];
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) != height) throw new InvalidOperationException("App pixels unavailable.");
            var file = await StorageFile.GetFileFromPathAsync(CreateOutput(folder, name));
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
            await encoder.FlushAsync();
        }
        finally { SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(window.Handle, dc); }
    }
    private static string CreateOutput(string folder, string name)
    {
        var path = Path.Combine(folder, name); using (File.Create(path)) { } return path;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint count, byte[] pixels, ref BitmapInfo info, uint usage);
}
