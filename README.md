# PrinterWLAN

PrinterWLAN 是面向受信任局域网的 Windows 网页打印服务器。它作为 Windows Service 在 **Windows 10、Windows 11 或 Windows Server x64** 上运行；手机、平板和电脑只需打开浏览器、登录、上传 PDF 或 Word 文件，即可调用主机中已经安装好的 Windows 打印机及其驱动完成打印。

正式安装包是自包含的：包含 .NET 10 运行时、PDF.js、PDFium/SkiaSharp、独立的 LibreOffice Windows x64 运行时，以及原生组件所需的 Microsoft Visual C++ v14 运行库。目标电脑不需要自行安装 .NET、Visual C++ Runtime、Node.js、Python、Java、Microsoft Office 或 LibreOffice；安装完成后的运行也不依赖互联网、CDN、云服务或外部 API。

## 支持的 Windows

| 系统 | 支持范围 |
|---|---|
| Windows 10 x64 | 1809 / build 17763 或更高；普通 Home/Pro 建议使用最终版 22H2 / build 19045 |
| Windows 11 x64 | 正式支持 |
| Windows 11 ARM64 | 通过系统的 x64 兼容模式运行 |
| Windows Server x64 | 2019、2022、2025 |

Windows 10 Home/Pro 已结束微软常规安全支持，PrinterWLAN 仍提供安装与兼容处理，但用于长期联网环境时建议升级到仍受支持的 Windows 11。打印机厂商驱动不属于 PrinterWLAN，必须先在 Windows 中安装；USB、本地 TCP/IP 或共享打印机应安装为这台电脑上的系统打印队列，使 `LocalSystem` 服务可以访问。

## 安装与首次启动

