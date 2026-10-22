using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace YolanLive.WinUI.ViewModels
{
    public class HistoryVM : BaseViewModel
    {
        public HistoryVM()
        {
            Items = new ObservableCollection<HistoryItem>();
            CleanCommand = new RelayCommand(Clean);
        }
        public ICommand CleanCommand { get; set; }

        public ObservableCollection<HistoryItem> Items { get; set; }
        private string emptyMessage = "还没有观看记录";
        public string EmptyMessage { get => emptyMessage; private set { emptyMessage = value; DoPropertyChanged(nameof(EmptyMessage)); } }

        public async void LoadData() => await LoadAsync();
        public async Task LoadAsync()
        {
            if (Loading) return;
            CanRetry = false; StatusMessage = "";
            IsEmpty = Items.Count == 0;
            try
            {
                Loading = true;
                var history = await DatabaseHelper.GetHistory();
                Items.Clear();
                foreach (var item in history)
                {
                    Items.Add(item);
                }
                IsEmpty = Items.Count == 0;
                EmptyMessage = "还没有观看记录";
            }
            catch (Exception ex)
            {
                EmptyMessage = "观看历史加载失败，请重试"; CanRetry = true;
                StatusMessage = Items.Count > 0 ? "加载失败，保留上次内容。" + EmptyMessage : "";
                IsEmpty = Items.Count == 0;
                LogHelper.Log("历史读取失败", LogType.ERROR, ex);
            }
            finally
            {
                Loading = false;
            }
        }

        public override void Refresh()
        {
            base.Refresh();
            LoadData();
        }

        public void RemoveItem(HistoryItem item)
        {
            try
            {
                DatabaseHelper.DeleteHistory(item.ID);
                Items.Remove(item);
                IsEmpty = Items.Count == 0;
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }

        }
        public async void Clean()
        {
            try
            {

                var result = await Utils.ShowDialog("清空记录", $"确定要清除全部观看记录吗?");
                if (!result)
                {
                    return;
                }

                DatabaseHelper.DeleteHistory();
                Items.Clear();
                IsEmpty = true;
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }

        }

    }
}
