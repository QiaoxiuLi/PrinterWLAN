# Windows 10/11 真机验收

## 目标

证明同一个 `PrinterWLAN-Setup-x64.exe` 在普通 Windows 桌面系统上无需用户另装 .NET、Visual C++ Runtime、Node.js、Python、Java、Microsoft Office、LibreOffice 或 PDF 工具，即可完成安装、开机服务、登录、PDF/Word 处理和 Windows 打印驱动输出。

## 自动验收

Windows 11 Desktop 由 GitHub Actions 的 ARM64 桌面映像自动执行。x64 PrinterWLAN 必须成功安装、加载全部内置组件，并向真实 Windows Spooler 提交具有非零大小和有效页数的任务；随后由系统原生 ARM64 进程验证同一打印队列和 `Microsoft Print to PDF` 驱动能够生成有效 PDF。这样可以把应用提交链路与 GitHub ARM64 托管机的跨架构驱动限制分开记录。

Windows 10/11 x64 真机优先要求 PrinterWLAN 服务端到端生成带 `%PDF-` 签名的文件。若无人交互的 `Microsoft Print to PDF` 队列保留作业而不落盘，则必须同时证明 PrinterWLAN 已向 Windows Spooler 提交非空、至少一页的 x64 打印作业，并由独立的系统原生 x64 控制过程使用同一驱动生成有效 `%PDF-` 文件；不得仅凭 API 的“已发送”状态通过验收。

## Windows 10 真机验收命令

在 Windows 10 x64 管理员 PowerShell 中，从仓库根目录运行：

```powershell
$rng=[Security.Cryptography.RandomNumberGenerator]::Create()
$bytes=New-Object byte[] 24
$rng.GetBytes($bytes)
$rng.Dispose()
$password=[Convert]::ToBase64String($bytes)
./scripts/WindowsClientAcceptance.ps1 `
  -InstallerPath ./PrinterWLAN-Setup-x64.exe `
  -AdminPassword $password `
  -OutputDirectory ./artifacts/windows-10-acceptance
```

脚本只接受 Windows 10/11 Desktop，自动使用 Windows 内置 `Microsoft Print to PDF` 驱动和位于 `ProgramData` 诊断目录的本地文件端口建立临时测试队列。它会验证：

- 安装包与 Windows 版本/64 位架构；
- 内置 .NET 应用、Visual C++ 原生运行库、SQLite、PDFium、SkiaSharp、PDF.js 和 LibreOffice；
- Windows Service 为 Automatic、Delayed Auto Start，并依赖 Print Spooler；
- HTTP 健康检查、管理员登录、用户导入和用户登录；
- 管理员统一选择打印机，用户 API 不暴露或接受打印机身份；
- 中文文件名 DOCX → PDF、PDF 页面读取与网页打印任务；
- 任务通过真正的 Windows 打印驱动生成带 `%PDF-` 文件签名的输出。

成功后会生成：

- `compatibility-evidence.json`：Windows 版本、build、架构、PrinterWLAN 版本和服务配置；
- `windows-driver-output.pdf`：Windows 打印驱动实际输出；
- `print-path-evidence.json`：端到端或 ARM64 拆分验证模式与输出大小；
- `printerwlan-spooler-evidence.json`：在 Spooler 提交与原生驱动控制拆分验证时生成，记录 PrinterWLAN 提交任务的架构、大小、页数和队列状态；
- 上传、用户导出等烟雾测试中间证据。

验收脚本不会假装验证物理纸张。实体打印机不是 v1.3.0 GitHub Release 的硬门禁；使用具体品牌打印机正式部署时，仍建议管理员在后台选择该打印机，以 PDF 和 DOCX 各打印一份，确认纸张、方向、单双面、颜色、纸盒和分辨率与驱动能力一致。

## Release 门槛

此门槛用于 v1.3.0 或后续 Windows Desktop 正式版本，不阻塞 Windows Server 2025 x64 的 v1.2.0。只有同时满足以下条件，才可以为该 commit 创建桌面正式版 tag：

桌面兼容候选包使用 `PrinterWlanDesktopCompatibility=true` 单独构建。该构建会关闭 .NET apphost 的 CET 兼容标记，以兼容部分无法启动 CET apphost 的 Windows 10 22H2 设备；常规 Server 2025 构建不传入该参数，继续保留 CET 标记。两类构建不得混用，桌面候选包也不得覆盖 v1.2.0 Server 2025 Release 资产。

1. 在 Windows Server 2025 对准备发布的 commit 执行完整构建、单元/集成、升级、浏览器、打印驱动和卸载回归；
2. 在 Windows 10 x64 真机执行上述脚本并通过；
3. 在 Windows 11 x64 真机执行上述脚本并通过；
4. 在 Windows 11 ARM64 主机运行同一 x64 安装包并通过自动验收；
5. Windows 10/11 x64 真机验收的源代码 commit 与准备打 tag 的 commit 完全一致；
6. 将 GitHub Actions 仓库变量 `PRINTERWLAN_WINDOWS10_VALIDATED_COMMIT` 和 `PRINTERWLAN_WINDOWS11_VALIDATED_COMMIT` 都设为该完整 commit SHA。Server 2025 与 ARM64-host 结果由同一 commit/tag 的工作流依赖关系保证。

Release workflow 会核对这些变量；任意一项缺失或与 tag commit 不一致时会主动失败，不会创建 GitHub Release。
