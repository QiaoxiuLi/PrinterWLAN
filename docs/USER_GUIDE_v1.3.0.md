# PrinterWLAN v1.3.0 普通用户与管理员使用说明

适用系统：Windows 10 22H2 x64、Windows 11 x64；Windows Server 2025 x64 继续执行兼容回归  
适用对象：安装和维护 PrinterWLAN 的管理员，以及通过浏览器提交文件的普通用户

PrinterWLAN 把一台 Windows 电脑变成局域网网页打印主机。管理员统一选择 Windows 中已经安装的一台打印机并创建用户；普通用户不安装客户端、不选择打印机，只需使用浏览器登录、上传 PDF 或 Word 文件并设置当前打印机支持的打印参数。

> GitHub Actions 中的候选安装包不是正式 Release。请只从项目的 GitHub Releases 页面下载正式 v1.3.0，并同时下载校验和文件。PrinterWLAN 不包含打印机厂商驱动。

## 1. 安装前准备

管理员需要准备：

- 一台 64 位 Windows 10 22H2 或 Windows 11 电脑；建议安装全部可用的 Windows 安全更新；
- Windows 管理员账号；
- 受信任的家庭、办公室 LAN 或 VPN，客户端能访问主机 TCP 8080；
- 已在 Windows“打印机和扫描仪”中安装并能正常测试打印的 x64 打印机驱动；
- 正式 Release 中的 `PrinterWLAN-Setup-x64.exe` 和 `PrinterWLAN-Setup-x64.exe.sha256`。

安装包已包含程序运行所需的 .NET、Microsoft Visual C++ Runtime、PDF.js、PDFium/SkiaSharp 和 LibreOffice。目标电脑不需要另外安装 .NET、Node.js、Python、Java、Microsoft Office 或 LibreOffice，也不需要在运行时连接 CDN、云服务或外部 API。

网络共享打印机需要允许 Windows 的 `LocalSystem` 服务访问。只在当前登录用户会话中可见的临时打印队列不适合作为统一打印机。

## 2. 校验安装包

在安装包目录打开 PowerShell：

```powershell
Get-FileHash .\PrinterWLAN-Setup-x64.exe -Algorithm SHA256
```

把输出与 `.sha256` 文件中的哈希逐字比较。完全一致后，右键安装包并选择“以管理员身份运行”。安装程序会离线配置运行组件，创建 `PrinterWLAN` Windows Service、TCP 8080 防火墙规则、系统 PATH、开始菜单入口和管理员登录后的管理控制台任务。

## 3. 设置管理员密码

安装结束后，管理控制台会保持打开。在窗口中输入：

```cmd
printerwlan pwd "请换成至少8个字符的密码"
```

管理员网页登录没有用户名，只输入这里设置的密码。密码遗忘时可在主机管理控制台重新执行同一命令，不需要卸载或删除数据。不要把真实密码写进批处理、聊天记录或共享文档。

## 4. 检查状态

```cmd
printerwlan status
printerwlan doctor
```

`status` 显示版本、服务状态、HTTP 端口、局域网地址和 Windows 可见打印机数量。把类似 `http://192.168.1.20:8080` 的地址提供给同一网络中的用户。

`doctor` 检查 Windows 架构、内置 PDF.js、LibreOffice、SQLite、PDFium、SkiaSharp、ProgramData、Print Spooler 和打印机。出现“失败”时先不要投入使用；只有“尚未找到打印机”时，可以安装驱动后再次运行检查。

## 5. 管理员统一设置打印机

1. 用浏览器打开 `http://主机IP:8080`。
2. 点击右下角“管理员登录”，输入管理员密码。
3. 打开“打印机设置”。
4. 在 Windows 已安装打印机列表中选择目标打印机，点击“设为当前打印机”。

所有普通用户随后都使用这台打印机。用户页面不会显示打印机名称或选择器，也不能通过请求参数覆盖管理员设置。管理员以后切换打印机只影响新提交的任务，不会改写已排队任务。

如果未设置打印机，或当前打印机被删除、改名、离线，用户仍可上传和预览文件，但不能提交打印。系统不会自动切换到另一台打印机。

## 6. 创建普通用户

在“用户设置”导入 UTF-8 CSV，例如：

```csv
用户名
张三
李四
John Smith
```

