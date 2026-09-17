# PrinterWLAN v1.2.0

PrinterWLAN v1.2.0 是面向 **Windows Server 2025 x64** 的正式稳定版。本版本已在 GitHub `windows-2025` 干净环境中完成构建、安装、v1.1.0 原地升级、服务、真实非 loopback 局域网 HTTP、浏览器、PDF/Word、Windows PDF 打印驱动、卸载和校验和验证。

本 Release **不声明 Windows 10、Windows 11 或实体打印机已经完成正式支持/硬件验收**。更广泛的 Windows 桌面适配和实体硬件验证保留到 v1.3.0 或后续版本的独立验收门槛。

## 重要修复

- 修复局域网 IP 的普通 HTTP 页面因 `crypto.randomUUID()` 在非安全上下文不可用而停在“正在准备页面”的问题。设备 ID 与浏览器会话现在统一使用兼容 UUID v4 生成器：优先原生 `randomUUID`，回退到 `getRandomValues`，并带有不让页面白屏的最终保护。
- 新增通过 Windows runner 实际非 loopback IPv4 地址访问 `http://<runner-lan-ip>:8080` 的 Playwright 回归，验证页面初始化、管理员入口、普通用户登录、移动 viewport 和 Console 错误。
- 修复“PrinterWLAN 管理控制台”打开后立即退出。开始菜单、安装完成入口和 ONLOGON 计划任务统一使用持续存在的 `cmd.exe /k`，可直接运行 `status`、`doctor` 和 `pwd`。
- 增加本地 favicon，消除 `/favicon.ico` 类无意义资源错误且不依赖 CDN。

## Windows Server 2025 可靠性

- 安装包内置 .NET 10、PDF.js、PDFium/SkiaSharp、独立 LibreOffice 和 Microsoft Visual C++ v14 x64 Runtime，目标服务器无需另装运行依赖。
- `PrinterWLAN` 服务依赖 Print Spooler、使用 delayed-auto，并配置异常后 5 秒、15 秒、60 秒恢复重启。
- 升级时停止并原位更新现有服务，不采用 delete/create，避免“service marked for deletion”竞态。
- 安装或升级会清理同名防火墙规则后重建唯一 TCP 8080 入站规则。
- `printerwlan doctor` 检查 Windows/架构、PDF.js、LibreOffice、SQLite、PDFium、SkiaSharp、ProgramData、Spooler 和打印机可见性；关键组件缺失时返回非零状态。
- 产品版本由统一构建属性生成，CLI、文件属性、安装器、Web 和 Release 均为 1.2.0。

## 管理员统一打印机

- 管理员查看 Windows 打印队列并选择所有用户统一使用的一台打印机；选择在服务重启后保留。
- 普通用户不显示打印机名称、ID、列表或选择器，提交接口拒绝 `printer`、`printerId`、`printerName`、`targetPrinter` 等字段注入。
- 用户打印能力只来自管理员当前指定打印机的驱动。
- 未配置打印机时仍可上传和预览，但提交打印会被服务端阻止。
- 已配置打印机被删除、改名、离线或不可用时不会自动切换到其他打印机。

## 升级和卸载

- 自动化从官方 v1.1.0 安装包原地升级，验证保留网站名称、管理员密码、用户、用户密码数据、管理员打印机选择、Activity 使用记录、Diagnostics 和业务数据库。
- 卸载移除服务、防火墙规则、ONLOGON 任务、PATH 项和程序目录；可选择保留 `C:\ProgramData\PrinterWLAN` 以便重装，或明确永久删除全部数据。

## 下载与校验

Release 提供：

- `PrinterWLAN-Setup-x64.exe`
- `PrinterWLAN-Setup-x64.exe.sha256`
- `PrinterWLAN-v1.2.0-User-Guide-zh-CN.pdf`

请在 Windows Server 2025 x64 上以管理员身份运行安装包。打印机厂商驱动不包含在 PrinterWLAN 中，目标打印队列必须事先安装，并可由 `LocalSystem` 服务访问。自动化 Windows PDF 驱动验证证明软件打印链路，不能替代每一种实体打印机的现场出纸验收。
