import json
import os
import sys
import threading
import time
import urllib.request

# Suppress HF warnings before importing (must be set before huggingface_hub import)
os.environ["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1"
os.environ["HF_HUB_DISABLE_TOKEN_WARNING"] = "1"

from huggingface_hub import HfApi, hf_hub_download

# Cumulative progress tracking across all files
_progress = {
    "total": 0,
    "cum": 0,
    "lock": threading.Lock(),
    "last_report": 0,
}


class _ProgressTqdm:
    """Per-file progress bar that reports cumulative bytes to stderr."""

    def __init__(self, *args, **kwargs):
        self.total = kwargs.get("total", 0)
        self.n = kwargs.get("initial", 0)
        self._last_update = time.time()

    def update(self, n):
        self.n += n
        now = time.time()
        if now - self._last_update < 0.5 and (self.total <= 0 or self.n < self.total):
            return
        self._last_update = now
        with _progress["lock"]:
            cum = _progress["cum"] + self.n
        report = json.dumps({
            "type": "dl_progress",
            "downloaded": cum,
            "total": _progress["total"],
        })
        sys.stderr.write(report + "\n")
        sys.stderr.flush()

    def close(self):
        pass

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.close()


def _load_file_sizes(repo_id: str, files: list[str]) -> tuple[dict[str, int], int]:
    """Return (file_name -> size, total_bytes) via HEAD requests."""
    sizes: dict[str, int] = {}
    total = 0
    for f in files:
        url = f"https://huggingface.co/{repo_id}/resolve/main/{f}"
        req = urllib.request.Request(url, method="HEAD")
        try:
            resp = urllib.request.urlopen(req, timeout=15)
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

    model_name = sys.argv[1]
    cache_dir = sys.argv[2]
    repo_id = f"Systran/faster-whisper-{model_name}"

    try:
        # 1. List repo files (skip metadata)
        api = HfApi()
        all_files = api.list_repo_files(repo_id)
        files = [f for f in all_files if f not in (".gitattributes", "README.md")]

        if not files:
            print(f"No files found in {repo_id}")
            sys.exit(1)

        # 2. Get sizes upfront so the UI knows the total
        file_sizes, total_size = _load_file_sizes(repo_id, files)
        _progress["total"] = total_size

        # 3. Download each file through huggingface_hub (handles caching)
        for f in files:
            size = file_sizes.get(f, 0)
            if size == 0:
                continue
            hf_hub_download(
                repo_id,
                f,
                cache_dir=cache_dir,
                tqdm_class=_ProgressTqdm,
            )
            with _progress["lock"]:
                _progress["cum"] += size

        # 4. Signal completion
        sys.stderr.write(
            json.dumps({
                "type": "dl_progress",
                "downloaded": total_size,
                "total": total_size,
            })
            + "\n"
        )
        sys.stderr.flush()
        print("Download complete")

    except Exception as exc:
        sys.stderr.write(json.dumps({"type": "error", "message": str(exc)}) + "\n")
        sys.stderr.flush()
        sys.exit(1)


if __name__ == "__main__":
    main()
