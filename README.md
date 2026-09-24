# PrinterWLAN

PrinterWLAN 是一款运行在 Windows 电脑或服务器上的局域网网页打印软件。

管理员只需要在一台 Windows 主机上安装 PrinterWLAN、添加用户并指定打印机，同一局域网内的手机、平板和电脑便可以通过浏览器上传 PDF 或 Word 文件并提交打印。普通用户不需要安装客户端，也不需要了解打印机的网络地址或驱动设置。

当前正式版本：**v1.3.0**

- [下载 PrinterWLAN v1.3.0](https://github.com/QiaoxiuLi/PrinterWLAN/releases/tag/v1.3.0)
- [直接下载安装程序](https://github.com/QiaoxiuLi/PrinterWLAN/releases/download/v1.3.0/PrinterWLAN-Setup-x64.exe)
- [查看安装程序 SHA-256](https://github.com/QiaoxiuLi/PrinterWLAN/releases/download/v1.3.0/PrinterWLAN-Setup-x64.exe.sha256)
- [下载普通用户中文使用说明](https://github.com/QiaoxiuLi/PrinterWLAN/releases/download/v1.3.0/PrinterWLAN-v1.3.0-User-Guide-zh-CN.pdf)

## 主要功能

- 手机、平板和电脑均可通过浏览器使用，无需安装客户端。
- 支持上传和打印 PDF、DOC、DOCX 文件。
- Word 文件会自动转换为 PDF，无需安装 Microsoft Office。
- 管理员统一指定所有用户使用的打印机，普通用户不需要选择打印机。
- 根据当前打印机驱动自动提供纸张、方向、单双面、彩色、纸盒、分辨率、份数、页码范围、缩放和居中等选项。
- 支持创建、导入、导出和管理用户账号。
- 支持查看访问、登录、上传和打印记录。
- 作为 Windows Service 自动运行，Windows 重启后可以自动恢复服务。
- 软件运行所需组件已经包含在安装包中，日常使用不依赖互联网或云服务。

## 支持的系统

| Windows 系统 | 支持方式 |
|---|---|
| Windows Server 2025 x64 | 支持 |
| Windows 10 22H2 x64 | 支持 |
| Windows 11 x64 | 支持 |
| Windows 11 ARM64 | 通过 Windows 自带的 x64 应用兼容功能运行 |

PrinterWLAN 安装程序已经包含 .NET 运行环境、LibreOffice 文档转换组件、PDF 处理组件和所需的 Microsoft Visual C++ 运行组件。目标电脑不需要另外安装 .NET、LibreOffice、Microsoft Office、Node.js、Python 或 Java。

PrinterWLAN 不包含打印机厂商驱动。使用前，请先在 Windows 主机中正确安装目标打印机及其 Windows x64 驱动，并确认该打印机可以在 Windows 中正常使用。

## 使用方式

PrinterWLAN 包含两种使用角色：管理员和普通用户。

### 管理员

管理员负责：

- 设置管理员密码；
- 创建或导入普通用户；
- 统一选择 PrinterWLAN 当前使用的打印机；
- 查看打印机状态和驱动支持的打印选项；
- 查看、筛选和导出使用记录；
- 修改网站显示名称；
- 检查服务运行状态。

管理员选择的打印机会保存在服务器中，Windows 或 PrinterWLAN 服务重启后仍然有效。如果该打印机被删除、改名或处于不可用状态，系统不会自动切换到另一台打印机，管理员需要重新选择。

### 普通用户

普通用户只需要：

1. 在浏览器中打开 PrinterWLAN 地址；
2. 输入管理员分配的用户名和密码；
3. 上传 PDF、DOC 或 DOCX 文件；
4. 预览文件并设置纸张、页码、份数、单双面、彩色等选项；
5. 提交打印。

普通用户看不到 Windows 打印机列表，也不能自行更换打印机。所有任务都会使用管理员当前指定的打印机。

## 安装与首次配置

### 1. 准备 Windows 主机

在准备安装 PrinterWLAN 的电脑上：

- 安装目标打印机及其 Windows 驱动；
- 使用 Windows 自带功能确认打印机可以正常打印；
- 确保 Windows Print Spooler 服务没有被禁用；
- 确保需要使用 PrinterWLAN 的设备与该电脑处于同一可信局域网或 VPN。

### 2. 安装 PrinterWLAN

从 [GitHub Releases](https://github.com/QiaoxiuLi/PrinterWLAN/releases) 下载 `PrinterWLAN-Setup-x64.exe`，然后以管理员身份运行。

安装程序会自动：

- 安装 PrinterWLAN 程序和随包运行组件；
- 创建 PrinterWLAN Windows Service；
- 配置服务随 Windows 延迟自动启动；
- 配置 TCP 8080 入站防火墙规则；
- 添加开始菜单中的管理控制台和卸载入口。

### 3. 设置管理员密码

安装完成后会打开 PrinterWLAN 管理控制台。在窗口中输入：

```cmd
printerwlan pwd "请设置至少8个字符的密码"
```

管理员账号不需要用户名，只需要管理员密码。

### 4. 打开管理后台

在 Windows 主机或同一局域网中的设备上打开：

```text
http://服务器IP:8080
```

在网页右下角选择“管理员登录”，输入刚才设置的管理员密码。

如果不知道服务器 IP，可以在管理控制台中运行：

```cmd
printerwlan status
```

### 5. 选择打印机

进入管理后台的“打印机设置”，从 Windows 已安装的打印机中选择一台作为 PrinterWLAN 当前打印机。

在管理员完成选择前，普通用户可以上传和预览文件，但不能提交打印。

### 6. 添加用户

管理员可以在“用户设置”中添加或批量导入用户。CSV 文件可以使用以下格式：

```csv
用户名
张三
李四
John Smith
```

系统会为新用户生成密码。管理员可以显示、复制或导出用户凭据，再将用户名和密码分别交给对应用户。

## 打印文件与选项

支持的文件类型：

- `.pdf`
- `.doc`
- `.docx`

Word 文件会通过安装包内置的 LibreOffice 转换为 PDF，再进行预览和打印。

网页显示的打印选项来自管理员当前选择的打印机驱动。不同打印机支持的功能可能不同；驱动不支持的选项会被隐藏或禁用。

默认使用限制：

- 单个上传文件最大 100 MB；
- 每位用户在滚动 60 秒内最多创建一个打印任务；
- 单次任务的“所选页数 × 份数”不能超过 20；
- 页数较多的文件可以上传和预览，但可能需要分多次提交打印。

网页提示“已发送到打印机”表示 Windows 已经接受打印任务，不等同于纸张已经实际输出。实体打印机的缺纸、卡纸、离线、耗材不足等状态仍需在打印机或 Windows 打印队列中检查。

## 数据与文件

上传的文件只用于转换、预览和打印，并保存在随机临时目录中。任务完成、失败或取消后，系统会尽快清理相关临时文件；长时间无人继续操作的文件也会自动清理。

默认数据位置：

| 内容 | Windows 目录 |
|---|---|
| 用户、设置和业务数据 | `C:\ProgramData\PrinterWLAN\Data` |
| 临时上传和转换文件 | `C:\ProgramData\PrinterWLAN\Temp` |
| 使用与打印记录 | `C:\ProgramData\PrinterWLAN\Logs` |
| 程序诊断信息 | `C:\ProgramData\PrinterWLAN\Diagnostics` |

文件正文不会写入用户数据库或使用记录导出文件。

## 管理命令

在 PrinterWLAN 管理控制台中可以使用以下命令：

```cmd
printerwlan status
printerwlan doctor
printerwlan pwd "新的管理员密码"
```

- `status`：显示服务状态、端口、局域网访问地址、打印机数量和软件版本。
- `doctor`：检查 Windows、内置组件、数据目录、Print Spooler 和打印机是否可用。
- `pwd`：修改管理员密码，修改后立即生效。

## 局域网与安全提示

PrinterWLAN 默认使用 HTTP 端口 `8080`，适合部署在家庭、办公室、学校或其他受信任的局域网/VPN 中。

HTTP 不会加密登录密码和上传文件，因此：

- 不要把 PrinterWLAN 的 `8080` 端口直接暴露到互联网；
- 不要在不受信任的公共网络中使用；
- 需要通过互联网访问时，应由网络管理员配置 VPN 或带 HTTPS 的反向代理；
- 请为管理员和普通用户设置不容易猜测的密码。

## 常见问题

### 网页打不开

请确认：

- Windows 主机已经开机并连接网络；
- 访问设备与 Windows 主机位于同一局域网或 VPN；
- 地址使用了正确的服务器 IP 和端口，例如 `http://192.168.1.20:8080`；
- `printerwlan status` 显示服务和网站状态正常；
- Windows 防火墙没有删除或阻止 PrinterWLAN 的入站规则。

### 用户不能提交打印

常见原因包括：

- 管理员尚未在“打印机设置”中选择打印机；
- 已选择的打印机被删除、改名或离线；
- Windows Print Spooler 没有运行；
- 当前设置不受打印机驱动支持；
- 用户在 60 秒内已经提交过任务；
- 本次打印页数乘以份数超过 20。

管理员可以运行 `printerwlan doctor` 检查本机环境。

### 是否需要安装 Microsoft Office 或 LibreOffice

不需要。PrinterWLAN 安装包已经包含用于 Word 转换的独立 LibreOffice 运行组件。

### 是否支持手机和平板

支持。设备只需要能够访问 Windows 主机的局域网地址，并使用现代浏览器打开 PrinterWLAN 网页。

### 是否支持 Windows 11 ARM64

支持。PrinterWLAN x64 安装包通过 Windows 11 自带的 x64 应用兼容功能运行，不需要另装模拟器。

### 能否让不同用户选择不同打印机

不能。PrinterWLAN 采用管理员统一打印机模式，所有普通用户使用管理员当前指定的打印机。

## 卸载

可以从 Windows“已安装的应用”或开始菜单运行“卸载 PrinterWLAN”。

卸载时可以选择：

- 保留 `C:\ProgramData\PrinterWLAN`，以后重新安装时继续使用现有用户和设置；
- 完全删除数据，永久移除用户、设置和日志。

如果以后仍可能重新安装，建议选择保留数据。选择完全删除后，相关数据无法通过 PrinterWLAN 恢复。

## 许可与第三方组件

PrinterWLAN 使用 [MIT License](LICENSE)。随软件分发的第三方组件及其许可信息见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
