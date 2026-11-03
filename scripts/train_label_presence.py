"""Train one RGB-image label-presence classifier, validate camera aggregation, export ONNX.

Run with the existing mlagents Python environment; no mlagents-learn process is used.
"""
import argparse
import csv
import json
import random
from pathlib import Path

import numpy as np
from PIL import Image
import torch
from torch import nn


class PresenceNet(nn.Module):
    def __init__(self):
        super().__init__()
        self.features = nn.Sequential(
            nn.Conv2d(3, 8, 3, stride=2, padding=1), nn.ReLU(),
            nn.Conv2d(8, 16, 3, stride=2, padding=1), nn.ReLU(),
            nn.Conv2d(16, 32, 3, stride=2, padding=1), nn.ReLU(),
            nn.Conv2d(32, 32, 3, stride=2, padding=1), nn.ReLU(),
            nn.AvgPool2d(2), nn.Flatten(),
        )
        self.classifier = nn.Sequential(nn.Linear(32 * 3 * 3, 32), nn.ReLU(), nn.Linear(32, 1))

    def forward(self, images):
        return self.classifier(self.features(images))


def load_data(root):
    if not (root / "complete.json").exists():
        raise ValueError("Dataset generation is incomplete: complete.json is missing")
    with (root / "labels.csv").open(encoding="utf-8-sig", newline="") as handle:
        rows = list(csv.DictReader(handle))
    groups = {}
    for row in rows:
        groups.setdefault(row["sample"], []).append(row)
    for sample, group in groups.items():
        if len(group) != 5 or {int(r["camera"]) for r in group} != set(range(5)):
            raise ValueError(f"Sample {sample}: exactly five different cameras required")
        if len({r["split"] for r in group}) != 1 or len({r["true_face"] for r in group}) != 1:
            raise ValueError(f"Sample {sample}: inconsistent grouping")
        for row in group:
            if int(row["has_label"]) != int(row["camera"] == row["true_face"]):
                raise ValueError(f"Sample {sample}: invalid binary target")
    data = {}
    for split in ("train", "val", "test"):
        selected = [r for r in rows if r["split"] == split]
        if not selected or not any(int(r["has_label"]) for r in selected):
            raise ValueError(f"Missing examples in {split}")
        images = []
        for row in selected:
            with Image.open(root / row["file"]) as image:
                array = np.asarray(image.convert("RGB"))
                if array.shape != (96, 96, 3):
                    raise ValueError("Expected RGB 96x96 PNGs")
                images.append(array.transpose(2, 0, 1).copy())
        data[split] = (selected, torch.from_numpy(np.stack(images)),
                       torch.tensor([float(r["has_label"]) for r in selected]).reshape(-1, 1))
    return data


@torch.inference_mode()
def predict(model, images):
    model.eval()
    return torch.cat([model(batch.float() / 255).sigmoid().flatten()
                      for batch in images.split(128)]).numpy()


