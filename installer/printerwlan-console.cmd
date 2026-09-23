@echo off
chcp 65001 >nul
set "PATH=%~dp0;%PATH%"
title PrinterWLAN
echo.
echo PrinterWLAN 已启动
echo.
echo 管理员密码设置：
echo   printerwlan pwd "你的密码"
echo.
"%~dp0PrinterWLAN.exe" status
echo.
echo 输入 printerwlan status 可随时查看状态。
echo 输入 printerwlan doctor 可检查 Windows 和全部内置组件。
echo 输入 printerwlan pwd "你的密码" 可修改管理员密码。
echo.
echo 此窗口会保持打开；完成管理操作后可直接关闭窗口。
