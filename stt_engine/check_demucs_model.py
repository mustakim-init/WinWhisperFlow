import json
import os

FILENAME = "955717e8-8726e21a.th"
EXPECTED_MIN_SIZE = 70_000_000  # ~80MB


def get_cache_dir() -> str:
    torch_home = os.environ.get("TORCH_HOME")
    if torch_home:
        return os.path.join(torch_home, "hub", "checkpoints")
    return os.path.expanduser("~/.cache/torch/hub/checkpoints")


cache_dir = get_cache_dir()
model_path = os.path.join(cache_dir, FILENAME)

if os.path.exists(model_path) and os.path.getsize(model_path) >= EXPECTED_MIN_SIZE:
    size = os.path.getsize(model_path)
    print(json.dumps({"downloaded": True, "size": size}))
else:
    print(json.dumps({"downloaded": False, "size": 0}))
