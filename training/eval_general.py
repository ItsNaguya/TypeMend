"""General check (2026-11-14): how TypeMend does for an average writer, not the one person it was first built for.
Hand-written lines, none of them in the training lists (train3.SPELL, rp lines) or in samples.py.
python eval_general.py [model dir] [-v]   (default ../model). Model only: the apps' keep lists are not applied."""
import sys
import numpy as np, onnxruntime as ort
from train import enc, dec, BOS, EOS, wordpct

CLEAN = [  # well-written; must stay exactly the same
    "You may go now.", "May I ask something?", "I sent it yesterday.", "I bet you can't do it.", "That was a big hit.",
    "We met at the park.", "My cat sat on the mat.", "I'm going to the store. Are you coming?", "Hello, my name is Sarah!",
    "It's 5 PM in London right now.", "We're meeting on Monday at 3:30.", "The quick brown fox jumps over the lazy dog.",
    "Could you send me the file?", "She said: \"no way\".", "He let me borrow his bike.", "The light is too bright.",
    "Dinner is at 7:45, don't be late.", "I paid $20 for it.", "It's 10% off this week.", "Call me at 555-0199.",
    "We won 3-1 last night!", "My flight leaves at 06:15.", "She finished 2nd in the race.", "Meet me at Gate B12.",
    "I'm fond of old movies.", "The pot is on the stove.", "He bit his lip.", "They set the table.", "We sat in the back row.",
    "Lend me a pen, please.", "The wind was cold.", "I fed the dog.", "It's a fine day.", "The cup fell off the desk.",
    "Wait, what?!", "Oh no... not again.", "Well, that's that.", "Hmm, let me think.", "Sure, why not?", "I know, right?",
    "Emma and Liam are coming too.", "We visited Paris in June.", "Tom's car broke down.", "Ask Dr. Patel about it.",
    "The meeting moved to Thursday.", "I'll text you later.", "She's been there twice.", "Nobody saw it coming.",
    "That's not what I meant.", "Let's grab coffee sometime.",
]
TYPO = [  # (typed, meant): everyday typos and misspellings
    ("Teh meeting is at 3pm tomorow.", "The meeting is at 3pm tomorrow."), ("I wanted to aks you something", "I wanted to ask you something"),
    ("Can you plese send me the adress?", "Can you please send me the address?"), ("We shoudl go to the beach this weekend", "We should go to the beach this weekend"),
    ("Thnaks for the gift!", "Thanks for the gift!"), ("I'll be ther in five minuets", "I'll be there in five minutes"),
    ("Waht time does the movie start", "What time does the movie start"), ("I forgot my pasword again", "I forgot my password again"),
    ("Happy birhtday Anna!", "Happy birthday Anna!"), ("Ths is the best pizza in town", "This is the best pizza in town"),
    ("I dont understnad the question", "I dont understand the question"), ("Lets meet at the libary", "Lets meet at the library"),
    ("My phone battery is allmost dead", "My phone battery is almost dead"), ("The resturant was closed.", "The restaurant was closed."),
    ("It occured to me last night.", "It occurred to me last night."), ("We can't accomodate everyone.", "We can't accommodate everyone."),
    ("I'm not sure about the calender.", "I'm not sure about the calendar."), ("Have you eaten breakfest?", "Have you eaten breakfast?"),
    ("The wheather is awful today.", "The weather is awful today."), ("She's my best freind.", "She's my best friend."),
    ("I'll call you tonihgt.", "I'll call you tonight."), ("Thats a greta idea!", "Thats a great idea!"),
    ("Do you knwo where it is?", "Do you know where it is?"), ("I have to wokr late.", "I have to work late."),
    ("Ther are no more tickets.", "There are no more tickets."), ("I'm so hapy for you!", "I'm so happy for you!"),
    ("Where did you put the remtoe?", "Where did you put the remote?"), ("I lsot my keys.", "I lost my keys."),
    ("Can we talk latr?", "Can we talk later?"), ("This is so annoyign.", "This is so annoying."),
    ("I'm comming home now.", "I'm coming home now."), ("Did you finsh your homework?", "Did you finish your homework?"),
    ("We need more milk adn eggs.", "We need more milk and eggs."), ("She doesnt like spiders.", "She doesnt like spiders."),
    ("My brother is visting next week.", "My brother is visiting next week."), ("The train was delayd again.", "The train was delayed again."),
    ("Good mornign everyone!", "Good morning everyone!"), ("I really apreciate it.", "I really appreciate it."),
    ("See you on Firday.", "See you on Friday."), ("That was hilarous.", "That was hilarious."),
    ("I'm going to bed, goodnihgt.", "I'm going to bed, goodnight."), ("Could you hlep me with this?", "Could you help me with this?"),
    ("The kids are alseep.", "The kids are asleep."), ("I need a vacaton.", "I need a vacation."),
    ("Dont forget the meetign at 10:30.", "Dont forget the meeting at 10:30."), ("It costs $15, not $50.", "It costs $15, not $50."),
    ("I'm hungyr, let's eat.", "I'm hungry, let's eat."), ("Whats the wifi pasword?", "Whats the wifi password?"),
    ("The movie was realy long.", "The movie was really long."), ("I'll bring the dessret.", "I'll bring the dessert."),
]

d = next((a for a in sys.argv[1:] if not a.startswith("-")), "../model")
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
c = [(s, model(s)) for s in CLEAN]
print(f"clean       {sum(a == b for a, b in c)}/{len(c)} unchanged")
if show:
    for a, b in c:
        if a != b: print(f"    {a!r} -> {b!r}")
t = [(x, y, model(x)) for x, y in TYPO]
print(f"typos       {sum(y == r for _, y, r in t)}/{len(t)} fully right, words {wordpct([(y, r) for _, y, r in t]):.1f}%")
if show:
    for x, y, r in t:
        if y != r: print(f"    {x!r} -> {r!r}")
