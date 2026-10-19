# PlayWin（游窗）

<img src="PlayWin.WinUI/Assets/PlayWin.png" width="80" alt="PlayWin 图标" />

简洁的 Windows 游戏直播客户端。支持哔哩哔哩、抖音、虎牙和斗鱼，仅展示游戏分类，不显示弹幕。使用 WinUI 3，栏位采用磨砂材质，内容保持清晰。

**已发布版本：v1.0.7 · Windows x64 · MIT**

v1.0.7 合并浏览顶栏、统一平台与内容对齐，改善顶部留白；修复返回分类后图片不显示，以及卡片右侧空出整列的问题。详见 [更新说明](RELEASE_NOTES.md) 与 [发布检查](docs/validation/release-1.0.7.md)。

## v1.0.7 更新

- 平台切换、刷新与窗口按钮共用顶栏，侧栏贯通顶部，首页不再显示软件图标与名称；直播间保留房间标题。
- 平台切换条收薄至 36，选中块为 28，保留 14 号文字并垂直居中；窄窗口自动换行。
- 大类、卡片与页面操作统一对齐，设置标题与居中面板对齐；修复卡片宽度取整导致右侧空出整列。
- 修复滚动、离开分类并返回后图片不再显示：回收容器时保留 URL 绑定，像素资源仍在离页时释放。
- 提供安装版、便携版与 SHA-256；保留当前用户安装、覆盖升级保留数据及卸载清空数据。

## 下载

