# WinWhisper Flow

**Local speech-to-text for Windows 11.** A private, offline alternative to Wispr Flow — no internet needed, no data leaves your machine.

> ⚠️ **Work in progress.** This is built from scratch and may have rough edges. Contributions and feedback welcome.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Python](https://img.shields.io/badge/Python-3.10%2B-3776AB?logo=python)
![License](https://img.shields.io/badge/License-Dual--License-blue)
![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D4?logo=windows)

---

## Features

- **Completely offline** — uses `faster-whisper` locally, no cloud API calls
- **Voice recording** — real-time speech-to-text with live partial transcriptions
- **File transcription** — transcribe audio/video files with progress tracking
- **Music separation** — Demucs-powered vocal extraction for transcribing songs
- **Phone mic** — stream audio from your phone via local HTTP server + QR code
- **GPU acceleration** — auto-detects CUDA / DirectML, falls back to CPU
- **Editable transcripts** — fix mis-transcribed words in the text area
- **Dark mode** — built-in dark theme, respects system preference

---

## Quick Start

```powershell
# 1. Clone the repo
git clone https://github.com/mustakim-init/WinWhisperFlow.git
cd WinWhisperFlow

# 2. Run setup (creates venv, installs Python deps, restores .NET packages)
.\scripts\setup.ps1

# 3. Build the WebUI
cd WebUI
npm install
npm run build
cd ..

# 4. Run
dotnet run
```

### Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 8.0 or newer |
| Python | 3.10 or newer |
| Node.js | 18 or newer |
| Windows | 11 (build 19041+) |

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Alt+S` | Toggle recording |
| `Caps Lock` | Toggle recording (push-to-talk style) |
| `Esc` | Stop recording |

All shortcuts can be changed in Settings → Captures.

---

## Architecture Overview

```
┌─────────────────────────────────────────┐
│         WPF Host (WebView2)             │
│  ┌───────────────────────────────────┐  │
│  │   React UI (Tailwind + Vite)      │  │
│  │   ┌─────┬──────────┬──────────┐   │  │
│  │   │Voice│ File Txn │ Settings │   │  │
│  │   └──┬──┴──────────┴──────────┘   │  │
│  │      │ IPC (chrome.webview)        │  │
│  └──────┼────────────────────────────┘  │
└─────────┼───────────────────────────────┘
          │
┌─────────┴───────────────────────────────┐
│         C# Backend (.NET 8)             │
│  ┌──────────────────────────────────┐   │
│  │ NAudio (audio capture)           │   │
│  │ WhisperBridge (model management) │   │
│  │ UIBridge (IPC relay)             │   │
│  │ PhoneMicServer (HTTP + QR)       │   │
│  └──────────┬───────────────────────┘   │
└─────────────┼────────────────────────────┘
              │
┌─────────────┴────────────────────────────┐
│      Python Sidecar (faster-whisper)     │
│  CPU: standard faster-whisper            │
│  GPU: sherpa-onnx with DirectML/CUDA     │
│  Demucs: source separation (vocals)      │
└──────────────────────────────────────────┘
```

### Stack

| Layer | Technology |
|-------|------------|
| UI Framework | .NET 8 WPF + WebView2 |
| Frontend | React 18, TypeScript, Tailwind CSS v4, Vite 6 |
| Audio Capture | NAudio (16 kHz mono PCM) |
| STT Engine | `faster-whisper` (CPU), `sherpa-onnx` (GPU) |
| Separation | Demucs (PyTorch) |
| Phone Mic | Built-in HTTP server with QR pairing |
| Packaging | Inno Setup (installer), PyInstaller (Python) |

---

## Building for Distribution

```powershell
# Portable build (single folder with everything)
.\scripts\publish.ps1

# Installer (requires Inno Setup)
winget install JRSoftware.InnoSetup
.\scripts\build-installer.ps1
```

Outputs land in `artifacts/`.

---

## License

This project uses a **dual license**:

- **Non-Commercial use** — free (personal projects, hobby use)
- **Commercial use** — requires a paid license

See [LICENSE](LICENSE) for details. For commercial licensing inquiries: mioact2smart@gmail.com

---

## Credits

- [faster-whisper](https://github.com/SYSTRAN/faster-whisper) — STT engine
- [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) — GPU-accelerated inference
- [Demucs](https://github.com/facebookresearch/demucs) — source separation
- [NAudio](https://github.com/naudio/NAudio) — audio capture
- [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) — modern UI host
- [Voicebox](https://voicebox.moda) — design inspiration
