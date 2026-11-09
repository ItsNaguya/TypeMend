"""Experiment: second look at doubtful words, using a word list and the letters actually typed.
python lexfix.py            builds the word list if missing, tunes on the dev sets, then scores the newest real messages
Prints totals only. Per-message output goes to ../private/lexfix_outputs.txt, which is never committed.

Tuning uses: 40 made-up sentences, the first 5 real messages, 120 freshly garbled corpus sentences.
The newest 10 real messages are only scored, once, with the settings the tuning chose."""
import collections, json, os, random, re, sys, time
import numpy as np, onnxruntime as ort
from samples import PROTECTED
from train import enc, dec, BOS, EOS, wordpct, toks
from train2 import SLANG, SWEAR, GAME, casual, garble

MODEL = "../model"
LEX = "data/lexicon.txt"
MIN_COUNT = 12
o = ort.SessionOptions(); o.intra_op_num_threads = 4; o.inter_op_num_threads = 1
E = ort.InferenceSession(f"{MODEL}/enc.q.onnx", o); D = ort.InferenceSession(f"{MODEL}/dec.q.onnx", o)


def build_lexicon():
    c = collections.Counter()
    word = re.compile(r"[a-z']+")
    with open("data/corpus.txt", encoding="utf8") as f:
        for line in f:
            c.update(word.findall(line.lower()))
    for w in SLANG + SWEAR + GAME:
        c[w.lower()] += MIN_COUNT
    with open(LEX, "w", encoding="utf8") as f:
        for w, n in c.most_common():
            if n >= MIN_COUNT and len(w) > 1 and w.strip("'") == w:
                f.write(f"{w} {n}\n")


if not os.path.exists(LEX):
    t = time.time(); build_lexicon(); print(f"word list built in {time.time()-t:.0f}s", flush=True)
LEXICON = {}
with open(LEX, encoding="utf8") as f:
    for line in f:
        w, n = line.split(); LEXICON[w] = int(n)
BY_LEN = collections.defaultdict(list)
for w in LEXICON:                      # most common first, so a capped search keeps the likelier words
    BY_LEN[len(w)].append(w)
print("word list:", len(LEXICON), flush=True)


def osa(a, b, stop=99):
    """Edit distance counting a swap of neighbours as one edit."""
    if abs(len(a) - len(b)) > stop: return stop + 1
    p2, p1 = None, list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        c = [i]
        for j, cb in enumerate(b, 1):
            v = min(p1[j] + 1, c[j - 1] + 1, p1[j - 1] + (ca != cb))
            if p2 is not None and j > 1 and ca == b[j - 2] and a[i - 2] == cb: v = min(v, p2[j - 2] + 1)
            c.append(v)
        if min(c) > stop: return stop + 1
        p2, p1 = p1, c
    return p1[-1]


def typed_near(word, src, done):
    """The stretch of typed letters (spaces removed) that best matches this word, and how far off it is."""
    lo, hi = max(0, done - 12), min(len(src), done + len(word) + 12)
    best = (99, "")
    for i in range(lo, hi):
        for n in range(max(2, len(word) - 2), len(word) + 3):
            if i + n > hi: break
            s = src[i:i + n]; d = osa(word, s, best[0])
            if d < best[0]: best = (d, s)
    return best


def neighbours(s, limit, reach):
    found = []
    letters = set(s)
    for n in range(max(2, len(s) - 2), len(s) + 3):
        for w in BY_LEN[n]:
            if len(set(w) - letters) > reach: continue
            d = osa(s, w, reach)
            if d <= reach: found.append(w)
    found.sort(key=lambda w: -LEXICON[w])
    return found[:limit]


class Decoder:
    """Decoder calls are remembered per message, so trying many settings stays cheap."""
    def __init__(s, piece):
        s.m = E.run(None, {"x": np.array([enc(piece)], dtype=np.int64)})[0]; s.memo = {}; s.calls = 0
    def scores(s, prefixes):
        for p in dict.fromkeys(prefixes):      # the exported decoder takes one sequence at a time
            if p in s.memo: continue
            s.calls += 1
            out = D.run(None, {"y": np.array([p], dtype=np.int64), "m": s.m})[0][0]
            s.memo[p] = out - np.logaddexp.reduce(out)
        return [s.memo[p] for p in prefixes]


def word_scores(d, prefix, cands):
    """Model log-probability of each candidate word after the prefix, shared prefixes asked once."""
    ids = [tuple(enc(c)) for c in cands]
    totals = [0.0] * len(cands)
    for depth in range(max(map(len, ids))):
        live = [i for i, x in enumerate(ids) if len(x) > depth]
        lp = d.scores([tuple(prefix) + ids[i][:depth] for i in live])
        for i, v in zip(live, lp): totals[i] += float(v[ids[i][depth]])
    return totals


LETTER = {i for i, c in ((enc(ch)[0], ch) for ch in "abcdefghijklmnopqrstuvwxyz'")}
ORDINARY = re.compile(r"^[a-z][a-z']+$")


