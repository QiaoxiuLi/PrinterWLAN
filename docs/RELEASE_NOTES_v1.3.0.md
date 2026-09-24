# PrinterWLAN v1.3.0

PrinterWLAN v1.3.0 是面向 Windows 10/11 x64 桌面主机的兼容版本，同时支持 Windows Server 2025 x64 和 Windows 11 ARM64 主机中的 x64 应用兼容模式。正式 Release 只会在同一提交完成 Server 2025、Windows 10 x64、Windows 11 x64 和 Windows 11 ARM64-host 四个平台验收后创建；仓库中的候选构建或 GitHub Actions Artifact 不等于正式发布。

## Windows 桌面兼容

- 安装包继续自带 .NET 10、Microsoft Visual C++ v14 x64 Runtime、PDF.js、PDFium/SkiaSharp 和独立 LibreOffice，目标电脑不需要另装 .NET、Node.js、Python、Java、Microsoft Office 或 LibreOffice。
- 桌面安装包使用独立的 CET 兼容构建，解决部分 Windows 10 22H2 x64 设备在程序进入业务代码前提示“不完全支持 CET”并退出的问题；Windows Server 2025 常规构建不使用该开关。
- Windows PowerShell 5.1 验收脚本不再依赖 PowerShell 7 的 `-Form`、`-SkipHttpErrorCheck` 或进程树 `Kill(Boolean)` API。
- 所有包含中文用户名的 JSON 请求明确使用 UTF-8，避免 Windows PowerShell 5.1 默认编码造成登录失败。
- 管理控制台在 Windows 10 启动时切换到 UTF-8 代码页，中文提示可正确显示；测试读取 `.cmd` 时也显式使用 UTF-8。
- GitHub 专用环境文件只在 GitHub Actions 内写入，普通 Windows 10 真机运行验收脚本不会因路径为空而失败；动态测试密码会在 Actions 日志中先行掩码。

## 管理员统一打印机

- 管理员在后台选择所有用户统一使用的一台 Windows 打印机，普通用户不查看、选择或通过 API 指定打印机。
- 统一打印机设置、用户、管理员密码、网站名称、Activity 和 Diagnostics 在 v1.2.0 → v1.3.0 原地升级中保留。
- 未配置打印机时允许上传和预览，但服务端拒绝创建打印任务；目标打印机被删除、改名、离线或能力变化时不会静默切换到其他打印机。

## 已验证链路

- Windows 10 Pro 22H2 x64 build 19045.2965 真机：安装、Windows Service、无额外运行依赖、原生 SQLite/PDFium/SkiaSharp、内置 LibreOffice、管理控制台、LAN HTTP、中文用户导入与登录、PDF/DOCX、管理员打印机策略和 Microsoft Print to PDF 驱动链路。
- Windows 11 Pro 25H2 x64 build 26200 真机：使用与 Windows 10 相同的候选安装包完成无额外依赖、服务、LAN、PDF/DOCX、管理员打印机策略、Windows Spooler/打印驱动和卸载验收。
- Windows 11 ARM64 托管主机中的 x64 应用兼容模式：安装、服务、原生组件、LAN HTTP、PDF/DOCX 与 Windows 打印驱动链路。
- Windows Server 2025 x64：locked restore、构建、单元/集成测试、v1.2.0 原地升级、浏览器、打印驱动、卸载和数据保留回归。

Microsoft Print to PDF 能证明软件到 Windows Spooler/驱动的输出链路，但不能证明任意实体打印机已经出纸。实体打印机不作为 v1.3.0 GitHub Release 的发布门禁；正式部署前仍建议管理员针对实际型号检查 PDF、DOCX、纸张、方向、单双面、颜色、纸盒和分辨率。

## 安全与平台说明

Windows 10 普通版本及 .NET 10 对 Windows 10 的厂商支持范围有限。v1.3.0 的桌面兼容构建关闭 apphost CET 标记以覆盖已发现的 Windows 10 启动问题，这是一项明确的兼容性取舍；可升级时优先使用仍受支持并及时更新的 Windows 11。PrinterWLAN 只适合受信任 LAN/VPN，不应把 HTTP 8080 直接暴露到互联网。

## 下载与校验

正式 Release 提供：

- `PrinterWLAN-Setup-x64.exe`
- `PrinterWLAN-Setup-x64.exe.sha256`
- `PrinterWLAN-v1.3.0-User-Guide-zh-CN.pdf`

在管理员 PowerShell 中运行 `Get-FileHash .\PrinterWLAN-Setup-x64.exe -Algorithm SHA256`，确认结果与 `.sha256` 文件完全一致后再安装。打印机厂商驱动不包含在 PrinterWLAN 中，必须先在 Windows 安装目标 x64 打印队列，并确保 `LocalSystem` 服务可访问。
