import json
import os
from pathlib import Path
import sys
from faster_whisper import WhisperModel


def load_config():
    config_path = Path(__file__).with_name("config.json")
    if config_path.exists():
        return json.loads(config_path.read_text(encoding="utf-8-sig"))
    return {}


def write(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def main():
    config = load_config()
    model_name = os.environ.get("WINWHISPER_MODEL", config.get("model", "small"))
    device = os.environ.get("WINWHISPER_DEVICE", config.get("device", "cpu"))
    compute_type = os.environ.get("WINWHISPER_COMPUTE", config.get("compute_type", "int8"))
    cpu_threads = int(os.environ.get("WINWHISPER_CPU_THREADS", config.get("cpu_threads", 4)))
    num_workers = int(os.environ.get("WINWHISPER_NUM_WORKERS", config.get("num_workers", 1)))
    beam_size = int(os.environ.get("WINWHISPER_BEAM_SIZE", config.get("beam_size", 1)))
    vad_filter = os.environ.get("WINWHISPER_VAD_FILTER", str(config.get("vad_filter", False))).lower() in ("1", "true")
    vad_min_silence = int(os.environ.get("WINWHISPER_VAD_MIN_SILENCE", str(config.get("vad_min_silence_duration_ms", 300))))
    default_language = os.environ.get("WINWHISPER_LANGUAGE", config.get("default_language", "en"))
    no_speech_threshold = float(os.environ.get("WINWHISPER_NO_SPEECH_THRESHOLD", str(config.get("no_speech_threshold", 0.72))))
    log_prob_threshold = float(os.environ.get("WINWHISPER_LOG_PROB_THRESHOLD", str(config.get("log_prob_threshold", -0.8))))

    model = WhisperModel(
        model_name,
        device=device,
        compute_type=compute_type,
        cpu_threads=cpu_threads,
        num_workers=num_workers,
    )
    write({
        "ready": True,
        "model": model_name,
        "device": device,
        "compute_type": compute_type,
        "cpu_threads": cpu_threads,
        "num_workers": num_workers,
        "beam_size": beam_size,
    })

    for line in sys.stdin:
        try:
            request = json.loads(line)
            req_type = request.get("type")
            if req_type == "ping":
                write({"pong": True})
                continue
            elif req_type != "transcribe":
                write({"error": "Unsupported request type"})
                continue

            audio_path = request["audio_path"]
            language = request.get("language") or default_language
            segments, info = model.transcribe(
                audio_path,
                language=language,
                beam_size=beam_size,
                vad_filter=vad_filter,
                vad_parameters={"min_silence_duration_ms": vad_min_silence},
                condition_on_previous_text=False,
                temperature=0,
                no_speech_threshold=no_speech_threshold,
                log_prob_threshold=log_prob_threshold,
            )
            segment_list = list(segments)
            text = "".join(segment.text for segment in segment_list).strip()
            avg_log_values = [
                segment.avg_logprob
                for segment in segment_list
                if getattr(segment, "avg_logprob", None) is not None
            ]
            no_speech_values = [
                segment.no_speech_prob
                for segment in segment_list
                if getattr(segment, "no_speech_prob", None) is not None
            ]
            write({
                "text": text,
                "language": info.language,
                "language_probability": info.language_probability,
                "segment_count": len(segment_list),
                "avg_log_probability": sum(avg_log_values) / len(avg_log_values) if avg_log_values else None,
                "no_speech_probability": max(no_speech_values) if no_speech_values else None,
            })
        except Exception as exc:
            write({"error": str(exc)})


if __name__ == "__main__":
    main()
