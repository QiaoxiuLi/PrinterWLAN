# Windows 10/11 真机验收

## 目标

证明同一个 `PrinterWLAN-Setup-x64.exe` 在普通 Windows 桌面系统上无需用户另装 .NET、Visual C++ Runtime、Node.js、Python、Java、Microsoft Office、LibreOffice 或 PDF 工具，即可完成安装、开机服务、登录、PDF/Word 处理和 Windows 打印驱动输出。

## 自动验收

Windows 11 Desktop 由 GitHub Actions 的 ARM64 桌面映像自动执行。x64 PrinterWLAN 必须成功安装、加载全部内置组件，并向真实 Windows Spooler 提交具有非零大小和有效页数的任务；随后由系统原生 ARM64 进程验证同一打印队列和 `Microsoft Print to PDF` 驱动能够生成有效 PDF。这样可以把应用提交链路与 GitHub ARM64 托管机的跨架构驱动限制分开记录。

Windows 10/11 x64 真机不会启用上述 ARM64 特例：必须由 PrinterWLAN 服务本身端到端生成带 `%PDF-` 签名的文件，才会通过验收。

## Windows 10 真机验收命令

在 Windows 10 x64 管理员 PowerShell 中，从仓库根目录运行：

```powershell
$password=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
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
- `printerwlan-spooler-evidence.json`：仅在 Windows 11 ARM64 跨架构驱动回退时生成，记录 PrinterWLAN 提交任务的大小、页数和队列状态；
- 上传、用户导出等烟雾测试中间证据。

验收脚本不会假装验证物理纸张。使用具体品牌打印机时，还应在管理员后台选择该打印机，以 PDF 和 DOCX 各打印一份，确认纸张、方向、单双面、颜色、纸盒和分辨率与驱动能力一致。
