"""公式 Real-HAT-GAN Sharper の EMA 重みを FP32 ONNX に変換する開発用スクリプト。"""

import argparse
import hashlib
from pathlib import Path

import onnx
import torch
from spandrel import ModelLoader


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("checkpoint", type=Path)
parser.add_argument("output", type=Path)
args = parser.parse_args()

expected = "5800b67136006eb8cab3b4ed7c8d73b6a195bb18e6cc709b674f9aa069c00271"
if hashlib.sha256(args.checkpoint.read_bytes()).hexdigest() != expected:
    raise ValueError("公式 Sharper の検証済み重みと SHA-256 が一致しません。")
if args.output.exists():
    raise FileExistsError("既存の変換成果物は上書きしません。")

# spandrel は params より params_ema を優先して読み込む。
descriptor = ModelLoader().load_from_file(args.checkpoint)
if descriptor.architecture.name != "HAT" or descriptor.scale != 4:
    raise ValueError("期待する HAT x4 モデルではありません。")
model = descriptor.model.eval()
torch.set_num_threads(8)
with torch.inference_mode():
    torch.onnx.export(
        model,
        torch.zeros(1, 3, 64, 64),
        args.output,
        input_names=["input"],
        output_names=["output"],
        dynamic_axes={
            "input": {2: "height", 3: "width"},
            "output": {2: "output_height", 3: "output_width"},
        },
        opset_version=17,
        dynamo=False,
        external_data=False,
    )
onnx.checker.check_model(str(args.output))
print(args.output.stat().st_size, hashlib.sha256(args.output.read_bytes()).hexdigest())
