import json
import os
import sys
import time
import urllib.request
import urllib.error

DEMUCS_URL = "https://dl.fbaipublicfiles.com/demucs/hybrid_transformer/955717e8-8726e21a.th"
FILENAME = "955717e8-8726e21a.th"


def get_cache_dir() -> str:
    """Return the models directory from command-line arg, or fall back to torch hub cache."""
    if len(sys.argv) > 1:
        return sys.argv[1]
    torch_home = os.environ.get("TORCH_HOME")
    if torch_home:
        return os.path.join(torch_home, "hub", "checkpoints")
    return os.path.expanduser("~/.cache/torch/hub/checkpoints")


def report(downloaded: int, total: int, speed: float = 0.0):
    msg = json.dumps({
        "type": "dl_progress",
        "downloaded": downloaded,
        "total": total,
        "speed": speed,
    })
    sys.stderr.write(msg + "\n")
    sys.stderr.flush()


def main():
    cache_dir = get_cache_dir()
    os.makedirs(cache_dir, exist_ok=True)

    final_path = os.path.join(cache_dir, FILENAME)
    part_path = final_path + ".part"

    # Already downloaded
    if os.path.exists(final_path):
        size = os.path.getsize(final_path)
        report(size, size, 0.0)
        print("Download complete")
        return

    # Resume from partial
    downloaded = 0
    headers = {}
    if os.path.exists(part_path):
        downloaded = os.path.getsize(part_path)
        headers["Range"] = f"bytes={downloaded}-"

    req = urllib.request.Request(DEMUCS_URL, headers=headers)
    with urllib.request.urlopen(req, timeout=60) as resp:
        total = int(resp.headers.get("Content-Length", 0)) + downloaded

        mode = "ab" if downloaded > 0 else "wb"
        last_report = time.time()
        last_bytes = downloaded

        with open(part_path, mode) as f:
            while True:
                chunk = resp.read(65536)
                if not chunk:
                    break
                f.write(chunk)
                downloaded += len(chunk)
                now = time.time()
                if now - last_report >= 0.5:
                    elapsed = now - last_report
                    speed = (downloaded - last_bytes) / elapsed if elapsed > 0 else 0.0
                    report(downloaded, total, speed)
                    last_report = now
                    last_bytes = downloaded

    os.rename(part_path, final_path)
    report(total, total, 0.0)
    print("Download complete")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        sys.stderr.write(json.dumps({"type": "error", "message": str(exc)}) + "\n")
        sys.stderr.flush()
        sys.exit(1)
