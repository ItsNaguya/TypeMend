"""Made-up Discord-style sentences, garbled with the error patterns seen in the one real sample.
Reference = the clean sentence, lowercase, exactly as the writer would have meant it (slang and
non-standard grammar stay: repairing must not 'improve' them)."""
import random, json

CLEAN = [
 "you know i would never say that to your face",
 "i think we should go to the store before it gets dark",
 "ngl that was the funniest thing i have seen all week",
 "he dont even know what he is talking about lmao",
 "i aint going back there after what happened last time",
 "you been acting weird ever since we played that game",
 "fuck that guy he still owes me twenty bucks",
 "this shit is way harder than it looks",
 "are you gonna finish that or can i have it",
 "my neck hurts from sleeping on the couch again",
 "stop teasing me or i will tie you to the bed",
 "you only like me because i look good in a collar",
 "she said i was too horny for my own good and honestly fair",
 "keep talking like that and see what happens tonight",
 "i was so drunk i tried to kiss the door",
 "your ass is getting spanked if you lose this round",
 "wanna hop on later and run a few dungeons",
 "the boss keeps killing me at the very last phase",
 "my internet went down right when we started the raid",
 "idk why but this song makes me want to cry",
 "the cat knocked my coffee all over the keyboard",
 "we could watch a movie if you are not too tired",
 "bro you missed the best part of the whole stream",
 "i told you that would happen but you never listen",
 "there is no way she actually believed that story",
 "thanks for staying up with me last night it helped a lot",
 "i have been feeling better since i started going outside",
 "that dress looks way better on you than on the model",
 "you sound like my mother when you say things like that",
 "do you want me to bring anything when i come over",
 "damn that is cold even for you",
 "nobody asked you to get on your knees but here we are",
 "i would pay good money to see you try that again",
 "my brother always eats the last slice and blames the dog",
 "we seen him at the mall on friday with some girl",
 "lol you really thought you could beat me at my own game",
 "i need a nap and about three more cups of coffee",
 "what time are you getting home from work tomorrow",
 "you can have the window seat if you stop complaining",
 "tell them i said hello and that i miss them",
]
PROTECTED = {"ngl","lmao","dont","aint","fuck","shit","gonna","tie","collar","horny","drunk","ass","spanked",
             "wanna","idk","bro","damn","knees","seen","lol","been"}
# one word replaced by something nobody could work out: the honest answer is to mark it
UNCLEAR = [
 ("i left my qzvrk at your place last night", "qzvrk"),
 ("can you bring the xjtpl when you come over", "xjtpl"),
 ("she told me about the wpqmz and i laughed", "wpqmz"),
 ("we should try fkgvb next time we play", "fkgvb"),
 ("my zntqh broke again this morning", "zntqh"),
 ("he keeps calling me jrxvw for some reason", "jrxvw"),
 ("that hvzkq was way too expensive", "hvzkq"),
 ("do you still have my bqxtm from last week", "bqxtm"),
]
VOWELS = "aeiou"
def garble(s, rng, keep=()):
    # 1) word-internal damage, 2) space timing (last letter lands after the space, as in the sample)
    words = []
    for w in s.split():
        if w in keep or len(w) < 3:
            words.append(w); continue
        c = list(w); r = rng.random()
        if r < .13:   # swapped neighbours
            i = rng.randrange(0, len(c)-1); c[i], c[i+1] = c[i+1], c[i]
        elif r < .20 and len(c) > 3:  # dropped letter
            del c[rng.randrange(1, len(c))]
        elif r < .24 and len(c) > 3:  # wrong letter
            c[rng.randrange(1, len(c))] = rng.choice("tdrnesl")
        elif r < .28:  # extra letter
            c.insert(rng.randrange(1, len(c)), rng.choice("rtnes"))
        words.append("".join(c))
    out = ""
    for i, w in enumerate(words):
        out += w
        if i == len(words)-1: break
        r = rng.random()
        if r < .24 and len(w) > 1 and words[i] not in keep:
            last = out[-1]; out = out[:-1] + " " + last  # 'you see' -> 'yo usee'
        elif r < .31:
            pass      # missing space
        else:
            out += " "
    return out

def build(seed=7):
    rng = random.Random(seed)
    test = [{"id": f"s{i}", "ref": s, "in": garble(s, rng)} for i, s in enumerate(CLEAN)]
    unclear = [{"id": f"u{i}", "ref": s, "in": garble(s, rng, keep=(j,)), "junk": j} for i, (s, j) in enumerate(UNCLEAR)]
    return test, unclear

if __name__ == "__main__":
    t, u = build()
    def changed(a, b): return sum(x != y for x, y in zip(a.split(), b.split())) / max(len(b.split()), 1)
    print("share of words position-changed (rough):", round(sum(changed(x["in"], x["ref"]) for x in t)/len(t), 2))
    for x in t[:8] + u[:2]: print(x["in"], "\n  ->", x["ref"])
