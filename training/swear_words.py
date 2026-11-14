"""Builds the swear-word lists for the plugin (src/SwearWords.cs) and the web page (web/public/swearwords.js).

Two lists:
- WORDS: English and German swear words and chat swears kept exactly as typed (like the roleplay words).
- REAL: real English and German words one typo away from a swear word of 5+ letters. A typed word one typo
  away from such a swear word is repaired to it, unless it is one of these real words (so "witch" stays "witch").

python swear_words.py   (run from training/; needs data/corpus.txt and LibreOffice's en/de dictionaries)
"""
import collections, json, os, re, sys

ENGLISH = """
fuck fucks fucked fucker fuckers fucking fuckin fuckface fuckhead fuckwit fuckery motherfucker motherfuckers motherfucking
shit shitty shitting shithead shitshow shitload bullshit horseshit dipshit
damn damned dammit damnit goddamn goddamned goddamnit goddammit
bitch bitches bitchy bitching
asshole assholes dumbass jackass badass smartass
bastard bastards cunt cunts dick dicks dickhead dickheads cock cocks cocksucker
pussy pussies slut sluts slutty twat twats wanker wankers bollocks
crap crappy pissed pissing douche douchebag skank titties boobs
wtf stfu fml ffs omfg lmfao
""".split()

GERMAN = """
scheisse scheiße scheiss scheiß scheissegal scheißegal scheisskerl scheißkerl
verdammt verdammte verdammter verdammtes verdammich
mistkerl miststück arsch arschloch arschlöcher arschgeige arschkriecher
kacke kack kacken kackbratze fick ficken fickt gefickt verfickt verfickte verfickter
fotze huren hurensohn wichser wixer wichse schlampe drecksau dreckskerl
vollidiot trottel penner spasti missgeburt schwanz titten pimmel fresse hackfresse
verpiss verpisst pisse pisser dummkopf geil krass digga
""".split()

# Checked by hand: left out because the writer may type them as typos of common English words
# (hoe/how, tit/it, hell/hello, mist/must, hure/here, dumm/dumb, depp/deep, sau/say, leck/lack, spast/past),
# or one typical slip of this writer away from one (an added r/t/n/e/s, a dropped letter, a swapped vowel):
# ass/as, arse/are, prick/pick, tits/its, piss/pass, whore/where, shits/shots, wank/want. The model mostly keeps
# these already; on the list they could hide a real typo. The writer can still add them under Never change.
LEFT_OUT = "hoe tit hell mist hure dumm depp sau leck spast alter ass asses arse arsehole arseholes prick pricks tits piss pisses whore whores shits wank".split()

FUZZY_MIN = 5   # only swear words this long get typo repair; shorter ones are left to the model


def edits1(w):
    """All words one edit away: delete, swap neighbours, replace, insert (letters a-z and German ones)."""
    letters = "abcdefghijklmnopqrstuvwxyzäöüß"
    splits = [(w[:i], w[i:]) for i in range(len(w) + 1)]
    out = set()
    for a, b in splits:
        if b: out.add(a + b[1:])
        if len(b) > 1: out.add(a + b[1] + b[0] + b[2:])
        for c in letters:
            if b: out.add(a + c + b[1:])
            out.add(a + c + b)
    out.discard(w)
    return out


def hunspell_stems(path):
    words = set()
    with open(path, encoding="utf8", errors="ignore") as f:
        next(f)
        for line in f:
            stem = line.split("/")[0].strip().lower()
            if stem.isalpha(): words.add(stem)
    return words


def main():
    words = list(dict.fromkeys(ENGLISH + GERMAN))
    assert not set(words) & set(LEFT_OUT)
    lo = r"C:\Program Files\LibreOffice\share\extensions"
    en = hunspell_stems(os.path.join(lo, "dict-en", "en_US.dic")) | hunspell_stems(os.path.join(lo, "dict-en", "en_GB.dic"))
    de = hunspell_stems(os.path.join(lo, "dict-de", "de_DE_frami.dic"))
    # German stems with common endings, so inflected forms (Jacken, Wischers) count as real words too.
    de |= {s + e for s in de for e in ("e", "en", "er", "es", "n", "s", "em", "st", "t", "te")}
    counts = collections.Counter()
    with open("data/corpus.txt", encoding="utf8") as f:
        for line in f:
            counts.update(re.findall(r"[a-z]+", line.lower()))
    corpus = {w for w, c in counts.items() if c >= 2}
    real = en | de | corpus
    keep = {w.lower() for w in words}

    # Warn about kept words that are one typo away from a common English word (the writer could mean that word).
    common = [w for w, _ in counts.most_common(3000)]
    for w in words:
        if w in common: continue
        near = [c for c in common if c in edits1(w)] if len(w) <= 5 else []
        if near: print(f"note: {w} is one typo from {near[:5]}", file=sys.stderr)

    fuzzy = [w for w in words if len(w) >= FUZZY_MIN and w.isascii()]
    blocked = set()
    for w in fuzzy:
        for e in edits1(w):
            if e in real and e not in keep: blocked.add(e)
    blocked = sorted(blocked)
    print(f"{len(words)} kept words, {len(fuzzy)} with typo repair, {len(blocked)} real words protected", file=sys.stderr)

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    js_words, js_real = json.dumps(words, ensure_ascii=False), json.dumps(blocked, ensure_ascii=False)
    with open(os.path.join(root, "web", "public", "swearwords.js"), "w", encoding="utf8", newline="\n") as f:
        f.write("// Generated by training/swear_words.py; same lists as the plugin's SwearWords.cs. Do not edit by hand.\n")
        f.write(f"export const SWEAR_WORDS = {js_words};\n")
        f.write(f"export const SWEAR_MIN = {FUZZY_MIN};\n")
        f.write(f"export const REAL_NEAR_SWEARS = new Set({js_real});\n")
    cs = lambda xs: ", ".join(json.dumps(x, ensure_ascii=False) for x in xs)
    with open(os.path.join(root, "src", "SwearWords.cs"), "w", encoding="utf8", newline="\n") as f:
        f.write(f"""// Generated by training/swear_words.py; same lists as the web page's swearwords.js. Do not edit by hand.
namespace TypeMend;

public static partial class SwearWords
{{
    /// <summary>English and German swear words, kept exactly as typed.</summary>
    public static readonly string[] Words = {{ {cs(words)} }};

    /// <summary>Only swear words this long get their typos repaired.</summary>
    public const int MinLength = {FUZZY_MIN};

    /// <summary>Real English and German words one typo away from a swear word; these are never turned into one.</summary>
    public static readonly HashSet<string> RealNear = new(StringComparer.OrdinalIgnoreCase) {{ {cs(blocked)} }};
}}
""")


if __name__ == "__main__":
    main()
