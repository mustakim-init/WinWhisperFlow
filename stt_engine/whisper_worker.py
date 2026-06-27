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
    sys.stdout.buffer.write((json.dumps(payload, ensure_ascii=False) + "\n").encode("utf-8"))
    sys.stdout.buffer.flush()


def trim_repetitive_tail(segments):
    if len(segments) < 2:
        return segments
    trimmed = list(segments)
    while len(trimmed) > 1:
        last = trimmed[-1]
        words = last.text.strip().split()
        if len(words) < 4:
            break
        unique_ratio = len(set(w.lower() for w in words)) / len(words)
        if unique_ratio < 0.3:
            trimmed.pop()
            continue
        quarter = max(1, len(words) // 4)
        chunks = [words[i:i+quarter] for i in range(0, len(words), quarter)]
        unique_chunks = len(set(' '.join(c).lower() for c in chunks))
        if unique_chunks <= 1:
            trimmed.pop()
            continue
        break
    return trimmed


def filter_segments(segments):
    seen = set()
    result = []
    for seg in segments:
        if seg.no_speech_prob is not None and seg.no_speech_prob > 0.6:
            continue
        if seg.avg_logprob is not None and seg.avg_logprob < -0.8:
            continue
        text = seg.text.strip()
        text_lower = text.lower()
        if text_lower in seen:
            continue
        seen.add(text_lower)
        result.append(seg)
    return result


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
    no_speech_threshold = float(os.environ.get("WINWHISPER_NO_SPEECH_THRESHOLD", str(config.get("no_speech_threshold", 0.45))))
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
            file_mode = request.get("file_mode", False)

            if file_mode:
                use_vad = False
                use_no_speech = no_speech_threshold
                use_log_prob = log_prob_threshold
                use_temperature = 0
                use_word_timestamps = False
            else:
                use_vad = vad_filter
                use_no_speech = no_speech_threshold
                use_log_prob = log_prob_threshold
                use_temperature = 0
                use_word_timestamps = False

            transcribe_kwargs = dict(
                audio=audio_path,
                language=language,
                beam_size=beam_size,
                vad_filter=use_vad,
                vad_parameters={"min_silence_duration_ms": vad_min_silence},
                condition_on_previous_text=False,
                temperature=use_temperature,
                word_timestamps=use_word_timestamps,
                no_speech_threshold=use_no_speech,
                log_prob_threshold=use_log_prob,
            )

            segments, info = model.transcribe(**transcribe_kwargs)
            segment_list = []
            for i, segment in enumerate(segments):
                segment_list.append(segment)
                if i > 0 and i % 10 == 0:
                    write({"type": "heartbeat", "segments_decoded": i})

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
