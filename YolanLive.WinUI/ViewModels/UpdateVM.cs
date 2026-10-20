using YolanLive.WinUI.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YolanLive.WinUI.ViewModels;

public sealed class UpdateVM : BaseNotifyPropertyChanged
{
    internal static UpdateVM Instance { get; } = new(new UpdateClient(), UpdateClient.ParseVersion(typeof(App).Assembly.GetName().Version.ToString()));
    private readonly UpdateClient client;
    private readonly Version currentVersion;
    private CancellationTokenSource operation;
    private UpdateRelease release;
    private string installerPath;
    public string Status { get; private set; } = "手动检测新版本；下载安装包后可手动启动安装器";
    public double Progress { get; private set; }
    public bool Busy { get; private set; }
    public bool Downloading { get; private set; }
    internal string AvailableVersion => release != null && release.Version > currentVersion ? release.Tag : null;
    public bool CanCheck => !Busy;
    public bool HideProgress => !Downloading;
    public bool HideDownload => Busy || release == null || release.Version <= currentVersion || installerPath != null;
    public bool HideInstall => Busy || installerPath == null;
    public bool HideCancel => !Busy;

    internal UpdateVM(UpdateClient client, Version currentVersion) { this.client = client; this.currentVersion = currentVersion; }
    private void Notify()
    {
        foreach (var name in new[] { nameof(Status), nameof(Progress), nameof(CanCheck), nameof(HideProgress), nameof(HideDownload), nameof(HideInstall), nameof(HideCancel) }) DoPropertyChanged(name);
    }
    private void Begin(bool downloading)
    {
        operation = new CancellationTokenSource(); Busy = true; Downloading = downloading; Progress = 0;
        Status = downloading ? "正在下载安装包，切换页面后继续；下载完成后需手动启动安装器" : "正在检测新版本…";
        Notify();
    }
    private void Finish() { Busy = Downloading = false; operation.Dispose(); operation = null; Notify(); }
    private void Failed(Exception ex)
    {
        Status = ex is OperationCanceledException ? operation.IsCancellationRequested ? "已取消，可以重新尝试" : "请求超时，请稍后重试" :
            ex is HttpRequestException http && http.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests ? "GitHub 请求受限，请稍后重试" :
            ex is HttpRequestException ? "网络连接失败，请检查网络后重试" : ex is InvalidDataException ? ex.Message : "更新操作失败，请重试";
        if (ex is not OperationCanceledException || !operation.IsCancellationRequested) LogHelper.Log("更新操作失败", LogType.ERROR, ex);
    }
    public async Task CheckAsync()
    {
        if (Busy) return;
        Begin(false); release = null; installerPath = null;
        try
        {
            release = await client.CheckAsync(operation.Token);
            if (release.Version <= currentVersion) Status = release.Version == currentVersion ? "当前已是最新版本（" + release.Tag + "）" : "当前版本高于最新公开版本（" + release.Tag + "）";
            else
            {
                var saved = client.GetInstallerPath(release);
                if (await client.VerifyAsync(release, saved, operation.Token)) installerPath = saved;
                Status = installerPath == null ? "发现新版本 " + release.Tag + "，安装包 " + (release.Size / 1048576d).ToString("0.0") + " MB" : "新版本 " + release.Tag + " 已下载，点击启动安装器";
            }
        }
        catch (Exception ex) { release = null; installerPath = null; Failed(ex); }
        finally { Finish(); }
    }
    public async Task DownloadAsync()
    {
        if (HideDownload) return;
        Begin(true);
        try
        {
            installerPath = await client.DownloadAsync(release, value =>
            {
                Progress = value;
                Status = "正在下载 " + release.Tag + "（" + value.ToString("0") + "%），切换页面后继续";
                Notify();
            }, operation.Token);
            Status = release.Tag + " 已下载并校验完成，点击启动安装器；覆盖升级保留用户数据";
        }
        catch (Exception ex) { installerPath = null; Failed(ex); }
        finally { Finish(); }
    }
    public void Cancel() => operation?.Cancel();
    public async Task<bool> StartInstallerAsync()
    {
        if (HideInstall) return false;
        Begin(false);
        Status = "正在校验安装包并启动安装器…"; Notify();
        try
        {
            if (!await client.VerifyAsync(release, installerPath, operation.Token))
            {
                installerPath = null; throw new InvalidDataException("安装包已改变或不存在，请重新下载");
            }
            var process = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            if (process == null) throw new IOException("Installer did not start.");
            process.Dispose();
            return true;
        }
        catch (Exception ex) { Failed(ex); return false; }
        finally { Finish(); }
    }
}
