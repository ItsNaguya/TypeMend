# TypeMend measurement report (2026-11-09)

Measured on the exact compressed model files that ship. Figures are the share of words matching the intended sentence.

## Accuracy

| Test set | Unrepaired | Repaired | Fully correct messages |
|---|---|---|---|
| 40 made-up sentences | 43.9% | 95.6% | 30 of 40 |
| Real messages, first 5 | 51.9% | 96.3% | 3 of 5 |
| Real messages, newest 10 | 47.9% | 81.4% | 0 of 10 |
| Real messages, all 15 | 48.8% | 84.6% | 3 of 15 |

**Which number to trust:** the newest 10 real messages. The error patterns in training were modelled on the real messages, which flatters the first 5 most. The real messages themselves were never used as training data. The intended sentences are a human best guess, and some counted "errors" are harmless, such as "a bit" for "abit".

## Other measurements

- Slang, swearing and adult words kept: 20 of 23.
- Clean text left unchanged: 40 of 40. Short clean messages: 9 of 10.
- Marking guesses at the shipped threshold (0.9): catches 63% of wrong words and flags 3.8% of right ones.
- Made-up junk words kept as typed: 0 of 8. Unknown words get altered rather than copied.

## Cost

| | |
|---|---|
| Plugin download | 21.8 MB |
| Plugin memory, model loaded | 113 MB |
| Plugin, 2 CPU threads | median 0.85 s, longest message 4.7 s |
| Plugin, 1 CPU thread | median 1.3 s, longest message 7.7 s |
| Web page, WebAssembly, 4 threads | about 0.2 s for a one-sentence message |
| GPU use when running | none |

Speed was measured on an i7-14700KF. A Ryzen 9 5900X has not been tested. WebGPU was tried and rejected: 2-6x slower here, with a 4x larger download.

## Known errors

- Rare words get swapped for a similar common word. This can change the meaning, so these are the mistakes to watch for.
- Real-word typos need context the model doesn't have, such as "the enjoy" typed for "the emoji" across two messages.
- Game and product names not seen in training can get split or altered.

Training plateaued: over the last 100,000 of 144,000 steps the newest-10 score stayed between 79% and 83%. More of the same training is unlikely to help. The likely next gain is a personal word list for the writer.

## Not tested

- Nothing has run inside FFXIV yet: not the chat hook, the preview window, or loading ONNX Runtime in the game process.
- The web page has been tested locally only and is not deployed.
