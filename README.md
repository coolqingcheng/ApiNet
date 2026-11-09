# ApiNet

基于 .NET 10、Avalonia 和 CommunityToolkit.Mvvm 的跨平台网络调试工作台。布局参考 Postman，采用 VS Code 风格的紧凑深色主题。

[下载安装包](https://github.com/coolqingcheng/ApiNet/releases/latest) · [CI 构建](https://github.com/coolqingcheng/ApiNet/actions) · [问题反馈](https://github.com/coolqingcheng/ApiNet/issues)

![ApiNet 请求工作台](docs/images/workbench.png)

截图使用 Avalonia Headless + Skia 渲染真实界面，响应来自测试中的本地 HTTP 服务。

## 安装

| 系统 | 架构 | 下载文件 |
| --- | --- | --- |
| Windows | x64 / ARM64 | 对应架构的 `Setup.exe` |
| Linux | x64 / ARM64 | 对应架构的 `.AppImage`，执行前使用 `chmod +x` |
| macOS | Intel / Apple Silicon | 对应架构的 `.dmg` 或 `Portable.zip`，将应用复制到 Applications |

安装包自带运行时，无需安装 .NET。所有包均未签名，不通过应用商店分发。macOS 首次打开时，在“系统设置 → 隐私与安全性”中允许打开；应用应放在当前用户可写的位置，以便更新。Linux 的 CI 构建基线为 Ubuntu 24.04；部分发行版运行 AppImage 需要 FUSE。

Windows 从 0.1.1 起，再次运行 Setup.exe 会直接覆盖安装/修复，同版本也可修复缺失的程序文件，不再弹出“已经安装”的确认。安装时会关闭已有实例，完成后打开应用；工作区、环境、历史和本地 CA 保留在独立的数据目录。命令行支持 `--silent`（不自动启动）、`--no-start`、`--installto <目录>` 和 `--log <文件>`。

## 启动

安装 .NET 10 SDK 后，在项目目录运行：

```sh
dotnet restore
dotnet run --project src/ApiNet.Desktop
```

测试：

```sh
dotnet test ApiNet.sln
```

UI 测试通过 Avalonia Headless + Skia 渲染真实 XAML，快照保存在 `artifacts/screenshots`。这些是无窗口渲染快照，不代表已在六种系统/架构上完成实机验证。

测试覆盖真实 HTTP/HTTPS、WebSocket、代理、增量导入、SQLite、文件下载及 MVVM/界面渲染。六个平台的 Native AOT 安装包由 GitHub Actions 构建，实际结果见对应的工作流。自动化测试不等于所有系统的安装和升级流程均已完成实机验证。

v0.1.0 验证记录：

- Windows、Linux、macOS 均通过 32 项自动化测试（24 项服务/网络测试 + 8 项桌面测试）。
- 六个 RID 的 Native AOT 编译、Velopack 打包及 Release 上传全部成功，见[发布工作流](https://github.com/coolqingcheng/ApiNet/actions/runs/42586154097)。
- Windows x64 原生程序已在本机验证启动、工作区数据库创建和正常退出。
- 使用 Velopack 测试定位器模拟旧版本，已从公开 GitHub Releases 检查、下载并校验真实的 Windows x64 更新包，无需访问令牌；这项检查未执行应用替换和重启。

## 已实现

- 多级文件夹：新建、重命名、删除、拖动移动；请求支持 HTTP 和 WebSocket。
- 每个请求独立标签页；单击打开、保存、复制、关闭与草稿恢复；支持 Ctrl+N、Ctrl+S、Ctrl+W。
- HTTP 方法、URL、可增删/启用的 Query 和 Header 参数行、Bearer Token、重定向控制、取消和响应查看。
- URL、参数、Header、认证和请求体中的 `{{变量}}` 高亮；JSON/XML/HTML 语法高亮和 JSON/XML 格式化，支持 JSON、XML、Text、HTML、JavaScript 请求体类型。显式填写的 Content-Type 优先于类型默认值。
- 文件响应完整流式接收，自动提示保存到本地；取消选择后仍可点击“保存响应”。文本响应也可保存原始内容，无需重新发送请求。
- WebSocket：ws/wss、握手 Headers、子协议、文本/JSON、Base64 二进制、双向消息及会话历史。
- 公共变量及环境变量，使用 `{{baseUrl}}`、`{{token}}` 等引用；支持嵌套引用，缺失或循环引用会阻止发送。
- SQLite 保存工作区和历史；历史可按时间、方法、环境、执行结果、URL/名称/文件夹关键词筛选，恢复后不会自动发送。
- Swagger 2.0、OpenAPI 3.0/3.1 JSON 导入到选定文件夹；按首个 tag 分组；导入源绑定、增量更新、字段冲突预览、来源移除标记。
- 导出 OpenAPI 3.0.3，保留相关模型引用，通过 `x-apinet-*` 扩展恢复文件夹及 WebSocket 配置。
- 本地 HTTP/1.1 正向代理、HTTPS CONNECT 隧道、可选 HTTPS 解密、WebSocket 帧转发及查看、抓包转调试。
- 关闭时退出或隐藏到托盘；Windows/macOS 提供托盘菜单，Linux 关闭到托盘选项当前回退为最小化窗口。
- Velopack 检查更新、后台自动下载、立即重启升级/退出时升级。开发运行不会尝试安装更新。

## 基本使用

左侧选择文件夹后新建请求，或右键导入 OpenAPI。选择“工作区根目录”后，可在根目录新建文件夹或导出整个工作区。每个导入源保存文件路径，再次导入同一路径到同一文件夹会增量更新；文件迁移时需重新选择导入源。

在顶部“管理变量”中配置公共变量和开发/测试等环境。当前环境覆盖公共变量。导入的新服务会在需要时创建独立的 `import_xxxxxx.baseUrl`，避免复用已有服务地址。路径参数如 `{{id}}` 需要在变量中配置。

Query 和 Headers 使用“添加参数”和每行的删除按钮编辑，取消勾选可保留参数但不发送。Query 值在解析变量后进行 URL 编码。Body 类型会自动设置默认 Content-Type，Headers 中手动指定的值会覆盖默认值。JSON/XML 高亮在编辑期间生效，`{{变量}}` 单独以橙色突出显示。

![请求体高亮](docs/images/body-editor.png)

修改请求后点击保存或 Ctrl+S。退出应用会保留未保存草稿，重启后恢复标签页；网络连接不会自动重连。历史恢复的数据可能已脱敏，重新发送前需补充认证信息。

## 代理抓包

默认监听 `127.0.0.1:8877`，启动后手动将客户端代理指向此地址。例如：

```sh
curl --proxy http://127.0.0.1:8877 http://example.com
```

HTTPS 默认透传 TLS；需要解密时，在设置中开启 HTTPS 解密，导出 CA 并由用户在客户端信任，再重启代理。程序不自动改写系统代理或信任库。CA 私钥保存在本地应用数据目录的 `certificates` 中。

HTTP 抓包和主动 HTTP 调试分别保留至多 1 MB、4 MB 的响应预览；代理继续转发完整响应，主动请求的完整响应流式写入临时文件供下载，不受预览限制。临时响应在下一次请求、关闭标签或退出时清理，历史不保存临时路径或二进制正文。WebSocket 抓包按帧展示，压缩帧/二进制帧显示 Base64。列表最多显示最近 1000 条抓包，主动 WebSocket 每次会话最多保留 1000 条消息。

## 数据目录

Windows 默认使用 `%LOCALAPPDATA%/ApiNetData` 保存数据，与 `%LOCALAPPDATA%/ApiNet` 中的安装文件分开。0.1.0 的工作区数据库、待提交的 SQLite WAL 和 `certificates` 会自动迁移；迁移不覆盖已有新版数据库，源文件保留到覆盖安装或用户自行清理。其他系统继续使用系统 LocalApplicationData 下的 `ApiNet`。

`workspace.db` 保存配置、草稿和历史。环境变量中的敏感值保存在本机数据库，历史中的常见认证头及标记为敏感的变量值会脱敏。可用 `APINET_DATA_DIR` 环境变量指定独立的数据目录，该目录应位于安装目录之外。Windows 安装日志默认保存在 `%LOCALAPPDATA%/ApiNetSetupLogs/setup.log`。

## Native AOT 发布

发布使用原生 AOT，目标为 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`。在对应操作系统上用 PowerShell 7 执行：

```powershell
./scripts/publish.ps1 -Rid win-x64 -Version 0.1.0
```

- Windows 需要 Visual Studio C++ 桌面构建工具和 Windows SDK，ARM64 额外需要 ARM64 C++ 工具链。生成未签名 Setup.exe。
- Linux 需要 clang、zlib 开发库和 squashfs-tools，生成 AppImage。CI 的 Linux 基线为 Ubuntu 24.04。
- macOS 需要 Xcode Command Line Tools，生成便携 `.app.zip` 和 `.dmg`。不配置 Developer ID 签名、公证或商店发布。

在仓库 Actions 中运行 **Native AOT packages**，输入递增的 SemVer 版本号（例如 `0.1.1`）。工作流先运行测试，再并行构建六个平台，全部成功后发布同名 GitHub Release，同时上传安装包、更新包、架构索引和 SHA256 校验文件。无需配置签名密钥或个人访问令牌，发布使用工作流内置的 `GITHUB_TOKEN`。本地输出在 `artifacts/releases/{RID}`。

## 在线更新源

默认更新源已填写公开仓库地址：

```text
https://github.com/coolqingcheng/ApiNet
```

Velopack 直接读取 GitHub Releases，用户无需 GitHub 令牌。CI 的 Actions artifacts 用于构建之间传递文件，应用使用 Release 中的长期下载地址。每个平台有独立通道：`win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`；对应 `releases.{通道}.json` 与 `*.nupkg`，不要重命名或删除更新资产。

启动及每四小时检查一次，也可手动检查。发现新版本后自动下载并校验，再提示立即重启升级或退出时升级。“退出时升级”只在真正退出应用时执行，隐藏到托盘会继续运行。开发运行不安装更新。首版需要先安装正式包；后续发布更高版本即可获得更新。尚未完成六个平台的连续版本安装/升级实机验证。

## 第一版边界

- 代理 HTTPS 解密协商 HTTP/1.1，暂不提供 HTTP/2、HTTP/3、gRPC、透明代理或系统代理自动接管。
- OpenAPI 只接收单文件 JSON 的本地引用；外部引用需预先打包，暂不支持 YAML。Swagger 2.0 的部分复杂 formData、OAuth 流程和 OpenAPI 3.1 JSON Schema 特性需要后续完善。
- OpenAPI 文件不包含抓包、历史和完整环境集。第三方工具重新导出时，可能丢弃 `x-apinet-*` 扩展。
- 历史筛选当前加载最近 1000 条记录；抓包列表为当前运行期间的数据。
- AOT 构建脚本和 CI 已提供；是否成功发布以对应平台的实际构建结果为准。

## 代码结构

```text
src/ApiNet.Desktop          Views / ViewModels / Services / Styles / Assets
src/ApiNet.Core             模型、接口、源生成 JSON 上下文
src/ApiNet.Infrastructure   HTTP、WebSocket、变量、SQLite、OpenAPI、更新
src/ApiNet.Proxy            正向代理、HTTP 协议读写、本地 CA
src/ApiNet.Setup            Windows 原生安装入口、覆盖修复和旧数据迁移
tests/ApiNet.Tests          服务与网络集成测试
tests/ApiNet.Desktop.Tests  MVVM 交互及 XAML 渲染测试
```

窗口只组合视图，功能按目录拆分；MainWindowViewModel 的协调命令按 Workspace、History、Capture、OpenApi 分文件。各请求 Tab 维护自己的状态，网络与存储实现不依赖 Avalonia。
