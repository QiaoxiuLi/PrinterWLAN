# PrinterWLAN v1.0.0 Acceptance Checklist

只有 Windows Server 2025 Release workflow 实际通过后，发布提交才会把所有硬性项目标记为完成。本清单中的实现项均有源码或自动测试路径；真实纸张输出单独注明，不用 Fake 结果冒充物理打印。

## 平台、安装与运行

- [x] Windows Server 2025 workflow 通过
- [x] `net10.0-windows`、`win-x64` self-contained publish
- [x] 真正的 `PrinterWLAN` Windows Service，Automatic、LocalSystem
- [x] Windows 启动时无需管理员登录即可启动网站
- [x] 登录计划任务与开始菜单 `PrinterWLAN 管理控制台`
- [x] `printerwlan pwd` 即时生效且只保存 hash
- [x] `printerwlan status`
- [x] HTTP only，无 HTTPS redirect、证书或 CDN
- [x] `0.0.0.0:8080` 与安装器防火墙规则
- [x] `/health` 只返回简单状态
- [x] Inno Setup 自包含 `PrinterWLAN-Setup-x64.exe`
- [x] 卸载时默认建议保留 ProgramData，并允许完全删除

## 身份、用户与设置

- [x] 首页先显示用户登录
- [x] 管理员无用户名登录，入口固定在右下角
- [x] User/Admin Cookie Session、HttpOnly、HTTP 可用、退出失效
- [x] CSRF、防暴力尝试、权限策略、友好错误
- [x] 管理员密码未设置提示
- [x] CSV UTF-8/BOM/中文/引号/可选表头与 CsvHelper parser
- [x] Unicode NFC、trim、case-insensitive 查重
- [x] 密码使用 CSPRNG 且恰好 10 位
- [x] password hash + Windows DPAPI LocalMachine 可导出密文
- [x] 重复用户名轮换密码、旧密码失效、sequence 不变
- [x] 导入事务与 `ORDER BY sequence ASC`
- [x] 用户列表不预传明文，按需显示/复制
- [x] UTF-8 BOM 凭据 CSV 严格按 sequence 导出
- [x] 网站名称修改同步到用户可见页面和标题

## 文档与打印

- [x] PDF、DOC、DOCX 类型与文件签名服务端验证
- [x] 100 MB 可配置上传限制、UUID 工作目录
- [x] PDF.js 6.3.289 本地静态资源，无 CDN
- [x] LibreOffice 26.8.0 Word→PDF，唯一 profile/目录、超时、kill tree、输出验证
- [x] PDF/Word 统一 PDF 预览与受保护 no-store URL
- [x] 文件名/扩展名/检测 MIME/大小/四类时间/页数元数据
- [x] 成功、失败、取消和约 60 分钟过期清理；启动/每小时清理残留
- [x] 文件正文不进入数据库、日志、导出或缓存
- [x] Windows 实时打印机列表与默认打印机
- [x] 动态纸张、方向、单双面、页码、份数、逐份、颜色、纸盒、分辨率、缩放、居中
- [x] 提交与实际打印前再次验证驱动能力
- [x] page range 去重、排序、非法值与越界检查
- [x] 所选页数 × 份数 ≤ 20 的服务端硬限制
- [x] authenticated user ID 的持久化 60 秒限流与并发锁
- [x] `System.Threading.Channels` 单 Worker 队列
- [x] PDFtoImage/PDFium/SkiaSharp → `PrintDocument` → Windows driver
- [x] queued/processing/sent/failed/interrupted，sent 文案不冒充物理完成
- [ ] 使用真实物理打印机完成纸张输出（CI 无硬件，发布说明明确标注）

## 使用记录与可靠性

- [x] 匿名访问、设备 UUID、Session、IP、UA、语言、平台、viewport、screen、timezone、referrer
- [x] 登录成功/失败、访问、上传、预览、提交、打印结果、登出
- [x] 完整文件/打印设置/时间/结果，禁止密码/Cookie/正文/hash
- [x] `app.db` 与日志 SQLite/Diagnostics 分离
- [x] WAL、busy_timeout、foreign_keys 与关键事务
- [x] 服务器本地日期固定 10 天切片与跨月/跨年测试
- [x] checkpoint、VACUUM、integrity check、ZIP 校验后删除原 SQLite
- [x] 历史 ZIP 临时解压只读查询与 TTL Cache
- [x] 每小时和重启后的 rollover/归档维护
- [x] 40 GB 上限、约 38 GB 目标、只删除最旧已归档切片
- [x] 分页筛选统一记录与用户记录入口
- [x] 按切片导出 BOM CSV、manifest 和临时清理
- [x] 清空日志重新验证管理员密码且保留业务数据
- [x] Serilog Diagnostics 独立滚动保留 14 天

## UI、测试与发布

- [x] 320/360/390/768/1366/1920 Playwright viewport 覆盖
- [x] 手机预览在上、设置在下；桌面 60/40；移动管理顶部导航
- [x] nowrap/min-width/320px 防止短标签单字换行
- [x] 登录、CSV、顺序、密码显示、导出、预览、20 页和日志 UI 测试
- [x] Unit、Integration、Fake printer 与 installer smoke test 工程
- [x] 固定依赖、官方 URL、SHA-256 和 machine-readable manifest
- [x] 第三方 Notice/License 打包
- [x] README、Release Notes、MIT License
- [x] `windows-2025` CI 与 `v*` Release workflow、`contents: write`、官方 `gh`
- [ ] `v1.0.0` tag workflow 全部通过
- [ ] 正式非 Draft、非 Prerelease GitHub Release 已发布
