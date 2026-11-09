ApiNet 0.1.4：新增 Android APK 与手机、平板界面适配。

- 新增 Android 8.0+ ARM64 / x86_64 APK，支持系统安装和同一签名的覆盖更新。
- 手机采用抽屉导航、请求切换与分区编辑；平板停靠导航，横屏收起重复上下文区域，触控按钮最小 44dp。
- WebSocket 在手机上使用消息、发送、握手分区，编辑器随屏幕高度伸缩，横屏也可编辑消息和握手参数。
- 移动端支持 HTTP/WebSocket、文件夹、环境变量、历史、在线 Swagger/OpenAPI 导入、JSON 导入导出、文件上传、响应保存、深浅主题及常用工具。
- Android 文件选择器通过流读写文档 URI，上传文件保留在应用私有目录；无需请求整个存储的访问权限。
- Android 从 GitHub Release 下载 APK，校验 SHA256，再由系统确认安装。签名密钥保存在仓库 Secrets，后续升级沿用。
- 将共享 UI/MVVM 拆入 ApiNet.UI，桌面和 Android 保持独立启动及更新实现。桌面 Native AOT 和 Velopack 升级继续保留。
- Android 显式 HTTP 代理仅在前台使用，暂不提供 VPN 全局抓包、后台常驻或读取其他应用数据库；Postman/ApiPost 使用 JSON 导入。

桌面包仍提供 Windows/Linux/macOS x64 + ARM64。Android 使用 .NET for Android Profiled AOT，桌面使用 Native AOT。APK 由项目密钥签名；桌面安装包仍未签名。

`*.nupkg`、`releases.*.json` 用于桌面在线更新；Android 使用对应架构的 APK。SHA256SUMS.txt 提供校验。
