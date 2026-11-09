# TypeMend

Repairs typing, and nothing else, on the writer's own PC. It is built with a person in mind whose disability affects how they type. It keeps their meaning, slang, tone and adult content, marks words it is unsure about, and always shows what was typed next to the repair. Nothing is sent anywhere for processing.

Two front ends share one small model (22 MB, CPU only):

- **Dalamud plugin** (`src/`): catches a chat line when Enter is pressed and shows a preview. Enter sends the repair, "Send as typed" sends the original, Esc cancels. `/typemend` turns it on or off. Commands are left alone; only chat lines are repaired.
- **Web page** (`web/`): the fallback for patch days when plugins can't load. Paste, repair, copy. English and German.

## Folders

| Folder | What it is |
|---|---|
| `src/` | The plugin (.NET 10, Dalamud API 15) |
| `model/` | The model files the plugin ships |
| `web/` | The web page; `node web/serve.mjs` runs it locally on port 3021 |
| `tests/Harness/` | Runs the plugin's model and chat parsing code outside the game |
| `training/` | How the model was trained and measured |
| `private/` | Real test messages. Git-ignored and must stay out of any public place. |

## Build

```bash
dotnet build TypeMend.csproj -c Release
```

The install package is `bin/Release/TypeMend/latest.zip`. For a test install, add `bin/Release/TypeMend.dll` under Dalamud's Dev Plugin Locations.

## Web hosting

The server must send `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Embedder-Policy: require-corp` for multi-threaded speed. Without them the page still works on a single thread.

## Credits

Trained on sentences from [SODA](https://huggingface.co/datasets/allenai/soda) (Kim et al., Allen Institute for AI, CC BY 4.0). Runs on [ONNX Runtime](https://github.com/microsoft/onnxruntime) (MIT).

See `REPORT.md` for measured accuracy and known errors.
