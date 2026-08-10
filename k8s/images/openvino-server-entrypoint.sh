#!/bin/bash
# OpenVINO 推理服务器启动脚本
# 环境变量:
#   OPENVINO_MODEL_PATH   - LLM 模型目录 (必填)
#   OPENVINO_DEVICE       - 推理设备 GPU/CPU/NPU/AUTO (默认 GPU)
#   OPENVINO_LLM_PORT     - LLM 服务端口 (默认 8000)
#   OPENVINO_VISION_PORT  - 视觉服务端口 (默认 8801)
#   OPENVINO_MAX_CONTEXT  - 最大上下文长度 (默认 4096)
#   OPENVINO_EXTRA_ARGS   - 额外启动参数
#   START_VISION          - 是否启动视觉服务 (默认 true)
#   VISION_MODEL_3B       - 3B 视觉模型目录
#   VISION_MODEL_7B       - 7B 视觉模型目录
set -euo pipefail

MODEL_PATH="${OPENVINO_MODEL_PATH:-/models/Qwen2.5-VL-7B-Instruct-int4-ov}"
DEVICE="${OPENVINO_DEVICE:-GPU}"
LLM_PORT="${OPENVINO_LLM_PORT:-8000}"
VISION_PORT="${OPENVINO_VISION_PORT:-8801}"
MAX_CONTEXT="${OPENVINO_MAX_CONTEXT:-4096}"
EXTRA_ARGS="${OPENVINO_EXTRA_ARGS:-}"
START_VISION="${START_VISION:-true}"

echo "============================================"
echo " Baihua OpenVINO Inference Server"
echo "============================================"
echo " Model:  $MODEL_PATH"
echo " Device: $DEVICE"
echo " LLM Port:    $LLM_PORT"
echo " Vision Port: $VISION_PORT"
echo "============================================"

if [ ! -d "$MODEL_PATH" ]; then
    echo "[ERROR] Model directory not found: $MODEL_PATH"
    echo "        Please download the model first."
    echo "        Expected path: /models/<model-name>/"
    echo "        Available models in /models:"
    ls -1 /models/ 2>/dev/null || echo "        (empty or not mounted)"
    exit 1
fi

# Start LLM server (OpenAI-compatible)
echo "[INFO] Starting LLM server on port $LLM_PORT ..."
python3 /app/openvino_llm_server.py \
    --model "$MODEL_PATH" \
    --device "$DEVICE" \
    --port "$LLM_PORT" \
    --max-context-size "$MAX_CONTEXT" \
    $EXTRA_ARGS &
LLM_PID=$!
echo "[INFO] LLM server PID: $LLM_PID"

# Start Vision server (optional)
if [ "$START_VISION" = "true" ]; then
    VISION_MODEL_3B="${VISION_MODEL_3B:-/models/Qwen2.5-VL-3B-Instruct-int4-ov}"
    VISION_MODEL_7B="${VISION_MODEL_7B:-/models/Qwen2.5-VL-7B-Instruct-int4-ov}"

    echo "[INFO] Starting Vision server on port $VISION_PORT ..."
    VISION_PORT="$VISION_PORT" \
    VISION_MODEL_3B="$VISION_MODEL_3B" \
    VISION_MODEL_7B="$VISION_MODEL_7B" \
    VISION_DEVICE="$DEVICE" \
    python3 /app/vision_server.py &
    VISION_PID=$!
    echo "[INFO] Vision server PID: $VISION_PID"
else
    echo "[INFO] Vision server disabled (START_VISION=false)"
    VISION_PID=""
fi

# Wait for any process to exit
wait -n
EXIT_CODE=$?

echo "[WARN] A server process exited (code: $EXIT_CODE), shutting down..."
kill $LLM_PID 2>/dev/null || true
[ -n "$VISION_PID" ] && kill $VISION_PID 2>/dev/null || true
exit $EXIT_CODE
