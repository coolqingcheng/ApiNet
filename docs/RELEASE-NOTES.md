ApiNet 0.1.1：修复 Windows 重复安装体验。

- 再次运行 Setup.exe 直接覆盖安装/修复，不再弹出“已经安装”的确认；同版本也能恢复缺失文件。
- Windows 工作区、环境、历史和本地 CA 迁到独立的 ApiNetData 目录，覆盖安装保留这些数据。
- 自动迁移 0.1.0 的数据库及待提交的 WAL，已有新版数据不会被旧数据覆盖。
- 安装包继续使用 Velopack 安装布局和 GitHub Releases 更新源。

保留第一版功能：

- 多级文件夹、独立标签页、HTTP 与 WebSocket 调试。
- 可增删/启用的 Query 和 Header 参数；环境变量输入高亮。
- JSON/XML/HTML 请求体与响应高亮，完整文件响应下载保存。
- Swagger/OpenAPI JSON 导入、增量合并及导出。
- 历史筛选、本地代理、可选 HTTPS 解密、WebSocket 抓包。
- Velopack 自动下载更新，立即升级或真正退出时升级。

Windows 选择对应架构的 Setup.exe；Linux 使用 AppImage；macOS 使用 DMG 或 Portable.zip，将应用拖入可写的 Applications 目录。所有包均未签名；macOS 首次启动需在系统设置中允许打开。Linux AppImage 需先赋予执行权限。

`*.nupkg`、`releases.*.json` 为应用在线更新使用，请保留原始文件名。SHA256SUMS.txt 提供下载校验。

六个包来自各平台 CI 的 Native AOT 构建；自动化测试不代表六种环境下均已完成安装和连续版本升级的实机验证。首版边界见 README。
