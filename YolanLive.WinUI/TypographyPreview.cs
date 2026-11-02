using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using YolanLive.WinUI.Helper;

namespace YolanLive.WinUI;

// Isolated, opt-in design preview. Default application fonts remain unchanged.
internal sealed class TypographyPreview
{
    private readonly FontFamily sarasa = new("ms-appx:///Assets/Fonts/SarasaUiSC-Regular.ttf#Sarasa UI SC");
    private readonly FontFamily rounded = (FontFamily)App.Current.Resources["HeadingFontFamily"];
    private readonly FontFamily yahei = new("Microsoft YaHei UI");
    private readonly List<(TextBlock Text, bool Secondary)> labels = new();
    private readonly List<TextBlock> bodies = new();
    private readonly List<Control> controls = new();
    private readonly List<Border> surfaces = new();
    private readonly List<TextBlock> metricSamples = new();
    private readonly List<ToggleButton> tabs = new();
    private readonly Page page = new();
    private readonly ComboBox sizePicker = new() { ItemsSource = new[] { 14d, 15d }, SelectedIndex = 0, Width = 88, FontSize = 14 };
    private readonly ComboBox themePicker = new() { ItemsSource = new[] { "浅色", "深色" }, SelectedIndex = 0, Width = 96, FontSize = 14 };
    private readonly TextBlock scaleLabel;
    private readonly Grid comparison = new() { ColumnSpacing = 20, RowSpacing = 20 };

