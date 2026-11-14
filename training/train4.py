"""v4 training text (2026-11-14): TypeMend for everyone, not one writer. Continues v3 rather than replacing it.
Mix per message: 30% the v3 text (one writer's patterns, tired typing, roleplay, swears, short chat), so nothing is
forgotten; 50% standard written lines kept as written (capitals, punctuation, apostrophes) with everyday typos;
20% standard lines left clean, so correct text and unusual-but-right words ("sat", "bit") stay as they are.
Numbers, times and prices are mixed in, because the corpus has almost none ("3:30" became "3 :30")."""
import random
from train3 import sample as sample3, NEIGH, REAL, SPELL_RE

def number(r):
    h, m = r.randint(1, 12), r.choice(["00", "15", "30", "45", f"{r.randint(0, 59):02d}"])
    return r.choice([
        f"at {h}:{m}", f"at {r.randint(0, 23):02d}:{m}", f"by {h} {r.choice(['PM', 'AM', 'pm', 'am'])}", f"at {h}pm", f"for ${r.randint(2, 99)}",
        f"about {r.randint(2, 90)}%", f"on the {r.choice(['1st', '2nd', '3rd', '4th', '15th', '21st'])}", f"{r.randint(0, 5)}-{r.randint(0, 5)}",
        f"call {r.randint(200, 999)}-{r.randint(1000, 9999)}", f"room {r.randint(100, 499)}", f"Gate {r.choice('ABCDE')}{r.randint(1, 40)}",
        f"{r.randint(2, 30)} minutes", f"{r.randint(2, 12)} times", f"in {r.randint(1990, 2026)}", f"{r.randint(1, 9)}.{r.randint(0, 9)} stars",
    ])

def with_numbers(s, r):
    if r.random() > .15: return s
    w = s.split()
    w.insert(r.randrange(len(w) + 1), number(r))
    return " ".join(w)

def typo_word(w, r):
    """One everyday slip in a word, keeping its letter case. None when the slip would make another real word."""
    core = w.strip(".,!?;:\"'()")
    if len(core) < 3 or not core.isalpha(): return None
    if core[0].isupper() and core.lower() not in REAL: return None                        # names: the model can't know them
    lead = w[:w.index(core)]; tail = w[w.index(core) + len(core):]
    c = list(core); x = r.random(); i = r.randrange(len(c))
    if x < .30:
        j = r.randrange(len(c) - 1); c[j], c[j + 1] = c[j + 1], c[j]                       # teh, wokr
    elif x < .52 and len(c) > 3: del c[r.randrange(1, len(c))]                            # latr, finsh
    elif x < .62:                                                                         # comming / aproach
        k = next((k for k in range(1, len(c)) if c[k] == c[k - 1]), None)
        if k is not None: del c[k]
        else: c.insert(i, c[i])
    elif x < .82 and c[i].lower() in NEIGH:                                               # wheather-style neighbour key
        n = r.choice(NEIGH[c[i].lower()]); c[i] = n.upper() if c[i].isupper() else n
    elif c[i].lower() in NEIGH:
        c.insert(i + r.choice((0, 1)), r.choice(NEIGH[c[i].lower()]))                     # hwlp
    t = "".join(c)
    if t == core or t.lower() in REAL: return None
    return lead + t + tail

def standard_typos(s, r):
    sev = r.uniform(.05, .35)
    out = []
    for w in s.split():
        if r.random() < sev:
            t = typo_word(w, r)
            out.append(t if t else w)
        else:
            out.append(w)
    t = " ".join(out)
    for rx, typed in SPELL_RE:                                                            # definately, recieve (non-words only)
        if rx.search(t.lower()) and r.random() < .2:
            t = rx.sub(lambda m: r.choice(typed), t)
    if len(out) > 3 and r.random() < .05:                                                 # two words run together
        k = r.randrange(len(out) - 1); ws = t.split(); ws[k:k + 2] = [ws[k] + ws[k + 1]] if k + 1 < len(ws) else ws[k:k + 2]; t = " ".join(ws)
    return t

def sample(lines, r, join):
    """Returns (clean target, typed input)."""
    x = r.random()
    if x < .30: return sample3(lines, r, join)
    c = " ".join(with_numbers(r.choice(lines).strip(), r) for _ in range(join))[:370]
    if x < .80:
        t = standard_typos(c, r)
        return c, t
    return c, c

if __name__ == "__main__":
    r = random.Random(4)
    lines = open("data/corpus.txt", encoding="utf8").read().split("\n")[:50000]
    for _ in range(14):
        c, t = sample(lines, r, 1); print(f"{t!r}\n   -> {c!r}")
