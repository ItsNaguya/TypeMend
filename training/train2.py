"""v2: larger model, written so it exports to ONNX with free message length. python train2.py <minutes>"""
import json, math, random, sys, time
import torch, torch.nn as nn, torch.nn.functional as F
from train import enc, dec, PAD, BOS, EOS, V, repair, wordpct, name, EMO

SLANG = "lol lmao haha hehe xD ngl tbh idk bro damn omg wtf yeah nah uhm hmm pls thx ofc rn imo fr btw dc ff fc rp irl afk brb gg tbf ppl abit kinda".split()
SWEAR = "fuck fucking shit damn ass bitch hell crap dick horny naked sexy kinky bed tied rope kiss spank collar moan".split()
GAME = ("raid savage tank healer dps duty glam gil limsa gridania eorzea miqote viera hrothgar lalafell FC RP ERP mount minion crafter "
        "ultimate extreme roulette dungeon shiva lifestream puppeteer penumbra glamourer moodles dalamud plugin overlay emote emoji discord server "
        "command macro housing wardrobe retainer").split()
def casual(s, r):
    if r.random() < .9: s = s.lower()
    if r.random() < .7: s = s.replace("'", "")
    if r.random() < .75: s = s.rstrip(".")
    for a, b, p in [("you've seen", "you seen", .5), ("have been", "been", .2), ("because", "cuz", .03), ("people", "ppl", .25), ("going to", "gonna", .4),
                    ("want to", "wanna", .4), ("kind of", "kinda", .4), ("doesn't", "dont", .2), ("doesnt", "dont", .2), ("isn't", "aint", .05), ("myself", "my self", .08)]:
        if a in s and r.random() < p: s = s.replace(a, b)
    w = s.split()
    for lst, p in [(SLANG, .4), (SWEAR, .07), (GAME, .14)]:
        while r.random() < p: w.insert(r.randrange(len(w)+1), r.choice(lst))
    if r.random() < .25: w.insert(r.randrange(len(w)+1), name(r))
    if r.random() < .08: w.append(r.choice(EMO))
    if r.random() < .03: w.append(f"{r.randint(0,23):02d}.{r.randint(0,59):02d}")
    return " ".join(w)
VOW = {"a": "eo", "e": "ai", "i": "e", "o": "eau", "u": "oa"}
def garble(s, r):
    sev = 0 if r.random() < .08 else r.uniform(.3, 1.4)
    ws = []
    for w in s.split():
        if not w.isalpha() or len(w) < 2 or w[0].isupper() or r.random() > .92:
            ws.append(w); continue
        c = w; x = r.random()
        for i in range(len(c)-1, 0, -1):                 # doubled letters typed once (realy, colector)
            if c[i] == c[i-1] and r.random() < .55*sev: c = c[:i]+c[i+1:]
        if c.endswith("ing") and r.random() < .3*sev: c = c[:-3]+"ig"
        if len(c) > 3 and "nd" in c and r.random() < .1*sev: c = c.replace("nd", "d", 1)
        c = list(c)
        if w.endswith("d") and len(w) > 3 and r.random() < .3*sev: c[-1] = "t"
        elif w.endswith("t") and len(w) > 3 and r.random() < .05*sev: c[-1] = "d"
        elif w == "my" and r.random() < .1*sev: c = list("may")
        elif w == "maybe" and r.random() < .25*sev: c = list("may")
        elif x < .18*sev and len(c) > 2:
            i = r.randrange(len(c)-1); c[i], c[i+1] = c[i+1], c[i]
        elif x < .25*sev and len(c) > 3: del c[r.randrange(1, len(c)-1)]
        elif x < .33*sev and len(c) > 3: c.pop()
        elif x < .39*sev:
            ix = [i for i, h in enumerate(c) if h in VOW and i]
            if ix:
                i = r.choice(ix); c[i] = r.choice(VOW[c[i]])
        elif x < .41*sev and len(c) > 3: c[r.randrange(1, len(c))] = r.choice("tdrnesl")
        elif x < .45*sev and len(c) > 1: c.insert(r.randrange(1, len(c)), r.choice("rtnes"))
        ws.append("".join(c))
    out = ""
    for i, w in enumerate(ws):
        out += w
        if i == len(ws)-1: break
        x = r.random()
        if x < .28*sev and len(w) > 1 and w[-1].isalpha(): out = out[:-1]+" "+out[-1]
        elif x < .34*sev and len(w) > 2 and w[-2].isalpha(): out = out[:-2]+out[-1]+" "+out[-2]
        elif x < .43*sev: pass
        else: out += " "
    return out

