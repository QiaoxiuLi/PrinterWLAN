# PrinterWLAN v1.0.0

PrinterWLAN 的第一个正式版本，为 Windows Server 2025 x64 提供可离线安装、浏览器访问的局域网打印服务。

主要功能：

- self-contained .NET 10 Windows Service，默认监听 `0.0.0.0:8080`
- 手机、平板和桌面浏览器响应式用户界面
- PDF、DOC、DOCX 上传与本地 PDF.js 统一预览
- 内置独立 LibreOffice Windows x64 运行时，无需 Microsoft Office
- 从 Windows 驱动实时读取打印机、纸张、双面、彩色、纸盒、分辨率等能力
- PDFium/SkiaSharp 渲染与 Windows `PrintDocument` 系统打印管线
- 单 Worker 后台打印队列、每用户 60 秒限流、每任务最多 20 页
- CSV 用户导入、Unicode 用户名、安全 10 位随机密码、重复导入密码轮换与固定顺序
- 匿名访问、登录、上传、预览、打印及失败记录
- 10 个服务器本地自然日日志切片、ZIP 归档、历史查询、40 GB FIFO 管理、导出和受保护清空
- Inno Setup 一键安装 Windows Service、防火墙、系统 PATH、管理控制台与登录计划任务

安装：下载 `PrinterWLAN-Setup-x64.exe`，在 Windows Server 2025 x64 上以管理员身份运行；完成后在管理 CMD 执行 `printerwlan pwd "你的密码"`，再访问 `http://服务器IP:8080`。

安全边界：本版本设计给受信任 LAN/VPN，提供 HTTP 而不提供 TLS。请勿把端口直接暴露到互联网。
