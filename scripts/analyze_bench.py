"""Analyze benchmark JSONs and print a Windows vs WSL2 comparison report."""
from __future__ import annotations

import json
import statistics
from pathlib import Path


def load(p: str):
    return json.loads(Path(p).read_text(encoding="utf-8"))


WIN_7B = Path(r"C:\Users\lumin\src\baihuagu\out-win-7b-full.json")
WSL_7B = Path(r"C:\Users\lumin\src\baihuagu\out-wsl-7b-full.json")
WIN_3B = Path(r"C:\Users\lumin\src\baihuagu\out-smoke2-win.json")
WSL_3B = Path(r"C:\Users\lumin\src\baihuagu\out-smoke-wsl.json")


def collect(run: dict) -> dict:
    # Filter meaningful outputs (>=50 tokens) — skip the 1-token trivial prompt.
    ok = [p for p in run["prompts"] if p.get("output_tokens", 0) >= 50]
    return {
        "load_ms": run["load_time_ms"],
        "ttft_avg_ms": statistics.mean(p["ttft_ms"] for p in ok) if ok else 0.0,
        "ttft_med_ms": statistics.median(p["ttft_ms"] for p in ok) if ok else 0.0,
        "decode_tps_avg": statistics.mean(
            (p["output_tokens"] - 1) / max((p["total_time_ms"] - p["ttft_ms"]) / 1000.0, 0.001)
            for p in ok
        ) if ok else 0.0,
        "e2e_tps_avg": statistics.mean(
            p["output_tokens"] / max(p["total_time_ms"] / 1000.0, 0.001)
            for p in ok
        ) if ok else 0.0,
        "n": len(ok),
        "error": run.get("error"),
    }


def index(runs: list[dict]) -> dict[str, dict[str, dict]]:
    d: dict[str, dict[str, dict]] = {}
    for r in runs:
        d.setdefault(r["model"], {})[r["device"]] = collect(r)
    return d


win7 = index(load(WIN_7B)["runs"])
wsl7 = index(load(WSL_7B)["runs"])

MODEL = "Qwen2.5-VL-7B-Instruct-int4-ov"

SEP = "=" * 120
print(SEP)
print("Qwen2.5-VL-7B-Instruct-int4-ov  —  Windows vs WSL2  性能对比 (排除1-token短输出)")
print(SEP)
hdr = f"| {'设备 (平台)':<21} | {'Load(s)':>7} | {'TTFT(ms)':>9} | {'Decode(t/s)':>12} | {'E2E(t/s)':>10} | {'样本':>4} | 备注 |"
print(hdr)
print("|" + "-" * 23 + "|" + "-" * 9 + "|" + "-" * 11 + "|" + "-" * 14 + "|" + "-" * 12 + "|" + "-" * 6 + "|" + "-" * 36 + "|")

w_cpu = win7.get(MODEL, {}).get("CPU")
w_gpu = win7.get(MODEL, {}).get("GPU")
w_npu = win7.get(MODEL, {}).get("NPU")
s_cpu = wsl7.get(MODEL, {}).get("CPU")
s_gpu = wsl7.get(MODEL, {}).get("GPU")


def row(label: str, r: dict, note: str = "") -> str:
    if r["error"]:
        note = "FAILED: " + r["error"][:60]
    return (
        f"| {label:<21} | {r['load_ms']/1000:>7.1f} | {r['ttft_avg_ms']:>9.0f} | "
        f"{r['decode_tps_avg']:>12.2f} | {r['e2e_tps_avg']:>10.2f} | {r['n']:>4} | {note:<36} |"
    )


if w_cpu and s_cpu:
    speed = w_cpu["decode_tps_avg"] / max(s_cpu["decode_tps_avg"], 0.001)
    ttft = 100 * (1 - s_cpu["ttft_avg_ms"] / max(w_cpu["ttft_avg_ms"], 1))
    note1 = f"Decode 比 WSL2 快 {speed:.1f}×；TTFT 快 {ttft:.0f}%"
