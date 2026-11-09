"""Experiment: does a small language model repair the writer's typing better, and does it leave their words alone?
python llmtest.py <hugging face model name>
Runs on the PC only. Prints totals; per-message output goes to ../private/llm_<name>.txt, never committed."""
import json, re, sys, time
import numpy as np, onnxruntime as ort, torch
from transformers import AutoModelForCausalLM, AutoTokenizer
from samples import PROTECTED
from train import enc, dec, BOS, EOS, wordpct, toks

NAME = sys.argv[1]
SHORT = NAME.split("/")[-1]
torch.cuda.set_per_process_memory_fraction(0.3)     # a third of the 32 GB card at most
tok = AutoTokenizer.from_pretrained(NAME)
model = AutoModelForCausalLM.from_pretrained(NAME, torch_dtype=torch.float16, device_map={"": 0}).eval()
print("model loaded", SHORT, "graphics memory GB:", round(torch.cuda.memory_allocated() / 1e9, 2), flush=True)

o = ort.SessionOptions(); o.intra_op_num_threads = 4; o.inter_op_num_threads = 1
E = ort.InferenceSession("../model/enc.q.onnx", o); D = ort.InferenceSession("../model/dec.q.onnx", o)
def first_pass(p):
    m = E.run(None, {"x": np.array([enc(p)], dtype=np.int64)})[0]
    y = [BOS]
    for _ in range(len(p) + 6 + len(p) // 4):
        s = D.run(None, {"y": np.array([y], dtype=np.int64), "m": m})[0][0]
        b = int(s.argmax())
        if b == EOS: return dec(y)
        y.append(b)
    return p

SYSTEM = ("You repair typing for a writer whose disability affects typing. Letters often land after the next space, "
          "get swapped, dropped or doubled, and words get spelled by sound. Write the sentence they meant.\n"
          "Rules: keep their exact words, slang, swearing, adult content, grammar, lower case and missing punctuation. "
          "Fix only typing and spelling. Never add, remove, censor, soften, explain or rephrase anything. "
          "If the text is already fine, return it unchanged. Output only the repaired text.")
# made-up examples, none are the writer's messages
SHOTS = [("i wil lgo tot he shpo tomrow an dbuy som emilk", "i will go to the shop tomorrow and buy some mile", "i will go to the shop tomorrow and buy some milk"),
         ("fuc kthat bos sh eis suc ha bitc hlol", "fuck that bus she is such a bitch lol", "fuck that boss she is such a bitch lol"),
         ("she told me witch one to pick an di dint no wat too sya", "she told me witch one to pick and i dint no wat too say", "she told me which one to pick and i didnt know what to say"),
         ("ok see you later", "ok see you later", "ok see you later")]
def ask(text, guess=None):
    def fmt(typed, g): return f"Typed: {typed}" + (f"\nFirst guess, may be wrong: {g}" if g is not None else "")
    msgs = [{"role": "system", "content": SYSTEM}]
    for a, g, b in SHOTS:
        msgs += [{"role": "user", "content": fmt(a, g if guess is not None else None)}, {"role": "assistant", "content": b}]
    msgs.append({"role": "user", "content": fmt(text, guess)})
    prompt = tok.apply_chat_template(msgs, tokenize=False, add_generation_prompt=True, enable_thinking=False)
    ids = tok(prompt, return_tensors="pt").to(model.device)
    with torch.no_grad():
        out = model.generate(**ids, do_sample=False, temperature=None, top_p=None, top_k=None, max_new_tokens=len(text) // 2 + 30, pad_token_id=tok.eos_token_id)
    answer = tok.decode(out[0][ids["input_ids"].shape[1]:], skip_special_tokens=True)
    return re.sub(r"<think>.*?</think>", "", answer, flags=re.S).strip()

ev = json.load(open("../private/eval.json", encoding="utf8"))
GROUPS = ["test", "real", "real3", "real4"]
guess = {s["in"]: first_pass(s["in"].strip()) for g in GROUPS for s in ev[g]}
pct = lambda rows: round(wordpct(rows), 1)
print("current model alone:", {g: pct([(s["ref"], guess[s["in"]]) for s in ev[g]]) for g in GROUPS}, flush=True)
with open(f"../private/llm_{SHORT}.txt", "w", encoding="utf8") as f:
    for mode, use_guess in (("language model alone", False), ("second step after current model", True)):
        results, times = {}, []
        for g in GROUPS:
            rows = []
            for s in ev[g]:
                t = time.perf_counter(); a = ask(s["in"].strip(), guess[s["in"]] if use_guess else None); times.append(time.perf_counter() - t)
                rows.append((s["ref"], a))
                if g != "test": f.write(f"[{mode}]\nIN  {s['in']}\nOUT {a}\nREF {s['ref']}\n\n")
            results[g] = rows
        print(mode, {g: pct(r) for g, r in results.items()}, flush=True)
        extra = [len(toks(a)) - len(toks(r)) for rows in results.values() for r, a in rows]
        print("  messages with 3+ words more than intended:", sum(e >= 3 for e in extra), "fewer:", sum(e <= -3 for e in extra), "of", len(extra), flush=True)
        refusal = re.compile(r"\b(sorry|i can.?t|cannot help|as an ai|inappropriate|typed:)\b", re.I)
        print("  looks refused or chatty:", sum(bool(refusal.search(a)) for rows in results.values() for r, a in rows), flush=True)
        kept = total = 0
        for r, a in results["test"]:
            for w in set(toks(r)) & PROTECTED:
                total += 1; kept += w in toks(a)
        print(f"  slang, swearing, adult words kept: {kept}/{total}", flush=True)
        same = sum(ask(s["ref"], s["ref"] if use_guess else None) == s["ref"] for s in ev["test"])
        print(f"  clean text unchanged: {same}/40", flush=True)
        times.sort(); print(f"  seconds per message on the RTX 5090: median {times[len(times)//2]:.2f}, worst {times[-1]:.2f}", flush=True)
