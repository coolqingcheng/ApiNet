ApiNet 0.1.2：从本机 ApiPost / Postman 一键导入。

- 导入入口自动检测本机软件及残留数据，选择后读取副本，预览并导入到当前文件夹。
- 主窗口标题显示当前版本号；更新源固定为本项目 GitHub 仓库，设置中无需填写更新地址。
- 支持 Postman Collection v2/v2.1、旧版自动备份、环境/公共变量与 ApiPost 项目/单请求 JSON。
- 保留目录、请求、启用状态、变量和常用认证；再次导入或刷新来源可增量合并，保留本地冲突并标记来源移除。
- 新增 URL Encoded 和 Multipart 文本表单，支持参数行编辑、变量解析及实际 HTTP 编码。
- 本地数据库校验失败时可回退到最新自动备份，并在预览中提示；不能直接读取的版本可使用 JSON 文件导入。
- 脚本和前后置任务不会执行或迁移；暂不支持的认证和上传文件项会提示检查。仅云端保存或加密数据需在原软件导出。
- 本机验证了 ApiPost 8.2.5 数据读取、Postman 备份环境导入及重复导入去重；其他版本的真实数据兼容性见 README。

继续保留 HTTP/WebSocket、多标签、多级文件夹、变量高亮、JSON/XML 高亮、文件响应保存、历史筛选、代理抓包、HTTPS 解密、OpenAPI 导入导出和 Velopack 自动升级。

Windows Setup.exe 可直接覆盖安装/修复，保留独立目录中的工作区和环境数据。Windows 选择对应架构的 Setup.exe；Linux 使用 AppImage（需赋执行权限）；macOS 使用 DMG 或 Portable.zip。所有包均未签名，macOS 首次启动需允许打开。

`*.nupkg`、`releases.*.json` 用于应用在线更新，请保留原始文件名。SHA256SUMS.txt 提供下载校验。

六个平台由 CI 进行 Native AOT 构建；自动化测试不代表全部系统/架构已完成第三方软件数据迁移和连续版本升级的实机验证。
