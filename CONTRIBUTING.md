# Contributing to WinWhisper Flow

Thanks for your interest! This is a hobby project built from scratch, so all help is welcome.

## Getting Started

1. Fork the repo
2. Follow the [README](README.md) setup instructions
3. Create a branch: `git checkout -b feature/your-thing`
4. Make your changes
5. Run `npm run build` from `WebUI/` to verify the frontend compiles
6. Run `dotnet build` to verify the backend compiles
7. Push and open a Pull Request

## What Needs Help

- **Packaging** — Python PyInstaller builds are flaky
- **GPU support** — DirectML/CUDA model download paths
- **Demucs** — model loading and separation reliability
- **Phone mic** — cross-network streaming stability
- **Localization** — more languages for the UI
- **Bug fixes** — open an issue first

## Guidelines

- Keep it simple. This is a desktop tool, not a web service.
- No external API calls. Everything must run offline.
- Prefer existing patterns — look at similar files before writing new ones.
- Test your changes before opening a PR.

## Code of Conduct

Be respectful. This is a small project and everyone is learning.
