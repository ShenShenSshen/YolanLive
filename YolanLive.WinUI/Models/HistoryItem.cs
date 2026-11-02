using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YolanLive.WinUI.Models
{
    public class HistoryItem : ViewModels.BaseNotifyPropertyChanged
    {
        public int ID { get; set; }
        public string RoomID { get; set; }
        private string userName, photo, siteName;
        private DateTime watchTime;
        public string UserName { get => userName; set { userName = value; DoPropertyChanged(nameof(UserName)); } }
        public string Photo { get => photo; set { photo = value; DoPropertyChanged(nameof(Photo)); } }
        public string SiteName { get => siteName; set { siteName = value; DoPropertyChanged(nameof(SiteName)); DoPropertyChanged(nameof(PlatformLabel)); } }
        public string PlatformLabel => SiteName switch { "哔哩哔哩直播" => "哔哩哔哩", "抖音直播" => "抖音", _ => SiteName };
        public DateTime WatchTime { get => watchTime; set { watchTime = value; DoPropertyChanged(nameof(WatchTime)); DoPropertyChanged(nameof(DisplayTime)); } }
        public string DisplayTime => WatchTime.Date >= DateTime.Today.AddDays(-1) ? WatchTime.ToString("HH:mm") : WatchTime.ToString("yyyy-MM-dd HH:mm");
    }

    public sealed class HistoryGroup(string title) : System.Collections.ObjectModel.ObservableCollection<HistoryItem>
    {
        public string Title { get; } = title;
    }
}
