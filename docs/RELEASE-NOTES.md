ApiNet 第一版：基于 .NET 10 和 Avalonia 的跨平台网络调试工具。

- 多级文件夹、独立标签页、HTTP 与 WebSocket 调试。
- 可增删/启用的 Query 和 Header 参数；环境变量输入高亮。
- JSON/XML/HTML 请求体与响应高亮，完整文件响应下载保存。
- Swagger/OpenAPI JSON 导入、增量合并及导出。
- 历史筛选、本地代理、可选 HTTPS 解密、WebSocket 抓包。
- Velopack 自动下载更新，立即升级或真正退出时升级。

Windows 选择对应架构的 Setup.exe；Linux 使用 AppImage；macOS 使用 DMG 或 Portable.zip，将应用拖入可写的 Applications 目录。所有包均未签名；macOS 首次启动需在系统设置中允许打开。Linux AppImage 需先赋予执行权限。

`*.nupkg`、`releases.*.json` 为应用在线更新使用，请保留原始文件名。SHA256SUMS.txt 提供下载校验。

六个包来自各平台 CI 的 Native AOT 构建；自动化测试不代表六种环境下均已完成安装和连续版本升级的实机验证。首版边界见 README。
