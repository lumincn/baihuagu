"""
OpenVINO IR INT4 量化脚本（NNCF）。

用法：
    python quantize_ov_int4.py --src <含 openvino_model.bin/xml 的目录>

将 openvino_model.bin 量化为 INT4，大幅减小体积（~14GB → ~4.5GB），
适配 Intel Arc 核显 GPU。
"""

import argparse
import os
import sys
import time
from pathlib import Path


def log(msg: str):
    ts = time.strftime("%H:%M:%S")
    print(f"[{ts}] {msg}", flush=True)


def main():
    parser = argparse.ArgumentParser(description="OpenVINO IR INT4 量化")
    parser.add_argument("--src", required=True, help="含 openvino_model.bin/xml 的目录")
    args = parser.parse_args()

    src = Path(args.src).resolve()
    ov_xml = src / "openvino_model.xml"
    ov_bin = src / "openvino_model.bin"

    if not ov_bin.exists():
        log(f"错误：{ov_bin} 不存在")
        sys.exit(1)

    size_before = ov_bin.stat().st_size / 1024 / 1024
    log(f"源模型: {ov_bin} ({size_before:.0f}MB)")

    # 备份原始 FP 模型
    backup_bin = src / "openvino_model_fp.bin"
    backup_xml = src / "openvino_model_fp.xml"
    if not backup_bin.exists():
        import shutil
        log("备份原始 FP 模型...")
        shutil.copy2(ov_bin, backup_bin)
        shutil.copy2(ov_xml, backup_xml)

    log("加载 OpenVINO IR...")
    import openvino as ov
    core = ov.Core()
    model = core.read_model(str(ov_xml), str(ov_bin))
    log(f"模型已加载，参数量: {len(model.get_parameters())}")

    log("开始 INT4 权重量化（NNCF compress_weights，无需校准数据）...")
    t0 = time.time()

    import nncf

    try:
        quantized = nncf.compress_weights(model, mode=nncf.CompressWeightsMode.INT4_SYM)
    except Exception as e:
        log(f"INT4_SYM 失败 ({e})，尝试 INT4_ASYM...")
        quantized = nncf.compress_weights(model, mode=nncf.CompressWeightsMode.INT4_ASYM)

    elapsed = time.time() - t0
    log(f"量化完成，耗时 {elapsed:.0f}s")

    # 保存量化后的模型（先写到临时文件，避免文件锁冲突）
    log("保存量化模型...")
    tmp_xml = str(src / "openvino_model_q.xml")
    tmp_bin = str(src / "openvino_model_q.bin")
    ov.serialize(quantized, tmp_xml, tmp_bin)

    # 释放模型对象
    del quantized
    del model

    # 替换原文件
    import os
    os.replace(tmp_bin, str(ov_bin))
    os.replace(tmp_xml, str(ov_xml))

    size_after = ov_bin.stat().st_size / 1024 / 1024
    log(f"量化后: {size_after:.0f}MB (压缩比 {size_before/size_after:.1f}x)")

    # 清理备份
    if backup_bin.exists():
        backup_bin.unlink()
        backup_xml.unlink()
        log("已清理 FP 备份")

    log(f"完成！{size_before:.0f}MB → {size_after:.0f}MB")
    sys.exit(0)


if __name__ == "__main__":
    main()