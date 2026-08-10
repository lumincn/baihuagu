# -*- coding: utf-8 -*-
import io

p = 'services/Baihua.AI.Provider/OnnxRuntimeGenAIInference.cs'
s = io.open(p, encoding='utf-8').read()

old = '''            _logger.LogInformation("正在加载 ONNX 模型: {Path}", path);
            // 执行器选择（按优先级尝试，全部失败回退 CPU）：
            //   LOCAL_AI_OPENVINO=1  → OpenVINO EP（Linux/WSL2 GPU 正路，走 OpenCL→/dev/dxg）
            //   LOCAL_AI_DML=1       → DirectML EP（Windows 原生 GPU；Phi-3 int4 算子不兼容会崩，需验证）
            //   默认                  → CPU
            Model model;
            var openvino = Environment.GetEnvironmentVariable("LOCAL_AI_OPENVINO") == "1";
            var dml = Environment.GetEnvironmentVariable("LOCAL_AI_DML") == "1";

            if (openvino)
            {
                try
                {
                    var config = new Config(path);
                    config.ClearProviders();
                    // 优先 GPU，失败回退 CPU（OpenVINO EP 的设备名）
                    config.AppendProvider("OpenVINO");
                    model = new Model(config);
                    _logger.LogInformation("ONNX using OpenVINO EP (GPU if available)");
                }
                catch (Exception ovEx)
                {
                    _logger.LogWarning(ovEx, "OpenVINO EP init failed, fallback to CPU");
                    model = new Model(path);
                }
            }
            else if (dml)
            {'''
new = '''            _logger.LogInformation("正在加载 ONNX 模型: {Path}", path);
            // 执行器选择（自动检测，按优先级尝试，全部失败回退 CPU）：
            //   1. OpenVINO EP（Linux/WSL2 GPU 正路，走 OpenCL→/dev/dxg）——自动尝试，有 GPU 就用
            //   2. DirectML EP（Windows 原生 GPU）——LOCAL_AI_DML=1 强制
            //   3. CPU（默认回退）
            //   LOCAL_AI_OPENVINO=0 可显式禁用 OpenVINO EP（强制 CPU）
            Model model;
            var openvinoDisabled = Environment.GetEnvironmentVariable("LOCAL_AI_OPENVINO") == "0";
            var dml = Environment.GetEnvironmentVariable("LOCAL_AI_DML") == "1";

            if (!openvinoDisabled)
            {
                try
                {
                    var config = new Config(path);
                    config.ClearProviders();
                    // OpenVINO EP 自动选择设备：有 GPU 用 GPU，无则 CPU
                    config.AppendProvider("OpenVINO");
                    model = new Model(config);
                    _logger.LogInformation("ONNX using OpenVINO EP (auto device)");
                }
                catch (Exception ovEx)
                {
                    _logger.LogDebug(ovEx, "OpenVINO EP not available, try next");
                    model = null;
                }
            }

            if (model == null && dml)
            {'''
assert old in s, 'pattern NOT FOUND'
s = s.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('updated')
