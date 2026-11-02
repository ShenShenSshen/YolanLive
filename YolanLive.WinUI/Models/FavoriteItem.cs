using YolanLive.WinUI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace YolanLive.WinUI.Models
{
    public class FavoriteItem: BaseNotifyPropertyChanged
    {
        public int ID { get; set; }
        public string RoomID { get; set; }
        private string userName, photo, siteName;
        public string UserName { get => userName; set { if (userName == value) return; userName = value; DoPropertyChanged(nameof(UserName)); } }
        public string Photo { get => photo; set { if (photo == value) return; photo = value; DoPropertyChanged(nameof(Photo)); } }
        public string SiteName { get => siteName; set { if (siteName == value) return; siteName = value; DoPropertyChanged(nameof(SiteName)); } }


        private bool? confirmedLiveStatus;
        // Runtime status only; the saved Favorite table and import/export format stay unchanged.
        public bool? ConfirmedLiveStatus
        {
            get => confirmedLiveStatus;
            set
            {
                if (confirmedLiveStatus == value) return;
                confirmedLiveStatus = value;
                DoPropertyChanged(nameof(ConfirmedLiveStatus));
                DoPropertyChanged(nameof(LiveStatus));
                DoPropertyChanged(nameof(IsOffline));
                DoPropertyChanged(nameof(IsStatusUnknown));
            }
        }
        public bool LiveStatus
        {
            get => ConfirmedLiveStatus == true;
            set => ConfirmedLiveStatus = value;
        }
        public bool IsOffline => ConfirmedLiveStatus == false;
        public bool IsStatusUnknown => !ConfirmedLiveStatus.HasValue;

    }

    public sealed class FavoriteGroup(string title) : ObservableCollection<FavoriteItem>
    {
        public string Title { get; } = title;
        public string Header => $"{Title}（{Count}）";
        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnCollectionChanged(e);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Header)));
        }
    }
}
