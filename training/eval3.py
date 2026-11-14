"""Checks for the v3 training aims, on made-up sentences only (never the writer's messages):
tired typing, misspellings and mixed-up words, swear words, and 'good girl'-type roleplay phrases.
python eval3.py [model dir]   (default ../model). Model only: the apps' keep lists are not applied."""
import random, sys
import numpy as np, onnxruntime as ort
from train import enc, dec, BOS, EOS, wordpct
from train2 import garble
from train3 import tired
from samples import CLEAN
from train3 import SWEAR3
SWEAR_SET = set(SWEAR3) | {"wank", "whore", "tits", "piss", "bitch"}

SPELL = [  # (typed, meant)
    ("i definately want that", "i definitely want that"), ("its alot of work", "its a lot of work"),
    ("i could of won that", "i could have won that"), ("your going to love it", "youre going to love it"),
    ("their going to the raid tomorrow", "theyre going to the raid tomorrow"), ("he is better then me", "he is better than me"),
    ("i dont no what to say", "i dont know what to say"), ("becuz i sed so", "because i said so"),
    ("sumthing is rong with it", "something is wrong with it"), ("wich one shud i take", "which one should i take"),
    ("thats so wierd", "thats so weird"), ("see you tommorow", "see you tomorrow"), ("i wont loose this time", "i wont lose this time"),
    ("i cant beleive it", "i cant believe it"), ("my freind is coming over", "my friend is coming over"),
    ("i probly wont make it", "i probably wont make it"), ("i was so embarassing", "i was so embarrassing"),
    ("we should of left earlier", "we should have left earlier"), ("i recieved your message", "i received your message"),
    ("its diffrent now", "its different now"), ("wat are you doing", "what are you doing"), ("i realy like it", "i really like it"),
    ("thats enuf for today", "thats enough for today"), ("that makes no sence", "that makes no sense"), ("me to", "me too"),
]
KEEP = [  # typed correctly; must stay exactly the same
    "you know i would never say that to your face", "i seen it yesterday", "he dont even care", "i aint doing that",
    "you been acting weird", "good girl *pats your head*", "such a good girl", "be a good girl for me",
    "what a bastard", "you stupid cunt", "stfu i am trying to sleep", "fml i forgot again", "dammit not again",
    "that motherfucker pulled again", "what a dumbass", "jackass", "goddamn it", "lmfao", "bollocks", "holy shit",
]
SWEARS = ["that bastard pulled again", "stop being an asshole", "what a dumbass", "you motherfucker", "goddamn it",
          "fuck this shit", "that was bullshit", "son of a bitch", "dammit i died again", "you dickhead"]

d = sys.argv[1] if len(sys.argv) > 1 else "../model"
o = ort.SessionOptions(); o.intra_op_num_threads = 2
E = ort.InferenceSession(f"{d}/enc.q.onnx", o); D = ort.InferenceSession(f"{d}/dec.q.onnx", o)
def model(p):
    m = E.run(None, {"x": np.array([enc(p)], dtype=np.int64)})[0]; y = [BOS]
    for _ in range(len(p) + 6 + len(p) // 4):
        b = int(D.run(None, {"y": np.array([y], dtype=np.int64), "m": m})[0][0].argmax())
        if b == EOS: return dec(y)
        y.append(b)
    return p

show = "-v" in sys.argv
def run(name, pairs):
    outs = [(ref, model(typed), typed) for typed, ref in pairs]
    exact = sum(o == ref for ref, o, _ in outs)
    invented = sum(1 for ref, o, _ in outs for w in o.split() if w.strip("*~.,!?") in SWEAR_SET and w not in ref.split())
    print(f"{name:10s} words {wordpct([(ref, o) for ref, o, _ in outs]):5.1f}%   whole lines {exact}/{len(outs)}   swear words invented {invented}")
    if show:
        for ref, o, t in outs:
            if o != ref: print(f"    {t!r} -> {o!r}   (meant {ref!r})")

r = random.Random(11)
run("tired", [(tired(s, r), s) for s in CLEAN])
r = random.Random(21)
run("tired x3", [(tired(s, r), s) for s in CLEAN for _ in range(3)])   # more samples for the invented-swear count
run("spelling", SPELL)
run("keep", [(s, s) for s in KEEP])
SHORT = ["ok","okay","ok see you","ok sure","yes","no","yep","nope","lol","lmao","omg","hi","hey","hello","bye","gn","good night",
         "ty","thx","np","sure","maybe","idk","brb","wait","same","me too","nice","sorry","oops","haha","yeah","nah","why","what",
         "how","where","when","who","ok ok","fine","love you","aint no way","i aint mad","your turn","your hair looks nice",
         "is that your cat","you earned a treat","then we go","more than that","were you there","where were you"]
run("short", [(s, s) for s in SHORT])
r = random.Random(12)
run("swears", [(garble(s, r), s) for s in SWEARS for _ in range(3)])