- [安装版 PlayWin-1.0.7-x64-Setup.exe](https://github.com/ShenShenSshen/PlayWin/releases/download/v1.0.7/PlayWin-1.0.7-x64-Setup.exe)
- [便携版 PlayWin-x64.zip](https://github.com/ShenShenSshen/PlayWin/releases/download/v1.0.7/PlayWin-x64.zip)
- [SHA-256 校验文件](https://github.com/ShenShenSshen/PlayWin/releases/download/v1.0.7/SHA256SUMS.txt)
- [所有发布版本](https://github.com/ShenShenSshen/PlayWin/releases)

安装版默认安装到当前用户目录，创建开始菜单入口，默认创建桌面快捷方式，可取消。便携版解压后运行 `PlayWin.exe`，请保留整个目录。

支持 Windows 10 1903（18362）及以上版本和 Windows 11 x64。抖音、虎牙、斗鱼网页登录及部分平台请求需要 WebView2 Runtime。程序自带 .NET 和 Windows App SDK 运行依赖，无需安装开发 SDK。

## 软件截图

### 首页

![游窗首页：四平台切换与游戏直播卡片](docs/screenshots/home.png)

### 游戏分类

![游窗游戏分类：上图下名与游戏大类导航](docs/screenshots/categories.png)

### 播放器

![游窗播放器：分组操作栏、亮度与窗口操作](docs/screenshots/player.png)

截图沿用 v1.0.4 的隔离访客环境，展示主要功能；v1.0.7 的字体、卡片及顶栏布局已有调整。截图不含日常账号信息；直播内容与平台目录会随时间变化。

## 功能

- **四个平台，游戏优先**：按哔哩哔哩 / 抖音 / 虎牙 / 斗鱼切换首页和游戏分类；搜索、关注及历史统一管理。
- **浏览记忆**：恢复最后的平台和各平台游戏大类；每个平台可手动固定最多 6 个常看游戏到首页。
- **清晰的搜索结果**：显示平台标识和接口已提供的开播状态，直播标题可悬停查看全文。
- **随便逛逛**：从四个平台游戏首页随机选择已开播房间，提供本轮上一个／下一个，记录末尾继续随机，切换保持当前播放器和窗口模式。
- **轻量播放器**：清晰度与线路切换、截图、全屏、置顶小窗、独立窗口播放；双击全屏、空格暂停，底栏调节音量与视频亮度；无边框模式支持按住视频拖动，控件自动隐藏。
- **有限断流恢复**：最多重试 3 次，重新检查房间及播放地址；确认停播时停止，离开房间取消。
- **固定比例与位置记忆**：16:9 播放区域尺寸预设，适配显示器工作区；主窗口和独立窗口分别记住位置。
- **关注与历史**：日常关注页只显示确认开播的房间，管理入口可查看和移除停播主播；观看历史保留。支持 JSON 导入导出。
- **账号登录**：哔哩哔哩扫码登录，抖音、虎牙、斗鱼使用各自官方网页登录，浏览器与加密会话分别保存；区分登录失效、网络失败与平台接口异常。
- **外观与更新**：雾蓝浅色 / 深色主题、磨砂栏位，圆体标题与导航搭配微软雅黑 UI 正文；每次启动检测正式更新，设置支持后台下载安装包，校验后由用户启动安装器。
- **资源使用**：窗口位置及音量延迟 500 毫秒合并保存；日志按天或 10 MB 滚动、最多保留 7 份归档和 7 天；首页、分类详情和搜索每页最多保留 300 个直播条目。

公共数据请求复用连接、全应用最多并发 4 个，游戏目录缓存 10 分钟并合并重复加载；离开页面取消请求，最小化暂停列表检查。后台签名浏览器空闲后暂停或回收，图片按显示像素解码。实测方法、收益及限制见 [性能报告](PERFORMANCE_REPORT.md)。

没有弹幕、礼物效果或跨设备同步。


## 数据与升级

v1.0.1 起数据路径统一为 PlayWin，不自动导入 v1.0.0 的数据，需要重新登录并重新设置；后续覆盖升级保留 PlayWin 数据。**卸载安装版会清空登录、关注、历史、设置、日志、下载文件及浏览器缓存，全部不保留**。程序和安装包暂未进行代码签名，Windows 可能显示未知发布者。

平台接口变化、平台登录要求和网络状况可能影响直播获取。视频保持原比例，不拉伸或裁切；直播源自身的黑边不会被去除。

## 构建与验证

需要 .NET 10 SDK、Windows SDK 10.0.26100.0；安装包另外需要 Inno Setup。

```powershell
.\build-winui.ps1
dotnet run --project tests/WinUIDataCheck/WinUIDataCheck.csproj -c Release
.\run-smoke-test.ps1
.\build-installer.ps1 -CompilerPath '编译器目录\ISCC.exe'
```

解决方案：`PlayWin.slnx`。发布目录：`artifacts/PlayWin-win-x64`。

- `PlayWin.Core`：四个平台的接口与核心模型，项目文件 `PlayWin.Core.csproj`。
- `PlayWin.WinUI`：WinUI 3 客户端，项目文件 `PlayWin.csproj`。
- `tests`：数据、自检、图标和隔离安装验证。
- `installer/PlayWin.iss`：当前用户安装与卸载配置。

构建缓存、个人数据和发布产物不纳入源码。

本次发布检查覆盖视觉布局、分类图片返回与真实容器回收、完整冒烟、离线数据及隔离安装验证，结果见 [v1.0.7 发布检查](docs/validation/release-1.0.7.md)。测试使用 D 盘隔离访客目录，不读取日常账号。150%/200% 实机缩放、系统高对比度切换和四平台全部真实线路未完整验收；没有重复长期性能对照。v1.0.5 的 B站访客首页及虎牙所选线路未通过记录继续保留，详见 [播放器性能报告](PLAYER_PERFORMANCE_REPORT.md) 与 [客户端说明](PlayWin.WinUI/README.md)。

## 来源与许可证

设计参考项目：[https://github.com/xiaoyaocz/AllLive](https://github.com/xiaoyaocz/AllLive)。感谢原作者及项目依赖的维护者。

采用 [MIT 许可证](LICENSE)，保留原作者版权声明。与直播平台无官方关联。

虎牙播放令牌所需 Tars 编解码复用原项目中的平台无关代码，遵循 BSD 3-Clause；正文使用微软雅黑 UI，标题与导航使用资源圆体，字标使用寒蝉圆体；原 MiSans 资源保留用于对照，各字体许可随程序携带。

本地构建缓存默认放在项目的 artifacts/cache 下：nuget-packages 为本项目依赖，nuget-http 为下载缓存，temp 为编译、打包和自检临时文件。build-winui.ps1、build-installer.ps1、run-smoke-test.ps1 和隔离安装检查只在运行期间使用这些路径，不修改系统全局设置。直接运行 dotnet 命令时，项目依赖遵循 NuGet.Config；其他临时文件仍遵循当前终端的 TEMP/TMP 设置。
