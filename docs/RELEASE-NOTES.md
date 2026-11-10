ApiNet 0.1.7：工作区多选、标签栏与 WebSocket 界面统一。

- 左侧集合树移除选择框，按住 Ctrl 点击多选/取消选择（macOS 使用 ⌘）；普通点击单选并打开请求，多选和右键保留当前请求标签。导出直接使用选择范围，仍支持文件夹递归和重叠去重。
- 请求标签增加独立边框和间距，固定单行横向滚动；右侧“全部标签”列表支持名称、方法、URL 的模糊搜索，点击或按 Enter 切换后自动滚动定位。
- WebSocket 去掉旧折叠布局，改用与 HTTP 相同的 Params / Headers / Body / Authorization / Settings；Headers 共用三列表格和 Bulk Edit，Body 支持多种文本高亮和格式化，下方显示双向消息。
- 侧栏字体符号替换为固定尺寸矢量图标，避免首次显示缺失和鼠标移入后才重绘。
- Logo 改为网络节点图案，统一透明 SVG/PNG、多尺寸 ICO、窗口、任务栏、托盘、启动器与安装包。Windows 安装或首次启动新版本时刷新本应用的 Shell 图标缓存。

Windows Setup.exe 支持覆盖安装和同版本修复。Windows、Linux、macOS 均提供 x64 / ARM64 Native AOT 包，在线更新继续使用公开 GitHub Releases；`*.nupkg` 和 `releases.*.json` 用于更新，SHA256SUMS.txt 提供校验。
