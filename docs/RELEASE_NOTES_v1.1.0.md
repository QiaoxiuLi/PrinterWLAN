# PrinterWLAN v1.1.0

本版本将打印机选择权从普通用户端统一移交给管理员。

## 普通用户说明

- [下载 PrinterWLAN v1.1.0 普通用户使用说明 PDF](https://github.com/QiaoxiuLi/PrinterWLAN/releases/download/v1.1.0/PrinterWLAN-v1.1.0-User-Guide-zh-CN.pdf)
- 普通用户无需安装软件，也不需要选择打印机；在浏览器登录、上传文件、确认预览和打印参数后提交即可。

## 主要变化

- 管理后台新增“打印机设置”，显示 Windows 已安装打印机、状态、Windows 默认标记和 PrinterWLAN 当前打印机。
- 管理员选择持久化保存，Windows Service 或服务器重启后继续生效。
- 普通用户端完全移除打印机列表、名称和选择器，所有新任务自动使用管理员当前设置的打印机。
- 打印任务在提交时冻结实际打印机 ID 与名称；管理员之后切换打印机不会改变已排队任务。
- 未配置、已删除、改名、离线或不可用时阻止提交，不会静默回退到 Windows 默认打印机或其他打印机。
- 用户打印接口只返回当前打印机的能力，不暴露打印机身份；提交模型拒绝 `printer`、`printerId`、`printerName`、`targetPrinter` 等额外字段。
- 保留 v1.0.0 的数据库、用户、网站名称、日志和文件结构；升级后打印机选择保持为空，需管理员明确选择一次。

## 验证范围

- Windows Server 2025 Release 构建与测试
- 管理员列出、选择、切换与重启持久化
- A/B 打印机切换及旧任务绑定不变
- 用户无打印机列表、身份泄漏或字段注入
- 未配置与不可用状态阻止提交
- Playwright 桌面和移动端界面
- 自包含安装、Windows Service、PDF/Word、Fake printer、升级数据保留与静默卸载

## 硬件说明

自动化测试使用 Fake printer 验证完整任务链路。由于 GitHub Actions 没有物理打印设备，真实纸张输出仍需在目标 Windows Server 2025 和实际打印机上完成一次现场验收。
