using YolanLive.WinUI.Models;
using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using YolanLive.Core.Models;
using YolanLive.WinUI.Helper;
using Windows.UI.ViewManagement;
using Windows.UI.Popups;
using Windows.System.Display;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Display;
using Windows.UI;
using Windows.ApplicationModel.Core;
using System.Diagnostics;
using Newtonsoft.Json;
using Windows.UI.Core;
using FFmpegInteropX;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Windows.Media.Playback;
using System.Text;
using System.Runtime;
using System.Threading;
// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace YolanLive.WinUI.Views
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class LiveRoomPage : Page
    {
        readonly LiveRoomVM liveRoomVM;

        FFmpegInteropX.FFmpegMediaSource interopMSS;
        MediaPlayer mediaPlayer;

        DisplayRequest dispRequest;
        private bool displayActive;
        PageArgs pageArgs;
        private RandomBrowseSession randomSession;
        private System.Threading.CancellationTokenSource randomSwitchOperation;
        //当前处于小窗
        private bool isMini = false;
        private bool draggingVideo;
        private bool isFullWindow;
        private bool UsesExternalToolbar => isMini || (isFullWindow && (playbackWindow ?? App.GetWindowFor(this))?.AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped);
        private MainWindow playbackWindow;
        private AppWindow playbackAppWindow;
        private bool closingWindow;
        private readonly PlaybackRecovery recovery = new();
        private int playGeneration;
        private bool switchingLine;
        private DateTime healthySince;
        private bool floatingToolbar;
        private int toolbarPopupCount;
        private bool toolbarHiding;
        private int toolbarFadeGeneration;
        private Microsoft.UI.Xaml.Media.Animation.Storyboard toolbarFade;
        private readonly UISettings toolbarUISettings = new();
        private InputSystemCursor arrowCursor;
        private bool playbackWindowMinimized;
        DispatcherTimer controlTimer;
        private readonly DispatcherTimer miniHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
        private DateTime? miniPointerLeftAt;

        public LiveRoomPage()
        {
            this.InitializeComponent();
            control.SizeChanged += (_, _) => UpdateToolbarDensity();
            PlayBtnVolume.Flyout.Opening += (_, _) => ToolbarPopupOpened();
            PlayBtnVolume.Flyout.Closed += (_, _) => ToolbarPopupClosed();
            PlayBtnBrightness.Flyout.Opening += (_, _) => ToolbarPopupOpened();
            PlayBtnBrightness.Flyout.Closed += (_, _) => ToolbarPopupClosed();
            PlayerMoreMenu.Opening += (_, _) => ToolbarPopupOpened();
            PlayerMoreMenu.Closed += (_, _) => ToolbarPopupClosed();
            MiniMoreMenu.Opening += (_, _) => ToolbarPopupOpened();
            MiniMoreMenu.Closed += (_, _) => ToolbarPopupClosed();
            DisplayModeMenu.Opening += (_, _) => ToolbarPopupOpened();
            DisplayModeMenu.Closed += (_, _) => ToolbarPopupClosed();
            WindowSizeMenu.Opening += (_, _) => ToolbarPopupOpened();
            WindowSizeMenu.Closed += (_, _) => ToolbarPopupClosed();
            PlayQuality.Items.VectorChanged += (_, _) => PlayQuality.IsEnabled = PlayQuality.Items.Count > 0;
            PlayLine.Items.VectorChanged += (_, _) => PlayLine.IsEnabled = PlayLine.Items.Count > 0;
            PlayQuality.DropDownOpened += (_, _) => ToolbarPopupOpened();
            PlayQuality.DropDownClosed += (_, _) => ToolbarPopupClosed();
            PlayLine.DropDownOpened += (_, _) => ToolbarPopupOpened();
            PlayLine.DropDownClosed += (_, _) => ToolbarPopupClosed();

            liveRoomVM = new LiveRoomVM();
            dispRequest = new DisplayRequest();

            KeyDown += Page_KeyDown;
          
            liveRoomVM.ChangedPlayUrl += LiveRoomVM_ChangedPlayUrl;
            controlTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
            controlTimer.Tick += ControlTimer_Tick;
            miniHoverTimer.Tick += MiniHoverTimer_Tick;
            mediaPlayer = new MediaPlayer { IsMuted = Environment.GetCommandLineArgs().Contains("--smoke-test") };
            mediaPlayer.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
            mediaPlayer.PlaybackSession.BufferingStarted += PlaybackSession_BufferingStarted;
            mediaPlayer.PlaybackSession.BufferingProgressChanged += PlaybackSession_BufferingProgressChanged;
            mediaPlayer.PlaybackSession.BufferingEnded += PlaybackSession_BufferingEnded;
            mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;
            mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;

            Unloaded += LiveRoomPage_Unloaded;
            Loaded += (_, _) =>
            {
                playbackWindow = App.GetWindowFor(this);
                playbackWindowMinimized = playbackWindow.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
                playbackWindow.WindowModeChanged += PlaybackWindow_ModeChanged;
                playbackAppWindow = playbackWindow.AppWindow;
                playbackAppWindow.Changed += PlaybackWindow_Changed;
                playbackWindow.Closed += PlaybackWindow_Closed;
                playbackWindow.MiniToolbarLayoutChanged += PlaybackWindow_MiniToolbarLayoutChanged;
                playbackWindow.VideoDragStarted += PlaybackWindow_VideoDragStarted;
                playbackWindow.VideoDragCompleted += PlaybackWindow_VideoDragCompleted;
                playbackWindow.VideoDoubleClicked += PlaybackWindow_VideoDoubleClicked;
                playbackWindow.VideoPointerMoved += PlaybackWindow_VideoPointerMoved;
                playbackWindow.SetPlaybackViewport(player);
                playbackWindow.SetVideoDragExclusion(PlaybackFailure);
                UpdateWindowModeUI();
                if (SettingHelper.GetValue(SettingHelper.NEW_WINDOW_LIVEROOM, false))
                {
                    TitleBar.Visibility = Visibility.Visible;
                    App.GetWindowFor(this).SetTitleBar(TitleBar);

                }
            };
        }
        private void LiveRoomPage_Unloaded(object sender, RoutedEventArgs e)
        {
            SettingHelper.Flush();
            closingWindow = true;
            StopPlay(false);
            ReleasePlayer();
            toolbarFadeGeneration++;
            toolbarFade?.Stop();
            toolbarFade = null;
            toolbarPopupCount = 0;
            EndRandomBrowse();
            recovery.Cancel();
            playGeneration++;
            liveRoomVM.CancelPendingPlayback();
            controlTimer.Stop();
            miniHoverTimer.Stop();
            miniPointerLeftAt = null;
            if (playbackWindow != null)
            {
                playbackWindow.WindowModeChanged -= PlaybackWindow_ModeChanged;
                if (playbackAppWindow != null) playbackAppWindow.Changed -= PlaybackWindow_Changed;
                playbackAppWindow = null;
                playbackWindow.Closed -= PlaybackWindow_Closed;
                playbackWindow.MiniToolbarLayoutChanged -= PlaybackWindow_MiniToolbarLayoutChanged;
                playbackWindow.VideoDragStarted -= PlaybackWindow_VideoDragStarted;
                playbackWindow.VideoDragCompleted -= PlaybackWindow_VideoDragCompleted;
                playbackWindow.VideoDoubleClicked -= PlaybackWindow_VideoDoubleClicked;
                playbackWindow.VideoPointerMoved -= PlaybackWindow_VideoPointerMoved;
                playbackWindow.SetPlaybackViewport(null);
            }
            playbackWindow = null;
        }
        private void PlaybackWindow_Closed(object sender, WindowEventArgs args)
        { closingWindow = true; EndRandomBrowse(); StopPlay(false); ReleasePlayer(); }
        private void ReleasePlayer()
        {
            ProtectedCursor = null;
            arrowCursor?.Dispose();
            arrowCursor = null;
            if (mediaPlayer == null) return;
            mediaPlayer.PlaybackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
            mediaPlayer.PlaybackSession.BufferingStarted -= PlaybackSession_BufferingStarted;
            mediaPlayer.PlaybackSession.BufferingProgressChanged -= PlaybackSession_BufferingProgressChanged;
            mediaPlayer.PlaybackSession.BufferingEnded -= PlaybackSession_BufferingEnded;
            mediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;
            mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
            mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
            player.SetMediaPlayer(null); mediaPlayer.Dispose(); mediaPlayer = null;
            liveRoomVM.ChangedPlayUrl -= LiveRoomVM_ChangedPlayUrl;
        }
        private void SetPointerVisible(bool visible)
        {
            if (closingWindow) return;
            if (!visible) { if (ProtectedCursor != null) ProtectedCursor = null; return; }
            // A page needs one arrow, not a new native cursor on every mouse move/poll.
            arrowCursor ??= InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            if (!ReferenceEquals(ProtectedCursor, arrowCursor)) ProtectedCursor = arrowCursor;
        }
        private void PlaybackWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            var minimized = sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };
            if (minimized == playbackWindowMinimized) return;
            playbackWindowMinimized = minimized;
            UpdatePlayerTimers(updateHover: true);
        }
        private void UpdatePlayerTimers(bool updateHover = false)
        {
            var alive = IsLoaded && !closingWindow && playbackWindow != null;
            var active = alive && !playbackWindowMinimized;
            // Recovery accounting remains live while minimized; hidden controls do not.
            var recoveryCheck = alive && recovery.Attempts > 0 && healthySince != default &&
                mediaPlayer?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            var hideCheck = active && floatingToolbar && !UsesExternalToolbar && showControlsFlag >= 0;
            var needControl = recoveryCheck || hideCheck;
            if (controlTimer.IsEnabled != needControl) { if (needControl) controlTimer.Start(); else controlTimer.Stop(); }
            if (!updateHover) return;
            var needHover = active && UsesExternalToolbar && !draggingVideo;
            if (miniHoverTimer.IsEnabled != needHover) { if (needHover) miniHoverTimer.Start(); else miniHoverTimer.Stop(); }
        }
        private void PlaybackWindow_ModeChanged(object sender, EventArgs e)
        { UpdateWindowModeUI(); }
        private void PlaybackWindow_MiniToolbarLayoutChanged(object sender, EventArgs e) => ApplyMiniToolbarLayout();
        private void ApplyMiniToolbarLayout()
        {
            if (!UsesExternalToolbar || playbackWindow == null) return;
            var height = playbackWindow.MiniToolbarHeight;
            TopMiniToolbar.Height = new GridLength(0);
            BottomInfo.Height = new GridLength(height);
            Grid.SetRow(control, 2);
            control.VerticalAlignment = VerticalAlignment.Stretch;
            var desiredHeight = isMini ? 44 : 60;
            control.MinHeight = height > 0 ? Math.Min(desiredHeight, height) : desiredHeight;
            DockToolbarBackground.BorderThickness = new Thickness(0, 1, 0, 0);
        }
        private void MiniHoverTimer_Tick(object sender, object e)
        {
            if (!UsesExternalToolbar || !IsLoaded || closingWindow || draggingVideo || playbackWindow == null) return;
            if (toolbarPopupCount > 0 || playbackWindow.IsPointerInside())
            {
                miniPointerLeftAt = null;
                ShowControl(true);
            }
            else
            {
                miniPointerLeftAt ??= DateTime.UtcNow;
                if (DateTime.UtcNow - miniPointerLeftAt.Value >= TimeSpan.FromMilliseconds(500)) ShowControl(false);
            }
        }
        private void FinishToolbarVisibility(bool show)
        {
            control.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (UsesExternalToolbar)
            {
                playbackWindow?.SetMiniToolbarVisible(show);
                ApplyMiniToolbarLayout();
            }
        }
        private void HideTitleBar(bool hide)
        {
            if (!hide && App.GetWindowFor(this) is MainWindow owner) owner.ExtendsContentIntoTitleBar = true;
            if (SettingHelper.GetValue(SettingHelper.NEW_WINDOW_LIVEROOM, false))
            {
                Grid.SetRow(GridContent, hide ? 0 : 1);
                Grid.SetRowSpan(GridContent, hide ? 2 : 1);
                TitleBarGrid.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
                App.GetWindowFor(this)?.SetTitleBar(hide ? null : TitleBar);
            }
            else MessageCenter.HideTitlebar(hide);
        }

        #region 播放器事件
        private async void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            DispatcherQueue.TryEnqueue(PlayError);
        }

        private async void PlaybackSession_BufferingEnded(MediaPlaybackSession sender, object args)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                PlayerLoading.Visibility = Visibility.Collapsed;
            });

        }

        private async void PlaybackSession_BufferingProgressChanged(MediaPlaybackSession sender, object args)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                PlayerLoadText.Text = sender.BufferingProgress.ToString("p");
            });
        }

        private async void PlaybackSession_BufferingStarted(MediaPlaybackSession sender, object args)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                PlayerLoading.Visibility = Visibility.Visible;
                PlayerLoadText.Text = "缓冲中";
            });
        }

        private async void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {

                PlayError();
            });

        }

        private async void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded || closingWindow || mediaPlayer.Source == null) return;
                //保持屏幕常亮
                SetKeepAwake(mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing);
                PlayerLoading.Visibility = Visibility.Collapsed;
            });
        }

        private async void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded || closingWindow || mediaPlayer.Source == null) return;
                switch (sender.PlaybackState)
                {
                    case MediaPlaybackState.None:
                        break;
                    case MediaPlaybackState.Opening:
                        PlayerLoading.Visibility = Visibility.Visible;
                        PlayerLoadText.Text = "加载中";
                        break;
                    case MediaPlaybackState.Buffering:
                        PlayerLoading.Visibility = Visibility.Visible;
                        PlayerLoadText.Text = "缓冲中";
                        break;
                    case MediaPlaybackState.Playing:
                        healthySince = DateTime.UtcNow;
                        PlayerLoading.Visibility = Visibility.Collapsed;
                        PlayBtnPlay.Visibility = Visibility.Collapsed;
                        PlayBtnPause.Visibility = Visibility.Visible;
                        SetKeepAwake(true);
                        liveRoomVM.Living = true;
                        break;
                    case MediaPlaybackState.Paused:
                        SetKeepAwake(false);
                        PlayerLoading.Visibility = Visibility.Collapsed;
                        PlayBtnPlay.Visibility = Visibility.Visible;
                        PlayBtnPause.Visibility = Visibility.Collapsed;
                        break;
                    default:
                        break;
                }
                UpdatePlayerTimers();
            });
        }

        #endregion

        private async void Page_KeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (!IsLoaded || XamlRoot == null) return;
            var elent = FocusManager.GetFocusedElement(XamlRoot);
            if (elent is TextBox || elent is AutoSuggestBox)
            {
                args.Handled = false;
                return;
            }
            if (args.Key == Windows.System.VirtualKey.Space && !ReferenceEquals(elent, BtnFoucs) && (elent is ButtonBase || elent is Slider || elent is ComboBox)) return;

            args.Handled = true;
            switch (args.Key)
            {
                case Windows.System.VirtualKey.Space:
                    TogglePause();
                    break;

                case Windows.System.VirtualKey.Up:
                    if (mediaPlayer.Volume + 0.1 > 1)
                    {
                        mediaPlayer.Volume = 1;
                    }
                    else
                    {
                        mediaPlayer.Volume += 0.1;
                    }

                    TxtToolTip.Text = "音量:" + mediaPlayer.Volume.ToString("P");
                    ToolTip.Visibility = Visibility.Visible;
                    await Task.Delay(2000);
                    ToolTip.Visibility = Visibility.Collapsed;
                    break;

                case Windows.System.VirtualKey.Down:
                    if (mediaPlayer.Volume - 0.1 < 0)
                    {
                        mediaPlayer.Volume = 0;
                    }
                    else
                    {
                        mediaPlayer.Volume -= 0.1;
                    }

                    if (mediaPlayer.Volume == 0)
                    {
                        TxtToolTip.Text = "静音";
                    }
                    else
                    {
                        TxtToolTip.Text = "音量:" + mediaPlayer.Volume.ToString("P");
                    }
                    ToolTip.Visibility = Visibility.Visible;
                    await Task.Delay(2000);
                    ToolTip.Visibility = Visibility.Collapsed;
                    break;
                case Windows.System.VirtualKey.Escape:
                    if (isMini) MiniWidnows(false);
                    else if ((playbackWindow ?? App.GetWindowFor(this))?.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen) SetFullScreen(false);
                    else if (isFullWindow) SetFullWindow(false);

                    break;
                case Windows.System.VirtualKey.F8:
                case Windows.System.VirtualKey.T:
                    //小窗播放
                    MiniWidnows(!isMini);

                    break;
                case Windows.System.VirtualKey.F12:
                case Windows.System.VirtualKey.W:
                    SetFullWindow(PlayBtnFullWindow.Visibility == Visibility.Visible);
                    break;
                case Windows.System.VirtualKey.F11:
                case Windows.System.VirtualKey.F:
                case Windows.System.VirtualKey.Enter:
                    SetFullScreen(PlayBtnFullScreen.Visibility == Visibility.Visible);
                    break;
                case Windows.System.VirtualKey.F10:
                    await CaptureVideo();
                    break;
                case Windows.System.VirtualKey.GamepadA:
                    ShowControl(control.Visibility == Visibility.Collapsed);
                    break;

                case Windows.System.VirtualKey.GamepadLeftTrigger:
                    //刷新直播间
                    BottomBtnRefresh_Click(this, null);
                    break;
                case Windows.System.VirtualKey.GamepadB:
                    //退出直播间
                    this.Frame.GoBack();
                    break;
                case Windows.System.VirtualKey.GamepadRightTrigger:
                    //关注/取消关注
                    if (liveRoomVM.IsFavorite)
                    {
                        liveRoomVM.RemoveFavoriteCommand.Execute(null);
                        Utils.ShowMessageToast("已取消关注");
                    }
                    else
                    {
                        liveRoomVM.AddFavoriteCommand.Execute(null);
                        Utils.ShowMessageToast("已添加关注");
                    }

                    break;
                default:
                    break;
            }
        }

        private void LiveRoomVM_ChangedPlayUrl(object sender, string e)
        {
            if (!switchingLine) recovery.Reset();
            else recovery.Cancel();
            _ = SetPlayer(e);
        }
        private async Task<bool> SetPlayer(string url, bool recovering = false)
        {
            var generation = ++playGeneration;
            try
            {
                PlaybackFailure.Visibility = Visibility.Collapsed;
                PlayerLoading.Visibility = Visibility.Visible;
                PlayerLoadText.Text = "加载中";
                if (mediaPlayer != null)
                {
                    mediaPlayer.Pause();
                    mediaPlayer.Source = null;
                }
                if (interopMSS != null)
                {
                    interopMSS.Dispose();
                    interopMSS = null;
                }

                var config = new MediaSourceConfig();
                config.FFmpegOptions.Add("rtsp_transport", "tcp");
                config.FFmpegOptions.Add("rw_timeout", "15000000");
                var decoder = SettingHelper.GetValue<int>(SettingHelper.VIDEO_DECODER, 0);
                switch (decoder)
                {
                    case 1:
                        config.Video.VideoDecoderMode = VideoDecoderMode.ForceSystemDecoder;
                        break;
                    case 2:
                        config.Video.VideoDecoderMode = VideoDecoderMode.ForceFFmpegSoftwareDecoder;
                        break;
                    default:
                        config.Video.VideoDecoderMode = VideoDecoderMode.Automatic;
                        break;
                }
                if (liveRoomVM.SiteName == "哔哩哔哩直播")
                {
                    config.FFmpegOptions.Add("user_agent", "Mozilla/5.0 BiliDroid/1.12.0 (bbcallen@gmail.com)");
                    config.FFmpegOptions.Add("referer", "https://live.bilibili.com/");
                }
                if (liveRoomVM.SiteName == "虎牙直播") config.FFmpegOptions.Add("referer", "https://www.huya.com/");
                if (liveRoomVM.SiteName == "斗鱼直播") config.FFmpegOptions.Add("referer", "https://www.douyu.com/");
                try
                {
                    var source = await FFmpegMediaSource.CreateFromUriAsync(url, config);
                    if (generation != playGeneration || closingWindow || !IsLoaded) { source.Dispose(); return false; }
                    interopMSS = source;
                }
                catch (Exception ex)
                {
                    LogHelper.Log("播放器初始化失败", LogType.ERROR, ex);
                    if (!recovering && generation == playGeneration) PlayError();
                    return false;
                }

                mediaPlayer.AutoPlay = true;
                mediaPlayer.Volume = SliderVolume.Value;
                mediaPlayer.Source = interopMSS.CreateMediaPlaybackItem();
                player.SetMediaPlayer(mediaPlayer);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Log("播放器设置失败", LogType.ERROR, ex);
                if (!recovering && generation == playGeneration) PlayError();
                return false;
            }

        }

        private async void PlayError()
        {
            if (!IsLoaded || closingWindow || recovery.IsRunning || liveRoomVM.CurrentLine == null)
            {
                return;
            }
            // 当前线路播放失败，尝试下一个线路
            var index = liveRoomVM.Lines.IndexOf(liveRoomVM.CurrentLine);
            healthySince = default;
            if (index >= 0 && index < liveRoomVM.Lines.Count - 1)
            {
                switchingLine = true;
                try { liveRoomVM.CurrentLine = liveRoomVM.Lines[index + 1]; }
                finally { switchingLine = false; }
                return;
            }
            var outcome = await recovery.RunAsync(async cancellation =>
            {
                var url = await liveRoomVM.RefreshStreamAsync(cancellation);
                if (url == null) return RecoveryOutcome.Stopped;
                return await SetPlayer(url, true) ? RecoveryOutcome.Recovered : RecoveryOutcome.Failed;
            }, attempt =>
            {
                PlayerLoading.Visibility = Visibility.Visible;
                PlayerLoadText.Text = "正在恢复直播（" + attempt + "/3）";
            });
            if (!IsLoaded || closingWindow || outcome == RecoveryOutcome.Cancelled) return;
            if (outcome == RecoveryOutcome.Stopped)
            {
                playGeneration++;
                mediaPlayer.Pause();
                mediaPlayer.Source = null;
                liveRoomVM.Living = false;
                SetKeepAwake(false);
                interopMSS?.Dispose();
                interopMSS = null;
                PlayerLoading.Visibility = Visibility.Collapsed;
            }
            else if (outcome == RecoveryOutcome.Failed)
            {
                SetKeepAwake(false);
                PlayerLoading.Visibility = Visibility.Collapsed;
                PlaybackFailure.Visibility = Visibility.Visible;
                PlaybackFailureText.Text = recovery.LastError != null ? RequestErrorMessage.Report(recovery.LastError, liveRoomVM.SiteName) : "自动恢复未成功，请手动重试，或在设置中调整解码器。";
            }
        }

        private void StopPlay(bool resetWindowMode = true)
        {
            recovery.Cancel();
            playGeneration++;
            liveRoomVM.CancelPendingPlayback();
            if (mediaPlayer != null)
            {
                mediaPlayer.Pause();
                mediaPlayer.Source = null;
            }
            if (interopMSS != null)
            {
                interopMSS.Dispose();
                interopMSS = null;
            }

            controlTimer.Stop();
            SetPointerVisible(true);

            if (resetWindowMode)
            {
                if (isFullWindow && !closingWindow) SetFullWindow(false);
                SetFullScreen(false);
                MiniWidnows(false);
            }
            //取消屏幕常亮
            if (dispRequest != null)
            {
                try
                {
                    SetKeepAwake(false);
                }
                catch (Exception)
                {
                }

                dispRequest = null;
            }
        }
        private void SetKeepAwake(bool enabled)
        {
            if (displayActive == enabled) return;
            if (enabled) { dispRequest ??= new DisplayRequest(); dispRequest.RequestActive(); }
            else dispRequest?.RequestRelease();
            displayActive = enabled;
        }
        private void ControlTimer_Tick(object sender, object e)
        {
            if (!IsLoaded || XamlRoot == null) return;
            if (recovery.Attempts > 0 && healthySince != default && mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing && DateTime.UtcNow - healthySince >= TimeSpan.FromSeconds(30)) recovery.Reset();
            if (showControlsFlag != -1)
            {
                if (showControlsFlag >= 5)
                {
                    var elent = FocusManager.GetFocusedElement(XamlRoot);
                    if (!IsControlInteractionActive(elent as DependencyObject))
                    {
                        ShowControl(false);
                        showControlsFlag = -1;
                    }
                }
                else
                {
                    showControlsFlag++;
                }
            }
            UpdatePlayerTimers();
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            EndRandomBrowse();
            StopPlay();

            KeyDown -= Page_KeyDown;

            base.OnNavigatingFrom(e);
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.New)
            {
                pageArgs = e.Parameter as PageArgs;
                randomSession = pageArgs.RandomSession;
                UpdateRandomBrowseControls();
                LoadSetting();

                var siteInfo = MainVM.Sites.FirstOrDefault(x => x.LiveSite.Equals(pageArgs.Site));

                liveRoomVM.SiteLogo = siteInfo.Logo;
                liveRoomVM.SiteName = siteInfo.Name;

                var data = pageArgs.Data as LiveRoomItem;
                MessageCenter.ChangeTitle("", pageArgs.Site);

                liveRoomVM.LoadData(pageArgs.Site, data.RoomID);

            }
        }

        private void EndRandomBrowse()
        {
            randomSwitchOperation?.Cancel();
            randomSession?.End();
            UpdateRandomBrowseControls();
        }

        private void UpdateRandomBrowseControls()
        {
            var active = randomSession != null && !randomSession.Ended;
            var busy = randomSwitchOperation != null;
            foreach (var button in new[] { RandomPrevious })
            {
                button.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                button.IsEnabled = active && !busy && randomSession.CanGoPrevious;
            }
            foreach (var button in new[] { RandomNext })
            {
                button.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                button.IsEnabled = active && !busy;
            }
            UpdateToolbarDensity();
        }

        private async void RandomBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (randomSession == null || randomSession.Ended || randomSwitchOperation != null) return;
            var forward = ReferenceEquals(sender, RandomNext) || ReferenceEquals(sender, MiniOverflowNext);
            var session = randomSession;
            var operation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
            var roomChanged = false;
            randomSwitchOperation = operation;
            UpdateRandomBrowseControls();
            ShowControl(true);
            try
            {
                var choice = await session.FindAsync(forward, operation.Token);
                if (!IsLoaded || closingWindow || operation.IsCancellationRequested || session.Ended || session != randomSession) return;
                if (choice == null)
                {
                    Utils.ShowMessageToast(forward ? "暂未找到新的游戏直播，请稍后再试" : "前面的直播间已停播，暂无可返回的房间");
                    return;
                }
                StopPlay(false);
                roomChanged = true;
                recovery.Reset();
                healthySince = default;
                PlaybackFailure.Visibility = Visibility.Collapsed;
                pageArgs.Site = choice.Room.Source;
                pageArgs.Data = choice.Room;
                session.Commit(choice);
                var site = MainVM.Sites.First(s => s.LiveSite == choice.Room.Source);
                liveRoomVM.SiteLogo = site.Logo;
                liveRoomVM.SiteName = site.Name;
                MessageCenter.ChangeTitle("", site.LiveSite);
                PlayerLoading.Visibility = Visibility.Visible;
                PlayerLoadText.Text = "加载中";
                ShowControl(true);
                await liveRoomVM.LoadDataAsync(site.LiveSite, choice.Room.RoomID, operation.Token);
            }
            catch (OperationCanceledException)
            {
                if (roomChanged) liveRoomVM.CancelPendingPlayback();
                if (IsLoaded && !closingWindow && !session.Ended)
                {
                    if (roomChanged)
                    {
                        PlayerLoading.Visibility = Visibility.Collapsed;
                        PlaybackFailureText.Text = "房间加载超时，请重新播放或切换其他房间";
                        PlaybackFailure.Visibility = Visibility.Visible;
                    }
                    Utils.ShowMessageToast("切换超时，请稍后再试");
                }
            }
            catch (Exception ex)
            {
                if (IsLoaded && !closingWindow && !session.Ended) Utils.ShowMessageToast(ex.Message);
            }
            finally
            {
                if (randomSwitchOperation == operation) randomSwitchOperation = null;
                operation.Dispose();
                UpdateRandomBrowseControls();
            }
        }

        private async Task CaptureVideo()
        {
            try
            {
                string fileName = DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".png";
                StorageFolder applicationFolder = await StorageFolder.GetFolderFromPathAsync(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                StorageFolder folder = await applicationFolder.CreateFolderAsync("直播截图", CreationCollisionOption.OpenIfExists);
                StorageFile saveFile = await folder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                RenderTargetBitmap bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(player);
                var pixelBuffer = await bitmap.GetPixelsAsync();
                using (var fileStream = await saveFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fileStream);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                         (uint)bitmap.PixelWidth,
                         (uint)bitmap.PixelHeight,
                         XamlRoot.RasterizationScale * 96,
                         XamlRoot.RasterizationScale * 96,
                         pixelBuffer.ToArray());
                    await encoder.FlushAsync();
                }
                Utils.ShowMessageToast("截图已经保存至图片库");
            }
            catch (Exception)
            {
                Utils.ShowMessageToast("截图失败");
            }
        }

        private void LoadSetting()
        {
            //音量
            SliderVolume.ValueChanged -= PlayerVolumeChanged;
            var volume = SettingHelper.GetValue<double>(SettingHelper.PLAYER_VOLUME, 1.0);
            mediaPlayer.Volume = volume;
            SliderVolume.Value = volume;
            SliderVolume.ValueChanged += PlayerVolumeChanged;
            //亮度
            SliderBrightness.ValueChanged -= PlayerBrightnessChanged;
            var savedBrightness = SettingHelper.GetValue<double>(SettingHelper.PLAYER_BRIGHTNESS, 0);
            var brightness = double.IsFinite(savedBrightness) ? Math.Clamp(savedBrightness, 0, 1) : 0;
            BrightnessShield.Opacity = brightness;
            SliderBrightness.Value = (1 - brightness) * 100;
            BrightnessValue.Text = $"{SliderBrightness.Value:0}%";
            SliderBrightness.ValueChanged += PlayerBrightnessChanged;

        }

        private void PlayerVolumeChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            mediaPlayer.Volume = SliderVolume.Value;
            SettingHelper.SetValue(SettingHelper.PLAYER_VOLUME, SliderVolume.Value, defer: true);
        }

        #region 手势
        int showControlsFlag = 0;
        bool pointer_in_player = false;

        private void PlayerBrightnessChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            var brightness = 1 - SliderBrightness.Value / 100;
            BrightnessShield.Opacity = brightness;
            BrightnessValue.Text = $"{SliderBrightness.Value:0}%";
            SettingHelper.SetValue(SettingHelper.PLAYER_BRIGHTNESS, brightness, defer: true);
        }
        private void ShowControl(bool show)
        {
            if (draggingVideo) return;
            show = !floatingToolbar || show || toolbarPopupCount > 0;
            showControlsFlag = UsesExternalToolbar ? -1 : show ? 0 : -1;
            UpdatePlayerTimers();
            SetPointerVisible(UsesExternalToolbar || show || !pointer_in_player);
            if (show && control.Visibility == Visibility.Visible && !toolbarHiding) return;
            if (!show && (control.Visibility == Visibility.Collapsed || toolbarHiding)) return;
            var from = control.Visibility == Visibility.Collapsed ? 0 : control.Opacity;
            if (UsesExternalToolbar && show) FinishToolbarVisibility(true);
            toolbarFade?.Stop();
            toolbarFade = null;
            toolbarHiding = !show;
            var generation = ++toolbarFadeGeneration;
            if (!floatingToolbar || !toolbarUISettings.AnimationsEnabled)
            {
                control.Opacity = 1;
                FinishToolbarVisibility(show);
                toolbarHiding = false;
                return;
            }
            control.Visibility = Visibility.Visible;
            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = from, To = show ? 1 : 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150))
            };
            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, control);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
            storyboard.Completed += (_, _) =>
            {
                if (generation != toolbarFadeGeneration) return;
                FinishToolbarVisibility(show);
                storyboard.Stop();
                control.Opacity = 1;
                toolbarHiding = false;
                toolbarFade = null;
            };
            toolbarFade = storyboard;
            storyboard.Begin();
        }

        private void ToolbarPopupOpened()
        {
            if (!IsLoaded || closingWindow) return;
            toolbarPopupCount++;
            ShowControl(true);
        }
        private void ToolbarPopupClosed()
        {
            toolbarPopupCount = Math.Max(0, toolbarPopupCount - 1);
            miniPointerLeftAt = null;
            if (IsLoaded && !closingWindow) ShowControl(true);
        }
        private void UpdateToolbarDensity()
        {
            var availableWidth = Math.Min(VideoSurface.ActualWidth, control.ActualWidth > 0 ? control.ActualWidth : VideoSurface.ActualWidth);
            if (availableWidth <= 0) return;
            var overflow = availableWidth < 1060;
            var padding = availableWidth >= 1100 ? 24 : 16;
            var spacing = availableWidth >= 1100 ? 12 : 10;
            var volumeWidth = availableWidth >= 1250 ? 84 : 40;
            var random = RandomNext.Visibility == Visibility.Visible;
            var playWidth = random ? 132 : 40;
            var soundWidth = 2 * volumeWidth + 12;
            var rightWidth = overflow ? 180 + 2 * spacing : 256 + 4 * spacing;
            var optionGap = availableWidth >= 1100 ? 16 : 12;
            var middleWidth = availableWidth - 2 * padding - 72 - playWidth - soundWidth - rightWidth;
            var compact = isMini || middleWidth < 256 + optionGap;
            var optionWidth = Math.Clamp(Math.Min(availableWidth * .16, (middleWidth - optionGap) / 2), 128, 190);
            QualityFrame.Width = LineFrame.Width = optionWidth;
            QualityLabel.Visibility = LineLabel.Visibility = optionWidth < 144 ? Visibility.Collapsed : Visibility.Visible;
            PlayBtnVolume.Width = PlayBtnBrightness.Width = volumeWidth;
            PlayBtnVolume.Padding = PlayBtnBrightness.Padding = volumeWidth == 40 ? new Thickness(0) : new Thickness(10, 0, 10, 0);
            VolumeLabel.Visibility = BrightnessLabel.Visibility = volumeWidth == 40 ? Visibility.Collapsed : Visibility.Visible;
            FavoriteActions.Visibility = BottomBtnRefresh.Visibility = overflow ? Visibility.Collapsed : Visibility.Visible;
            control.MaxWidth = double.PositiveInfinity;
            PlayControls.Spacing = 10;
            SoundControls.Spacing = 12;
            PlaybackOptions.Spacing = optionGap;
            RoomActions.Spacing = spacing;
            StandardToolbarLayout.Padding = new Thickness(padding, 0, padding, 0);
            StandardControl.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            MiniControl.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            BottomBtnExitMiniWindows.Visibility = UsesExternalToolbar ? Visibility.Visible : Visibility.Collapsed;
            // Keep the small rail to three groups with full-size buttons.
            MiniOverflowPrevious.Visibility = MiniOverflowNext.Visibility = compact && random ? Visibility.Visible : Visibility.Collapsed;
            MiniOverflowPrevious.IsEnabled = RandomPrevious.IsEnabled;
            MiniOverflowNext.IsEnabled = RandomNext.IsEnabled;
        }
        private void WindowSizeMenu_Opening(object sender, object e) => PopulateSizeMenu(WindowSizeMenu.Items);
        private void PopulateSizeMenu(IList<MenuFlyoutItemBase> items)
        {
            items.Clear();
            var window = playbackWindow ?? App.GetWindowFor(this);
            foreach (var size in MainWindow.WindowSizeOptions)
            {
                var item = new ToggleMenuFlyoutItem { Text = size, IsChecked = size == window?.SelectedWindowSize };
                item.Click += (_, _) => App.SetWindowSize(size);
                items.Add(item);
            }
        }
        private void DisplayModeMenu_Opening(object sender, object e)
        {
            var full = (playbackWindow ?? App.GetWindowFor(this))?.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            NormalWindowItem.IsChecked = !full && !isFullWindow && !isMini;
            PlayBtnFullWindow.IsChecked = false; PlayBtnExitFullWindow.IsChecked = isFullWindow;
            PlayBtnFullScreen.IsChecked = false; PlayBtnExitFullScreen.IsChecked = full;
            PlayBtnMinWindow.IsChecked = isMini;
        }
        private void NormalWindow_Click(object sender, RoutedEventArgs e) { SetWindowMode(AppWindowPresenterKind.Default); SetFullWindow(false); }
        private void PlayerMoreMenu_Opening(object sender, object e)
        {
            OverflowFavorite.Text = liveRoomVM.IsFavorite ? "取消关注" : "关注";
            OverflowFavorite.Visibility = OverflowRefresh.Visibility = FavoriteActions.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
            OverflowSeparator.Visibility = OverflowFavorite.Visibility;
        }
        private void MiniVolumeButton_Click(object sender, RoutedEventArgs e) => PlayBtnVolume.Flyout.ShowAt(MiniVolumeButton);
        private void MiniBrightnessButton_Click(object sender, RoutedEventArgs e) => PlayBtnBrightness.Flyout.ShowAt(MiniBrightnessButton);
        private void MiniFavoriteItem_Click(object sender, RoutedEventArgs e)
        {
            if (liveRoomVM.IsFavorite) liveRoomVM.RemoveFavoriteCommand.Execute(null);
            else liveRoomVM.AddFavoriteCommand.Execute(null);
        }
        private void MiniMoreMenu_Opening(object sender, object e)
        {
            MiniFavoriteItem.Text = liveRoomVM.IsFavorite ? "取消关注" : "关注";
            MiniQualityMenu.Items.Clear();
            foreach (var quality in liveRoomVM.Qualities ?? new List<LivePlayQuality>())
            {
                var item = new ToggleMenuFlyoutItem { Text = quality.Quality, IsChecked = quality == liveRoomVM.CurrentQuality };
                item.Click += (_, _) => liveRoomVM.CurrentQuality = quality;
                MiniQualityMenu.Items.Add(item);
            }
            MiniQualityMenu.IsEnabled = MiniQualityMenu.Items.Count > 0;
            MiniLineMenu.Items.Clear();
            foreach (var line in liveRoomVM.Lines ?? new List<PlayurlLine>())
            {
                var item = new ToggleMenuFlyoutItem { Text = line.Name, IsChecked = line == liveRoomVM.CurrentLine };
                item.Click += (_, _) => liveRoomVM.CurrentLine = line;
                MiniLineMenu.Items.Add(item);
            }
            MiniLineMenu.IsEnabled = MiniLineMenu.Items.Count > 0;
            PopulateSizeMenu(MiniSizeMenu.Items);
            MiniModeMenu.Items.Clear();
            var full = (playbackWindow ?? App.GetWindowFor(this))?.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            MiniSizeMenu.IsEnabled = !full;
            var fullItem = new MenuFlyoutItem { Text = full ? "退出全屏" : "全屏" };
            fullItem.Click += (_, _) => SetFullScreen(!full);
            MiniModeMenu.Items.Add(fullItem);
            if (!isMini)
            {
                var windowItem = new MenuFlyoutItem { Text = isFullWindow ? "退出窗口内全屏" : "窗口内全屏" };
                windowItem.Click += (_, _) => SetFullWindow(!isFullWindow);
                MiniModeMenu.Items.Add(windowItem);
            }
            var miniItem = new MenuFlyoutItem { Text = isMini ? "恢复窗口" : "置顶小窗" };
            miniItem.Click += (_, _) => MiniWidnows(!isMini);
            MiniModeMenu.Items.Add(miniItem);
        }
        private void Grid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (IsInteractiveSource(e?.OriginalSource as DependencyObject)) return;
            if (e != null) e.Handled = true;
            if (isMini)
            {
                MiniWidnows(false);
                return;
            }

            if (PlayBtnFullScreen.Visibility == Visibility.Visible)
            {
                PlayBtnFullScreen_Click(sender, null);
            }
            else
            {

                PlayBtnExitFullScreen_Click(sender, null);
            }
        }
        private void Grid_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            pointer_in_player = true;
            if (UsesExternalToolbar) ShowControl(true);
        }
        private void VideoSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(VideoSurface);
            if (!IsInteractiveSource(e.OriginalSource as DependencyObject) && point.Properties.IsLeftButtonPressed &&
                !point.Properties.IsRightButtonPressed && !point.Properties.IsMiddleButtonPressed)
                BtnFoucs.Focus(FocusState.Programmatic);
        }
        private void PlaybackWindow_VideoDoubleClicked(object sender, EventArgs e) => Grid_DoubleTapped(null, null);
        private void PlaybackWindow_VideoPointerMoved(object sender, EventArgs e) => ShowControl(true);
        private void PlaybackWindow_VideoDragStarted(object sender, EventArgs e)
        {
            if (closingWindow) return;
            draggingVideo = true;
            BtnFoucs.Focus(FocusState.Programmatic);
            miniHoverTimer.Stop();
            toolbarFadeGeneration++; toolbarFade?.Stop(); toolbarFade = null;
            toolbarHiding = false; control.Opacity = 1;
            FinishToolbarVisibility(false);
        }
        private void PlaybackWindow_VideoDragCompleted(object sender, EventArgs e)
        {
            draggingVideo = false; miniPointerLeftAt = null;
            UpdatePlayerTimers(updateHover: true);
        }

        private void Grid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!IsLoaded || XamlRoot == null) return;
            if (e != null && sender is FrameworkElement surface)
            {
                var point = e.GetCurrentPoint(surface).Position;
                if (point.X >= 0 && point.Y >= 0 && point.X < surface.ActualWidth && point.Y < surface.ActualHeight) return;
            }
            pointer_in_player = false;
            SetPointerVisible(true);
            if (UsesExternalToolbar) return; // Include the expanded rail and allow time to reach it.
            if (!IsControlInteractionActive(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject)) ShowControl(false);
        }

        private void Grid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            ShowControl(true);
            SetPointerVisible(true);
        }
        private static bool IsInteractiveSource(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ButtonBase || source is Slider || source is ComboBox || source is TextBox || source is MenuFlyoutItem) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }
        private static bool IsControlInteractionActive(DependencyObject source) => source is TextBox || source is AutoSuggestBox || source is MenuFlyoutItem ||
            (source is ComboBox combo && combo.IsDropDownOpen) || (source is Slider slider && slider.PointerCaptures.Count > 0);
        internal void TogglePause()
        {
            if (mediaPlayer.Source == null) return;
            if (mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Paused) mediaPlayer.Play();
            else mediaPlayer.Pause();
        }
        #endregion
        #region 窗口操作
        private void PlayBtnFullScreen_Click(object sender, RoutedEventArgs e)
        {
            SetFullScreen(true);
        }

        private void PlayBtnExitFullScreen_Click(object sender, RoutedEventArgs e)
        {
            SetFullScreen(false);
        }

        private void PlayBtnExitFullWindow_Click(object sender, RoutedEventArgs e)
        {
            SetFullWindow(false);
        }

        private void PlayBtnFullWindow_Click(object sender, RoutedEventArgs e)
        {
            SetFullWindow(true);
        }

        private void PlayBtnMinWindow_Click(object sender, RoutedEventArgs e)
        {
            MiniWidnows(true);
        }
        private void SetFullWindow(bool full)
        {
            if (isFullWindow == full) return;
            var window = playbackWindow ?? App.GetWindowFor(this);
            if (window?.IsMiniMode == true || window?.AppWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped)
                window?.SetWindowMode(AppWindowPresenterKind.Default);
            window?.SetContentExpanded(full);
            toolbarFadeGeneration++;
            toolbarFade?.Stop(); toolbarFade = null; toolbarHiding = false;
            control.Opacity = 1; control.Visibility = Visibility.Collapsed;
            miniPointerLeftAt = null;
            isFullWindow = full;
            UpdateWindowModeUI();
        }
        private void SetFullScreen(bool full) => SetWindowMode(full ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        private void MiniWidnows(bool mini) => SetWindowMode(mini ? AppWindowPresenterKind.CompactOverlay : AppWindowPresenterKind.Default);
        private void SetWindowMode(AppWindowPresenterKind kind)
        {
            if (closingWindow) return;
            App.GetWindowFor(this).SetWindowMode(kind);
            UpdateWindowModeUI();
        }
        private void UpdateWindowModeUI()
        {
            var window = playbackWindow ?? App.GetWindowFor(this);
            if (window == null) return;
            var full = window.AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            var wasMini = isMini;
            isMini = window.IsMiniMode;
            if (wasMini != isMini)
            {
                toolbarFadeGeneration++;
                toolbarFade?.Stop();
                toolbarFade = null;
                toolbarHiding = false;
                control.Opacity = 1;
                control.Visibility = Visibility.Collapsed;
                miniPointerLeftAt = null;
            }
            HideTitleBar(full || isMini || isFullWindow);
            PlayBtnFullScreen.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            PlayBtnExitFullScreen.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
            WindowSizeButton.IsEnabled = !full;
            PlayBtnFullWindow.Visibility = isFullWindow ? Visibility.Collapsed : Visibility.Visible;
            PlayBtnExitFullWindow.Visibility = isFullWindow ? Visibility.Visible : Visibility.Collapsed;
            TopMiniToolbar.Height = new GridLength(0);
            BottomInfo.Height = full || isMini || isFullWindow ? new GridLength(0) : GridLength.Auto;
            floatingToolbar = full || isMini || isFullWindow;
            Grid.SetRow(control, floatingToolbar && !UsesExternalToolbar ? 1 : 2);
            control.VerticalAlignment = floatingToolbar && !UsesExternalToolbar ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            control.MinHeight = isMini ? 44 : 60;
            control.Margin = floatingToolbar && !UsesExternalToolbar ? new Thickness(12, 0, 12, 12) : new Thickness(0);
            control.RequestedTheme = floatingToolbar && !UsesExternalToolbar ? ElementTheme.Dark : ElementTheme.Default;
            DockToolbarBackground.Visibility = floatingToolbar && !UsesExternalToolbar ? Visibility.Collapsed : Visibility.Visible;
            DockToolbarBackground.BorderThickness = new Thickness(0, 1, 0, 0);
            FloatingToolbarBackground.Visibility = floatingToolbar && !UsesExternalToolbar ? Visibility.Visible : Visibility.Collapsed;
            UpdateToolbarDensity();
            if (UsesExternalToolbar)
            {
                ApplyMiniToolbarLayout();
            }
            ShowControl(!UsesExternalToolbar || toolbarPopupCount > 0 || window.IsPointerInside());
            UpdatePlayerTimers(updateHover: true);
            window.RefreshVideoDragRegions(force: true);
        }
        private void BottomBtnExitMiniWindows_Click(object sender, RoutedEventArgs e)
        {
            if (isMini) MiniWidnows(false);
            else SetFullWindow(false);
        }
        private void ClosePlaybackWindow_Click(object sender, RoutedEventArgs e) => (playbackWindow ?? App.GetWindowFor(this))?.Close();

        private async void PlayTopBtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            await CaptureVideo();
        }

        private void PlayBtnPlay_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Play();
        }

        private void PlayBtnPause_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Pause();
        }

        #endregion

        private void BottomBtnShare_Click(object sender, RoutedEventArgs e)
        {
            if (liveRoomVM.detail == null)
            {
                return;
            }
            Utils.SetClipboard(liveRoomVM.detail.Url);
            Utils.ShowMessageToast("已复制链接到剪切板");
        }

        private async void BottomBtnOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (liveRoomVM.detail == null)
            {
                return;
            }
            await Windows.System.Launcher.LaunchUriAsync(new Uri(liveRoomVM.detail.Url));
        }

        private void BottomBtnPlayUrl_Click(object sender, RoutedEventArgs e)
        {
            if (liveRoomVM.CurrentLine == null)
            {
                return;
            }
            Utils.SetClipboard(liveRoomVM.CurrentLine.Url);
            Utils.ShowMessageToast("已复制链接到剪切板");
        }

        private void BottomBtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            recovery.Reset();
            PlaybackFailure.Visibility = Visibility.Collapsed;
            if (liveRoomVM.Loading) return;
            if (mediaPlayer != null)
            {
                mediaPlayer.Pause();
                mediaPlayer.Source = null;
            }
            if (interopMSS != null)
            {
                interopMSS.Dispose();
                interopMSS = null;
            }

            liveRoomVM.LoadData(pageArgs.Site, liveRoomVM.RoomID);
        }

        private void PlayerGrid_SizeChanged(object sender, SizeChangedEventArgs args)
        {
            ((Grid)sender).Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height) };
            UpdateToolbarDensity();
        }
    }
}
