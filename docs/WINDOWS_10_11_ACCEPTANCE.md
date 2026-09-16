# Windows 10/11 真机验收

## 目标

证明同一个 `PrinterWLAN-Setup-x64.exe` 在普通 Windows 桌面系统上无需用户另装 .NET、Visual C++ Runtime、Node.js、Python、Java、Microsoft Office、LibreOffice 或 PDF 工具，即可完成安装、开机服务、登录、PDF/Word 处理和 Windows 打印驱动输出。

## 自动验收

Windows 11 Desktop 由 GitHub Actions 自动执行。Release 只有在 Windows Server 2025 测试与 Windows 11 Desktop 系统打印驱动测试都通过后才能发布。

## Windows 10 真机验收命令

在 Windows 10 x64 管理员 PowerShell 中，从仓库根目录运行：

```powershell
$password=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
./scripts/WindowsClientAcceptance.ps1 `
  -InstallerPath ./PrinterWLAN-Setup-x64.exe `
  -AdminPassword $password `
  -OutputDirectory ./artifacts/windows-10-acceptance
```

脚本只接受 Windows 10/11 Desktop，自动使用 Windows 内置 `Generic / Text Only` 驱动和本地文件端口建立临时测试队列。它会验证：

- 安装包与 Windows 版本/64 位架构；
- 内置 .NET 应用、Visual C++ 原生运行库、SQLite、PDFium、SkiaSharp、PDF.js 和 LibreOffice；
- Windows Service 为 Automatic、Delayed Auto Start，并依赖 Print Spooler；
- HTTP 健康检查、管理员登录、用户导入和用户登录；
- 管理员统一选择打印机，用户 API 不暴露或接受打印机身份；
- 中文文件名 DOCX → PDF、PDF 页面读取与网页打印任务；
- 任务通过真正的 Windows 打印驱动生成非空 `.prn` 输出文件。

成功后会生成：

- `compatibility-evidence.json`：Windows 版本、build、架构、PrinterWLAN 版本和服务配置；
- `windows-driver-output.prn`：Windows 打印驱动实际输出；
- 上传、用户导出等烟雾测试中间证据。

验收脚本不会假装验证物理纸张。使用具体品牌打印机时，还应在管理员后台选择该打印机，以 PDF 和 DOCX 各打印一份，确认纸张、方向、单双面、颜色、纸盒和分辨率与驱动能力一致。
