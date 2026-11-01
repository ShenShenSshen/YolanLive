# 免费更新下载加速 · 本地预览

日期：2026-11-01。版本仍为 1.1.2；本轮未生成安装包、未发布 GitHub，也未修改日常数据目录。

## 行为

- 更新版本、安装包名称、大小和 SHA-256 仍直接读取本项目 GitHub latest 正式 Release。
- 安装包依次使用 `gh-proxy.org`、`ghfast.top`、GitHub 原站；连接失败、HTTP 错误、无效范围或校验失败时切换下一源，每源仅尝试一次。
- 等待响应头超过 15 秒或读取数据连续 20 秒无进展时，取消底层操作并换源。整次下载上限保留为 15 分钟；用户取消不会继续尝试其他源。
- 当前源显示在设置更新状态行中；离开设置仍在后台下载。
- 网络中断或取消保留 `.exe.partial`。重试及换源使用 HTTP Range；服务器忽略 Range、返回完整文件时重新写入，避免重复拼接。错误范围不写入文件。
- 文件完整且 SHA-256 与 GitHub 元数据一致后才改为安装器名称；启动安装前仍再次校验。完整但校验失败的临时文件删除。
- 第三方渠道只接收本项目公开安装包 URL，不接收平台登录 Cookie、账号资料或 GitHub 令牌。共享更新客户端禁用 Cookie 容器。
- 不新增依赖、不更改播放流程、界面布局或平台请求接口。

## 可复现命令

仓库根目录执行（需要本机 .NET / WinUI 构建环境）：

```powershell
& .\with-build-cache.ps1 {
    dotnet publish .\YolanLive.WinUI\YolanLive.csproj -c Release -p:Platform=x64 `
        -o .\artifacts\YolanLive-update-preview --nologo
}
& .\tests\UpdateCheck.ps1 -Exe .\artifacts\YolanLive-update-preview\YolanLive.exe
& .\tests\UpdateCheck.ps1 -Exe .\artifacts\YolanLive-update-preview\YolanLive.exe -Live -NoProxy
```

检查入口为既有 UpdateClient / UpdateVM 与设置页面。HTTP 响应仅在外部传输边界受控；使用实际文件流及原有 SHA-256 校验。所有检查使用 D 盘隔离访客目录，不读取日常账号。

## 结果

- 原实现：既有 4 组更新检查通过；新增“GitHub 不可达仍可取得安装包”用例失败，证据 `update-acceleration/offline-red-fallback.txt`。
- 接入加速后，该用例通过；新增“取消后保留并续传”用例在旧删除逻辑下失败，证据 `update-acceleration/offline-red-resume.txt`。
- 最终 9 组检查全部通过，见 `update-acceleration/offline-final.txt`：版本和元数据约束、下载校验和缓存、版本与网络状态、跨页面后台下载与手动安装、GitHub 不可达加速、取消重试续传、忽略或伪造 Range、损坏加速源与原站兜底、真实 15/20 秒超时及底层取消。
- 两个加速源及原站的前 64 KiB 请求返回 206，字节与本地已发布安装包一致，见 `update-acceleration/source-probes.json`。此片段结果不等同于所有渠道完整下载验证。
- 真实软件更新代码：禁用显式代理，经 `gh-proxy.org` 完整下载 v1.1.2 安装器 **92,597,834 字节**，GitHub SHA-256 校验通过，安装器未执行。见 `update-acceleration/live-final.txt`。
- 最终预览路径：`artifacts/YolanLive-update-preview/YolanLive.exe`；完整目录必须一起保留，不能只复制 exe。

## 限制

- 当前本机网络可成功直连 GitHub；没有复现用户原来下载失败的网络，也没有禁用操作系统级 TUN、路由器代理等。`-NoProxy` 只禁用测试 HTTP 客户端的显式代理。
- 公共源不是本项目自有服务，可用性和带宽不受本项目控制。未证明全国不同运营商均可访问，也不承诺固定下载速度。
- 元数据仍需要 GitHub API 可访问；本次只处理“已检测到更新但安装包下载失败”。
- 第二加速源完成片段验证及受控换源检查，本次未从它再次完整下载整个安装器。
- 旧版客户端不会仅因服务端更新自动获得新下载逻辑；需要先安装一次包含此次修改的版本。预览仍为 1.1.2，正常界面在 GitHub 最新版本同为 1.1.2 时不会提供更新下载；上述独立入口直接验证实际下载路径。
- 本次未重跑完整播放、窗口模式或长期性能检查。

## 参考

- [GitHub Release 资产及 digest](https://docs.github.com/en/rest/releases/assets)
- [GH-Proxy 前缀用法与支持类型](https://gh-proxy.com/docs/quick-start)
- [GH-Proxy 服务条款](https://gh-proxy.com/terms/)
- [GHFast 服务入口](https://ghfast.top/)
