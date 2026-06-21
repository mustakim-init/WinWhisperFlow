import sys
import json
import time

try:
    import huggingface_hub.utils
    original_tqdm = huggingface_hub.utils.tqdm

    class CustomTqdm(original_tqdm):
        def __init__(self, *args, **kwargs):
            import os
            # Redirect the visual console print output to devnull
            kwargs["file"] = open(os.devnull, "w")
            super().__init__(*args, **kwargs)
            self.file_name = kwargs.get("desc", "file")
            self.is_bytes = (kwargs.get("unit") == "B")
            self._last_time = 0

        def update(self, n=1):
            super().update(n)
            if not self.is_bytes:
                return
            now = time.time()
            if now - self._last_time >= 0.5 or self.n == self.total:
                self._last_time = now
                rate = self.format_dict.get("rate")
                speed = rate if rate is not None else 0.0
                sys.stderr.write(
                    json.dumps({
                        "type": "dl_progress",
                        "downloaded": self.n,
                        "total": self.total or 0,
                        "speed": speed,
                    })
                    + "\n"
                )
                sys.stderr.flush()

    huggingface_hub.utils.tqdm = CustomTqdm
except ImportError:
    pass

import huggingface_hub

def main():
    if len(sys.argv) < 3:
        print("Usage: download_cpu_model.py <model_name> <models_dir>")
        sys.exit(1)

    model_name = sys.argv[1]
    models_dir = sys.argv[2]
    repo_id = f"Systran/faster-whisper-{model_name}"

    try:
        # Prevent symlinks warning since we are printing JSON to stderr
        import os
        os.environ["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1"
        
        huggingface_hub.snapshot_download(
            repo_id=repo_id,
            local_files_only=False,
            cache_dir=models_dir,
        )
        print("Download complete")
    except Exception as exc:
        sys.stderr.write(json.dumps({"type": "error", "message": str(exc)}) + "\n")
        sys.stderr.flush()
        sys.exit(1)

if __name__ == "__main__":
    main()
