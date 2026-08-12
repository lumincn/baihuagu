@echo off
REM ============================================================
REM Register/update Baihua OpenVINO Host Windows service
REM Right-click this file -> Run as administrator
REM All logic lives in install-openvino-host-service.ps1
REM NOTE: keep this .bat ASCII-only (cmd parses .bat as ANSI,
REM       Chinese chars in UTF-8 cause mojibake errors)
REM ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-openvino-host-service.ps1"
echo.
pause