else:
    note1 = ""
print(row("CPU (Windows 原生)", w_cpu or {}, note1))
if s_cpu:
    print(row("CPU (WSL2 Arch)", s_cpu, "受 /mnt/c 跨文件系统+Linux版调度影响"))

if w_gpu and s_gpu:
    load_x = s_gpu["load_ms"] / max(w_gpu["load_ms"], 1)
    ttft_ratio = s_gpu["ttft_avg_ms"] / max(w_gpu["ttft_avg_ms"], 1)
    decode_gap = w_gpu["decode_tps_avg"] - s_gpu["decode_tps_avg"]
    note2 = f"Win 加载快 {load_x:.1f}×；TTFT 比值 {ttft_ratio:.2f}；吞吐差 {decode_gap:+.1f}"
else:
    note2 = ""
print(row("GPU Arc 130T (Win)", w_gpu or {}, note2))
if s_gpu:
    print(row("GPU Arc 130T (WSL2)", s_gpu, "DXG 直通可用；内核编译比 Win 慢"))

if w_npu:
    note_npu = ""
    if w_npu["error"]:
        note_npu = "NPU 不支持 7B VL 算子/内存"
    print(row("NPU AI Boost (Win)", w_npu, note_npu))
print("| NPU (WSL2)            |      N/A |       N/A |          N/A |        N/A |  N/A | WSL2 不支持 NPU 直通              |")

# 3B smoke test supplement
print()
print("=" * 90)
print("Qwen2.5-VL-3B-Instruct-int4-ov  —  CPU 补充对比 (smoke test，单轮)")
print("=" * 90)
try:
    win3 = index(load(WIN_3B)["runs"]).get("Qwen2.5-VL-3B-Instruct-int4-ov", {}).get("CPU")
    wsl3 = index(load(WSL_3B)["runs"]).get("Qwen2.5-VL-3B-Instruct-int4-ov", {}).get("CPU")
    if win3:
        print(f"  3B CPU (Windows) :  Load {win3['load_ms']/1000:.1f}s  TTFT {win3['ttft_avg_ms']:.0f}ms  "
              f"Decode {win3['decode_tps_avg']:.1f} t/s  E2E {win3['e2e_tps_avg']:.1f} t/s  (n={win3['n']})")
    if wsl3:
        print(f"  3B CPU (WSL2)    :  Load {wsl3['load_ms']/1000:.1f}s  TTFT {wsl3['ttft_avg_ms']:.0f}ms  "
              f"Decode {wsl3['decode_tps_avg']:.1f} t/s  E2E {wsl3['e2e_tps_avg']:.1f} t/s  (n={wsl3['n']})")
    if win3 and wsl3:
        sp = win3['decode_tps_avg'] / max(wsl3['decode_tps_avg'], 0.001)
        tf = 100 * (1 - wsl3['ttft_avg_ms'] / max(win3['ttft_avg_ms'], 1))
        print(f"  -> Windows 领先 :  Decode {sp:.1f}×   TTFT 快 {tf:.0f}%")
except Exception as e:
    print(f"  (3B data unavailable: {e})")

# Platform tags
print()
print("平台与环境：")
p_win = load(WIN_7B)["platform"]
p_wsl = load(WSL_7B)["platform"]
print(f"  Windows  : Python {p_win['python']}, OpenVINO {p_win['openvino'].split('-')[0]}, "
      f"devices={load(WIN_7B)['devices_available']}")
print(f"  WSL2 Arch: Python {p_wsl['python']}, OpenVINO {p_wsl['openvino'].split('-')[0]}, "
      f"devices={load(WSL_7B)['devices_available']}  dxg={p_wsl.get('wsl_dxg')}")
print("  硬件    : Intel Core Ultra 5 225H (Lunar Lake) / GPU=Arc 130T 16GB / NPU=Intel AI Boost")
print(f"  脚本    : scripts/openvino_benchmark.py  repeats=2  prompts=3 (长短混合)")
