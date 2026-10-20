using YolanLive.Core.Interface;
using YolanLive.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using YolanLive.WinUI.Helper;
using System.Windows.Input;

namespace YolanLive.WinUI.ViewModels
{
    public class LiveRoomVM : BaseViewModel
    {
        public event EventHandler<string> ChangedPlayUrl;
        public LiveRoomVM()
        {
            AddFavoriteCommand = new RelayCommand(AddFavorite);
            RemoveFavoriteCommand = new RelayCommand(RemoveFavorite);

        }
        public ICommand AddFavoriteCommand { get; set; }
        public ICommand RemoveFavoriteCommand { get; set; }



        ILiveSite Site;

        private string _siteLogo = "ms-appx:///Assets/Placeholder/Placeholder1x1.png";

        public string SiteLogo
        {
            get { return _siteLogo; }
            set { _siteLogo = value; DoPropertyChanged("SiteLogo"); }
        }

        private string _siteName;
        public string SiteName
        {
            get { return _siteName; }
            set { _siteName = value; DoPropertyChanged("SiteName"); }
        }

        private bool _isFavorite = false;
        public bool IsFavorite
        {
            get { return _isFavorite; }
            set { _isFavorite = value; DoPropertyChanged("IsFavorite"); }
        }

        private long? FavoriteID { get; set; }

        object RoomId;
        public LiveRoomDetail detail { get; set; }

        private long _Online = 0;
        public long Online
        {
            get { return _Online; }
            set { _Online = value; DoPropertyChanged("Online"); }
        }
        private string _RoomID;

        public string RoomID
        {
            get { return _RoomID; }
            set { _RoomID = value; DoPropertyChanged("RoomID"); }
        }


        private string _photo = "ms-appx:///Assets/Placeholder/Placeholder1x1.png";
        public string Photo
        {
            get { return _photo; }
            set { _photo = value; DoPropertyChanged("Photo"); }
        }

        private string _name;
        public string Name
        {
            get { return _name; }
            set { _name = value; DoPropertyChanged("Name"); }
        }
        private string _title;
        public string Title
        {
            get { return _title; }
            set { _title = value; DoPropertyChanged("Title"); }
        }
        private bool _living = true;

        public bool Living
        {
            get { return _living; }
            set { _living = value; DoPropertyChanged("Living"); }
        }


        private List<LivePlayQuality> qualities;
        public List<LivePlayQuality> Qualities
        {
            get { return qualities; }
            set { qualities = value; DoPropertyChanged("Qualities"); }
        }

        private LivePlayQuality currentQuality;
        private bool restoringQuality;
        private bool suppressPlayback;
        private int loadGeneration, urlGeneration;
        private CancellationTokenSource loadCancellation, urlCancellation;
        public LivePlayQuality CurrentQuality
        {
            get { return currentQuality; }
            set
            {
                if (value == null || value == currentQuality)
                {
                    return;
                }
                currentQuality = value;
                if (!restoringQuality && Site != null) SettingHelper.SetValue(SettingHelper.PREFERRED_QUALITY + Site.Name, value.Quality);
                DoPropertyChanged("CurrentQuality");
                if (!suppressPlayback) LoadPlayUrl();
            }

        }

        private List<PlayurlLine> lines;
        public List<PlayurlLine> Lines
        {
            get { return lines; }
            set { lines = value; DoPropertyChanged("Lines"); }
        }


        private PlayurlLine currentLine;
        public PlayurlLine CurrentLine
        {
            get { return currentLine; }
            set
            {
                if (value == null || value == currentLine)
                {
                    return;
                }
                currentLine = value;
                DoPropertyChanged("CurrentLine");
                if (!suppressPlayback) ChangedPlayUrl?.Invoke(this, value.Url);
            }

        }


