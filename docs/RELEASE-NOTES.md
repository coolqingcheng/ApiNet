ApiNet 0.1.6：重做 Postman 风格的参数编辑界面。

- Headers、Params、表单和 WebSocket Headers 统一为 KEY / VALUE / DESCRIPTION 表格，使用紧凑行高、细分隔线、灰色焦点边框和深浅主题配色。
- 直接在底部空行输入即可新增，保留输入焦点；支持悬停删除、全选启停、停用行弱化和 Tab / Shift+Tab 切换单元格。
- 增加 Bulk Edit / Key-Value Edit，保留重复 key、顺序、描述和匹配行的文件类型；发送、保存和草稿持久化可直接使用有效的批量修改，格式错误在编辑区提示并阻止发送/保存。
- Multipart 的 Text / File 类型切换移到 KEY 列，文件选择放在 VALUE 列，继续支持文本与多个文件混合上传。
- 参数描述支持本机保存、Postman / ApiPost / OpenAPI 导入和 Markdown / PDF / OpenAPI 导出；编辑 URL 时保留同名查询参数的描述。
- Headers 标签显示参数数量，调整请求区默认高度，自动跟随重定向移到 Settings 标签页。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows、Linux、macOS 均提供 x64 / ARM64 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。
