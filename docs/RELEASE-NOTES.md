ApiNet 0.1.15：换上简洁可爱的奶油小狗图标。

- 新 Logo 采用蓝底、奶油脸和焦糖垂耳，用圆脸和简洁五官提升任务栏小尺寸下的辨识度。
- 统一替换应用内 Logo、窗口、任务栏、系统托盘、启动器和 Windows 安装包图标；macOS 应用与 Linux 包使用同一图标来源。
- 提供透明边缘 SVG、1024 px PNG 和 16–256 px 的九种尺寸 ICO，适配不同缩放比例，避免黑色底边。
- Windows 安装和新版本首次启动会通知 Shell 刷新本应用的图标。
- 保留深色、浅色、午夜蓝主题，紧凑参数表格，以及响应 Headers 的表格 / 纯文本切换。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows x64、Linux x64、macOS ARM64 均提供 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。

更新说明展示功能自 0.1.11 客户端起生效；更早版本需要先升级，后续升级提示即可显示更新内容。
