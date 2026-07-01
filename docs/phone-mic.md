# Phone Mic Setup

Phone Mic lets you use your smartphone as a wireless microphone for WinWhisper Flow — useful if your PC's built-in mic is poor, your desk mic is inconvenient to use, or you want to dictate from across the room.

No app install is required on your phone — it works through your phone's browser.

## How it works

WinWhisper Flow starts a small local web server on your PC (over HTTPS, using a self-signed certificate generated on your device). Your phone connects to that server **over your local Wi-Fi network** and streams microphone audio to it, which is then transcribed exactly like your PC's own microphone.

Nothing leaves your local network — see [Privacy & Security](privacy-and-security.md).

## Requirements

- Your PC and your phone must be on the **same Wi-Fi network**.
- Your phone needs a modern browser (Safari on iOS, Chrome on Android).
- Your PC's firewall must allow local connections on the phone-mic port (WinWhisper Flow requests this automatically on first use).

## Connecting your phone

1. Open WinWhisper Flow and go to the **Phone Mic** page.
2. A QR code and a local URL (something like `https://192.168.x.x:8766`) will be shown.
3. On your phone, scan the QR code with your camera app, or type the URL into your phone's browser.
4. Your phone will warn that the site's certificate isn't trusted — this is expected, since the certificate is generated locally on your PC rather than issued by a public certificate authority. Choose **Advanced → Proceed** (wording varies by browser).
5. Allow microphone access when prompted.
6. Your phone is now paired. Recording starts and stops from the WinWhisper Flow app on your PC (using your hotkey or the on-screen controls) — your phone just streams audio.

## Why the "not secure" warning?

Browsers flag any HTTPS certificate that wasn't issued by a recognized public authority, even if the connection is otherwise fully encrypted. WinWhisper Flow generates its own certificate locally so that the connection between your phone and PC is encrypted **without** requiring a public domain or internet-issued certificate — appropriate for a local-network-only tool. You can safely proceed past this warning since the connection stays entirely within your Wi-Fi network.

## Troubleshooting

| Problem | Fix |
|---|---|
| QR code won't load / times out | Confirm both devices are on the same Wi-Fi network (not one on Wi-Fi and one on mobile data, and not a "guest" network that isolates devices from each other). |
| Phone shows a certificate warning and won't let you proceed | Some browsers require you to tap "Show details" or "Advanced" to reveal the "proceed anyway" option — it's there, just collapsed by default. |
| Microphone permission denied | Check your phone's browser has microphone permission in your phone's OS settings, not just the in-page prompt. |
| Connection drops mid-recording | Move closer to your router, or check nothing on your network (VPN, firewall, router client isolation) is blocking device-to-device traffic. |
| Windows Firewall blocked the connection | Allow WinWhisper Flow through the firewall when prompted, or add a manual rule for the port shown on the Phone Mic page (default `8766`). |

More general issues: see the full [Troubleshooting guide](troubleshooting.md).