def metrics(rows, probabilities, threshold):
    targets = np.array([int(row["has_label"]) for row in rows])
    binary = probabilities >= threshold
    tp = int(((targets == 1) & binary).sum())
    fp = int(((targets == 0) & binary).sum())
    fn = int(((targets == 1) & ~binary).sum())
    tn = int(((targets == 0) & ~binary).sum())
    groups = {}
    for row, score in zip(rows, probabilities):
        groups.setdefault(row["sample"], []).append((row, float(score)))
    confusion = np.zeros((6, 6), dtype=int)
    for group in groups.values():
        best_row, best_score = max(group, key=lambda value: value[1])
        predicted = int(best_row["camera"]) if best_score >= threshold else 5
        confusion[int(best_row["true_face"]), predicted] += 1
    return dict(threshold=float(threshold), image_accuracy=float((binary == targets).mean()),
                precision=tp / max(1, tp + fp), recall=tp / max(1, tp + fn),
                tp=tp, fp=fp, fn=fn, tn=tn,
                samples=len(groups), face_accuracy=float(np.trace(confusion) / len(groups)),
                face_total=confusion.sum(axis=1).tolist(), face_correct=np.diag(confusion).tolist(),
                confusion_matrix=confusion.tolist())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", type=Path)
    parser.add_argument("--output", type=Path, default=Path("results/label_presence"))
    parser.add_argument("--epochs", type=int, default=25)
    parser.add_argument("--seed", type=int, default=20261103)
    args = parser.parse_args()
    if args.dataset is None:
        candidates = sorted(p.parent for p in Path("LabelPresenceDataset").glob("*/complete.json"))
        if not candidates:
            parser.error("Generate the dataset in Unity first, or specify --dataset")
        args.dataset = candidates[-1]
    args.output.mkdir(parents=True, exist_ok=True)
    random.seed(args.seed)
    np.random.seed(args.seed)
    torch.manual_seed(args.seed)
    torch.set_num_threads(4)
    torch.use_deterministic_algorithms(True)
    print("Loading", args.dataset, flush=True)
    data = load_data(args.dataset)
    train_rows, train_images, train_targets = data["train"]
    model = PresenceNet()
    optimizer = torch.optim.Adam(model.parameters(), lr=0.001)
    positive = train_targets.sum()
    criterion = nn.BCEWithLogitsLoss(pos_weight=(train_targets.numel() - positive) / positive)
    best_score, best_loss, best_threshold, patience = -1.0, float("inf"), 0.5, 0
    history = []
    for epoch in range(1, args.epochs + 1):
        model.train()
        total_loss = 0
        for indexes in torch.randperm(len(train_images)).split(128):
            optimizer.zero_grad(set_to_none=True)
            loss = criterion(model(train_images[indexes].float() / 255), train_targets[indexes])
            loss.backward()
            optimizer.step()
            total_loss += float(loss) * len(indexes)
        val_rows, val_images, val_targets = data["val"]
        probabilities = predict(model, val_images)
        candidates = [metrics(val_rows, probabilities, t) for t in np.linspace(0.05, 0.95, 37)]
        validation = max(candidates, key=lambda m: (m["face_accuracy"], m["image_accuracy"], -abs(m["threshold"] - 0.5)))
        val_loss = float(nn.functional.binary_cross_entropy(torch.tensor(probabilities).reshape(-1, 1).clamp(1e-6, 1-1e-6), val_targets))
        record = dict(epoch=epoch, loss=total_loss / len(train_images), val_loss=val_loss, **validation)
        history.append(record)
        print(f"epoch {epoch:02d} loss={record['loss']:.4f} val_face={validation['face_accuracy']:.3%} "
              f"precision={validation['precision']:.3%} recall={validation['recall']:.3%} threshold={validation['threshold']:.3f}", flush=True)
        if validation["face_accuracy"] > best_score or (validation["face_accuracy"] == best_score and val_loss < best_loss):
            best_score, best_loss, best_threshold = validation["face_accuracy"], val_loss, validation["threshold"]
            torch.save(model.state_dict(), args.output / "best.pt")
            patience = 0
        else:
            patience += 1
        if patience >= 7:
            break
    model.load_state_dict(torch.load(args.output / "best.pt", map_location="cpu", weights_only=True))
    model.eval()
    report = dict(dataset=str(args.dataset.resolve()), seed=args.seed, history=history)
    for split in ("val", "test"):
        rows, images, _ = data[split]
        scores = predict(model, images)
        report[split] = metrics(rows, scores, best_threshold)
        if split == "test":
            with (args.output / "test_predictions.csv").open("w", newline="", encoding="utf-8") as handle:
                writer = csv.writer(handle)
                writer.writerow(["file", "sample", "camera", "has_label", "true_face", "score"])
                for row, score in zip(rows, scores):
                    writer.writerow([row[k] for k in ("file", "sample", "camera", "has_label", "true_face")] + [float(score)])
    deployed = nn.Sequential(model, nn.Sigmoid()).eval()
    torch.onnx.export(deployed, torch.zeros(1, 3, 96, 96), str(args.output / "label_presence.onnx"),
                      input_names=["image"], output_names=["label_score"], opset_version=13)
    import onnx
    onnx.checker.check_model(onnx.load(str(args.output / "label_presence.onnx")))
    deployment = dict(threshold=best_threshold, width=96, height=96, layout="NCHW", channels="RGB",
                      range=[0, 1], origin="top_left", output="sigmoid_label_score",
                      cameras=["Top", "Front", "Back", "Left", "Right"], no_detection="Bottom")
    (args.output / "deployment.json").write_text(json.dumps(deployment, indent=2), encoding="utf-8")
    (args.output / "metrics.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print("Held-out test:", json.dumps(report["test"]), flush=True)
    print("ONNX:", (args.output / "label_presence.onnx").resolve(), flush=True)


if __name__ == "__main__":
    main()
