"""Checks the int8 model files the way a CPU without VNNI (AVX2 only, e.g. Ryzen 5000) runs them.
ONNX Runtime adds pairs of uint8*int8 products in 16 bits there and caps them; full 8-bit weights overflow.
python cpu_check.py <dir with enc.q.onnx, dec.q.onnx>     Fails when the simulated output differs from this PC's."""
import sys, numpy as np, onnx, onnxruntime as ort
from onnx.reference import ReferenceEvaluator
from onnx.reference.op_run import OpRun

class MatMulInteger(OpRun):
    def _run(self, A, B, a_zp=None, b_zp=None):
        a = A.astype(np.int32); b = B.astype(np.int32)
        if b.shape[0] % 2: a = np.concatenate([a, np.zeros(a.shape[:-1] + (1,), np.int32)], -1); b = np.concatenate([b, np.zeros((1,) + b.shape[1:], np.int32)], 0)
        pair = a[..., 0::2, None] * b[0::2] + a[..., 1::2, None] * b[1::2]
        out = np.clip(pair, -32768, 32767).sum(-2)
        if a_zp is not None: out -= int(a_zp) * B.astype(np.int32).sum(0)
        return (out.astype(np.int32),)

def runner(kind, name):
    if kind == "ort":
        s = ort.InferenceSession(f"{W}/{name}.q.onnx"); return lambda feeds: s.run(None, feeds)[0]
    mdl = onnx.load(f"{W}/{name}.q.onnx")
    for o in mdl.opset_import:
        if o.domain in ("", "ai.onnx"): o.version = 21
    r = ReferenceEvaluator(mdl, new_ops=[MatMulInteger]); return lambda feeds: r.run(None, feeds)[0]

def decode(text, enc, dec):
    x = np.array([[ord(c) - 29 for c in text]], np.int64); m = enc({"x": x})
    ids = [1]; out = ""; low = 0
    for _ in range(len(text) + 6 + len(text) // 4):
        o = dec({"y": np.array([ids], np.int64), "m": m})[0]
        b = int(o.argmax())
        if b == 2: return out, low
        p = 1 / np.exp(o - o[b]).sum(); low += p < 0.9
        ids.append(b); out += chr(b + 29)
    return out + " [RUNAWAY]", low

W = sys.argv[1]
lines = ["if dont wnat to go then i jsut stay home tonight", "i decide to jsut drop everything and go to bed",
         "can yuo bring the colar and teh rope later"]
bad = 0
pc = (runner("ort", "enc"), runner("ort", "dec")); sim = (runner("sim", "enc"), runner("sim", "dec"))
for t in lines:
    a, b = decode(t, *pc)[0], decode(t, *sim)[0]
    print(("same " if a == b else "DIFF ") + f"{a}  |  {b}"); bad += a != b
sys.exit(f"{bad} line(s) differ on a CPU without VNNI" if bad else 0)
