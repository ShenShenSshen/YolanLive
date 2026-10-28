# 第一轮播放稳定性预览

日期：2026-10-23。基于 v1.1.1，本地源码及完整预览目录为 `artifacts/YolanLive-stability-preview`，版本号保持 1.1.1；没有生成安装包、提交、推送或发布 GitHub。本批没有更换配色、改变窗口模式规则或调整解码与缓冲参数。

## 已确认与修正

### B站游戏首页与随机选房

真实分类目录列出具体游戏，没有此前 `OverviewSources` 依赖的虚拟 `area_id=0` 条目。旧逻辑漏掉网游、手游、单机的父分区入口。现在从目录中的已确认游戏父分区生成查询入口，保留赛事 561，排除娱乐和非游戏赛事；不修改分类导航原始对象。

[回归失败](stability-round1/red-bili-overview.txt)与[通过](stability-round1/green-bili-overview.txt)使用相同的公开目录映射接口，验证三类游戏入口、赛事过滤与对象不被改写。

### B站访客签名

官方 `nav` 接口在未登录时返回 `code=-101`，同时仍提供公开 `wbi_img` 参数。旧的通用响应检查在读取这些参数前抛出登录错误，使访客房间详情请求无法完成签名。

现在只在这个签名参数接口中允许访客响应，并验证两个参数的 URL 和 32 位十六进制键格式；其他房间、搜索与账号接口的错误和登录判断不变。签名仍在本机完成，不读取日常账号、不伪造登录状态。

[公开签名接口失败](stability-round1/red-bili-guest-sign.txt)与[通过](stability-round1/green-bili-guest-sign.txt)调用真实 `BiliBili.GetWbiSign`。这是网络集成检查，不冒充可控离线响应测试。

### 播放地址请求生命周期

房间加载此前通过异步事件启动播放地址请求，却不等待其完成。慢地址请求尚未返回时，房间加载任务及忙碌状态已经结束；地址请求等待方也未能独立遵守调用方取消。

现在房间加载等待地址解析；地址解析关联调用方取消及原有 25 秒预算，通过可取消等待释放控制流程。原有过期结果检查保留。即使测试替身忽略取消并迟到返回，也不会触发旧房间播放。取消后仍能重新选择地址。不保证强行停止不遵守取消的第三方任务，只保证等待方退出与迟到结果失效。

[先失败](stability-round1/red-address-lifetime.txt)后修正；新行为进入完整冒烟检查。测试覆盖挂起地址、取消、迟到结果、保留当前线路及取消后的恢复。

## 验证结果与保留问题

| 检查 | 结果 | 限制 |
|---|---|---|
| 离线数据与目录检查 | 通过 | [记录](stability-round1/data-checks.txt)，使用 D 盘隔离数据 |
| 完整冒烟 | 116 项中 115 项通过 | [记录](stability-round1/final-smoke.txt)：失败为既有底栏空闲隐藏检查，不是旧的 60 像素尺寸断言 |
| 后续播放器专项 | 地址生命周期及四轮控件／模式检查通过 | [记录](stability-round1/diagnostic-controls.txt)，尺寸异常本次未复现，未猜测性修改窗口逻辑 |
| 随机播放专项 | 首轮 5/6，后续 6/6 | [首轮](stability-round1/final-random.txt)预取启动超时；增加状态诊断后[后续](stability-round1/diagnostic-random.txt)通过，没有延长原有 6 秒期限，未证明间歇性根因消除 |
| 四平台访客真实取流 | B站、抖音、斗鱼进入播放 | [修正前](stability-round1/baseline-platforms.txt)与[修正后](stability-round1/final-platforms.txt)不是相同房间测速，不推导固定性能提升比例 |
| 虎牙访客抽样 | 失败 | 同一抽样房间三条地址返回 HTTP 403，解码未成功；不是本机解码器故障的证据，未证明全平台、所有线路或登录后不可用 |

播放器专项保留尺寸、边框、置顶状态、视频区域、模式恢复及底栏断言。底栏隐藏失败和预取超时均保留原始记录，后续单轮通过不视为修复。没有通过删除断言、增大容差或增加等待时限改变结果。

实际媒体检查以播放器进入 Playing 且获得视频尺寸为准，不等同于测量可见首个像素。使用静音的 D 盘隔离访客目录，不读取日常登录；不记录 Cookie、签名地址、接口正文或密钥。没有新增进程线程／总内存硬上限，也未重复长期性能对照。

## 运行与复现

先关闭其他预览实例，再运行 `artifacts/YolanLive-stability-preview/YolanLive.exe`；请保留完整目录。现有正式版和 GitHub 资产未替换。

通过 `with-build-cache.ps1` 运行下列检查，并将 `YOLANLIVE_SMOKE_PROFILE` 设为实际存在的 D 盘 `TEMP/YolanLive-smoke-test` 子目录：

```powershell
YolanLive.exe --smoke-test --player-controls-check
YolanLive.exe --smoke-test --random-browse-checks
YolanLive.exe --smoke-test --random-browse-live
```

目录映射与真实访客签名检查：

```powershell
dotnet run --project tests/WinUIDataCheck/WinUIDataCheck.csproj -c Release -- --overview-check
dotnet run --project tests/WinUIDataCheck/WinUIDataCheck.csproj -c Release -- --bili-sign-live
```

后续优先项仍为虎牙访客地址 403、底栏隐藏及预取时序的稳定复现。当前预览不能宣称第一轮所有问题已解决或四平台全面通过。