    private TypographyPreview(MainWindow window)
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "SarasaUiSC-Regular.ttf")))
            throw new InvalidOperationException("请保留完整预览目录及其字体文件。");
        window.Title = "游岚 · 字体原尺寸对照";
        var root = new Grid { Padding = new Thickness(24, 0, 24, 24), RowSpacing = 16 };
        root.RowDefinitions.Add(new() { Height = new GridLength(48) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new());
        var caption = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        caption.Children.Add(Label("游岚 · 字体对照", 14, sarasa));
        root.Children.Add(caption); window.SetTitleBar(caption);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/YolanLive.svg")), Width = 40, Height = 40 });
        tools.Children.Add(Label("正文大小", 14, sarasa)); tools.Children.Add(sizePicker);
        tools.Children.Add(themePicker);
        scaleLabel = Label("", 13, sarasa, true); tools.Children.Add(scaleLabel);
        Grid.SetRow(tools, 1); root.Children.Add(tools);
        comparison.ColumnDefinitions.Add(new()); comparison.ColumnDefinitions.Add(new());
        comparison.RowDefinitions.Add(new() { Height = GridLength.Auto }); comparison.RowDefinitions.Add(new() { Height = GridLength.Auto });
        comparison.Children.Add(Panel(false));
        var next = Panel(true); Grid.SetColumn(next, 1); comparison.Children.Add(next);
        comparison.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 880;
            Grid.SetRow(next, narrow ? 1 : 0); Grid.SetColumn(next, narrow ? 0 : 1);
            Grid.SetColumnSpan((FrameworkElement)comparison.Children[0], narrow ? 2 : 1); Grid.SetColumnSpan(next, narrow ? 2 : 1);
        };
        var scroll = new ScrollViewer { Content = comparison, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        page.Content = root; window.Root.Content = page;
        sizePicker.SelectionChanged += (_, _) =>
        {
            var size = (double)sizePicker.SelectedItem;
            foreach (var text in bodies) { text.FontSize = size; text.LineHeight = size + 8; }
            foreach (var control in controls) control.FontSize = size;
        };
        foreach (var control in controls) control.FontWeight = FontWeights.Normal;
        themePicker.SelectionChanged += (_, _) => ApplyTheme(themePicker.SelectedIndex == 1);
        ApplyTheme(false);
        page.Loaded += (_, _) => scaleLabel.Text = "系统缩放 " + (page.XamlRoot.RasterizationScale * 100).ToString("0") + "%";
        window.AppWindow.Resize(new SizeInt32(1080, 790)); window.Activate();
    }
    private TextBlock Label(string value, double size, FontFamily family, bool secondary = false, bool resize = false)
    {
        var text = new TextBlock { Text = value, FontFamily = family, FontSize = size, LineHeight = size + 8,
            FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        labels.Add((text, secondary)); if (resize) bodies.Add(text);
        return text;
    }
    private Border Panel(bool proposed)
    {
        var body = proposed ? sarasa : yahei;
        var nav = proposed ? sarasa : rounded;
        var numbers = proposed ? sarasa : new FontFamily("ms-appx:///Assets/Fonts/Satoshi-Regular.ttf#Satoshi, Microsoft YaHei UI");
        var stack = new StackPanel { Spacing = 16 };
        stack.Children.Add(Label(proposed ? "新搭配：圆体标题＋更纱 UI SC" : "现有：圆体导航＋微软雅黑 UI", 15, sarasa, true));
        stack.Children.Add(Label("分类", 22, rounded));
        var platform = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var name in new[] { "哔哩哔哩", "抖音", "虎牙", "斗鱼" })
        {
            var button = new ToggleButton { Content = name, FontFamily = nav, FontSize = 14, MinWidth = name.Length > 2 ? 90 : 68,
                Height = 40, CornerRadius = new CornerRadius(8), IsChecked = name == "哔哩哔哩", IsTabStop = true };
            button.Click += (_, _) => { foreach (var item in platform.Children.OfType<ToggleButton>()) item.IsChecked = item == button; ApplyTheme(themePicker.SelectedIndex == 1); };
            controls.Add(button); tabs.Add(button); platform.Children.Add(button);
        }
        stack.Children.Add(platform);
        stack.Children.Add(Label("网游    手游    单机游戏    游戏赛事", 14, nav, resize: true));
        var search = new AutoSuggestBox { PlaceholderText = "全平台搜索或粘贴链接", QueryIcon = new SymbolIcon(Symbol.Find),
            FontFamily = body, FontSize = 14, Style = (Style)App.Current.Resources["BrowseSearchStyle"], HorizontalAlignment = HorizontalAlignment.Stretch };
        controls.Add(search); stack.Children.Add(search);
        stack.Children.Add(Label("彩虹六号排位赛，今晚继续上分", 14, body, resize: true));
        stack.Children.Add(Label("主播名与平台信息 · 正在直播", 13, body, true));
        stack.Children.Add(Label("1.2万    1280×720    60 FPS", 13, numbers, true));
        stack.Children.Add(Label("常规", 16, nav));
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(Label("窗口大小", 14, body, resize: true));
        var sizes = new ComboBox { ItemsSource = new[] { "960×540", "1280×720" }, SelectedIndex = 1, Width = 154, FontFamily = body, FontSize = 14 };
        Grid.SetColumn(sizes, 1); row.Children.Add(sizes); controls.Add(sizes); stack.Children.Add(row);
        stack.Children.Add(Label("按 16:9 视频区域调整播放窗口", 13, body, true));
        var sample = Label("测量：鹰隼酣战，清晰观看 0123456789", 14, body, resize: true);
        metricSamples.Add(sample); stack.Children.Add(sample);
        stack.Children.Add(Label("音乐、图像、线路与画质；Aa Bb Gg 1Il0O", 14, body, resize: true));
        var surface = new Border { Padding = new Thickness(24), CornerRadius = new CornerRadius(12), Child = stack };
        surfaces.Add(surface); return surface;
    }
    private void ApplyTheme(bool dark)
    {
        page.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
        page.Background = new SolidColorBrush(dark ? Color.FromArgb(255, 24, 29, 39) : Color.FromArgb(255, 247, 248, 250));
        var main = new SolidColorBrush(dark ? Color.FromArgb(255, 230, 237, 245) : Color.FromArgb(255, 40, 59, 80));
        var secondary = new SolidColorBrush(dark ? Color.FromArgb(255, 173, 189, 208) : Color.FromArgb(255, 90, 110, 130));
        foreach (var (text, muted) in labels) text.Foreground = muted ? secondary : main;
        foreach (var surface in surfaces) surface.Background = new SolidColorBrush(dark ? Color.FromArgb(255, 32, 38, 51) : Colors.White);
        foreach (var tab in tabs)
        {
            tab.Foreground = main;
            tab.Background = new SolidColorBrush(tab.IsChecked == true
                ? dark ? Color.FromArgb(255, 52, 61, 97) : Color.FromArgb(255, 220, 233, 244)
                : Colors.Transparent);
        }
    }
    internal static void Show(MainWindow window) => _ = new TypographyPreview(window);
    internal static async Task CaptureAsync(MainWindow window)
    {
        var preview = new TypographyPreview(window);
        var report = new List<string>();
        foreach (var size in new[] { 14d, 15d })
        foreach (var dark in new[] { false, true })
        {
            preview.sizePicker.SelectedItem = size; preview.themePicker.SelectedIndex = dark ? 1 : 0;
            await Task.Delay(250); preview.page.UpdateLayout();
            if (preview.bodies.Any(t => t.FontSize != size || t.RenderTransform is ScaleTransform)) throw new Exception("Font preview resized controls instead of the font.");
            if (preview.controls.Any(c => c.FontWeight.Weight != 400)) throw new Exception("Font comparison has mismatched control weights.");
            foreach (var text in preview.metricSamples) text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var widths = preview.metricSamples.Select(t => t.DesiredSize.Width).ToArray();
            if (Math.Abs(widths[0] - widths[1]) < .1) throw new Exception("Candidate font metrics are identical to YaHei; verify bundled font resolution.");
            var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(preview.page);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            if (pixels.Length == 0) throw new Exception("Preview did not render.");
            if (pixels[3] != 255) throw new Exception("Preview capture lost the page background.");
            var folder = await StorageFolder.GetFolderFromPathAsync(AppPaths.EnsureDirectory());
            var file = await folder.CreateFileAsync($"font-{size:0}-{(dark ? "dark" : "light")}.png", CreationCollisionOption.ReplaceExisting);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
            report.Add($"PASS native preview size={size}, dark={dark}, rasterization={preview.page.XamlRoot.RasterizationScale}, YaHeiWidth={widths[0]:0.00}, SarasaWidth={widths[1]:0.00}");
        }
        File.WriteAllLines(Path.Combine(AppPaths.EnsureDirectory(), "font-preview.log"), report);
    }
}
