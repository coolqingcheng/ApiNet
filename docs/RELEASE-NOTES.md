ApiNet 0.1.16：换用简洁的蓝色几何图标。

- 新 Logo 采用 VS Code 风格的抽象折带，三块蓝色折面组成 Net 的首字母 N，轮廓清晰。
- 图标使用透明背景，调整图形占比，让任务栏、托盘和应用内小尺寸图标更容易辨认。
- 统一替换应用内 Logo、窗口、任务栏、系统托盘、启动器和 Windows 安装包图标；macOS 应用与 Linux 包使用同一来源。
- 提供 SVG、1024 px PNG 和 16–256 px 的九种尺寸 ICO；Windows 安装和新版本首次启动会通知 Shell 刷新本应用图标。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows x64、Linux x64、macOS ARM64 均提供 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。

更新说明展示功能自 0.1.11 客户端起生效；更早版本需要先升级，后续升级提示即可显示更新内容。
