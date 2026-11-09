"""Measures the shipped int8 model files, with the same steps as Repairer.cs and worker.js.
python final_eval.py <dir with enc.q.onnx, dec.q.onnx>
Prints totals. Per-message output goes to ../private/final_outputs.txt, which is never committed."""
import difflib, json, re, statistics as st, sys, time
import numpy as np, onnxruntime as ort
from samples import PROTECTED
from train import enc, dec, BOS, EOS, wordpct, toks

directory = sys.argv[1]
o = ort.SessionOptions(); o.intra_op_num_threads = 2; o.inter_op_num_threads = 1
E = ort.InferenceSession(f"{directory}/enc.q.onnx", o); D = ort.InferenceSession(f"{directory}/dec.q.onnx", o)
ev = json.load(open("../private/eval.json", encoding="utf8"))

def pieces(t, n=300):
    t = t.strip(); out = []
    while len(t) > n:
        k = t.rfind(" ", 0, n); k = k if k >= 20 else n
        out.append(t[:k]); t = t[k:].lstrip()
    return out + ([t] if t else [])
def decode(p):
    m = E.run(None, {"x": np.array([enc(p)], dtype=np.int64)})[0]
    y, c = [BOS], []
    for _ in range(len(p) + 6 + len(p) // 4):
        s = D.run(None, {"y": np.array([y], dtype=np.int64), "m": m})[0][0]
        b = int(s.argmax())
        if b == EOS: return dec(y), c
        c.append(float(1 / np.exp(s - s[b]).sum())); y.append(b)
    return p, [0.0] * len(p)
def repair(t):
    texts, confs = [], []
    for p in pieces(t):
        a, c = decode(p)
        if confs: confs.append(1.0)
        texts.append(a); confs += c
    return " ".join(texts), confs

results = {k: [(s, *repair(s["in"])) for s in ev[k]] for k in ev}
real_old, real_new = results["real"], results["real3"]
pct = lambda rows: round(wordpct([(s["ref"], a) for s, a, _ in rows]), 1)
raw = lambda rows: round(wordpct([(s["ref"], s["in"]) for s, _, _ in rows]), 1)
exact = lambda rows: sum(toks(s["ref"]) == toks(a) for s, a, _ in rows)
print(f"made-up 40: {pct(results['test'])}% (unrepaired {raw(results['test'])}%), exact {exact(results['test'])}/40")
print(f"real, first 5: {pct(real_old)}% (unrepaired {raw(real_old)}%), exact {exact(real_old)}/5")
print(f"real, newest 10: {pct(real_new)}% (unrepaired {raw(real_new)}%), exact {exact(real_new)}/10")
print(f"real, all 15: {pct(real_old + real_new)}% (unrepaired {raw(real_old + real_new)}%)")
kept = total = 0
for s, a, _ in results["test"]:
    for w in set(toks(s["ref"])) & PROTECTED:
        total += 1; kept += w in toks(a)
print(f"slang, swearing, adult words kept: {kept}/{total}")
print("clean text unchanged:", sum(repair(s["ref"])[0] == s["ref"] for s in ev["test"]), "/ 40")
print("junk word kept as typed:", sum(s["junk"] in a for s, a, _ in results["unclear"]), "/ 8")
short = ["ok", "yes", "im here", "wait", "thank you", "lol", "omg no", "see you", "good night", "hi"]
print("short clean messages unchanged:", sum(repair(t)[0] == t for t in short), "/", len(short), [repair(t)[0] for t in short])
for thr in (.5, .7, .8, .9, .95):
    wrong = caught = right = flagged = 0
    for s, a, c in real_old + real_new + results["test"]:
        ws = [(m.group().lower(), min(c[m.start():m.end()])) for m in re.finditer(r"\S+", a)]
        good = set()
        for b in difflib.SequenceMatcher(None, s["ref"].lower().split(), [w for w, _ in ws], autojunk=False).get_matching_blocks():
            good |= set(range(b.b, b.b + b.size))
        for i, (_, p) in enumerate(ws):
            if i in good: right += 1; flagged += p < thr
            else: wrong += 1; caught += p < thr
    print(f"mark below {thr}: catches {100*caught/wrong:.0f}% of wrong words, flags {100*flagged/right:.1f}% of right words")
T = []
for s in ev["real"] + ev["real3"]:
    t = time.perf_counter(); repair(s["in"]); T.append(time.perf_counter() - t)
print(f"2 CPU threads: median {st.median(T):.2f}s, worst {max(T):.2f}s")
with open("../private/final_outputs.txt", "w", encoding="utf8") as f:
    for s, a, _ in real_old + real_new:
        f.write(f"IN  {s['in']}\nOUT {a}\nREF {s['ref']}\n\n")
