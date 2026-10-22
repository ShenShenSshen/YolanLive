using YolanLive.WinUI.Helper;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace YolanLive.WinUI.ViewModels
{
    public class BaseNotifyPropertyChanged:INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public virtual void DoPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    public class BaseViewModel : BaseNotifyPropertyChanged
    {
        public BaseViewModel()
        {
            RefreshCommand = new RelayCommand(Refresh);
            LoadMoreCommand = new RelayCommand(LoadMore);
        }
        public ICommand LoadMoreCommand { get;  set; }
        public ICommand RefreshCommand { get;  set; }

        public int Page { get; set; } = 1;

        private bool _loading;
        private bool _canRetry;
        private string _statusMessage = "";
        public bool CanRefresh => !Loading;
        public bool CanRetry { get => _canRetry && !Loading; protected set { _canRetry = value; DoPropertyChanged(nameof(CanRetry)); } }
        public string StatusMessage
        {
            get => _statusMessage;
            protected set { _statusMessage = value; DoPropertyChanged(nameof(StatusMessage)); DoPropertyChanged(nameof(ShowInlineStatus)); }
        }
        public bool ShowInlineStatus => !IsEmpty && !Loading && !string.IsNullOrEmpty(StatusMessage);
        public bool ShowEmptyStatus => IsEmpty && !Loading;
        public bool ShowLoadingPlaceholder => IsEmpty && Loading;
        public bool Loading
        {
            get { return _loading; }
            set { _loading = value; DoPropertyChanged("Loading"); DoPropertyChanged(nameof(CanRefresh)); DoPropertyChanged(nameof(CanRetry)); DoPropertyChanged(nameof(ShowInlineStatus)); DoPropertyChanged(nameof(ShowEmptyStatus)); DoPropertyChanged(nameof(ShowLoadingPlaceholder)); }
        }

        private bool _canLoadMore;
        public bool CanLoadMore
        {
            get { return _canLoadMore; }
            set { _canLoadMore = value; DoPropertyChanged("CanLoadMore"); }
        }

        

        private bool _empty=false;
        public bool IsEmpty
        {
            get { return _empty; }
            set { _empty = value; DoPropertyChanged("IsEmpty"); DoPropertyChanged(nameof(ShowInlineStatus)); DoPropertyChanged(nameof(ShowEmptyStatus)); DoPropertyChanged(nameof(ShowLoadingPlaceholder)); }
        }


        public virtual void Refresh()
        {
            Page = 1;
        }
        public virtual void LoadMore()
        {

        }

        public virtual void HandleError(Exception ex,string message="出现错误，已记录")
        {
            if (LogHelper.IsNetworkError(ex))
            {
                Utils.ShowMessageToast("请检查网络连接情况");
            }
            else
            {
                LogHelper.Log(ex.Message, LogType.ERROR, ex);
                Utils.ShowMessageToast(message);
            }
        }

    }
}
