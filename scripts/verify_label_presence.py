"""Compare saved Unity inference scores with PyTorch and the exported ONNX."""
import json
from pathlib import Path

import numpy as np
from PIL import Image
import torch
from onnx.reference import ReferenceEvaluator
from train_label_presence import PresenceNet


def main():
    folders = sorted(Path("results/label_presence_pair_check").glob("*/report.json"))
    if not folders:
        raise RuntimeError("Run the Unity presence evaluation first")
    report_path = folders[-1]
    report = json.loads(report_path.read_text(encoding="utf-8"))
    model = PresenceNet().eval()
    model.load_state_dict(torch.load("results/label_presence/best.pt", map_location="cpu", weights_only=True))
    torch.set_num_threads(4)
    onnx = ReferenceEvaluator("results/label_presence/label_presence.onnx")
    unity_error, onnx_error, count = 0.0, 0.0, 0
    for index, pair in enumerate(report["pairs"][:5], start=1):
        for state in ("original", "removed"):
            for camera in range(5):
                with Image.open(report_path.parent / f"{index:02d}_{state}_{camera}.png") as image:
                    data = np.asarray(image.convert("RGB"), dtype=np.float32).transpose(2, 0, 1)[None] / 255
                with torch.inference_mode():
                    expected = float(model(torch.from_numpy(data)).sigmoid().item())
                actual = float(onnx.run(None, {"image": data})[0].item())
                unity_error = max(unity_error, abs(expected - pair[state + "Scores"][camera]))
                onnx_error = max(onnx_error, abs(expected - actual))
                count += 1
    result = dict(images=count, max_unity_pytorch_error=unity_error, max_onnx_pytorch_error=onnx_error,
                  paired_tested=report["tested"], paired_passed=report["passed"])
    print(json.dumps(result, indent=2))
    assert count == 50 and unity_error < 1e-4 and onnx_error < 1e-4, "Inference parity failed"
    assert report["tested"] == 30 and report["passed"] == 30, "Label-removal check failed"
    (report_path.parent / "parity.json").write_text(json.dumps(result, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