每个新用户会得到 10 位随机密码。管理员可以显示、复制或导出凭据，再通过安全渠道分别发给用户。重复导入同一用户名会立即生成新密码，旧密码随即失效。

## 7. 普通用户打印

1. 在浏览器打开管理员提供的局域网地址。
2. 输入用户名和密码登录。
3. 选择或拖入 `.pdf`、`.doc`、`.docx` 文件，单个文件最大 100 MB。
4. 等待预览和打印设置出现。
5. 选择纸张、方向、单双面、页码范围、份数、彩色/黑白、纸盒、分辨率、缩放和居中。页面只显示管理员当前打印机驱动真正提供的能力。
6. 点击“提交打印”。

每次任务的页数乘以份数最多为 20；大文档可以分多次打印。页面显示“已发送到打印机”表示 Windows 已接受打印调用，不等于纸张已经物理输出。

## 8. 文件与隐私

上传文件仅保存在 `C:\ProgramData\PrinterWLAN\Temp` 的随机临时目录，用于转换、预览和打印。任务完成、失败或取消后会尽快删除；无人继续操作的残留最多约 60 分钟，服务启动和后台维护也会清理过期文件。文件正文不会写入数据库、使用记录、导出或缓存。

默认数据位置：

| 内容 | 目录 |
|---|---|
| 用户、密码哈希和设置 | `C:\ProgramData\PrinterWLAN\Data` |
| 临时上传和转换 | `C:\ProgramData\PrinterWLAN\Temp` |
| 使用与打印记录 | `C:\ProgramData\PrinterWLAN\Logs` |
| 程序诊断 | `C:\ProgramData\PrinterWLAN\Diagnostics` |
| 历史查询缓存 | `C:\ProgramData\PrinterWLAN\Cache` |

## 9. 升级 v1.2.0

直接以管理员身份运行 v1.3.0 安装包进行原地升级，不要先卸载。安装器会更新现有服务并保留网站名称、管理员密码、用户及其密码数据、管理员统一打印机选择、Activity、Diagnostics 和业务数据库。

升级完成后依次运行：

```cmd
printerwlan status
printerwlan doctor
```

再由管理员确认统一打印机仍正确，并用测试用户分别提交一份 PDF 和 DOCX。

## 10. 卸载

从 Windows“已安装的应用”或开始菜单运行卸载。卸载会移除服务、防火墙规则、登录任务、PATH 项和程序目录。卸载程序会询问是否保留 `C:\ProgramData\PrinterWLAN`：

- 选择保留：以后重装可继续使用原用户、设置和日志；
- 选择完全删除：永久移除全部 PrinterWLAN 数据，无法恢复。

## 11. 常见问题

### 网页打不开

在主机运行 `printerwlan status`，确认服务与网站均正常；使用显示出的真实局域网地址，不要在其他设备使用 `127.0.0.1`。确认客户端与主机处于同一 LAN/VPN，并检查 TCP 8080 防火墙规则。

### Windows 10 提示 CET 或程序立即退出

确认安装的是正式 v1.3.0 桌面安装包，不是 v1.2.0 Server 2025 安装包或旧候选文件。重新核对 SHA-256 后执行修复安装；若仍失败，请运行 `printerwlan doctor` 并把 `Diagnostics` 交给管理员。

### 中文管理控制台乱码

确认使用 v1.3.0 开始菜单入口。v1.3.0 会自动切换 UTF-8 代码页；不要继续使用旧安装目录中复制出的 `.cmd` 文件。

### Word 文件无法预览

运行 `printerwlan doctor`，确认内置 LibreOffice 通过。受密码保护、损坏或格式异常的 Word 文件可能无法转换，可先另存为 PDF。

### 能预览但不能提交打印

请联系管理员检查是否已经设置统一打印机，以及该打印机是否仍存在、在线并可由 `LocalSystem` 访问。普通用户无法自行换用其他打印机。

## 12. 网络安全

PrinterWLAN 使用局域网 HTTP，不会自动创建 HTTPS 证书。不要把 TCP 8080 直接暴露到互联网；如组织要求 TLS，应由网络管理员在可信边界部署反向代理。Windows 10 普通版本及相关运行时的厂商支持范围有限；可升级时优先使用仍受支持并及时更新的 Windows 11。
