# WinWhisper Flow Documentation

Welcome to the WinWhisper Flow docs. WinWhisper Flow is a local, offline speech-to-text app for Windows 11 — this is the full documentation set for using, configuring, troubleshooting, and building it.

If you're new here, start with **Installation** and then the **User Guide**.

## For Users

| Guide | What it covers |
|---|---|
| [Installation](installation.md) | System requirements, installing from the setup wizard, first-run setup, updating, and uninstalling. |
| [User Guide](user-guide.md) | Day-to-day usage: live dictation, file transcription, music/vocal transcription, transcript history, and interface overview. |
| [Phone Mic Setup](phone-mic.md) | Using your phone as a wireless microphone over your local network. |
| [Configuration Reference](configuration.md) | Every setting in the app explained — hotkeys, models, audio devices, transcription tuning, storage locations. |
| [Troubleshooting](troubleshooting.md) | Fixes for the most common issues: setup failures, GPU not detected, hotkey not working, poor accuracy, phone mic connection problems. |
| [FAQ](faq.md) | Quick answers to common questions about privacy, models, languages, licensing, and system impact. |
| [Privacy & Security](privacy-and-security.md) | What data the app touches, what it stores locally, and what (if anything) leaves your machine. |

## For Developers

| Guide | What it covers |
|---|---|
| [Building from Source](building-from-source.md) | Setting up a dev environment, running from source, building the installer, and project layout. |
| [Contributing](../CONTRIBUTING.md) | How to submit changes and where help is most needed. |
| [Changelog](../CHANGELOG.md) | Version history and release notes. |

### Architecture at a glance

WinWhisper Flow is split into three layers that run as separate local processes on your machine:

1. **UI layer** — a React 19 + TypeScript frontend, rendered inside a WebView2 control hosted by a .NET 8 WPF window. This is what you see and interact with.
2. **Backend layer** — the .NET 8 host process. It captures audio (NAudio), listens for the global hotkey, manages the phone-mic HTTPS server, and relays IPC messages between the UI and the speech engine.
3. **Speech engine layer** — a Python sidecar process, invoked by the backend, that runs `faster-whisper` on CPU or `sherpa-onnx` on GPU (CUDA/DirectML), plus Demucs for vocal separation when transcribing music.

These layers only talk to each other over local IPC and local process I/O — nothing is sent to the internet during transcription. See [Privacy & Security](privacy-and-security.md) for details.

## Getting help

- Something not covered here? [Open an issue](https://github.com/mustakim-init/WinWhisperFlow/issues/new) on GitHub.
- Found a mistake in the docs? PRs to `/docs` are welcome — see [Contributing](../CONTRIBUTING.md).
