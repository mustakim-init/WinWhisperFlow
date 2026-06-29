import json, os, sys, time, urllib.error, urllib.request


def _load_file_sizes(repo_id: str, files: list[str]) -> tuple[dict[str, int], int]:
    """Return (file_name -> size, total_bytes) via HEAD requests."""
    sizes: dict[str, int] = {}
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
    if len(sys.argv) < 3:
        print("Usage: download_cpu_model.py <model_name> <models_dir>")
        sys.exit(1)

    # Disable Xet Storage so hf_hub uses http_get with per-chunk progress updates
    os.environ["HF_HUB_DISABLE_XET"] = "1"

    model_name = sys.argv[1]
    cache_dir = sys.argv[2]
    repo_id = f"Systran/faster-whisper-{model_name}"

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

    os.makedirs(cache_dir, exist_ok=True)

    # Determine expected files
    # Whisper models have: model.bin, tokenizer.json, config.json, vocab.json, merges.txt (or .model)
    all_files = [
        "model.bin",
        "tokenizer.json",
        "config.json",
        "vocab.json",
        "merges.txt",
    ]

    sizes, total_size = _load_file_sizes(repo_id, all_files)

    # Filter to files that actually exist on remote
    expected_files = [f for f in all_files if sizes.get(f, 0) > 0]
    expected_total = sum(sizes[f] for f in expected_files)

    if not expected_files:
        sys.stderr.write(
            json.dumps({"type": "error", "message": f"No files found for model '{model_name}' at {repo_id}"})
            + "\n"
        )
        sys.stderr.flush()
        sys.exit(1)

    sys.stderr.write(json.dumps({"type": "dl_progress", "total": expected_total, "downloaded": 0, "speed": 0}) + "\n")
    sys.stderr.flush()

    try:
        snapshot_download(
            repo_id=repo_id,
            allow_patterns=expected_files,
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

    print("Download complete")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        sys.stderr.write(json.dumps({"type": "error", "message": str(exc)}) + "\n")
        sys.stderr.flush()
        sys.exit(1)
