# TypeMend measurement report (2026-11-09)

Measured on the exact compressed model files that ship. Figures are the share of words matching the intended sentence.

## Accuracy

| Test set | Unrepaired | Repaired | Fully correct messages |
|---|---|---|---|
| 40 made-up sentences | 43.9% | 95.6% | 30 of 40 |
| Real messages, first 5 | 51.9% | 96.3% | 3 of 5 |
| Real messages, newest 10 | 47.9% | 81.4% | 0 of 10 |
| Real messages, all 15 | 48.8% | 84.6% | 3 of 15 |
| 6 later real messages, added after training ended (128 words) | 53.8% | 89.1% | 1 of 6 |

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

Training plateaued: over the last 100,000 of 144,000 steps the newest-10 score stayed between 79% and 83%. More of the same training is unlikely to help.

## Word list experiment (not shipped)

A second look at doubtful words, choosing among similar words from a 25,558-word list by model score and closeness to the typed letters (`training/lexfix.py`). Settings were tuned without the newest 10 real messages, which were then scored once.

| | Current model | With second look |
|---|---|---|
| Real messages, newest 10 | 81.4% | 82.9% |
| 40 made-up sentences | 95.6% | 96.3% |
| Real messages, first 5 | 96.3% | 96.3% |
| 120 freshly garbled corpus sentences | 98.1% | 98.0% |
| Time per message, 4 threads | median about 0.5 s (estimated from the 2-thread figure) | median 2.5 s, longest 9.4 s |

The gain on the newest 10 is about 5 words out of 374, too small to tell from chance, at several times the cost. It was left out of the plugin and the web page. In those 10 messages, 12 of the 374 intended words are not on the list, so a personal word list could recover about 3 points at most. More real messages are the better next step.

## Not tested

- Nothing has run inside FFXIV yet: not the chat hook, the preview window, or loading ONNX Runtime in the game process.
- The wrong-channel safeguard has only had its decision logic tested outside the game. In game, check a line held in say, then a channel switch before confirming: it must refuse and keep the text. If the writer uses XIVInstantMessenger, also test a tell in Eureka or Bozja.
- Other plugins that send chat also pass through TypeMend. A message split in two by another plugin can arrive out of order while the first part waits in the preview.
- The web page is live at https://typemend.naguya.tech/ as an unlisted test (noindex, not on the NaguSuite page).
