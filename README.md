# WinWhisper Flow

Professional-grade Windows 11 local speech-to-text application.

## Stack

- UI: .NET 8 WPF with a WebView2 frontend (Tailwind CSS, Material 3, Vite)
- Audio capture: NAudio, 16 kHz mono PCM
- STT engine: Python sidecar using `faster-whisper`
- Streaming: Pseudo-streaming with chunked partial transcriptions
- Packaging: Standalone Python `.exe` via PyInstaller
- Languages: Whisper multilingual models support English and Bangla
- Text injection: Unicode clipboard paste by default, `SendInput` Unicode method included
- Global hotkey: low-level Win32 keyboard hook for `Caps Lock` and `Ctrl+Alt+S`
- Autostart: current-user Windows Run registration
- Phone Mic: Built-in local HTTP server to stream audio from a mobile device with QR code pairing

## Setup

Install:

- .NET 8 SDK or newer
- Python 3.10 or newer
- Node.js (for WebUI development)
- Visual Studio 2022 with .NET desktop workload, or build from terminal

To set up the development environment:

```powershell
.\scripts\setup.ps1
```

Build and run for development:

```powershell
cd WebUI
npm install
npm run build
cd ..
dotnet run
```

## Creating a Portable Build

The publish script will compile the WebUI, package the Python STT engine into standalone `.exe` binaries using PyInstaller, and compile the .NET app.

```powershell
.\scripts\publish.ps1
```

The resulting files will be in `artifacts\publish\WinWhisperFlow`.

## Creating an Installer

Create a Windows installer after installing Inno Setup:

```powershell
winget install JRSoftware.InnoSetup
.\scripts\build-installer.ps1
```

## Model selection

Models can be dynamically loaded and configured via the Web UI's setting panel. GPU acceleration (CUDA/DirectML) is supported and autodetected, but can be managed from the UI.
