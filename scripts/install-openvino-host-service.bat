@echo off
REM ============================================================
REM 注册百花 OpenVINO Host Windows 服务（需管理员权限运行）
REM 右键本文件 -> 以管理员身份运行
REM ============================================================
setlocal

set PY=C:\Users\lumin\AppData\Local\Programs\Python\Python312\python.exe
set SCRIPT=C:\Users\lumin\src\baihuagu\services\Baihua.AI.Provider\LocalVision\openvino_host.py
set SERVICE=BaihuaOpenVinoHost

echo [1/3] 创建服务 %SERVICE% ...
sc.exe create %SERVICE% binPath= "\"%PY%\" \"%SCRIPT%\" --port 8866 --bind 127.0.0.1" start= auto DisplayName= "Baihua OpenVINO Host"
if errorlevel 1 (
    echo 创建失败（可能已存在，尝试更新配置）...
    sc.exe config %SERVICE% binPath= "\"%PY%\" \"%SCRIPT%\" --port 8866 --bind 127.0.0.1" start= auto
)

echo [2/3] 设置服务描述 ...
sc.exe description %SERVICE% "百花 OpenVINO LLM/Embedding 托管服务（管理 openvino_llm_server.py 实例：8000 对话/8001 代码/8002 嵌入）"

echo [3/3] 启动服务 ...
sc.exe start %SERVICE%
if errorlevel 1 (
    echo 启动失败，请检查：sc.exe query %SERVICE%
) else (
    echo 服务已启动！
)

echo.
echo 验证：curl http://127.0.0.1:8866/status
endlocal
pause
