using YolanLive.WinUI.Helper;
using YolanLive.WinUI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Windows.Storage.Pickers;
using Windows.Storage;
using Newtonsoft.Json;
using Windows.UI.Popups;

namespace YolanLive.WinUI.ViewModels
{
    public class FavoriteVM : BaseViewModel
    {
        public FavoriteVM()
        {
            Items = new ObservableCollection<FavoriteItem>();
            InputCommand = new RelayCommand(Input);
            OutputCommand = new RelayCommand(Output);
            TipCommand = new RelayCommand(Tip);
        }

        public ICommand InputCommand { get; set; }
        public ICommand OutputCommand { get; set; }
        public ICommand TipCommand { get; set; }
        public IReadOnlyList<FavoriteGroup> Groups { get; } = new[] {
            new FavoriteGroup("直播中"), new FavoriteGroup("未开播"), new FavoriteGroup("状态待确认") };
        public event Action ProjectionChanging;
        public event Action ProjectionChanged;
        private string filterText = "";
        public string FilterText
        {
            get => filterText;
            set { if (filterText == value) return; filterText = value ?? ""; DoPropertyChanged(nameof(FilterText)); UpdateGroups(); }
        }

        private void UpdateGroups()
        {
            ProjectionChanging?.Invoke();
            var matches = Items.Where(item => ListProjection.Matches(FilterText, item.UserName, item.SiteName, item.RoomID)).ToArray();
            for (var index = 0; index < Groups.Count; index++)
            {
                var status = index;
                ListProjection.Sync(Groups[index], matches.Where(item => (item.ConfirmedLiveStatus switch { true => 0, false => 1, _ => 2 }) == status));
            }
            IsEmpty = matches.Length == 0;
            DoPropertyChanged(nameof(EmptyMessage));
            ProjectionChanged?.Invoke();
        }


        private ObservableCollection<FavoriteItem> _items;
        public ObservableCollection<FavoriteItem> Items
        {
            get { return _items; }
            set { _items = value; DoPropertyChanged("Items"); }
        }


        private bool _loadingLiveStatus;

        public bool LoaddingLiveStatus
        {
            get { return _loadingLiveStatus; }
            set { _loadingLiveStatus = value; DoPropertyChanged("LoaddingLiveStatus"); }
        }



