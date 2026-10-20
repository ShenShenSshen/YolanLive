using YolanLive.WinUI.Helper;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YolanLive.WinUI;
public partial class App : Application
{
    public static string DisplayName => (string)Current.Resources["AppDisplayName"];
    public static List<MainWindow> Windows { get; } = new();
    public static MainWindow ActiveWindow { get; private set; }
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            if (Environment.GetCommandLineArgs().Contains("--category-repro")) CategoryRepro.RecordError(e.Exception);
            LogHelper.Log("应用错误", LogType.ERROR, e.Exception);
            Utils.ShowMessageToast(e.Message);
            e.Handled = true;
        };
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Contains("--smoke-test") || Environment.GetCommandLineArgs().Contains("--category-repro"))
        {
            var window = new MainWindow();
            RegisterWindow(window);
            try
            {
                if (Environment.GetCommandLineArgs().Contains("--visual-checks")) await VisualDesignChecks.RunAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--category-repro")) await CategoryRepro.RunAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--player-metrics")) await PlayerPerformanceChecks.RunAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--image-checks")) await CategoryImageChecks.RunAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--border-check")) await WindowBorderChecks.RunAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--performance-playback")) await PerformanceRegressionChecks.PlaybackAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--performance-checks")) await PerformanceRegressionChecks.RunAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--favorite-check")) await PerformanceChecks.FavoriteAsync();
                else if (Environment.GetCommandLineArgs().Contains("--performance")) await PerformanceChecks.RunAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--browse-preferences-check")) BrowseChecks.VerifyRestart();
                else if (Environment.GetCommandLineArgs().Contains("--platform-live")) await PlatformChecks.LiveAsync(window);
                else if (Environment.GetCommandLineArgs().Contains("--update-download-check")) await UpdateChecks.LiveAsync();
                else await SmokeTests.RunAsync();
            }
            catch (Exception ex)
            {
                if (!Environment.GetCommandLineArgs().Contains("--smoke-test") || Environment.GetCommandLineArgs().Contains("--performance")) throw;
                var reports = Directory.CreateDirectory(AppPaths.DataDirectory);
                File.WriteAllText(Path.Combine(reports.FullName, "smoke-test.log"), "FAIL Smoke test interrupted\n" + ex);
                Environment.ExitCode = 1;
            }
            finally { window.Close(); }
            return;
        }
        try
        {
            LogHelper.Log("启动游岚", LogType.INFO);
            await DouyinAccount.Instance.InitLoginInfo();
            await WebAccount.For(LiveSite.Huya).InitAsync();
            await WebAccount.For(LiveSite.Douyu).InitAsync();
            await DatabaseHelper.InitializeDatabase();
            OpenWindow(typeof(BaseFramePage), args.Arguments);
        }
        catch (Exception ex)
        {
            LogHelper.Log("启动失败", LogType.FATAL, ex);
            var window = new MainWindow();
            RegisterWindow(window);
            window.Root.Content = new TextBlock { Text = "启动失败：" + ex.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24) };
            window.Activate();
        }
    }
    public static MainWindow OpenWindow(Type page, object data = null)
    {
        var window = new MainWindow(page == typeof(Views.LiveRoomPage));
        RegisterWindow(window);
        window.Root.Navigate(page, data);
        window.Activate();
        return window;
    }
    private static void RegisterWindow(MainWindow window)
    {
        Windows.Add(window);
        ActiveWindow = window;
        var douyu = (YolanLive.Core.Douyu)ViewModels.MainVM.Sites.First(s => s.LiveSite is YolanLive.Core.Douyu).LiveSite;
        if (douyu.SignAsync == null) { var browser = new DouyuScriptBrowser(); douyu.SignAsync = browser.SignAsync; douyu.CancellableSignAsync = browser.SignAsync; }
        if (!Environment.GetCommandLineArgs().Contains("--smoke-test") || Environment.GetCommandLineArgs().Contains("--performance"))
        {
            var douyin = (YolanLive.Core.Douyin)ViewModels.MainVM.Sites.First(s => s.LiveSite is YolanLive.Core.Douyin).LiveSite;
            if (douyin.WebRequestAsync == null) { var browser = new DouyinWebRequest(); douyin.WebRequestAsync = browser.GetAsync; douyin.CancellableWebRequestAsync = browser.GetAsync; }
        }
        window.Activated += (_, args) =>
        {
            // Deactivation can arrive after Closed; never restore a removed native window.
            if (args.WindowActivationState != WindowActivationState.Deactivated && Windows.Contains(window))
                ActiveWindow = window;
        };
        window.Closed += (_, _) =>
        {
            Windows.Remove(window);
            ActiveWindow = Windows.LastOrDefault();
        };
    }
    public static MainWindow GetWindowFor(UIElement element) => Windows.FirstOrDefault(w => w.Root.XamlRoot != null && w.Root.XamlRoot == element.XamlRoot) ?? ActiveWindow;
    public static void SetTitleBar()
    {
        foreach (var window in Windows)
            window.Root.RequestedTheme = (ElementTheme)SettingHelper.GetValue(SettingHelper.THEME, 0);
    }
    public static void SetWindowSize(string value)
    {
        value = MainWindow.ValidateWindowSize(value);
        SettingHelper.SetValue(SettingHelper.WINDOW_SIZE, value);
        foreach (var window in Windows) window.ApplyWindowSize(value);
    }
}
