Post.NET 0.1.20：ApiNet 正式更名为 Post.NET。

- 产品名称统一为 Post.NET，窗口标题显示“Post.NET 版本号 — API 调试工具”，顶部标识、托盘、设置、导出文档与安装信息同步更新。
- 代码项目、解决方案和命名空间统一改为 PostNet，README 与构建发布配置同步更新。
- Windows、Linux、macOS 的公开安装包使用 Post.NET 名称，继续提供原有三个 Native AOT 目标。
- 保留旧版更新标识、启动入口、数据目录与 OpenAPI 扩展，旧版可直接在线升级，已有工作区、环境、历史、设置和本地 CA 继续使用。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows x64、Linux x64、macOS ARM64 均提供 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。

更新说明展示功能自 0.1.11 客户端起生效；更早版本需要先升级，后续升级提示即可显示更新内容。