1. 从 [GitHub Releases](https://github.com/QiaoxiuLi/PrinterWLAN/releases) 下载 `PrinterWLAN-Setup-x64.exe` 和对应的 `.sha256` 文件。
2. 在受支持的 Windows 10、Windows 11 或 Windows Server 上以管理员身份运行安装程序。
3. 安装程序会离线配置全部随包运行组件，创建依赖 Windows Print Spooler 的 `PrinterWLAN` 延迟自动启动服务、TCP 8080 入站防火墙规则、系统 PATH、开始菜单入口，以及管理员登录桌面时打开的管理 CMD 任务。
4. 在自动打开的管理窗口中设置管理员密码：

   ```cmd
   printerwlan pwd "请使用至少8个字符的密码"
   ```

5. 在同一网络的设备打开 `http://服务器IP:8080`，通过右下角“管理员登录”进入后台，在“打印机设置”中选择所有用户统一使用的打印机。
6. 运行 `printerwlan doctor` 检查 Windows 版本、内置原生组件、数据目录、Print Spooler 和打印机；运行 `printerwlan status` 查看服务、端口和局域网地址。

Windows 中必须预先安装目标打印机及其 x64 驱动，并确保 `LocalSystem` 服务上下文可以访问该打印机。PrinterWLAN 不包含厂商打印机驱动。

## 用户与打印流程

网站首页是用户登录页，没有自助注册。右下角的浅色“管理员登录”入口进入管理后台，管理员账号没有用户名。

管理员在“用户设置”中导入只有一列的 UTF-8 CSV：

```csv
用户名
张三
李四
John Smith
```

表头可以是 `username`、`用户名`，也可以完全没有表头。每个新用户会得到一个由加密安全随机数生成器产生的 10 位密码；管理员可按需显示、复制或导出当前密码。重复导入已有用户名会立即重新生成密码，但永久保留原 `sequence` 位置。用户列表和凭据导出始终严格按首次导入顺序排列。

管理员在“打印机设置”中看到 Windows 当前安装的打印机、状态和 Windows 默认标记，并为 PrinterWLAN 选择唯一的当前打印机。该选择保存在服务端，服务或服务器重启后仍然有效。首次升级到 v1.1.0 时不会静默选择 Windows 默认打印机，管理员必须明确选择；如果已选打印机被删除、改名或离线，系统不会自动改用其他打印机。

用户登录后可拖放或选择 `.pdf`、`.doc`、`.docx` 文件。用户端不显示打印机名称、打印机列表或选择器，也不能通过 API 指定打印机；所有新任务在提交时绑定管理员当时选定的打印机。管理员之后切换打印机不会改写已经排队的任务。未配置或当前打印机不可用时，用户仍可上传和预览，但不能提交打印。

Word 文件由随安装包提供的 LibreOffice 独立进程转换为临时 PDF，PDF 与 Word 都通过本地 PDF.js 预览，并通过同一 PDFium/Windows PrintDocument 管线打印。用户可用的设置由管理员所选打印机的驱动实时提供，包括纸张、方向、单双面、页码范围、份数、逐份、彩色、纸盒、分辨率、缩放和居中；不支持的能力会隐藏或禁用。提交时和实际发送前，服务端都会重新验证打印机身份、状态和驱动能力。

每位用户滚动 60 秒内最多创建一个任务。所选页数乘以份数不得超过 20；100 页文档仍可上传和预览，只需分多次打印。网页中的“已发送到打印机”表示 Windows 打印调用已接受任务，不表示纸张已经物理输出。

## 文件与日志数据

上传文件只保存在随机 UUID 临时目录中，用于转换、预览和打印。任务成功、失败或取消后会尽快删除；无人继续操作的文件最多约 60 分钟，后台维护和服务启动时会清理过期残留。文件正文不会写入数据库、日志、导出或缓存。

默认数据位置：

| 内容 | 目录 |
|---|---|
| 业务数据库、用户和设置 | `C:\ProgramData\PrinterWLAN\Data` |
| 临时上传、转换和短期导出 | `C:\ProgramData\PrinterWLAN\Temp` |
| 使用记录与打印记录 | `C:\ProgramData\PrinterWLAN\Logs` |
| 程序诊断日志 | `C:\ProgramData\PrinterWLAN\Diagnostics` |
| 历史日志查询缓存 | `C:\ProgramData\PrinterWLAN\Cache` |

业务库与行为日志库分离。行为日志按服务器本地日期每 10 个自然日切片；已结束切片在 SQLite checkpoint、VACUUM、完整性检查后压缩成 ZIP，历史查询时临时只读解压。日志目录硬上限 40 GB，超过上限时只从最旧的、已成功压缩的切片开始删除，直到约 38 GB；当前写入切片不会被容量清理删除。

管理员可以筛选和分页查看访问、登录、上传、预览、打印及失败记录，按切片导出包含 UTF-8 BOM CSV 和 `manifest.json` 的 ZIP。“清空日志”必须重新验证管理员密码，只清除使用/打印记录与查询缓存，不会删除用户、密码、网站名称或管理员凭据。Serilog 诊断日志单独滚动保留 14 天，不显示在使用记录页面。

## 配置

默认监听所有网卡的纯 HTTP 端口 8080：

```json
{
  "PrinterWLAN": {
    "Port": 8080,
    "MaxUploadBytes": 104857600,
    "TempRetentionMinutes": 60,
    "WordConversionTimeoutSeconds": 60,
    "LogMaxBytes": 42949672960,
    "LogCleanupTargetBytes": 40802189312
  }
}
```

修改 `C:\Program Files\PrinterWLAN\appsettings.json` 后重启服务使端口和容量配置生效。修改端口时还需由服务器管理员同步调整 Windows Firewall 入站规则。网页显示名称在管理后台修改后立即生效，不会更改服务名和可执行文件名。

PrinterWLAN 有意只提供 HTTP，适合隔离、可信的 LAN/VPN。HTTP 不会对网络中的登录密码和文件传输提供 TLS 加密；不要把 8080 端口直接暴露到互联网。若组织需要 TLS，应在可信网络边界部署由组织管理的反向代理，本程序不会自动创建证书或强制 HTTPS。

## 管理命令

```cmd
printerwlan status
printerwlan doctor
printerwlan pwd "新的管理员密码"
```

`doctor` 在本机逐项检查操作系统、PDF.js、LibreOffice、SQLite、PDFium、SkiaSharp、数据目录、Print Spooler 和打印机可见性；缺少打印机是可后续处理的提示，缺少运行组件或 Spooler 则返回失败。`status` 显示服务状态、HTTP 端口、局域网地址、打印机数量和版本。`pwd` 直接更新业务数据库中的管理员 password hash，无需重启服务，密码本身不会写入日志。

## 卸载

从 Windows“已安装的应用”或开始菜单运行卸载程序。卸载会停止并删除 Windows Service、防火墙规则和登录计划任务。卸载程序会询问是否保留 `C:\ProgramData\PrinterWLAN`；默认建议保留，以便以后重新安装继续使用。选择完全删除会永久移除用户配置和日志。

## 从源码构建

开发/构建依赖：.NET 10 SDK `10.0.401`、PowerShell 7、Inno Setup 6，以及只用于 Playwright 测试的 Node.js（目标服务器不需要）。

```powershell
git clone https://github.com/QiaoxiuLi/PrinterWLAN.git
cd PrinterWLAN
./scripts/Prepare-ThirdParty.ps1
dotnet restore PrinterWLAN.slnx --locked-mode
dotnet build PrinterWLAN.slnx -c Release --no-restore
dotnet test PrinterWLAN.slnx -c Release --no-build
dotnet publish src/PrinterWLAN/PrinterWLAN.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/publish
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\PrinterWLAN.iss
```

`Prepare-ThirdParty.ps1` 从 [`third-party/manifests/dependencies.json`](third-party/manifests/dependencies.json) 读取固定 URL 和 SHA-256，校验后准备 PDF.js、LibreOffice 和 Microsoft Visual C++ v14 运行库。哈希不匹配会立即终止构建。大型第三方二进制、`bin`、`obj`、运行数据库、日志和安装产物均不提交到 Git。

## GitHub Actions

CI 首先在 `windows-2025` 执行 locked restore、Release build、单元/集成测试、`win-x64` self-contained publish、依赖下载与 SHA-256 校验、Inno Setup 编译、静默安装、Windows Service/HTTP/CLI/登录/PDF/Word/Fake printer smoke test、Playwright 多 viewport 测试和静默卸载。随后同一个安装包必须在 GitHub 的 Windows 11 Desktop runner 上通过安装、`doctor`、LibreOffice、PDFium、SQLite、服务重启配置，并通过系统 `Microsoft Print to PDF` 驱动产生真实打印输出文件，Release job 才能继续发布。

仓库还提供 [`scripts/WindowsClientAcceptance.ps1`](scripts/WindowsClientAcceptance.ps1)。在 Windows 10 x64 真机上运行它，可以执行与 Windows 11 CI 相同的安装、登录、PDF/Word、管理员统一打印机、Windows 打印驱动、服务和卸载前检查，并生成 `compatibility-evidence.json` 验收证据。CI 的 Windows 11 结果不能冒充 Windows 10 真机结果。

推送 `v*` tag 会运行相同完整验证，然后使用 GitHub 官方 `gh` CLI 创建非 Draft、非 Prerelease Release，并上传安装包与 SHA-256。

## 项目结构

```text
src/PrinterWLAN/                     ASP.NET Core、服务、打印、文档、存储与网页
tests/PrinterWLAN.Tests/             单元与数据库行为测试
tests/PrinterWLAN.IntegrationTests/  HTTP/权限集成测试
tests/PrinterWLAN.UiTests/           Playwright 响应式与主要流程测试
installer/                           Inno Setup 与管理 CMD
scripts/                             第三方准备和安装包 Smoke Test
third-party/manifests/               可复现依赖版本、URL、SHA-256
docs/                                验收清单与 Release Notes
```

## 第三方组件与许可

本项目自身使用 [MIT License](LICENSE)。第三方组件、版本和许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。构建过程会把来自固定发行包的权威 License/Notice 文本加入安装内容，LibreOffice 作为未修改的独立程序分发和调用。
