# Building from Source

This guide is for contributors and anyone who wants to run or package WinWhisper Flow themselves instead of using the prebuilt installer.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| Windows | 11, build 19041+ | Required target platform |
| .NET SDK | 8.0+ | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Python | 3.10+ | Used for the local venv during development (the packaged app bundles its own runtime instead) |
| Node.js | 18+ | For the WebUI (React/Vite) |
| Inno Setup 6 | latest | Only needed if you're building the installer (`winget install JRSoftware.InnoSetup`) |

## Project layout

```
WinWhisperFlow/
├─ App.xaml(.cs)              # WPF application entry point
├─ MainWindow.xaml(.cs)       # Main window (hosts WebView2)
├─ OverlayWindow.xaml(.cs)    # Recording overlay shown during dictation
├─ Services/                  # C# backend: audio capture, hotkeys, IPC bridge,
│                              # phone mic server, model/runtime management, etc.
├─ WebUI/                     # React + TypeScript + Tailwind frontend
│  ├─ src/pages/               # App pages (Dictate, Captures, Phone Mic, Models...)
│  └─ src/pages/Settings/      # Settings sub-pages
├─ stt_engine/                 # Python speech-to-text sidecar
│  ├─ whisper_worker.py        # CPU worker (faster-whisper)
│  ├─ whisper_worker_gpu.py    # GPU worker (sherpa-onnx)
│  ├─ demucs_wrapper.py        # Vocal/source separation
│  └─ download_*.py            # Model download scripts
├─ installer/                  # Inno Setup installer script
└─ scripts/                    # PowerShell build/dev scripts
```

## Running from source

```powershell
git clone https://github.com/mustakim-init/WinWhisperFlow.git
cd WinWhisperFlow

# Sets up a Python venv, installs CPU + GPU + Demucs requirements,
# preloads the default model, and restores .NET packages.
.\scripts\setup.ps1

# Build the frontend once (the .NET build also triggers this automatically
# via an MSBuild target, but building it explicitly first is faster to iterate on).
cd WebUI
npm install
npm run build
cd ..

# Run
dotnet run
# or:
.\scripts\start.ps1
```

For frontend-only iteration, you can run the Vite dev server (`npm run dev` inside `WebUI/`) instead of rebuilding on every change, then point the WPF host at it per your local dev setup.

## Building a portable release

```powershell
.\scripts\publish.ps1
```

This builds the WebUI, bundles the Python runtime (`scripts/build-python.ps1`), and produces a self-contained `win-x64` publish output, zipped to:

```
artifacts\WinWhisperFlow-portable.zip
```

## Building the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
winget install JRSoftware.InnoSetup
.\scripts\build-installer.ps1
```

This runs `publish.ps1` first, then compiles `installer\WinWhisperFlow.iss` into a Windows installer under `artifacts\installer\`.

## Key services to know before contributing

| Service | Responsibility |
|---|---|
| `WhisperBridgeService` | Manages the Python STT sidecar process lifecycle and communication |
| `UIBridge` | Relays IPC messages between the WebView2 frontend and the .NET backend |
| `AudioCaptureService` | Captures microphone audio via NAudio |
| `GlobalHotkeyService` | Installs the low-level keyboard hook for the global dictation hotkey |
| `GpuDetectionService` | Detects CUDA/DirectML availability and picks a transcription backend |
| `PhoneMicService` | Runs the local HTTPS server + pairing flow for the phone mic feature |
| `RuntimeSetupService` | Drives the guided first-run setup (Python runtime + model install) |
| `SettingsStore` | Reads/writes `settings.json` and the speech/music transcription profiles |
| `TranscriptionHistory` | Manages the local transcript history shown in Captures |
| `UpdateService` | Checks GitHub Releases for newer versions |

## Running checks before opening a PR

```powershell
# Frontend
cd WebUI
npm run build

# Backend
cd ..
dotnet build
```

See [CONTRIBUTING.md](../CONTRIBUTING.md) for the full contribution workflow and current focus areas.
