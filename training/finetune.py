"""v3 fine-tune (2026-11-14): continues from data/final.pt with the v3 training text (train3.sample).
python finetune.py <total minutes>      Resumable: run the same command again after a stop.
Gentler than run_training.py, so a video can play smoothly beside it: GPU memory capped, a long rest after
every step (the GPU works about two thirds of the time), low CPU priority, a pause when the GPU runs hot.
Create data/PAUSE to pause (delete it to go on). Writes data/ft_final.pt; data/final.pt is never touched."""
import ctypes, json, math, os, random, sys, time
import torch, torch.nn.functional as F
from train import enc, PAD, BOS, EOS, V
from train2 import Net2, evaluate
import importlib
sample = importlib.import_module(sys.argv[5] if len(sys.argv) > 5 else "train3").sample   # training text: train3 (v3) or train4 (v4)

# python finetune.py <minutes> [<tag> <start weights> <base lr> <text module>]: v3 was "finetune.py 75 ft3 data/final.pt 1.5e-4",
# v4 "finetune.py 60 ft4 data/ft3_final.pt 1e-4 train4"
TAG = sys.argv[2] if len(sys.argv) > 2 else "ft"
CKPT, BACKUP, PAUSE = f"data/{TAG}_ckpt.pt", f"data/{TAG}_ckpt.bak.pt", "data/PAUSE"
START = sys.argv[3] if len(sys.argv) > 3 else "data/final.pt"
OUT = f"data/{TAG}_final.pt"
SAVE_EVERY_S, EVAL_EVERY_STEPS = 300, 4000
BATCH_BY_JOIN = {1: 192, 2: 72, 3: 36}
GPU_MEMORY_SHARE = 0.55
BASE_LR, WARMUP_S, REST_FRACTION, HOT_C, RESUME_C = float(sys.argv[4]) if len(sys.argv) > 4 else 1.5e-4, 60, 0.5, 78, 70

def gpu_temp():
    try: return torch.cuda.temperature()
    except Exception: return 0

def save(state):
    tmp = CKPT + ".tmp"
    with open(tmp, "wb") as f:
        torch.save(state, f); f.flush(); os.fsync(f.fileno())
    if os.path.exists(CKPT): os.replace(CKPT, BACKUP)
    os.replace(tmp, CKPT)

def load():
    for path in (CKPT, BACKUP):
        if os.path.exists(path):
            try: return torch.load(path, map_location="cuda", weights_only=False)
            except Exception as e: print("could not read", path, e, flush=True)
    return None

def main():
    minutes = float(sys.argv[1]); end = minutes * 60
    ctypes.windll.kernel32.SetPriorityClass(ctypes.windll.kernel32.GetCurrentProcess(), 0x4000)  # below normal
    ctypes.windll.kernel32.SetThreadExecutionState(0x80000001)
    torch.set_num_threads(2)
    lines = open("data/corpus.txt", encoding="utf8").read().split("\n")
    ev = json.load(open("../private/eval.json", encoding="utf8"))
    torch.cuda.set_per_process_memory_fraction(GPU_MEMORY_SHARE)
    dev = "cuda"; net = Net2().to(dev)
    opt = torch.optim.AdamW(net.parameters(), lr=BASE_LR, weight_decay=1e-4); scaler = torch.amp.GradScaler("cuda")
    r = random.Random(31); step = 0; trained = 0.0
    s = load()
    if s:
        net.load_state_dict(s["net"]); opt.load_state_dict(s["opt"]); scaler.load_state_dict(s["scaler"])
        step, trained = s["step"], s["trained"]; r.setstate(s["rng"])
        print(f"resumed at step {step}, {trained/60:.1f} min trained", flush=True)
    else:
        net.load_state_dict(torch.load(START, map_location=dev))
        print(f"start from {START}: {evaluate(net, ev, dev)}", flush=True)
    last_save = last_temp = time.time(); slice_start = time.time()
    while True:
        now = time.time(); done = trained + (now - slice_start)
        if done >= end: break
        if os.path.exists(PAUSE):
            print("paused", flush=True); paused = time.time()
            while os.path.exists(PAUSE): time.sleep(5)
            slice_start += time.time() - paused; print("going on", flush=True); continue
        for g in opt.param_groups:
            g["lr"] = BASE_LR * min(1, done / WARMUP_S) * (.03 + .97 * .5 * (1 + math.cos(math.pi * done / end)))
        join = r.choices([1, 2, 3], [.65, .25, .10])[0]; A, B = [], []
        for _ in range(BATCH_BY_JOIN[join]):
            c, typed = sample(lines, r, join)
            A.append(enc(typed)[:385]); B.append([BOS] + enc(c) + [EOS])
        la, lb = max(map(len, A)), max(map(len, B))
        x = torch.tensor([a + [PAD] * (la - len(a)) for a in A], device=dev)
        y = torch.tensor([b + [PAD] * (lb - len(b)) for b in B], device=dev)
        t0 = time.time()
        try:
            with torch.amp.autocast("cuda"):
                o = net.step(y[:, :-1], net.memory(x), x.eq(PAD))
                loss = F.cross_entropy(o.reshape(-1, V), y[:, 1:].reshape(-1), ignore_index=PAD, label_smoothing=.02)
            opt.zero_grad(set_to_none=True); scaler.scale(loss).backward(); scaler.unscale_(opt)
            torch.nn.utils.clip_grad_norm_(net.parameters(), 1.0); scaler.step(opt); scaler.update(); step += 1
        except torch.cuda.OutOfMemoryError:
            print("batch too large for the memory cap, skipped", flush=True)
            opt.zero_grad(set_to_none=True); del x, y; torch.cuda.empty_cache(); continue
        torch.cuda.synchronize()
        time.sleep((time.time() - t0) * REST_FRACTION)

        if time.time() - last_temp > 20:
            last_temp = time.time(); temp = gpu_temp()
            if temp >= HOT_C:
                print(f"GPU {temp}C, cooling down", flush=True); paused = time.time()
                while gpu_temp() > RESUME_C and time.time() - paused < 600: time.sleep(10)
                slice_start += time.time() - paused
        if step % EVAL_EVERY_STEPS == 0:
            print(f"{(trained + time.time() - slice_start)/60:6.1f} min step {step} loss {loss.item():.3f} gpu {gpu_temp()}C eval {evaluate(net, ev, dev)}", flush=True)
        if time.time() - last_save > SAVE_EVERY_S:
            trained += time.time() - slice_start; slice_start = time.time()
            save({"net": net.state_dict(), "opt": opt.state_dict(), "scaler": scaler.state_dict(), "step": step, "trained": trained, "rng": r.getstate()})
            last_save = time.time()
            print(f"saved at {trained/60:.1f} min, step {step}, loss {loss.item():.3f}, gpu {gpu_temp()}C", flush=True)
    trained += time.time() - slice_start
    print("final step", step, evaluate(net, ev, dev), flush=True)
    save({"net": net.state_dict(), "opt": opt.state_dict(), "scaler": scaler.state_dict(), "step": step, "trained": trained, "rng": r.getstate()})
    tmp = OUT + ".tmp"
    with open(tmp, "wb") as f:
        torch.save(net.state_dict(), f); f.flush(); os.fsync(f.fileno())
    os.replace(tmp, OUT); print("FINISHED", flush=True)

if __name__ == "__main__":
    main()
