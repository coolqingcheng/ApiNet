ApiNet 0.1.18：收紧左侧工作区菜单树的节点间距。

- 文件夹、HTTP 请求和 WebSocket 请求的树节点行高由 32 px 收紧为 24 px，同一屏可显示更多请求。
- 减少节点上下内边距，保留原有字号、图标及层级缩进，文字与展开箭头保持垂直居中。
- 深色、浅色和午夜蓝主题使用一致的紧凑行距，支持原有的展开/折叠、单选、Ctrl/⌘ 多选及右键菜单。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows x64、Linux x64、macOS ARM64 均提供 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。

更新说明展示功能自 0.1.11 客户端起生效；更早版本需要先升级，后续升级提示即可显示更新内容。