        private int generation;
        private CancellationTokenSource requests = new();
        private string emptyMessage = "还没有关注主播";
        public string EmptyMessage { get => !string.IsNullOrWhiteSpace(FilterText) && Items.Count > 0 && IsEmpty ? "没有匹配的关注，可清除筛选条件" : emptyMessage; private set { emptyMessage = value; DoPropertyChanged(nameof(EmptyMessage)); } }
        public async void LoadData() => await LoadAsync();
        public async Task LoadAsync()
        {
            if (Loading) return;
            var current = generation;
            var cancellation = requests.Token;
            IsEmpty = Groups.All(group => group.Count == 0);
            CanRetry = false; StatusMessage = "";
            Loading = true;
            LoaddingLiveStatus = true;
            try
            {
                var saved = await DatabaseHelper.GetFavorites();
                if (current != generation || cancellation.IsCancellationRequested) return;
                // Show saved follows immediately on the first visit, even while status requests are pending.
                if (Items.Count == 0)
                {
                    foreach (var item in saved) Items.Add(item);
                    UpdateGroups();
                    IsEmpty = Items.Count == 0;
                }
                using var limit = new System.Threading.SemaphoreSlim(4);
                var checks = await Task.WhenAll(saved.Select(async item =>
                {
                    await limit.WaitAsync(cancellation);
                    try
                    {
                        if (current != generation) return (item, live: false, failed: false, error: (Exception)null);
                        var site = MainVM.Sites.FirstOrDefault(s => s.Name == item.SiteName);
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        return (item, live: site != null && await site.LiveSite.GetLiveStatus(item.RoomID, timeout.Token).WaitAsync(timeout.Token), failed: site == null, error: (Exception)null);
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested) { LogHelper.Log("关注开播状态读取失败", LogType.ERROR, ex); return (item, live: false, failed: true, error: ex); }
                    finally { limit.Release(); }
                }));
                if (current != generation) return;
                var retained = Items.ToDictionary(item => item.ID);
                var updated = new List<FavoriteItem>();
                foreach (var check in checks)
                {
                    var item = retained.TryGetValue(check.item.ID, out var previous) ? previous : check.item;
                    item.UserName = check.item.UserName; item.Photo = check.item.Photo; item.SiteName = check.item.SiteName;
                    item.ConfirmedLiveStatus = check.failed ? null : check.live;
                    updated.Add(item);
                }
                ListProjection.Sync(Items, updated);
                UpdateGroups();
                var failure = checks.FirstOrDefault(c => c.error != null);
                EmptyMessage = failure.error != null ? RequestErrorMessage.Report(failure.error, failure.item.SiteName) : checks.Any(c => c.failed) ? "关注状态查询失败，请稍后刷新" : "还没有关注主播";
                CanRetry = checks.Any(c => c.failed);
                StatusMessage = CanRetry && Items.Count > 0 ? "部分开播状态查询失败，已标为“状态待确认”。" + EmptyMessage : "";
                IsEmpty = Groups.All(group => group.Count == 0);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { if (current == generation) { EmptyMessage = "关注数据加载失败，请重试"; CanRetry = true; StatusMessage = Items.Count > 0 ? "刷新失败，保留上次内容。" + EmptyMessage : ""; IsEmpty = Items.Count == 0; LogHelper.Log("关注读取失败", LogType.ERROR, ex); } }
            finally { if (current == generation) { Loading = false; LoaddingLiveStatus = false; } }
        }
        public void CancelPending() { generation++; requests.Cancel(); requests.Dispose(); requests = new(); if (Loading) StatusMessage = ""; Loading = false; LoaddingLiveStatus = false; }
        public override async void Refresh()
        {
            if (Loading) return;
            foreach (var site in MainVM.Sites.Select(s => s.LiveSite).OfType<YolanLive.Core.Douyin>()) site.ClearLiveStatusCache();
            CancelPending(); await LoadAsync();
        }

        public void RemoveItem(FavoriteItem item)
        {
            try
            {
                DatabaseHelper.DeleteFavorite(item.ID);
                Items.Remove(item);
                UpdateGroups();
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }

        }

        public async void Input()
        {
          
            // 打开文件选择器
            FileOpenPicker picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.ViewMode = PickerViewMode.List;
            picker.CommitButtonText = "导入";

            Utils.InitializeWithWindow(picker);
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    var json = await FileIO.ReadTextAsync(file);
                    var items = Newtonsoft.Json.JsonConvert.DeserializeObject<List<FavoriteJsonItem>>(json);
                    foreach (var item in items)
                    {

                        DatabaseHelper.AddFavorite(new FavoriteItem()
                        {
                            SiteName = item.SiteName,
                            RoomID = item.RoomId,
                            UserName = item.UserName,
                            Photo = item.Face,
                        });
                    }
                    Utils.ShowMessageToast("导入成功");
                    CancelPending();
                    Refresh();
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                    Utils.ShowMessageToast("导入失败");
                }
            }
        }

        public async void Output()
        {
            // 打开文件选择器
            FileSavePicker picker = new FileSavePicker();
            picker.FileTypeChoices.Add("Json", new List<string>() { ".json" });
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.SuggestedFileName = "favorite.json";

            Utils.InitializeWithWindow(picker);
            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    var items = new List<FavoriteJsonItem>();
                    foreach (var item in Items)
                    {
                        var siteId = "";
                        switch(item.SiteName)
                        {
                            case "哔哩哔哩直播":
                                siteId = "bilibili";
                                break;
                            case "抖音直播":
                                siteId = "douyin";
                                break;
                        }

                        items.Add(new FavoriteJsonItem()
                        {
                            SiteId = siteId,
                            Id = $"{siteId}_{item.RoomID}",
                            RoomId = item.RoomID,
                            UserName = item.UserName,
                            Face = item.Photo,
                            AddTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.M")
                        });
                    }
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(items);
                    await FileIO.WriteTextAsync(file, json);
                    Utils.ShowMessageToast("导出成功");
                }
                catch (Exception ex)
                {
                    HandleError(ex);
                    Utils.ShowMessageToast("导出失败");
                }
            }


        }

        public void Tip()
        {
            MessageDialog dialog = new MessageDialog(@"该程序兼容Simple Live，您可以导入Simple Live的关注数据，导出的数据也可以在Simple Live中导入。", "导入导出说明");
            Utils.InitializeWithWindow(dialog);
            _ = dialog.ShowAsync();
        }
    }

    public class FavoriteJsonItem
    {
        [JsonProperty("siteId")]
        public string SiteId;

        [JsonProperty("id")]
        public string Id;

        [JsonProperty("roomId")]
        public string RoomId;

        [JsonProperty("userName")]
        public string UserName;

        [JsonProperty("face")]
        public string Face;

        [JsonProperty("addTime")]
        public string AddTime;

        [JsonIgnore]
        public string SiteName
        {
            get
            {
                switch (SiteId)
                {
                    case "bilibili":
                        return "哔哩哔哩直播";
                    case "douyin":
                        return "抖音直播";
                    default:
                        return "未知";
                }
            }
        }
        
    }
}
