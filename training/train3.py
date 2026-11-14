"""v3 training text (2026-11-14): adds to the writer's own error patterns (train2.garble) the mistakes of
tired typing (neighbouring keys, two keys at once, a key held a bit long), common misspellings and mixed-up
words (definately, alot, could of, your going), more swear words, and roleplay lines. None of the test
sentences are used here.

Kept as they are, by design: slang, dialect and grammar (dont, aint, i seen, you been); repairs must not
'improve' how someone talks."""
import random, re
from train import name, EMO
from train2 import casual, garble, VOW

# ---- keyboard: QWERTY neighbours ----
ROWS = ["qwertyuiop", "asdfghjkl", "zxcvbnm"]
NEIGH = {}
for ri, row in enumerate(ROWS):
    for ci, ch in enumerate(row):
        n = set()
        for dr in (-1, 0, 1):
            rr = ri + dr
            if 0 <= rr < len(ROWS):
                for dc in (-1, 0, 1) if dr == 0 else ((0, 1) if dr < 0 else (-1, 0)):
                    cc = ci + dc
                    if (dr or dc) and 0 <= cc < len(ROWS[rr]): n.add(ROWS[rr][cc])
        NEIGH[ch] = "".join(sorted(n))

import os
REAL = {l.split()[0].lower() for l in open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "data", "lexicon.txt"), encoding="utf8") if l.strip()}

def tired(s, r):
    """Fat-finger slips: a neighbouring key instead of the right one, both keys at once, a key held too long.
    Words under 3 letters are left alone, and a slip that makes another real word ("now" -> "how") is undone:
    those need context the model doesn't have, and training on them teaches it to change correct words."""
    sev = r.uniform(.4, 1.3)
    out = []
    for w in s.split():
        if not w.isalpha() or len(w) < 3 or w[0].isupper() or r.random() > .55:
            out.append(w); continue
        c = list(w)
        for _ in range(1 if r.random() < .8 else 2):
            i = r.randrange(len(c)); ch = c[i].lower(); x = r.random()
            if ch not in NEIGH: continue
            if x < .5 * sev: c[i] = r.choice(NEIGH[ch])                                   # hekko
            elif x < .8 * sev: c.insert(i + r.choice((0, 1)), r.choice(NEIGH[ch]))      # hwello
            elif x < .95 * sev and 0 < i < len(c) - 1 and c[i] != c[i - 1] and c[i] != c[i + 1]:
                c.insert(i, c[i])                                                        # heello (never 3 in a row: stretches are on purpose)
        t = "".join(c)
        out.append(w if t != w and t.lower() in REAL else t)
    return " ".join(out)

# ---- misspellings: (meant, typed variants). Applied to the clean text to make the typed side. ----
# Only typed forms that are not real words. Mixed-up real words (your/youre, there/their, then/than, where/were,
# know/no, lose/loose) were tried in pass 2 and taught the model to change correct words ("your thigh" -> "youre thigh").
SPELL = [
    ("definitely", "definately definatly definetly defintely"), ("a lot", "alot"), ("could have", "could of"),
    ("would have", "would of"), ("should have", "should of"), ("separate", "seperate"), ("because", "becuz becaus becasue"),
    ("something", "sumthing somthing"), ("which", "wich"), ("should", "shud"), ("would", "wud"),
    ("receive", "recieve"), ("believe", "belive beleive"), ("weird", "wierd"),
    ("tomorrow", "tommorow tomorow tommorrow"), ("until", "untill"), ("what", "wat"), ("said", "sed"), ("wrong", "rong"),
    ("enough", "enuf enogh"), ("again", "agen"), ("probably", "probly probaly"), ("really", "realy relly"),
    ("friend", "freind frend"), ("thought", "thougt"), ("whether", "wether"),
    ("necessary", "neccessary necesary"), ("embarrassing", "embarassing"), ("disappear", "dissapear"),
    ("beginning", "begining"), ("finally", "finaly"), ("basically", "basicly"), ("literally", "literaly"),
    ("everyone", "evryone"), ("sense", "sence"), ("surprise", "suprise"), ("government", "goverment"),
    ("interesting", "intresting"), ("different", "diffrent"), ("actually", "actualy acually"), ("know", "kno"),
]
HOMO = []
SPELL_RE = [(re.compile(rf"\b{re.escape(a)}\b"), b.split()) for a, b in SPELL + HOMO]

def misspell(s, r):
    for rx, typed in SPELL_RE:
        if rx.search(s) and r.random() < .45:
            s = rx.sub(lambda m: r.choice(typed) if r.random() < .8 else m.group(0), s)
    return s

