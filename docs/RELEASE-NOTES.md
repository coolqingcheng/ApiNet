ApiNet 0.1.9：省略地址协议时默认使用 HTTP。

- HTTP 请求地址未填写协议时自动使用 `http://`，支持域名、localhost、IPv4/IPv6 和带端口地址，例如 `localhost:8080/api`。
- 环境变量解析后应用默认协议，保留查询参数和片段；`//example.com/api` 也会使用 HTTP，显式 HTTPS 地址保留。
- 请求预览、实际发送和历史使用同一完整地址，输入框及已保存的变量表达式保留原样。
- 空地址、无效地址及不支持的显式协议继续提示错误，WebSocket 的 ws/wss 地址保留。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows、Linux、macOS 均提供 x64 / ARM64 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。
