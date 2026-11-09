ApiNet 0.1.5：文档多选导出、透明 Logo 与桌面发布调整。

- 集合树支持勾选多个文件夹和请求；文件夹递归包含所有子项，重叠选择自动去重，底部显示实际请求数。
- 支持导出 Markdown、PDF 和 OpenAPI JSON，使用当前已打开标签页的编辑内容；未勾选时导出当前选中项，也可切换到整个工作区。
- Markdown/PDF 包含目录、URL、Query、Headers、表单、请求体、WebSocket 子协议，以及导入文档中可用的描述、响应和引用模型。PDF 内置中文字体，支持换行、分页和重复表头。
- 导出保留变量占位符，敏感变量脱敏，常见认证/Cookie 头和本机上传文件路径不导出；历史、抓包和实际响应不参与文档导出。
- 重新设计橙白 Logo，移除不透明深色背景与黑边，提供透明 SVG、1024px PNG 和九种尺寸的 ICO，统一用于窗口、托盘及安装包。
- 停止 Android 打包，后续仅发布 Windows、Linux、macOS 的 x64 / ARM64 六个 Native AOT 桌面目标。

Windows Setup.exe 支持直接覆盖安装和同版本修复；桌面安装包仍未签名。在线更新继续使用本仓库公开 GitHub Releases，支持后台下载、立即升级或退出时升级。

`*.nupkg`、`releases.*.json` 用于桌面在线更新。SHA256SUMS.txt 提供校验。
