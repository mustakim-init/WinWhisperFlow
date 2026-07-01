# Troubleshooting

## Setup fails or gets stuck

The first-run setup downloads a local Python runtime and a starter model. If it fails:

1. **Check your internet connection** — setup requires network access; normal use afterward does not.
2. **Check disk space** — you need a few GB free for the runtime and model.
3. **Antivirus/endpoint protection** — some antivirus tools quarantine or block the bundled Python interpreter or its downloaded packages. Temporarily allow WinWhisper Flow, or add an exclusion for `%LOCALAPPDATA%\WinWhisperFlow`.
4. **Retry** — setup steps are individually re-runnable; restarting the app will resume from where it left off rather than starting over.
5. If it still fails, check `%LOCALAPPDATA%\WinWhisperFlow\winwhisper.log` for the specific error and include it when [filing an issue](https://github.com/mustakim-init/WinWhisperFlow/issues/new).

## Hotkey doesn't trigger recording

- Confirm the hotkey shown in **Settings → Captures** matches what you're pressing.
- Some hotkey combinations are intercepted by other apps (game overlays, other dictation/hotkey tools, remote-desktop clients) before they reach WinWhisper Flow. Try a different combination.
- If you're using a remote desktop or virtual machine, global keyboard hooks may not pass through — this is a Windows limitation, not specific to WinWhisper Flow.
- Restart the app — the hotkey hook is installed at startup.

## GPU not detected / running on CPU unexpectedly

- Open **Settings → Hardware** to see what WinWhisper Flow detected.
- **NVIDIA users:** make sure you have a recent NVIDIA driver installed (CUDA support comes from the driver, not a separate CUDA toolkit install).
- **AMD/Intel users:** DirectML acceleration requires a DirectX 12-capable GPU and an up-to-date graphics driver.
- If you recently updated your graphics driver, restart WinWhisper Flow so hardware detection re-runs.
- Running on CPU is not a bug — it's the automatic fallback when no compatible GPU is found. It just means transcription will be slower, especially with larger models.

## Transcription is slow

- Try a smaller model (`tiny`/`base`/`small`) — see the [model comparison table](configuration.md#models).
- Confirm you're actually using GPU acceleration if you have a compatible GPU (**Settings → Hardware**).
- Very long files or high beam-size settings increase processing time — see [Configuration Reference](configuration.md#transcription-advanced-tuning).
- Background CPU/GPU load from other apps (games, other AI tools) competes for the same resources.

## Transcription accuracy is poor

- Try a larger model — `small` is a good baseline, but `medium`/`large-v3` are noticeably more accurate for accents, background noise, or technical vocabulary.
- Add frequently used names/jargon as **hotwords** (**Settings → Transcription**).
- Reduce background noise and use a closer/better microphone if possible.
- For music/lyrics, make sure you're using the dedicated music transcription mode, not standard dictation — see the [User Guide](user-guide.md#transcribing-music-or-vocals).

## Auto-paste doesn't work in a specific app

- Some applications (elevated/administrator windows, certain games, some secure input fields like password boxes) block simulated keyboard input from other apps for security reasons — this is a Windows-level restriction, not something WinWhisper Flow can bypass.
- Try running WinWhisper Flow as administrator if you specifically need to dictate into an elevated application.
- As a fallback, the transcript is always available in **Captures** to copy manually.

## Phone mic won't connect

See the dedicated [Phone Mic Setup guide](phone-mic.md#troubleshooting) for connection-specific issues.

## App won't start / crashes on launch

1. Make sure you're on Windows 11, build 19041 or newer.
2. Make sure the **WebView2 Runtime** is installed (it ships with most modern Windows 11 installs, but you can install it manually from [Microsoft's WebView2 page](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) if needed).
3. Check `%LOCALAPPDATA%\WinWhisperFlow\winwhisper.log` for a stack trace or error.
4. Try a clean reinstall: uninstall, delete `%LOCALAPPDATA%\WinWhisperFlow`, then reinstall from the latest release.

## Still stuck?

[Open an issue](https://github.com/mustakim-init/WinWhisperFlow/issues/new) with:

- Your Windows version (`winver`)
- Your GPU (if any) and driver version
- The relevant section of `winwhisper.log`
- Steps to reproduce the problem
