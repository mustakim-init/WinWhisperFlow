# Frequently Asked Questions

**Is WinWhisper Flow really fully offline?**
Yes, after the one-time first-run setup (which downloads the local runtime and a starter model), transcription runs entirely on your device with no network calls. See [Privacy & Security](privacy-and-security.md) for details.

**Does it require an account or sign-in?**
No. There's no account, license key entry for the free tier, or sign-in of any kind for non-commercial use.

**Is it free?**
Yes, for non-commercial/personal use. Commercial use requires a paid license — see [LICENSE](../LICENSE) and the [main README](../README.md#license) for pricing tiers.

**What languages does it support?**
WinWhisper Flow uses OpenAI's Whisper models, which support dozens of languages. Accuracy varies by language and model size — larger models generally handle non-English languages better. Set your default language in **Settings → General**.

**Which model should I use?**
`small` is a good default balance of speed and accuracy. If you have a decent GPU, `medium` or `large-v3` will give noticeably better accuracy. See the full [model comparison](configuration.md#models).

**Does it work on Windows 10?**
It's not officially supported — the app targets Windows 11 APIs (build 19041+). It may partially work on Windows 10 but this isn't tested or guaranteed.

**Does it work on macOS or Linux?**
No, WinWhisper Flow is Windows-only (WPF + WebView2 + Windows-specific audio/hotkey APIs).

**How is this different from Windows' built-in Voice Access / dictation?**
WinWhisper Flow uses Whisper-family models, which are generally more accurate — especially for accents, background noise, and non-dictation phrasing — than Windows' built-in speech engine, and gives you control over model size, tuning, and where models are stored.

**How is this different from Wispr Flow or other cloud dictation tools?**
The core difference is that transcription happens **entirely locally** — no audio or text is sent to a third-party server. This means no subscription is required for the underlying transcription, and it works without an internet connection (after initial setup).

**Can I use my phone as a microphone?**
Yes — see [Phone Mic Setup](phone-mic.md). It works over your local Wi-Fi network with no app install required.

**Can I transcribe song lyrics?**
Yes, WinWhisper Flow includes a music transcription mode that uses Demucs to isolate vocals before transcribing. See [User Guide → Transcribing music or vocals](user-guide.md#transcribing-music-or-vocals).

**Where are my transcripts stored?**
Locally, in your history (**Captures** page) and in `%LOCALAPPDATA%\WinWhisperFlow`. Nothing is uploaded anywhere.

**Can I change the keyboard shortcut?**
Yes — **Settings → Captures → Keyboard shortcut**. Default is `Ctrl + Alt + S`.

**Why does the phone mic show a "not secure" warning in my browser?**
This is expected — see [Phone Mic Setup → Why the "not secure" warning?](phone-mic.md#why-the-not-secure-warning). The connection is still encrypted; the warning is only because the certificate is self-signed rather than issued by a public authority, which is normal for a local-network-only tool.

**Is my microphone audio ever saved to disk permanently?**
Temporary audio buffers are used during processing on your device; they are not uploaded anywhere. See [Privacy & Security](privacy-and-security.md) for exactly what's written to disk.

**Can I build it myself instead of using the installer?**
Yes — see [Building from Source](building-from-source.md).

**I have a question that's not answered here.**
[Open an issue](https://github.com/mustakim-init/WinWhisperFlow/issues/new) or check [Troubleshooting](troubleshooting.md).
