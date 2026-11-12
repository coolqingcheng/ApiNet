ApiNet 0.1.10：发布目标精简为三种架构。

- CI 和打包脚本只构建 Windows x64（win-x64）、Linux x64（linux-x64）和 macOS ARM64 / Apple Silicon（osx-arm64）。
- 下载说明与在线更新通道同步为这三个目标，提供对应安装包、更新包及 SHA256 校验文件。
- 包含 0.1.9 的地址改进：HTTP 地址省略协议时默认使用 `http://`，环境变量解析后同样生效；保留显式 HTTPS、查询参数、输入内容和 WebSocket 协议。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows x64、Linux x64、macOS ARM64 均提供 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。