def decode(d, piece, cfg):
    src = re.sub(r"\s", "", piece.lower())
    y, conf, word_from, done, word_low = [BOS], [], 1, 0, 1.0
    for _ in range(len(piece) + 6 + len(piece) // 4 + 40):
        lp = d.scores([tuple(y)])[0]; b = int(lp.argmax()); p = float(np.exp(lp[b]))
        if b in LETTER:
            y.append(b); conf.append(p); word_low = min(word_low, p); continue
        # a word just ended: give it a second look if it is doubtful
        word = dec(y[word_from:])
        if cfg and len(word) >= 3 and ORDINARY.match(word) and (word_low < cfg["thr"] or word not in LEXICON):
            dist, near = typed_near(word, src, done)
            cands = list(dict.fromkeys([word] + neighbours(near, cfg["limit"], cfg["reach"]) + neighbours(word, cfg["limit"] // 2, 1)))
            if len(cands) > 1:
                model = word_scores(d, y[:word_from], cands)
                total = [m - cfg["typed"] * typed_near(c, src, done)[0] - (0 if c in LEXICON else cfg["unknown"]) for m, c in zip(model, cands)]
                k = int(np.argmax(total))
                if k and total[k] - total[0] > cfg["margin"]:
                    new = cands[k]
                    y = y[:word_from] + list(enc(new))
                    conf = conf[:word_from - 1] + [min(1.0, np.exp(total[k] - total[0]) / (1 + np.exp(total[k] - total[0])))] * len(new)
                    word = new
                    lp = d.scores([tuple(y)])[0]; b = int(lp.argmax()); p = float(np.exp(lp[b]))
        done += len(word)
        if b == EOS: return dec(y), conf, True
        y.append(b); conf.append(p); word_from, word_low = len(y), 1.0
        if chr(b + 29) != " ": done += 1
    return piece, [0.0] * len(piece), False


def pieces(t, n=300):
    t = t.strip(); out = []
    while len(t) > n:
        k = t.rfind(" ", 0, n); k = k if k >= 20 else n
        out.append(t[:k]); t = t[k:].lstrip()
    return out + ([t] if t else [])


ev = json.load(open("../private/eval.json", encoding="utf8"))
r = random.Random(20261109)
lines = []
with open("data/corpus.txt", encoding="utf8") as f:
    for i, line in enumerate(f):
        if i % 20011 == 7: lines.append(line.strip())
r.shuffle(lines)
synth = []
for s in lines[:120]:
    c = casual(s, r); synth.append({"in": garble(c, r), "ref": c})
sets = {"test": ev["test"], "real5": ev["real"], "synth": synth, "real10": ev["real3"]}
decoders = {}
def run(sample, cfg):
    out = []
    for i, p in enumerate(pieces(sample["in"])):
        d = decoders.setdefault((sample["in"], i), Decoder(p))
        out.append(decode(d, p, cfg)[0])
    return " ".join(out)
def score(name, cfg): return round(wordpct([(s["ref"], run(s, cfg)) for s in sets[name]]), 2)
def dev(cfg):
    return {k: score(k, cfg) for k in ("test", "real5", "synth")}


t0 = time.time()
base = dev(None)
print("greedy (no second look):", base, f"{time.time()-t0:.0f}s", flush=True)
best, best_value = None, sum(base.values()) - 1e9
for thr in (.8, .95):
    for typed in (.8, 1.5, 2.5):
        for unknown in (1, 3):
            for margin in (0, 1, 2):
                cfg = dict(thr=thr, typed=typed, unknown=unknown, margin=margin, limit=12, reach=2)
                res = dev(cfg)
                # the made-up and real sets count most; a win on one set must not hide a loss on another
                value = res["test"] + res["real5"] + res["synth"] - 3 * sum(max(0, base[k] - res[k]) for k in res)
                print(cfg, res, round(value, 2), f"{time.time()-t0:.0f}s", flush=True)
                if value > best_value: best, best_value = cfg, value
print("chosen:", best, flush=True)
print("dev with chosen:", dev(best), flush=True)
print("newest 10, greedy:", score("real10", None), " chosen:", score("real10", best), " unrepaired:", round(wordpct([(s["ref"], s["in"]) for s in sets["real10"]]), 2), flush=True)

kept = total = 0
for s in ev["test"]:
    a = run(s, best)
    for w in set(toks(s["ref"])) & PROTECTED:
        total += 1; kept += w in toks(a)
print(f"slang etc kept: {kept}/{total}")
print("clean unchanged:", sum(run({"in": s["ref"]}, best) == s["ref"] for s in ev["test"]), "/ 40")
with open("../private/lexfix_outputs.txt", "w", encoding="utf8") as f:
    for s in ev["real"] + ev["real3"]:
        f.write(f"IN  {s['in']}\nOLD {run(s, None)}\nNEW {run(s, best)}\nREF {s['ref']}\n\n")
T, C = [], []
for s in ev["real"] + ev["real3"]:
    decoders.clear(); t = time.perf_counter(); run(s, best); T.append(time.perf_counter() - t)
    C.append(sum(d.calls for d in decoders.values()))
T.sort(); print(f"4 threads, second look on: median {T[len(T)//2]:.2f}s, worst {T[-1]:.2f}s, median decoder calls {sorted(C)[len(C)//2]}")
print(f"word list size: {os.path.getsize(LEX)/1e6:.2f} MB, {len(LEXICON)} words")
