import json
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


def _load_file_sizes(repo_id, files):
    sizes = {}
    total = 0
    for f in files:
        url = f"https://huggingface.co/{repo_id}/resolve/main/{f}"
        req = urllib.request.Request(url, method="HEAD")
        try:
            with urllib.request.urlopen(req, timeout=15) as resp:
                raw = resp.headers.get("Content-Length")
                size = int(raw) if raw else 0
        except Exception:
            size = 0
        sizes[f] = size
        total += size
    return sizes, total


def main():
    if len(sys.argv) < 2:
        print("Usage: download_gpu_model.py <model_name> [models_dir]")
        print(f"Supported: {', '.join(SHERPA_MODEL_MAP)}")
        sys.exit(1)

    # Disable Xet Storage so hf_hub uses http_get with per-chunk progress updates
    os.environ["HF_HUB_DISABLE_XET"] = "1"

    model_name = sys.argv[1]
    models_dir = sys.argv[2] if len(sys.argv) >= 3 else str(Path(__file__).parent / "models")

    sherpa_name = SHERPA_MODEL_MAP.get(model_name)
    if sherpa_name is None:
        raise ValueError(f"Unknown model: {model_name}")

    repo_id = f"csukuangfj/{sherpa_name}"
    dest_dir = Path(models_dir) / sherpa_name
    os.makedirs(dest_dir, exist_ok=True)

    all_patterns = [
        f"{model_name}-encoder.onnx",
        f"{model_name}-decoder.onnx",
        f"{model_name}-encoder.int8.onnx",
        f"{model_name}-decoder.int8.onnx",
        f"{model_name}-tokens.txt",
        f"{model_name}-encoder.weights",
        f"{model_name}-decoder.weights",
    ]

    sizes, total_size = _load_file_sizes(repo_id, all_patterns)
    expected_files = [f for f in all_patterns if sizes.get(f, 0) > 0]
    expected_total = sum(sizes[f] for f in expected_files)

    if not expected_files:
        sys.stderr.write(
            json.dumps({"type": "error", "message": f"No files found for model '{model_name}' at {repo_id}"})
            + "\n"
        )
        sys.stderr.flush()
        sys.exit(1)

    print(f"Model:   {sherpa_name}")
    print(f"Hub:     https://huggingface.co/{repo_id}")
    print(f"Save to: {dest_dir}")
    print()

    try:
        from tqdm import tqdm as _base_tqdm

        def _make_progress_tracker(fixed_total):
            class _JsonTqdm(_base_tqdm):
                def __init__(self, *args, **kwargs):
                    kwargs["file"] = open(os.devnull, "w")
                    super().__init__(*args, **kwargs)
                    self._last_time = 0.0
                    self._last_reported = -1

                def update(self, n=1):
                    super().update(n)
                    if self.unit != "B":
                        return
                    now = time.time()
                    if now - self._last_time < 0.5 and self.n != fixed_total:
                        return
                    self._last_time = now
                    downloaded = min(self.n, fixed_total)
                    rate = self.format_dict.get("rate")
                    # Avoid flooding with duplicate values
                    if self.n == self._last_reported:
                        return
                    self._last_reported = self.n
                    sys.stderr.write(
                        json.dumps({
                            "type": "dl_progress",
                            "downloaded": downloaded,
                            "total": fixed_total,
                            "speed": rate if rate is not None else 0.0,
                        })
                        + "\n"
                    )
                    sys.stderr.flush()
            return _JsonTqdm

        from huggingface_hub import snapshot_download
    except ImportError:
        sys.stderr.write(
            json.dumps({"type": "error", "message": "huggingface_hub or tqdm not installed. Run: pip install huggingface_hub tqdm"})
            + "\n"
        )
        sys.stderr.flush()
        sys.exit(1)

    sys.stderr.write(json.dumps({"type": "dl_progress", "total": expected_total, "downloaded": 0, "speed": 0}) + "\n")
    sys.stderr.flush()

    try:
        snapshot_download(
            repo_id=repo_id,
            local_dir=str(dest_dir),
            allow_patterns=all_patterns,
            tqdm_class=_make_progress_tracker(expected_total),
        )
    except Exception as exc:
        sys.stderr.write(json.dumps({"type": "error", "message": str(exc)}) + "\n")
        sys.stderr.flush()
        sys.exit(1)

    sys.stderr.write(
        json.dumps({"type": "dl_progress", "total": expected_total, "downloaded": expected_total, "speed": 0})
        + "\n"
    )
    sys.stderr.flush()

    # Verify files
    prefix = model_name
    encoder = dest_dir / f"{prefix}-encoder.onnx"
    decoder = dest_dir / f"{prefix}-decoder.onnx"
    tokens = dest_dir / f"{prefix}-tokens.txt"

    if not encoder.exists() or not decoder.exists() or not tokens.exists():
        missing = []
        if not encoder.exists(): missing.append("encoder")
        if not decoder.exists(): missing.append("decoder")
        if not tokens.exists(): missing.append("tokens")
        raise FileNotFoundError(f"Missing files after download: {', '.join(missing)}")

    # Verify shell .onnx files have their .weights files
    enc_size = encoder.stat().st_size
    dec_size = decoder.stat().st_size
    if enc_size < 10 * 1024 * 1024:
        enc_weights = dest_dir / f"{prefix}-encoder.weights"
        if not enc_weights.exists():
            raise FileNotFoundError(
                f"'{prefix}-encoder.onnx' is a shell ({enc_size / 1024 / 1024:.1f} MB) "
                f"but '{prefix}-encoder.weights' was not found. "
                "The model cannot load without its external weights."
            )
    if dec_size < 10 * 1024 * 1024:
        dec_weights = dest_dir / f"{prefix}-decoder.weights"
        if not dec_weights.exists():
            raise FileNotFoundError(
                f"'{prefix}-decoder.onnx' is a shell ({dec_size / 1024 / 1024:.1f} MB) "
                f"but '{prefix}-decoder.weights' was not found. "
                "The model cannot load without its external weights."
            )

    # Check INT8 files (non-fatal warning if missing)
    enc_int8 = dest_dir / f"{prefix}-encoder.int8.onnx"
    dec_int8 = dest_dir / f"{prefix}-decoder.int8.onnx"
    has_int8_enc = enc_int8.exists()
    has_int8_dec = dec_int8.exists()
    if not has_int8_enc:
        print(f"  Note: {prefix}-encoder.int8.onnx not available (FP32 encoder will be used)")
    if not has_int8_dec:
        print(f"  Note: {prefix}-decoder.int8.onnx not available (FP32 decoder will be used)")

    total_mb = sum(f.stat().st_size for f in dest_dir.glob("*")) / 1024 / 1024
    print()
    print(f"  Model ready. {total_mb:.1f} MB total"
          f" | FP32 encoder + INT8 encoder: {'yes' if has_int8_enc else 'no'}"
          f" | FP32 decoder + INT8 decoder: {'yes' if has_int8_dec else 'no'}")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        sys.stderr.write(json.dumps({"type": "error", "message": str(exc)}) + "\n")
        sys.stderr.flush()
        sys.exit(1)
