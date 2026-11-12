ApiNet 0.1.10：修复参数表格输入崩溃，发布目标精简为三种架构。

- 修复鼠标点击和编辑参数表格 KEY / VALUE 时的栈溢出崩溃：禁用单行编辑器的纵向滚动，避免光标与选区滚动递归触发；保留长值的横向滚动、变量高亮和键盘编辑。
- 修复覆盖 HTTP Params、Headers、表单及 WebSocket 参数表格，包含长文本、中文、选中替换和空行新增的回归验证。
- CI 和打包脚本只构建 Windows x64（win-x64）、Linux x64（linux-x64）和 macOS ARM64 / Apple Silicon（osx-arm64）。
- 下载说明与在线更新通道同步为这三个目标，提供对应安装包、更新包及 SHA256 校验文件。
- 包含 0.1.9 的地址改进：HTTP 地址省略协议时默认使用 `http://`，环境变量解析后同样生效；保留显式 HTTPS、查询参数、输入内容和 WebSocket 协议。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows x64、Linux x64、macOS ARM64 均提供 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。
