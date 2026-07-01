# User Guide

This guide covers everyday use of WinWhisper Flow once it's installed and set up.

## The interface

WinWhisper Flow's window is organized into a few main areas, accessible from the sidebar:

| Section | Purpose |
|---|---|
| **Dictate** | The main live-dictation view. Shows recording status and live partial transcription. |
| **Captures** | A searchable history of everything you've transcribed — from your mic, your phone, or a file. |
| **Phone Mic** | Pair your phone as a wireless microphone. |
| **Models** | Download, switch between, and manage Whisper models. |
| **Settings** | General, Audio, Captures (hotkeys), Transcription, Models, Hardware, Storage, and Logs. |

## Live dictation

1. Press your dictation hotkey (default: **`Ctrl + Alt + S`**) from anywhere in Windows — you don't need WinWhisper Flow focused.
2. A small overlay appears indicating that WinWhisper Flow is listening.
3. Speak naturally. Partial text appears as you talk.
4. Press the hotkey again to stop. The final transcription is typed into whatever text field or application you had focused (if **auto-paste** is enabled), and saved to your **Captures** history.

**Tips for best accuracy:**
- Use a decent microphone and reduce background noise where possible.
- Speak at a natural pace — pausing briefly between sentences helps the model punctuate correctly.
- If you dictate a lot of names, jargon, or acronyms, add them as **hotwords** in **Settings → Transcription** to bias recognition toward them.
- If transcription quality feels off for your voice/accent/language, try a larger model (see [Configuration Reference → Models](configuration.md#models)).

If nothing happens when you press the hotkey, see [Troubleshooting → Hotkey doesn't trigger recording](troubleshooting.md#hotkey-doesnt-trigger-recording).

## Transcribing a file

Use this when you have an existing audio or video file (interview, meeting recording, voice memo, etc.) you want transcribed:

1. Go to the file transcription option from the Dictate/Captures area (drag-and-drop or file picker).
2. Select your audio/video file. Most common formats are supported (the app uses FFmpeg internally to decode).
3. Transcription runs locally with progress shown; you can cancel at any time.
4. The result appears as an editable transcript and is saved to your history.

Longer files and larger models take longer, especially on CPU — see [Configuration Reference → Models](configuration.md#models) for a speed/accuracy comparison.

## Transcribing music or vocals

Standard speech models struggle with singing over instrumentation. WinWhisper Flow can run a **Demucs** source-separation pass first to isolate vocals before transcribing:

1. Select a music file for transcription.
2. Enable/select the music transcription mode (this uses a dedicated transcription profile tuned for lyrics — different defaults for repetition handling and silence detection than plain speech).
3. The app separates vocals from the instrumental track, then runs speech-to-text on the isolated vocals.

This is more CPU/GPU-intensive than normal dictation and takes noticeably longer, especially for longer songs.

## History (Captures)

Every dictation — from your microphone, your phone, or a file — is saved to **Captures**:

- **Search** your transcript history by text.
- **Copy** any past transcript with one click.
- **Delete** entries you no longer need.
- Each entry shows its **source** (mic, phone, or file) so you can tell them apart.

History is stored locally only — see [Privacy & Security](privacy-and-security.md).

## Editing transcripts

Speech recognition isn't perfect. You can edit any transcript directly in the text area before copying or after it's been captured — useful for fixing misheard names, technical terms, or punctuation.

## Dark mode

WinWhisper Flow ships with a dark theme by default. Toggle appearance in **Settings → General**.

## Using your phone as a mic

See the dedicated [Phone Mic Setup guide](phone-mic.md).

## Next steps

- [Configuration Reference](configuration.md) — customize hotkeys, models, audio input, and transcription tuning.
- [Troubleshooting](troubleshooting.md) — fix common issues.
