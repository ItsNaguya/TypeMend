"""Turns a training result into the ONNX files the plugin and web page use, and checks they match.
python export.py <checkpoint or final.pt> <output dir> [<output dir> ...]    Runs on the CPU only."""
import os, shutil, sys
import numpy as np, onnxruntime as ort, torch, torch.nn as nn
from onnxruntime.quantization import QuantType, quantize_dynamic
from train import BOS
from train2 import Net2

class Encoder(nn.Module):
    def __init__(s, n): super().__init__(); s.n = n
    def forward(s, x): return s.n.memory(x)
class Decoder(nn.Module):
    def __init__(s, n): super().__init__(); s.n = n
    def forward(s, y, m): return s.n.step(y, m)[:, -1]

def main():
    source, outputs = sys.argv[1], sys.argv[2:]
    torch.set_num_threads(2)
    state = torch.load(source, map_location="cpu", weights_only=False)
    net = Net2(); net.load_state_dict(state["net"] if "net" in state else state); net.eval()
    e, d = Encoder(net).eval(), Decoder(net).eval()
    work = "data/export"; os.makedirs(work, exist_ok=True)
    x = torch.randint(3, 90, (1, 11)); y = torch.cat([torch.tensor([[BOS]]), torch.randint(3, 90, (1, 2))], 1)
    with torch.no_grad(): m = e(x)
    kw = dict(opset_version=17, dynamo=False)
    torch.onnx.export(e, (x,), f"{work}/enc.onnx", input_names=["x"], output_names=["m"], dynamic_axes={"x": {1: "n"}, "m": {1: "n"}}, **kw)
    torch.onnx.export(d, (y, m), f"{work}/dec.onnx", input_names=["y", "m"], output_names=["o"], dynamic_axes={"y": {1: "k"}, "m": {1: "n"}}, **kw)
    for name in ("enc", "dec"):
        quantize_dynamic(f"{work}/{name}.onnx", f"{work}/{name}.q.onnx", weight_type=QuantType.QInt8)
    net.eval()

    options = ort.SessionOptions(); options.intra_op_num_threads = 2
    es, ds = (ort.InferenceSession(f"{work}/{n}.onnx", options) for n in ("enc", "dec"))
    worst = 0.0
    for n, k in ((5, 2), (37, 9), (200, 50), (300, 340)):
        xx = torch.randint(3, 90, (1, n)); yy = torch.randint(3, 90, (1, k))
        with torch.no_grad(): mm = net.memory(xx); t = net.step(yy, mm)[:, -1].numpy()
        worst = max(worst, float(np.abs(es.run(None, {"x": xx.numpy()})[0] - mm.numpy()).max()),
                    float(np.abs(ds.run(None, {"y": yy.numpy(), "m": mm.numpy()})[0] - t).max()))
    print("largest difference, exported vs original:", worst)
    if worst > 1e-3: sys.exit("export does not match the original model")
    for out in outputs:
        os.makedirs(out, exist_ok=True)
        for f in ("enc.q.onnx", "dec.q.onnx"):
            shutil.copyfile(f"{work}/{f}", f"{out}/{f}")
    print("int8 MB", round(sum(os.path.getsize(f"{work}/{n}.q.onnx") for n in ("enc", "dec")) / 1e6, 1),
          "float MB", round(sum(os.path.getsize(f"{work}/{n}.onnx") for n in ("enc", "dec")) / 1e6, 1))

if __name__ == "__main__":
    main()
