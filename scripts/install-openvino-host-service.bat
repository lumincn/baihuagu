@echo off
REM ============================================================
REM 注册 Baihua OpenVINO Host Windows 服务
REM 右键本文件 -> 以管理员身份运行
REM 实际逻辑在 install-openvino-host-service.ps1
REM ============================================================
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-openvino-host-service.ps1"
echo.
pause
