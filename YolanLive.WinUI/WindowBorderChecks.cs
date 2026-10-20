using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace YolanLive.WinUI;

internal static class WindowBorderChecks
{
    // Isolated screenshot regression for the actual two playback modes.
    internal static async Task RunAsync(MainWindow window)
    {
        var surface = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
        window.Root.Content = surface;
        window.SetPlaybackViewport(surface);
        window.ApplyWindowSize("960×540"); window.AppWindow.Show(); window.Activate();
        ((OverlappedPresenter)window.AppWindow.Presenter).IsAlwaysOnTop = true;
        await Task.Delay(400);
        window.SetContentExpanded(true); await Task.Delay(400);
        var report = new List<string>();
        foreach (var mini in new[] { false, true })
        {
            if (mini) window.SetWindowMode(AppWindowPresenterKind.CompactOverlay);
            await Task.Delay(300);
            ((OverlappedPresenter)window.AppWindow.Presenter).IsAlwaysOnTop = true; // Keep this diagnostic fixture unobscured.
            await Task.Delay(100);
            var frame = new Rect();
            if (DwmGetWindowAttribute(window.Handle, 9, ref frame, Marshal.SizeOf<Rect>()) != 0)
                throw new Exception("Cannot inspect visible window bounds");
            var width = frame.Right - frame.Left; var height = frame.Bottom - frame.Top;
            if (GetAncestor(WindowFromPoint(new Point { X = (frame.Left + frame.Right) / 2, Y = (frame.Top + frame.Bottom) / 2 }), 2) != window.Handle)
                throw new Exception("Isolated window is occluded; refusing to capture another app");
            var dc = GetDC(0); var memory = CreateCompatibleDC(dc);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            var bitmap = CreateDIBSection(dc, ref info, 0, out var data, 0, 0);
            if (bitmap == 0) throw new Exception("Cannot capture isolated window");
            var previous = SelectObject(memory, bitmap);
            var pixels = new byte[width * height * 4];
            try
            {
                if (!BitBlt(memory, 0, 0, width, height, dc, frame.Left, frame.Top, 0x00CC0020)) throw new Exception("Window capture failed");
                Marshal.Copy(data, pixels, 0, pixels.Length);
            }
            finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(0, dc); }
            var name = mini ? "mini" : "window-full";
            var directory = Helper.AppPaths.EnsureDirectory();
            using (var file = File.Create(Path.Combine(directory, name + ".png")))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
                await encoder.FlushAsync();
            }
            int light = 0, sampled = 0;
            // Avoid rounded corners and the desktop outside them. The fixture's entire client is black.
            void Sample(int x, int y)
            {
                var index = (y * width + x) * 4; sampled++;
                if (pixels[index] > 190 && pixels[index + 1] > 190 && pixels[index + 2] > 190) light++;
            }
            for (int inset = 0; inset < 3; inset++)
            {
                for (int x = 24; x < width - 24; x++) { Sample(x, inset); Sample(x, height - 1 - inset); }
                for (int y = 24; y < height - 24; y++) { Sample(inset, y); Sample(width - 1 - inset, y); }
            }
            report.Add(name + ": visible=" + width + "x" + height + ", client=" + window.AppWindow.ClientSize.Width + "x" + window.AppWindow.ClientSize.Height + ", style=" + GetWindowLongPtr(window.Handle, -16).ToString("X") + ", whiteEdgePixels=" + light + "/" + sampled);
        }
        File.WriteAllLines(Path.Combine(Helper.AppPaths.EnsureDirectory(), "border-check.log"), report);
        if (report.Exists(line => line.Contains("whiteEdgePixels=") && !line.Contains("whiteEdgePixels=0/"))) throw new Exception("Visible light border remains; see border-check.log and snapshots");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { internal uint Size; internal int Width, Height; internal ushort Planes, BitCount; internal uint Compression, ImageSize; internal int X, Y; internal uint Used, Important; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, ref Rect value, int size);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}