class Attn(nn.Module):
    def __init__(s, d, h, p):
        super().__init__(); s.h, s.k = h, d//h; s.q = nn.Linear(d, d); s.kv = nn.Linear(d, d*2); s.o = nn.Linear(d, d); s.dr = nn.Dropout(p)
    def forward(s, a, b, mask=None):
        B = a.shape[0]
        q = s.q(a).reshape(B, -1, s.h, s.k).transpose(1, 2)
        k, v = s.kv(b).chunk(2, -1)
        k = k.reshape(B, -1, s.h, s.k).transpose(1, 2); v = v.reshape(B, -1, s.h, s.k).transpose(1, 2)
        w = q @ k.transpose(-1, -2) / math.sqrt(s.k)
        if mask is not None: w = w.masked_fill(mask, -1e4)
        return s.o((s.dr(w.softmax(-1)) @ v).transpose(1, 2).reshape(B, -1, s.h*s.k))
class Block(nn.Module):
    def __init__(s, d, h, ff, p, cross):
        super().__init__(); s.a = Attn(d, h, p); s.n1 = nn.LayerNorm(d); s.cross = cross
        if cross: s.c = Attn(d, h, p); s.n2 = nn.LayerNorm(d)
        s.f = nn.Sequential(nn.Linear(d, ff), nn.GELU(), nn.Dropout(p), nn.Linear(ff, d)); s.n3 = nn.LayerNorm(d); s.dr = nn.Dropout(p)
    def forward(s, x, m=None, self_mask=None, mem_mask=None):
        n = s.n1(x); x = x + s.dr(s.a(n, n, self_mask))
        if s.cross: x = x + s.dr(s.c(s.n2(x), m, mem_mask))
        return x + s.dr(s.f(s.n3(x)))
class Net2(nn.Module):
    def __init__(s, d=384, h=8, n=6, ff=1152, p=.05):
        super().__init__(); s.e = nn.Embedding(V, d); s.p = nn.Embedding(400, d)
        s.E = nn.ModuleList(Block(d, h, ff, p, False) for _ in range(n)); s.D = nn.ModuleList(Block(d, h, ff, p, True) for _ in range(n))
        s.ne = nn.LayerNorm(d); s.nd = nn.LayerNorm(d); s.o = nn.Linear(d, V, bias=False); s.o.weight = s.e.weight
    def emb(s, x): return s.e(x) + s.p(torch.cumsum(torch.ones_like(x), 1)-1)
    def memory(s, x):
        pm = x.eq(PAD).unsqueeze(1).unsqueeze(2); h = s.emb(x)
        for b in s.E: h = b(h, self_mask=pm)
        return s.ne(h)
    def step(s, y, m, xmask=None):
        pos = torch.cumsum(torch.ones_like(y), 1)
        c = (pos.unsqueeze(2) < pos.unsqueeze(1)).unsqueeze(1)    # True = not allowed to look at that (future) letter
        c = c | y.eq(PAD).unsqueeze(1).unsqueeze(2)
        mm = None if xmask is None else xmask.unsqueeze(1).unsqueeze(2)
        h = s.emb(y)
        for b in s.D: h = b(h, m, c, mm)
        return s.o(s.nd(h))
def evaluate(net, ev, dev):
    r = {k: round(wordpct([(s["ref"], repair(net, s["in"], dev)[0]) for s in ev[k]]), 1) for k in ("test", "real", "real3")}
    return r
