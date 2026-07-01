# Installation Guide

## System requirements

| Requirement | Minimum | Recommended |
|---|---|---|
| OS | Windows 11, build 19041 or newer | Latest Windows 11 |
| CPU | Any 64-bit x86 CPU | 6+ cores for smooth CPU transcription |
| RAM | 8 GB | 16 GB (for `medium` / `large-v3` models) |
| Disk space | ~2 GB free | 6+ GB free if you plan to download multiple models |
| GPU (optional) | — | NVIDIA GPU (CUDA) or any DirectX 12-capable GPU (DirectML) for faster transcription |
| Network | Required only for first-run setup and model downloads | — |

> Windows 10 is not officially supported. The app targets `net8.0-windows10.0.19041.0` and relies on WebView2 and hotkey APIs available on Windows 11.

## Installing (recommended: installer)

1. Download the latest installer (`WinWhisperFlow-Setup-x.y.z.exe`) from the **[Releases page](https://github.com/mustakim-init/WinWhisperFlow/releases)**.
2. Run the installer and follow the prompts. No admin rights are required for a per-user install.
3. Launch WinWhisper Flow from the Start Menu.
4. On first launch, the app runs a **guided setup** (see below) — this is normal and only happens once.

If Windows SmartScreen flags the installer as "unrecognized," this is expected for a small, unsigned open-source project — click **More info → Run anyway**. You can verify the download against the checksum published on the release page.

## First-run setup

The first time you open WinWhisper Flow, it checks your system and prepares everything it needs:

1. **Hardware detection** — the app checks for an NVIDIA (CUDA) or DirectML-compatible GPU and picks the best available transcription backend, falling back to CPU if no GPU is found.
2. **Runtime setup** — a local, self-contained Python runtime is installed under your user profile (no system Python required, and it won't conflict with any Python you already have installed).
3. **Model download** — a default Whisper model (`small`) is downloaded so you can start dictating immediately. You can download additional or larger models later from the **Models** page.

This step requires an internet connection. Once setup finishes, WinWhisper Flow works **fully offline** — see [Privacy & Security](privacy-and-security.md).

If setup fails partway through, see [Troubleshooting → Setup fails or gets stuck](troubleshooting.md#setup-fails-or-gets-stuck).

## Updating

WinWhisper Flow checks for new versions and will let you know when an update is available. To update manually:

1. Download the latest installer from [Releases](https://github.com/mustakim-init/WinWhisperFlow/releases).
2. Run it — it installs over your existing version and keeps your settings, history, and downloaded models.

Your downloaded models are stored outside the app's install folder, so they are **not** re-downloaded when you update.

## Uninstalling

1. Open **Settings → Apps → Installed apps** in Windows.
2. Find **WinWhisper Flow** and choose **Uninstall**.

This removes the application files. To also remove your settings, history, and downloaded models, delete:

```
%LOCALAPPDATA%\WinWhisperFlow
```

## Where things are stored

| What | Location |
|---|---|
| App settings, hotkey, history | `%LOCALAPPDATA%\WinWhisperFlow\settings.json` |
| Application log | `%LOCALAPPDATA%\WinWhisperFlow\winwhisper.log` |
| Python runtime | `%LOCALAPPDATA%\WinWhisperFlow\runtime\python` |
| Downloaded models | `%LOCALAPPDATA%\WinWhisperFlow\runtime\models` (configurable — see [Configuration Reference](configuration.md#storage)) |

## Next steps

- [User Guide](user-guide.md) — learn the interface and start dictating.
- [Configuration Reference](configuration.md) — tune hotkeys, models, and transcription behavior.
