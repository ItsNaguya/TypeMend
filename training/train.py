"""Tiny character-level repair model. python train.py <minutes>"""
import json, math, random, re, sys, time, difflib
import torch, torch.nn as nn, torch.nn.functional as F

# ---- casual chat styling (training text only; none of this is the test set) ----
SLANG = "lol lmao haha hehe xD ngl tbh idk bro damn omg wtf yeah nah uhm hmm pls thx ofc rn imo fr".split()
SWEAR = "fuck fucking shit damn ass bitch hell crap dick horny naked sexy kinky bed tied rope kiss spank collar moan".split()
GAME = "raid savage tank healer dps duty glam gil limsa gridania eorzea miqote viera hrothgar lalafell FC RP ERP mount minion crafter ultimate extreme roulette dungeon".split()
EMO = [">///<", ":3", "^^", "uwu", "owo", "..", "...", "?!", "<3", ";)", ":D", "T_T"]
SYL = "ka ri no mi el ar yu se ta lo vi an sha ze ru ni ko da".split()
def name(r): return "".join(r.choice(SYL) for _ in range(r.randint(2, 3))).capitalize()
def casual(s, r):
    if r.random() < .85: s = s.lower()
    if r.random() < .6: s = s.replace("'", "")
    if r.random() < .6: s = s.rstrip(".")
    for a, b, p in [("you've seen", "you seen", .5), ("have been", "been", .2), ("because", "cuz", .05), ("people", "ppl", .15),
                    ("going to", "gonna", .4), ("want to", "wanna", .4), ("doesn't", "dont", .2), ("doesnt", "dont", .2), ("isn't", "aint", .05)]:
        if a in s and r.random() < p: s = s.replace(a, b)
    w = s.split()
    for lst, p in [(SLANG, .3), (SWEAR, .18), (GAME, .1)]:
        if r.random() < p: w.insert(r.choice([0, len(w), r.randrange(len(w)+1)]), r.choice(lst))
    if r.random() < .08: w.insert(r.randrange(len(w)+1), name(r))
    if r.random() < .08: w.append(r.choice(EMO))
    if r.random() < .03: w.append(f"{r.randint(0,23):02d}.{r.randint(0,59):02d}")
    return " ".join(w)

# ---- garbler v2, built from all 6 real messages ----
NEAR = "tdrnesl"
def garble(s, r):
    sev = 0 if r.random() < .08 else r.uniform(.25, 1.35)
    ws = []
    for w in s.split():
        if not w.isalpha() or len(w) < 2 or r.random() > .9:
            ws.append(w); continue
        c = list(w); x = r.random()
        if w.endswith("d") and len(w) > 3 and r.random() < .22*sev: c[-1] = "t"
        elif w == "my" and r.random() < .1*sev: c = list("may")
        elif w == "maybe" and r.random() < .25*sev: c = list("may")
        elif x < .16*sev and len(c) > 2:
            i = r.randrange(len(c)-1); c[i], c[i+1] = c[i+1], c[i]
        elif x < .22*sev and len(c) > 3: del c[r.randrange(1, len(c)-1)]
        elif x < .29*sev and len(c) > 3: c.pop()
        elif x < .32*sev and len(c) > 3: c[r.randrange(1, len(c))] = r.choice(NEAR)
        elif x < .36*sev: c.insert(r.randrange(1, len(c)), r.choice("rtnes"))
        ws.append("".join(c))
    out = ""
    for i, w in enumerate(ws):
        out += w
        if i == len(ws)-1: break
        x = r.random()
        if x < .26*sev and len(w) > 1 and w[-1].isalpha():
            out = out[:-1] + " " + out[-1]
        elif x < .31*sev and len(w) > 2 and w[-2].isalpha():      # 'like you' -> 'lie kyou'
            out = out[:-2] + out[-1] + " " + out[-2]
        elif x < .38*sev:
            pass
        else:
            out += " "
    return out