# ---- more swear words: the English list the keep list uses, inserted into casual chat ----
SWEAR3 = """fuck fucking fuckin fucked fucker motherfucker shit shitty bullshit damn dammit goddamn bitch bitches asshole
dumbass jackass badass bastard bastards cunt dick dickhead cock pussy slut twat wanker bollocks crap crappy pissed
douche douchebag wtf stfu fml ffs omfg lmfao ass hell piss tits whore""".split()
GERMAN_KEEP = "scheisse scheiss verdammt verdammte arschloch kacke fick ficken wichser hurensohn schlampe".split()

# ---- roleplay lines (made up from parts; the roleplay test lines in rp_eval.py are not among them) ----
WHO = ["Mistress", "Master", "Sir", "my lady", "pet", "kitten", "pup", "love", "darling", "you"]
ACT = ["leans in close", "tilts her head", "tugs the leash", "wraps the rope around your wrists", "pats your head", "smiles softly",
       "brushes your hair aside", "kneels beside you", "holds out the collar", "pins your wrists above your head", "pulls you onto her lap",
       "traces a finger down your spine", "bites her lip", "hands you a glass of water", "blushes and looks away", "giggles",
       "whispers in your ear", "tightens the cuffs", "loosens the knot", "takes off the blindfold", "nuzzles your neck",
       "wags her tail", "purrs", "squirms", "whimpers", "smirks", "rolls her eyes", "sighs happily", "stretches", "yawns"]
SAY = ["good girl", "good boy", "such a good girl", "good pet", "thats my good girl", "be a good girl for me", "who is a good girl",
       "do you want to be a good girl", "you are such a good boy", "good kitten", "on your knees", "look at me", "tell me your safeword",
       "color?", "green", "yellow", "red", "are you okay?", "you did so well", "i am so proud of you", "yes {who}", "please {who}",
       "thank you {who}", "sorry {who}", "come here", "not yet", "beg for it", "you are mine", "i missed you", "are you comfy?",
       "breathe for me", "drink some water", "time for aftercare", "brb gotta grab food", "ooc: is this okay for you?"]

def rp_line(r):
    say = r.choice(SAY).replace("{who}", r.choice(WHO))
    form = r.random()
    if form < .35: s = f"*{r.choice(ACT)}* {say}"
    elif form < .55: s = f"{say} *{r.choice(ACT)}*"
    elif form < .7: s = f"*{r.choice(ACT)} and {r.choice(ACT)}*"
    elif form < .8: s = f"~{r.choice(ACT)}~"
    elif form < .87: s = f"(({say}))"
    else: s = say
    if r.random() < .3: s += " " + r.choice(EMO)
    if r.random() < .15: s = name(r) + " " + s
    return s

# Pass 1 (90 min) used .12 and .08; pass 3 starts again from the shipped model with the fixes above. Pass 2 (2026-11-14) lowers swears and raises clean lines: after pass 1 the model
# sometimes turned a garbled ordinary word into a swear word ("last nitht" -> "last bitch") and changed clean lines.
SWEAR_SHARE, CLEAN_SHARE = .05, .18

def sample(lines, r, join):
    """Returns (clean target, typed input)."""
    parts = []
    for _ in range(join):
        x = r.random()
        if x < .15: c = rp_line(r)
        elif join == 1 and x < .35: c = casual(" ".join(r.choice(lines).split()[:r.randint(1, 4)]), r)   # short chat: "ok", "im here"
        else: c = casual(r.choice(lines), r)
        w = c.split()
        if r.random() < SWEAR_SHARE: w.insert(r.randrange(len(w) + 1), r.choice(SWEAR3))
        if r.random() < SWEAR_SHARE / 4: w.insert(r.randrange(len(w) + 1), r.choice(GERMAN_KEEP))
        parts.append(" ".join(w))
    c = " ".join(parts)[:370]
    typed = c
    mode = r.random()
    if mode < .30: typed = misspell(typed, r)
    if .20 < mode < .45: typed = tired(typed, r)
    typed = garble(typed, r) if mode < 1 - CLEAN_SHARE else (typed if typed != c else garble(typed, r))
    return c, typed

if __name__ == "__main__":
    import sys
    r = random.Random(int(sys.argv[1]) if len(sys.argv) > 1 else 1)
    lines = ["you know i would never say that to your face", "i think they're going to win tomorrow", "it was definitely a lot of fun",
             "i could have done better than that", "hello how are you doing today"]
    for _ in range(12):
        c, t = sample(lines, r, 1); print(f"{t!r}\n   -> {c!r}")
    print(NEIGH["g"], NEIGH["q"], NEIGH["m"])
