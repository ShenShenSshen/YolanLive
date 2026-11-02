# 圆体标题与更纱 UI SC：原尺寸对照

本文记录用户选择前的字体对照阶段；认可后的本地应用整合见 [字体与 D4 图标整合](visual-adoption.md)。

2026-11-02。已选图标为 D4 蓝青双色风翼：底色 `#EFF8FC`，上翼 `#5F91C9`，下翼 `#60B8BC`。

本轮交付原生 WinUI 3 字体对照窗口，保留当前软件默认字体和图标，版本仍为 1.1.4；没有生成安装器、发布 GitHub 或覆盖日常安装。

## 对照范围

- 左栏：现有资源圆体导航、微软雅黑 UI 正文、Satoshi 数字。
- 右栏：22 号大标题保留资源圆体，正文、导航、按钮、说明和数字统一使用 Sarasa UI SC Regular。
- 两栏相同布局、颜色及字重，顶部可切换正文 14/15 逻辑单位和浅/深主题。说明为 13 号，不缩放整个控件或文字。
- 顶部展示用户选定的 D4 风翼，尚未替换程序内嵌图标或正式版快捷方式。

入口：`artifacts/YolanLive-font-preview/Font-Comparison.cmd`。完整预览目录必须一起保留。启动脚本将临时目录与访客资料设到预览目录下的 `preview-data`，不加载日常账号，也不安装系统字体。

## 字体来源

使用官方 [Sarasa-Gothic v1.0.43](https://github.com/be5invis/Sarasa-Gothic/releases/tag/v1.0.43) 中的 `SarasaUiSC-TTF-1.0.43.7z`，仅提取 Regular：

- 内部字族：`Sarasa UI SC`，样式 `Regular`。
- 字体文件：24,049,996 字节。
- 已检查存在 `cvt `、`fpgm`、`prep` TrueType 微调表，未使用 Unhinted 包。
- 原字体不修改，随预览提供同版本 OFL 1.1 授权，位于 `Assets/PreviewFonts/Sarasa-OFL.txt`。
- 字体资源仅在 `TypographyPreviewAssets=true` 的预览构建中打包；正常构建不新增此字体包依赖。

```powershell
.\with-build-cache.ps1 {
    dotnet publish .\YolanLive.WinUI\YolanLive.csproj -c Release -p:Platform=x64 -p:TypographyPreviewAssets=true -o .\artifacts\YolanLive-font-preview --nologo
}
```

## 验证及边界

使用 D 盘隔离访客目录运行 `--smoke-test --font-preview-capture`，检查 14/15 号、浅/深主题的原生控件与截图。记录在预览目录的 `font-preview-checks.txt`。

本机实际 RasterizationScale 为 1。同一示例字符串在 14 号下微软雅黑测量宽度 255、更纱 263；15 号下分别为 273、282，结合内部字族及字体文件检查，确认候选字体有实际不同的量测结果。没有以不同颜色、字重或整页缩放制造清晰度差异。

首次捕获只渲染 Page.Content，漏掉 Page.Background，PNG 角落 alpha 为 0。先用像素断言复现失败，随后改为捕获完整 Page，并在原生截图检查中要求角落 alpha=255。初始图保留在 `artifacts/design-alternatives/font-14-light-before-background-fix.png`。

截图适合查看字体风格；聊天图片可能缩放，字重与清晰度应以本机交互窗口为准。本轮不宣称已消除用户显示器上的模糊，也未替换默认字体或重复完整播放器回归。
