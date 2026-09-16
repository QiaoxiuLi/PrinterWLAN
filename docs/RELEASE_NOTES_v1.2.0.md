# PrinterWLAN v1.2.0

本版本把原本面向 Windows Server 2025 的安装与验证链路扩展到普通 Windows 10/11 电脑，并继续保留管理员统一设置打印机的工作方式。

## Windows 10/11 原生适配

- 支持 Windows 10 x64 build 17763 或更高、Windows 11 x64，以及 Windows 11 ARM64 的 x64 兼容模式。
- 安装包继续自带 .NET 10、PDF.js、PDFium、SkiaSharp 和独立 LibreOffice。
- 新增随安装包固定版本分发的 Microsoft Visual C++ v14 x64/ARM64 运行库，安装程序自动静默配置，用户不需要另行下载依赖。
- Windows Service 改为依赖 Print Spooler，并使用延迟自动启动；系统启动阶段打印服务尚未就绪时，不会抢先启动失败。
- 服务升级不再删除后立即重建，而是停止并原位更新配置，避免普通桌面 Windows 上出现“服务已标记为删除”的升级竞态。
- 安装程序明确拒绝过旧 Windows，并在每次安装后运行内置兼容性检查。

## 新增自检

管理员可运行：

```cmd
printerwlan doctor
```

它会检查 Windows 版本与架构、PDF.js、LibreOffice、SQLite、PDFium、SkiaSharp、程序数据目录、Windows Print Spooler 和打印机可见性。缺少关键组件时命令返回失败，不再等到用户上传或打印时才暴露问题。

## 验证升级

- Windows Server 2025：完整构建、单元/集成、安装、服务、HTTP、浏览器、PDF、Word、模拟打印、升级与卸载。
- Windows 11 ARM Desktop：x64 发布候选完成安装和全部内置组件检查；PrinterWLAN 向真实 Spooler 提交非空单页任务，并由原生 ARM64 控制进程通过同一 `Microsoft Print to PDF` 驱动生成有效 PDF。该拆分专用于 GitHub ARM64 托管机的跨架构驱动限制。
- Windows 10：提供 `scripts/WindowsClientAcceptance.ps1` 真机验收脚本，执行与 Windows 11 相同的安装、文档和系统打印驱动链路并生成 JSON 证据。
- v1.1.0 → v1.2.0 升级保留网站名称、用户、凭据、日志、数据库和管理员已选打印机。
- Release 增加人工验收 commit 门槛：Windows 10/11 x64 与实体打印机未针对同一 commit 验收时，工作流拒绝发布。

## 仍需注意

- PrinterWLAN 不包含打印机厂商驱动。目标打印机必须先作为 Windows 系统打印队列安装，并能由 `LocalSystem` 服务访问。
- 自动化系统打印使用 Windows 自带 PDF 打印驱动验证完整软件链路；不同品牌物理打印机的最终出纸仍需在对应硬件上现场验收。
- Windows 10 Home/Pro 已结束微软常规安全支持，产品可安装并提供兼容检查，但长期部署建议使用仍受支持的 Windows 11。