# ---- model ----
PAD, BOS, EOS = 0, 1, 2
def enc(s): return [ord(c)-29 if 32 <= ord(c) <= 126 else ord("?")-29 for c in s]
def dec(ids): return "".join(chr(i+29) for i in ids if i > 2)
V = 3 + 95; MAXLEN = 400
class Net(nn.Module):
    def __init__(s, d=256, h=8, n=4, ff=768):
        super().__init__()
        s.e = nn.Embedding(V, d); s.p = nn.Embedding(MAXLEN, d)
        s.enc = nn.TransformerEncoder(nn.TransformerEncoderLayer(d, h, ff, .05, batch_first=True, norm_first=True), n, enable_nested_tensor=False)
        s.dec = nn.TransformerDecoder(nn.TransformerDecoderLayer(d, h, ff, .05, batch_first=True, norm_first=True), n)
        s.o = nn.Linear(d, V)
    def emb(s, x): return s.e(x) + s.p(torch.arange(x.size(1), device=x.device))
    def memory(s, x): return s.enc(s.emb(x), src_key_padding_mask=x.eq(PAD))
    def step(s, y, m, xmask):
        tgt = nn.Transformer.generate_square_subsequent_mask(y.size(1), device=y.device, dtype=torch.bool)
        return s.o(s.dec(s.emb(y), m, tgt_mask=tgt, tgt_key_padding_mask=y.eq(PAD), memory_key_padding_mask=xmask))
@torch.no_grad()
def repair(net, text, dev):
    net.eval()
    x = torch.tensor([enc(text)[:MAXLEN-2]], device=dev); m = net.memory(x)
    y = [BOS]; probs = []
    for _ in range(min(len(text)+25, MAXLEN-1)):
        p = F.softmax(net.step(torch.tensor([y], device=dev), m, x.eq(PAD))[0, -1], -1)
        v, i = p.max(-1)
        if i.item() == EOS: break
        y.append(i.item()); probs.append(v.item())
    net.train()
    return dec(y), probs
def toks(s): return re.findall(r"[a-z']+", s.lower())
def wordpct(pairs):
    M = N = 0
    for ref, hyp in pairs:
        a, b = toks(ref), toks(hyp)
        M += sum(x.size for x in difflib.SequenceMatcher(None, a, b, autojunk=False).get_matching_blocks()); N += max(len(a), len(b))
    return 100*M/N
def evaluate(net, ev, dev):
    return {k: round(wordpct([(s["ref"], repair(net, s["in"], dev)[0]) for s in ev[k]]), 1) for k in ("test", "real")}

if __name__ == "__main__":
    minutes = float(sys.argv[1]); r = random.Random(11)
    lines = open("corpus.txt", encoding="utf8").read().split("\n")
    print("lines", len(lines), flush=True)
    ev = json.load(open("eval.json", encoding="utf8"))
    dev = "cuda"; net = Net().to(dev)
    print("params", sum(p.numel() for p in net.parameters())) if False else print("params", sum(p.numel() if False else p.nelement() for p in net.parameters()), flush=True)
    opt = torch.optim.AdamW(net.parameters(), lr=5e-4, weight_decay=1e-4); scaler = torch.amp.GradScaler("cuda")
    t0 = time.time(); end = minutes*60; step = 0; best = -1
    while (el := time.time()-t0) < end:
        for g in opt.param_groups: g["lr"] = 5e-4*(min(1, el/60)*(.02+.98*.5*(1+math.cos(math.pi*el/end))))
        A, B = [], []
        for _ in range(256):
            c = casual(r.choice(lines), r); A.append(enc(garble(c, r))[:200]); B.append([BOS]+enc(c)[:200]+[EOS])
        la, lb = max(map(len, A)), max(map(len, B))
        x = torch.tensor([a+[PAD]*(la-len(a)) for a in A], device=dev); y = torch.tensor([b+[PAD]*(lb-len(b)) for b in B], device=dev)
        with torch.amp.autocast("cuda"):
            m = net.memory(x); o = net.step(y[:, :-1], m, x.eq(PAD))
            loss = F.cross_entropy(o.reshape(-1, V), y[:, 1:].reshape(-1), ignore_index=PAD, label_smoothing=.03)
        opt.zero_grad(set_to_none=True); scaler.scale(loss).backward(); scaler.unscale_(opt)
        nn.utils.clip_grad_norm_(net.parameters(), 1.0); scaler.step(opt); scaler.update(); step += 1
        if step % 2500 == 0:
            e = evaluate(net, ev, dev)
            print(f"{time.time()-t0:7.0f}s step {step} loss {loss.item():.3f} eval {e}", flush=True)
            torch.save(net.state_dict(), "last.pt")
    e = evaluate(net, ev, dev); print("final", e, flush=True); torch.save(net.state_dict(), "last.pt")
