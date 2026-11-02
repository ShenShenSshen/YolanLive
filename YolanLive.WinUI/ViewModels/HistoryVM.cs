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
        public IReadOnlyList<HistoryGroup> Groups { get; } = new[] { new HistoryGroup("今天"), new HistoryGroup("昨天"), new HistoryGroup("更早") };
        public event Action ProjectionChanging;
        public event Action ProjectionChanged;
        private string filterText = "";
        public string FilterText { get => filterText; set { if (filterText == value) return; filterText = value ?? ""; DoPropertyChanged(nameof(FilterText)); UpdateGroups(); } }
        private void UpdateGroups()
        {
            ProjectionChanging?.Invoke();
            var matches = Items.Where(item => ListProjection.Matches(FilterText, item.UserName, item.SiteName, item.RoomID)).ToArray();
            for (var index = 0; index < Groups.Count; index++)
            {
                var dateGroup = index;
                ListProjection.Sync(Groups[index], matches.Where(item => (item.WatchTime.Date == DateTime.Today ? 0 : item.WatchTime.Date == DateTime.Today.AddDays(-1) ? 1 : 2) == dateGroup));
            }
            IsEmpty = matches.Length == 0; DoPropertyChanged(nameof(EmptyMessage));
            ProjectionChanged?.Invoke();
        }
        private string emptyMessage = "还没有观看记录";
        public string EmptyMessage { get => !string.IsNullOrWhiteSpace(FilterText) && Items.Count > 0 && IsEmpty ? "没有匹配的观看记录，可清除筛选条件" : emptyMessage; private set { emptyMessage = value; DoPropertyChanged(nameof(EmptyMessage)); } }

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
                var retained = Items.ToDictionary(item => item.ID);
                var updated = new List<HistoryItem>();
                foreach (var item in history)
                {
                    if (retained.TryGetValue(item.ID, out var previous))
                    {
                        previous.UserName = item.UserName; previous.Photo = item.Photo; previous.SiteName = item.SiteName; previous.WatchTime = item.WatchTime;
                        updated.Add(previous);
                    }
                    else updated.Add(item);
                }
                ListProjection.Sync(Items, updated);
                UpdateGroups();
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
                UpdateGroups();
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
                UpdateGroups();
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }

        }

    }
}
