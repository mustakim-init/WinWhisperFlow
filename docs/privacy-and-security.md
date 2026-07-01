# Privacy & Security

WinWhisper Flow was built specifically as a **local, private alternative** to cloud dictation tools. This page explains exactly what happens to your data and when (if ever) the app talks to the network.

## Core principle

**Speech-to-text processing happens entirely on your own machine.** Your microphone audio, phone-mic audio, and transcribed text are never uploaded to any server for the purpose of transcription. There is no telemetry, analytics, or usage tracking built into the app.

## When WinWhisper Flow does use the network

The app is not network-isolated by design — a few specific, limited actions do require internet access:

| Action | What it contacts | Why |
|---|---|---|
| First-run setup | Model repositories (Hugging Face) | To download the local speech-recognition runtime and Whisper model files |
| Downloading additional/different models | Model repositories (Hugging Face) | Same as above, on demand |
| Update check | GitHub's public API (`api.github.com`) | To check whether a newer release is available |

None of these actions transmit your audio, transcripts, microphone data, or personal information — they only fetch public model files or version metadata. You can decline to run setup or check for updates and use the app fully offline once models are downloaded.

## What is stored locally

| Data | Where | Notes |
|---|---|---|
| Settings (hotkey, theme, transcription tuning) | `%LOCALAPPDATA%\WinWhisperFlow\settings.json` | Plain local JSON file |
| Transcript history | Local app storage | Only on your device; deletable from the **Captures** page |
| Downloaded models | `%LOCALAPPDATA%\WinWhisperFlow\runtime\models` (configurable) | Public, pre-trained Whisper model weights — not personal data |
| Application log | `%LOCALAPPDATA%\WinWhisperFlow\winwhisper.log` | Diagnostic log for troubleshooting; you choose what to share when filing a bug report |
| Temporary audio buffers | Local temp storage during active transcription | Used transiently during processing; not part of the permanent history unless the transcription itself is saved |

## Phone Mic and your local network

The [Phone Mic](phone-mic.md) feature streams audio from your phone to your PC **over your local Wi-Fi network only**, using a self-signed HTTPS connection. It does not route through any external server, relay, or cloud service — your phone and PC communicate directly, so both devices must be on the same network.

## Microphone and system permissions

WinWhisper Flow requests:

- **Microphone access**, to capture audio for dictation.
- **Global keyboard hook**, to detect your dictation hotkey system-wide (this is required for a hotkey to work outside the app's own window — it does not log or transmit unrelated keystrokes).
- **Local network / firewall access**, for the Phone Mic feature.

## Open source

WinWhisper Flow's source code is publicly available on [GitHub](https://github.com/mustakim-init/WinWhisperFlow), so its data handling can be independently reviewed rather than taken on faith. If you find a privacy or security concern, please [open an issue](https://github.com/mustakim-init/WinWhisperFlow/issues/new) or, for sensitive reports, contact **mioact2smart@gmail.com** directly.

## Related

- [Configuration Reference → Storage](configuration.md#storage) — changing where models and data are stored.
- [FAQ](faq.md) — quick privacy-related answers.
