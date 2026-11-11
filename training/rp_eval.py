"""Roleplay check: made-up adult roleplay lines (actions in asterisks, scene vocabulary, honorifics, symbols),
scrambled the way the writer types. Measures words repaired, scene words kept, symbols kept, clean lines untouched.
python rp_eval.py [model dir]   (default ../model)"""
import os, random, re, sys
import numpy as np, onnxruntime as ort
from train import enc, dec, BOS, EOS, wordpct, toks
from train2 import garble

LINES = [
    "*kneels at your feet and looks up at you* yes Mistress",
    "*clips the leash to your collar and tugs gently* come along pet",
    "good girl *pats your head* you earned a treat",
    "*whimpers softly against the gag* mmph",
    "*tightens the rope around your wrists* too tight? tell me your safeword",
    "my safeword is red, i am fine Sir",
    "*traces the riding crop along your thigh* count them for me",
    "*shivers and bites her lip* please Master",
    "((brb gotta grab food))",
    "((ooc: is this still okay for you?))",
    "~purrs and nuzzles into your hand~",
    "*blushes deeply* d-do i have to say it out loud?",
    "*tugs on the chain* on your knees, now",
    "*squirms in the restraints* that is not fair",
    "you will address me as Mistress tonight, understood?",
    "*presses a soft kiss to your bruised wrist* aftercare time",
    "*wraps you in a blanket and hands you some water* you did so well",
    "yes Sir, thank you Sir <3",
    "*slowly unbuckles the cuffs* breathe for me, good",
    "*giggles and wiggles away* catch me first",
    "*grabs your hair and pulls your head back* look at me",
    "behave or the blindfold goes back on",
    "*spanks you once* that was for talking back",
    "hehe *wags tail* am i a good pup?",
    "*sits on the throne and crosses her legs* kneel",
    "*kisses down your neck* mine",
    "*ties the last knot and steps back to admire it* perfect",
    "green, keep going",
    "yellow, can we slow down a little",
    "*smirks* you look cute when you beg",
]
SCENE = set("""kneels kneel mistress master sir leash collar pet whimpers gag rope wrists safeword red crop thigh
shivers bites chain knees squirms restraints aftercare bruised blanket cuffs unbuckles spanks pup wags tail throne
blindfold beg smirks knot pulls hair purrs nuzzles blushes ooc brb green yellow""".split())
SYMBOLS = re.compile(r"[*~()<>3\-?!,.:]")
d = sys.argv[1] if len(sys.argv) > 1 else "../model"
o = ort.SessionOptions(); o.intra_op_num_threads = 4
E = ort.InferenceSession(f"{d}/enc.q.onnx", o); D = ort.InferenceSession(f"{d}/dec.q.onnx", o)
def model(p):
    m = E.run(None, {"x": np.array([enc(p)], dtype=np.int64)})[0]; y = [BOS]
    for _ in range(len(p) + 6 + len(p) // 4):
        b = int(D.run(None, {"y": np.array([y], dtype=np.int64), "m": m})[0][0].argmax())
        if b == EOS: return dec(y)
        y.append(b)
    return p
r = random.Random(7)
rows, kept, total, symbols_same, examples = [], 0, 0, 0, []
for line in LINES:
    for _ in range(3):
        typed = garble(line, r)
        out = model(typed)
        rows.append((line, out))
        for w in set(toks(line)) & SCENE:
            total += 1; kept += w in toks(out)
        symbols_same += SYMBOLS.findall(out) == SYMBOLS.findall(line)
        if len(examples) < 12 and out != line: examples.append((typed, out))
print(f"roleplay words correct: {wordpct(rows):.1f}% (unrepaired {wordpct([(a, garble(a, random.Random(7))) for a, _ in rows]):.1f}%)")
print(f"exact lines: {sum(a == b for a, b in rows)}/{len(rows)}")
print(f"scene words kept: {kept}/{total}")
print(f"symbols exactly as intended: {symbols_same}/{len(rows)}")
clean = [(l, model(l)) for l in LINES]
print(f"clean roleplay lines left untouched: {sum(a == b for a, b in clean)}/{len(clean)}")
for a, b in clean:
    if a != b: print(f"  CLEAN CHANGED  {a!r} -> {b!r}")
for a, b in examples: print(f"  {a!r} -> {b!r}")

# ---- with the planned fixes: roleplay words kept when typed exactly, and ~ (( )) kept as typed ----
ROLEPLAY = """aftercare safeword safewords crop thigh thighs collar collared leash gag gagged cuffs cuffed rope ropes shibari
blindfold blindfolded restraints restrained kneel kneels kneeling whimper whimpers squirm squirms spank spanks spanking
Mistress Master Sir Daddy Mommy pet pup puppy kitten brat bratty subspace dom domme sub switch ooc brb afk mmph mmm nngh
hnng mhm purr purrs nuzzle nuzzles smirk smirks""".split()
KEEP = re.compile(r"(?<![\w'])(" + "|".join(map(re.escape, ROLEPLAY)) + r")(?![\w'])", re.I)
MARKS = re.compile(r"(~+|\(\(+|\)\)+)")
def around(text, pattern, inner):
    parts = pattern.split(text); out = ""
    for i, p in enumerate(parts):
        if i % 2 or not re.search(r"[a-z]", p, re.I): out += p; continue
        if p.startswith(" "): out += " "
        out += inner(p.strip())
        if p.endswith(" "): out += " "
    return out
def keeping(text):
    typed = KEEP.findall(text)
    if not typed: return model(text)
    first = model(text)
    return first if KEEP.findall(first) == typed else around(text, KEEP, model)
def fixed(text): return around(text, MARKS, keeping)
r = random.Random(7)
rows2, kept2 = [], 0
for line in LINES:
    for _ in range(3):
        typed = garble(line, r); out = fixed(typed); rows2.append((line, out))
        kept2 += sum(w in toks(out) for w in set(toks(line)) & SCENE)
clean2 = [(l, fixed(l)) for l in LINES]
print(f"WITH FIXES: words correct {wordpct(rows2):.1f}%, exact {sum(a == b for a, b in rows2)}/{len(rows2)}, scene words kept {kept2}/{total}, clean untouched {sum(a == b for a, b in clean2)}/{len(clean2)}")
for a, b in clean2:
    if a != b: print(f"  CLEAN CHANGED  {a!r} -> {b!r}")