        public async void LoadData(ILiveSite site, object roomId) => await LoadDataAsync(site, roomId);
        internal async Task LoadDataAsync(ILiveSite site, object roomId, CancellationToken cancellationToken = default)
        {
            CancelPendingPlayback();
            var generation = ++loadGeneration;
            var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operation.CancelAfter(TimeSpan.FromSeconds(40));
            loadCancellation = operation;
            try
            {
                Loading = true;
                Site = site;
                RoomID = roomId?.ToString();
                detail = null;
                currentQuality = null;
                currentLine = null;
                Qualities = null;
                Lines = null;
                DoPropertyChanged(nameof(CurrentQuality));
                DoPropertyChanged(nameof(CurrentLine));
                Photo = "";

                RoomId = roomId;
                var result = await site.GetRoomDetail(roomId, operation.Token);
                if (generation != loadGeneration) return;
                detail = result;
                RoomID = result.RoomID;

                Online = result.Online;
                Title = result.Title;
                SetWindowTitle();

                Name = result.UserName;
                MessageCenter.ChangeTitle(Title + " - " + Name, Site);
                if (!string.IsNullOrEmpty(result.UserAvatar))
                {
                    Photo = result.UserAvatar;
                }
                Living = result.Status;
                //检查收藏情况
                FavoriteID = DatabaseHelper.CheckFavorite(RoomID, Site.Name);
                IsFavorite = FavoriteID != null;

                if (detail.Status)
                {
                    var qualities = await site.GetPlayQuality(result, operation.Token);
                    if (generation != loadGeneration) return;
                    Qualities = qualities;
                    if (Qualities != null && Qualities.Count > 0)
                    {
                        restoringQuality = true;
                        try { CurrentQuality = GetPreferredQuality(Site, Qualities); }
                        finally { restoringQuality = false; }
                    }
                    // var u = await Site.GetPlayUrls(result, q[0]);
                    //ChangedPlayUrl?.Invoke(this, u[0]);
                }
                DatabaseHelper.AddHistory(new Models.HistoryItem()
                {
                    Photo = Photo,
                    RoomID = RoomID,
                    SiteName = Site.Name,
                    UserName = Name
                });

            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            { if (generation == loadGeneration && !cancellationToken.IsCancellationRequested) HandleError(new TimeoutException(), "房间加载超时，请重试"); }
            catch (Exception ex)
            {
                if (generation == loadGeneration) HandleError(ex, RequestErrorMessage.Report(ex, site.Name));
            }
            finally
            {
                if (generation == loadGeneration) Loading = false;
                if (loadCancellation == operation) loadCancellation = null;
                operation.Dispose();
            }
        }

        internal static LivePlayQuality GetPreferredQuality(ILiveSite site, List<LivePlayQuality> available)
        {
            var preference = SettingHelper.GetValue(SettingHelper.PREFERRED_QUALITY + site.Name, "");
            return available.FirstOrDefault(q => q.Quality == preference) ?? available.FirstOrDefault();
        }
        private void SetWindowTitle()
        {
            if (SettingHelper.GetValue(SettingHelper.NEW_WINDOW_LIVEROOM, false))
            {
                App.ActiveWindow.Title = $"{Title} - {SiteName}";
            }
        }

        private void AddFavorite()
        {
            if (Site == null || RoomID == null || RoomID == "0" || RoomID == "") return;
            DatabaseHelper.AddFavorite(new Models.FavoriteItem()
            {
                Photo = Photo,
                RoomID = RoomID,
                SiteName = Site.Name,
                UserName = Name
            });
            FavoriteID = DatabaseHelper.CheckFavorite(RoomID, Site.Name);
            IsFavorite = FavoriteID.HasValue;
            MessageCenter.UpdateFavorite();
        }
        private void RemoveFavorite()
        {
            if (Site == null || string.IsNullOrEmpty(RoomID)) return;
            FavoriteID = DatabaseHelper.CheckFavorite(RoomID, Site.Name);
            if (FavoriteID.HasValue) DatabaseHelper.DeleteFavorite(FavoriteID.Value);
            FavoriteID = null;
            IsFavorite = false;
            MessageCenter.UpdateFavorite();
        }

        public async void LoadPlayUrl()
        {
            urlCancellation?.Cancel();
            var operation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            urlCancellation = operation;
            var generation = ++urlGeneration;
            try
            {
                var data = await Site.GetPlayUrls(detail, CurrentQuality, operation.Token);
                if (generation != urlGeneration) return;
                if (data.Count == 0)
                {
                    Utils.ShowMessageToast("加载播放地址失败");
                    return;
                }
                List<PlayurlLine> ls = new List<PlayurlLine>();
                for (int i = 0; i < data.Count; i++)
                {
                    ls.Add(new PlayurlLine()
                    {
                        Name = $"线路{i + 1}",
                        Url = data[i]
                    });
                }

                Lines = ls;
                CurrentLine = Lines[0];
            }
            catch (OperationCanceledException) when (operation.IsCancellationRequested)
            { if (generation == urlGeneration) Utils.ShowMessageToast("播放地址加载超时，请重试"); }
            catch (Exception ex)
            {
                if (generation == urlGeneration) Utils.ShowMessageToast(RequestErrorMessage.Report(ex, Site.Name));
            }
            finally { if (urlCancellation == operation) urlCancellation = null; operation.Dispose(); }




        }
        internal void CancelPendingPlayback() { loadGeneration++; urlGeneration++; loadCancellation?.Cancel(); urlCancellation?.Cancel(); Loading = false; }
        internal async Task<string> RefreshStreamAsync(CancellationToken cancellation)
        {
            if (Site == null || RoomId == null) throw new InvalidOperationException("没有正在播放的直播间");
            var generation = loadGeneration;
            var refreshed = await Site.GetRoomDetail(RoomId, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (generation != loadGeneration) throw new OperationCanceledException(cancellation);
            Living = refreshed.Status;
            if (!Living) return null;
            var available = await Site.GetPlayQuality(refreshed, cancellation);
            var quality = available.FirstOrDefault(q => q.Quality == CurrentQuality?.Quality) ?? GetPreferredQuality(Site, available);
            if (quality == null) throw new InvalidOperationException("平台未返回可用画质");
            var urls = await Site.GetPlayUrls(refreshed, quality, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (generation != loadGeneration) throw new OperationCanceledException(cancellation);
            if (urls.Count == 0) throw new InvalidOperationException("平台未返回播放地址");
            urlGeneration++;
            detail = refreshed;
            restoringQuality = suppressPlayback = true;
            try
            {
                Qualities = available;
                CurrentQuality = quality;
                Lines = urls.Select((url, index) => new PlayurlLine { Url = url, Name = "线路" + (index + 1) }).ToList();
                CurrentLine = Lines[0];
            }
            finally { restoringQuality = suppressPlayback = false; }
            return urls[0];
        }
    }

    public class PlayurlLine
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }
}
