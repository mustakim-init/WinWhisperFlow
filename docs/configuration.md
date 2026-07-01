# Configuration Reference

All settings live under the **Settings** section of the app and are stored locally in:

```
%LOCALAPPDATA%\WinWhisperFlow\settings.json
```

## General

| Setting | Description | Default |
|---|---|---|
| Theme | Light or dark appearance | Dark |
| Sound effects | Plays a sound when recording starts/stops/errors | On |
| Start on boot | Launches WinWhisper Flow automatically at Windows login | Off |
| Language | Default language hint passed to the speech model | English (`en`) |

## Captures (hotkey & dictation)

| Setting | Description | Default |
|---|---|---|
| Keyboard shortcut | Global hotkey combination to start/stop dictation. Click to open the chord picker and record a new combination (e.g. `Ctrl+Alt+D`). | `Ctrl + Alt + S` |
| Auto-paste | Automatically pastes the finished transcript into your focused application | On |
| Dictation model | Which downloaded Whisper model is used for live dictation | `small` |

> The hotkey is a single global chord (not push-to-talk) — press once to start recording, press the same combination again to stop.

## Audio

| Setting | Description |
|---|---|
| Input device | Which microphone WinWhisper Flow captures from. Lists all Windows recording devices. |

## Models

WinWhisper Flow uses OpenAI's Whisper model family, run locally via `faster-whisper` (CPU) or `sherpa-onnx` (GPU). You can download and switch between multiple sizes:

| Model | Relative speed | Relative accuracy | Notes |
|---|---|---|---|
| `tiny` | Fastest | Lowest | Good for quick drafts or low-power hardware |
| `base` | Fast | Fair | Decent for clear English speech |
| `small` | Balanced | Good | **Recommended default** — good speed/accuracy tradeoff on CPU or GPU |
| `medium` | Slower | Better | Noticeably more accurate; benefits a lot from GPU |
| `large-v3` | Slowest | Best | Maximum accuracy; may be too slow on CPU-only systems |
| `turbo` | Fast (GPU) | High | Fast **and** accurate, but GPU-recommended — heavy on CPU |

Models are downloaded on demand from the **Models** page and can be paused, resumed, or cancelled mid-download. Only downloaded models can be selected for dictation or file transcription.

### GPU acceleration

On startup, WinWhisper Flow checks your hardware and picks the best backend automatically:

1. **NVIDIA GPU with CUDA** → uses `sherpa-onnx` with CUDA acceleration.
2. **Any DirectX 12-capable GPU** (AMD, Intel, or NVIDIA without CUDA) → uses `sherpa-onnx` with DirectML.
3. **No compatible GPU** → falls back to `faster-whisper` on CPU.

You can see which backend is active on the **Hardware** settings page.

## Transcription (advanced tuning)

These settings control the underlying Whisper decoding behavior. Defaults are tuned to work well out of the box — most people won't need to touch these. WinWhisper Flow keeps **two independent profiles**: one for normal speech dictation, one for music/vocal transcription (which uses different defaults, since lyrics behave very differently from speech).

| Setting | Description | Speech default | Music default |
|---|---|---|---|
| Beam size | Number of candidate transcriptions considered per segment. Higher = more accurate, slower. | 1 | 5 |
| Best of | Number of candidate samples generated when using sampling. | 5 | 5 |
| Temperature | Randomness in decoding; `0` is fully deterministic. | 0 | 0 |
| VAD filter | Voice-activity detection — skips silent segments. | Off | Off |
| No-speech threshold | Confidence threshold above which a segment is treated as silence. | 0.45 | 0.6 |
| Log-prob threshold | Minimum average log-probability for a segment to be kept. | -0.8 | -1.0 |
| Repetition penalty | Discourages the model from repeating phrases. | 1.0 | 1.2 |
| No-repeat n-gram size | Blocks repeated n-grams of this size outright (`0` disables). | 0 | 3 |
| Length penalty | Adjusts preference for longer vs. shorter output. | 1.0 | 1.0 |
| Compression ratio threshold | Flags/discards segments that look like repetitive gibberish. | 2.4 | 2.4 |
| Condition on previous text | Whether each segment considers prior text as context. | On | Off |
| Hallucination silence threshold | Seconds of silence after which the model resets to avoid hallucinated text. | 0 | 2 |
| Hotwords | Custom words/phrases to bias recognition toward (names, jargon, product terms). | — | — |

If you're not sure what to change, start with **hotwords** (biggest impact for names/jargon) before touching decoding parameters.

## Storage

| Setting | Description | Default |
|---|---|---|
| Model directory | Where downloaded models are stored on disk. Change this if you're low on space on your system drive. | `%LOCALAPPDATA%\WinWhisperFlow\runtime\models` |

## Logs

The **Logs** page shows recent application log output, useful when reporting a bug. The full log file is also available directly at:

```
%LOCALAPPDATA%\WinWhisperFlow\winwhisper.log
```

Attach this file (or the relevant excerpt) when [filing an issue](https://github.com/mustakim-init/WinWhisperFlow/issues/new).

## Resetting settings

To reset everything to defaults, close WinWhisper Flow and delete:

```
%LOCALAPPDATA%\WinWhisperFlow\settings.json
```

This does **not** delete your downloaded models or transcript history.
