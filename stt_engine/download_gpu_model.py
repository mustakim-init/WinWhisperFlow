import os
import sys
import time
import urllib.request
from pathlib import Path

SHERPA_MODEL_MAP = {
    "tiny": "sherpa-onnx-whisper-tiny",
    "base": "sherpa-onnx-whisper-base",
    "small": "sherpa-onnx-whisper-small",
    "medium": "sherpa-onnx-whisper-medium",
    "large-v3": "sherpa-onnx-whisper-large-v3",
    "turbo": "sherpa-onnx-whisper-turbo",
}


_last_time: float | None = None
_last_bytes = 0


def progress_hook(block_num, block_size, total_size):
    global _last_time, _last_bytes
    now = time.time()
    downloaded = block_num * block_size
    downloaded_mb = downloaded / 1024 / 1024
    total_mb = total_size / 1024 / 1024

    if _last_time is None:
        _last_time = now
        _last_bytes = downloaded
        return

    if now - _last_time < 0.5 and downloaded < total_size:
        return

    elapsed = now - _last_time
    bytes_sec = (downloaded - _last_bytes) / elapsed if elapsed > 0 else 0
    speed = bytes_sec / 1024 / 1024

    if total_size > 0:
        pct = downloaded / total_size * 100
        eta_sec = (total_size - downloaded) / max(bytes_sec, 1)
        eta_min = int(eta_sec // 60)
        eta_sec = int(eta_sec % 60)
        eta_str = f"{eta_min}:{eta_sec:02d}"
    else:
        pct = 0
        eta_str = "?"

    bar_len = 40
    filled = int(bar_len * downloaded / max(total_size, 1))
    bar = "=" * filled + "-" * (bar_len - filled)

    print(
        f"\r[{bar}] {downloaded_mb:.1f}/{total_mb:.1f}MB ({pct:.1f}%)  "
        f"{speed:.1f}MB/s  ETA {eta_str}     ",
        end="",
        flush=True,
    )

    _last_time = now
    _last_bytes = downloaded


def _find_file(model_dir, pattern):
    matches = list(Path(model_dir).glob(pattern))
    return str(matches[0]) if matches else None


def urlretrieve_with_retry(url, dest_path, retries=3):
    for attempt in range(retries):
        try:
            urllib.request.urlretrieve(url, dest_path, reporthook=progress_hook)
            return
        except Exception as exc:
            if attempt < retries - 1:
                print(f"\n  Retry {attempt+1}/{retries}: {exc}")
                time.sleep(2)
            else:
                raise


def download_fp32_model(model_name, models_dir):
    sherpa_name = SHERPA_MODEL_MAP.get(model_name)
    if sherpa_name is None:
        raise ValueError(f"Unknown model: {model_name}")

    model_dir = Path(models_dir) / sherpa_name
    model_dir.mkdir(parents=True, exist_ok=True)
    prefix = model_name  # e.g. "turbo", "small"

    base_url = f"https://huggingface.co/csukuangfj/{sherpa_name}/resolve/main"

    mandatory = [
        f"{prefix}-encoder.onnx",
        f"{prefix}-decoder.onnx",
        f"{prefix}-tokens.txt",
    ]

    # encoder.weights only exists for large models (turbo, large-v3, medium)
    optional = [f"{prefix}-encoder.weights"]

    all_files = mandatory + optional

    print(f"Model:   {sherpa_name}")
    print(f"Hub:     {base_url}")
    print(f"Save to: {model_dir}")
    print()

    for fname in all_files:
        dest = model_dir / fname
        if dest.exists() and dest.stat().st_size > 0:
            print(f"  [cached] {fname} ({dest.stat().st_size / 1024 / 1024:.1f} MB)")
            continue

        print(f"  Downloading {fname}...")
        url = f"{base_url}/{fname}"
        try:
            urlretrieve_with_retry(url, str(dest))
        except Exception as exc:
            if fname in optional:
                print(f"\r  [skip] {fname} (not available: {exc})")
                continue
            raise
        size_mb = dest.stat().st_size / 1024 / 1024
        print(f"\r  [done] {fname} ({size_mb:.1f} MB)")

    # Verify we have encoder + decoder + tokens (weights are optional)
    if not _find_file(model_dir, f"{prefix}-encoder.onnx"):
        raise FileNotFoundError(f"Encoder model not found after download.")
    if not _find_file(model_dir, f"{prefix}-decoder.onnx"):
        raise FileNotFoundError(f"Decoder model not found after download.")
    if not _find_file(model_dir, f"{prefix}-tokens.txt"):
        raise FileNotFoundError(f"Tokens file not found after download.")

    print()
    print(f"  FP32 model ready. Total: {sum(f.stat().st_size for f in model_dir.glob('*')) / 1024 / 1024:.1f} MB")


def main():
    if len(sys.argv) < 2:
        print("Usage: download_gpu_model.py <model_name> [models_dir]")
        print(f"Supported: {', '.join(SHERPA_MODEL_MAP)}")
        input("\nPress Enter to close...")
        sys.exit(1)

    model_name = sys.argv[1]
    models_dir = sys.argv[2] if len(sys.argv) >= 3 else str(Path(__file__).parent / "models")
    os.environ["WINWHISPER_MODELS_DIR"] = models_dir

    print("=" * 60)
    print("  WinWhisperFlow - GPU Model Download (FP32 from Hugging Face)")
    print("=" * 60)
    print()

    try:
        download_fp32_model(model_name, models_dir)
        print()
        print("=" * 60)
        print("  Download complete! The model will now load on GPU.")
        print("=" * 60)
    except Exception as exc:
        print()
        print(f"ERROR: {exc}")
        input("\nPress Enter to close...")
        sys.exit(1)


if __name__ == "__main__":
    main()
