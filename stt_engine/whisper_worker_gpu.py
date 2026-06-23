import base64
import json
import os
import sys
from pathlib import Path


SHERPA_MODEL_MAP = {
    "tiny": "sherpa-onnx-whisper-tiny",
    "base": "sherpa-onnx-whisper-base",
    "small": "sherpa-onnx-whisper-small",
    "medium": "sherpa-onnx-whisper-medium",
    "large-v3": "sherpa-onnx-whisper-large-v3",
    "turbo": "sherpa-onnx-whisper-turbo",
}

SAMPLE_RATE = 16000
N_FFT = 400
HOP_LENGTH = 160


def load_config():
    config_path = Path(__file__).with_name("config.json")
    if config_path.exists():
        return json.loads(config_path.read_text(encoding="utf-8-sig"))
    return {}


def write(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


# ---------------------------------------------------------------------------
# DML / ONNX path helpers  (imports are deferred inside methods)
# ---------------------------------------------------------------------------

def _find_file(model_dir, pattern):
    matches = list(Path(model_dir).glob(pattern))
    return str(matches[0]) if matches else None


def find_onnx_model_files(model_name):
    models_dir = Path(os.environ.get("WINWHISPER_MODELS_DIR",
                     str(Path(__file__).parent / "models")))
    sherpa_name = SHERPA_MODEL_MAP.get(model_name, model_name)
    model_dir = models_dir / sherpa_name
    prefix = model_name

    encoder = _find_file(model_dir, f"{prefix}-encoder.onnx")
    decoder = _find_file(model_dir, f"{prefix}-decoder.onnx")
    tokens = _find_file(model_dir, f"{prefix}-tokens.txt")

    if encoder and decoder and tokens:
        return encoder, decoder, tokens

    encoder = _find_file(model_dir, f"{prefix}-encoder.int8.onnx")
    decoder = _find_file(model_dir, f"{prefix}-decoder.int8.onnx")
    if encoder and decoder and tokens:
        return encoder, decoder, tokens

    raise FileNotFoundError(
        f"ONNX model not found in {model_dir}. "
        "Run: python download_gpu_model.py <model_name>"
    )


def load_tokens(filename):
    tokens = {}
    with open(filename, "r") as f:
        for line in f:
            parts = line.strip().split()
            if len(parts) >= 2:
                tokens[int(parts[1])] = parts[0]
    return tokens


def load_audio(path):
    import soundfile as sf
    data, sr = sf.read(path, always_2d=True, dtype="float32")
    data = data[:, 0]
    if sr != SAMPLE_RATE:
        import librosa
        data = librosa.resample(data, orig_sr=sr, target_sr=SAMPLE_RATE)
    return data, SAMPLE_RATE


class WhisperONNXModel:
    def __init__(self, encoder_path, decoder_path, tokens_path, providers):
        import onnxruntime
        session_opts = onnxruntime.SessionOptions()
        session_opts.intra_op_num_threads = int(
            os.environ.get("WINWHISPER_CPU_THREADS", "4")
        )
        session_opts.graph_optimization_level = onnxruntime.GraphOptimizationLevel.ORT_ENABLE_BASIC
        session_opts.enable_mem_pattern = False

        self.encoder = onnxruntime.InferenceSession(
            encoder_path, sess_options=session_opts, providers=providers,
        )
        self.decoder = onnxruntime.InferenceSession(
            decoder_path, sess_options=session_opts, providers=providers,
        )

        meta = self.encoder.get_modelmeta().custom_metadata_map
        self.n_text_layer = int(meta["n_text_layer"])
        self.n_text_ctx = int(meta["n_text_ctx"])
        self.n_text_state = int(meta["n_text_state"])
        self.n_mels = int(meta["n_mels"])
        encoder_input_shape = self.encoder.get_inputs()[0].shape
        mel_frames_val = encoder_input_shape[2] if len(encoder_input_shape) >= 3 else None
        try:
            self.expected_mel_frames = int(mel_frames_val)
        except (ValueError, TypeError):
            self.expected_mel_frames = 3000
        self.sot_id = int(meta["sot"])
        self.eot_id = int(meta["eot"])
        self.translate_id = int(meta["translate"])
        self.transcribe_id = int(meta["transcribe"])
        self.no_timestamps_id = int(meta["no_timestamps"])
        self.no_speech_id = int(meta["no_speech"])
        self.blank_id = int(meta["blank_id"])

        self.sot_sequence = list(
            map(int, meta.get("sot_sequence", "").split(","))
        )
        self.sot_sequence.append(self.no_timestamps_id)

        self.lang2id = {}
        all_lang_tokens = list(
            map(int, meta.get("all_language_tokens", "").split(","))
        )
        all_lang_codes = meta.get("all_language_codes", "").split(",")
        if len(all_lang_tokens) == len(all_lang_codes):
            self.lang2id = dict(zip(all_lang_codes, all_lang_tokens))

        self.tokens = load_tokens(tokens_path)

    def run_encoder(self, mel):
        outputs = self.encoder.run(None,
            {self.encoder.get_inputs()[0].name: mel})
        return outputs[0], outputs[1]

    def run_decoder(self, tokens, k_cache, v_cache, cross_k, cross_v, offset):
        logits, k_cache, v_cache = self.decoder.run(None, {
            self.decoder.get_inputs()[0].name: tokens,
            self.decoder.get_inputs()[1].name: k_cache,
            self.decoder.get_inputs()[2].name: v_cache,
            self.decoder.get_inputs()[3].name: cross_k,
            self.decoder.get_inputs()[4].name: cross_v,
            self.decoder.get_inputs()[5].name: offset,
        })
        return logits, k_cache, v_cache

    def transcribe(self, audio_path, language="en",
                   no_speech_threshold=0.45, log_prob_threshold=-0.8):
        import librosa
        import numpy as np

        audio, sr = load_audio(audio_path)
        mel_filters = librosa.filters.mel(
            sr=SAMPLE_RATE, n_fft=N_FFT, n_mels=self.n_mels,
            fmin=0, fmax=SAMPLE_RATE / 2, htk=True, norm="slaney",
        )

        spec = librosa.stft(audio, n_fft=N_FFT, hop_length=HOP_LENGTH,
                            win_length=N_FFT, window="hann", center=True,
                            pad_mode="reflect")
        power = np.abs(spec) ** 2
        mel = mel_filters @ power
        T = mel.shape[1]

        expected = self.expected_mel_frames
        hop = expected - 200
        texts = []

        for start in range(0, T, hop):
            end = min(start + expected, T)
            mel_chunk = mel[:, start:end]

            log_spec = np.log10(np.clip(mel_chunk, a_min=1e-10, a_max=None))
            log_spec = np.maximum(log_spec, log_spec.max() - 8.0)
            mel_norm = (log_spec + 4.0) / 4.0

            mel_padded = np.zeros((self.n_mels, expected), dtype=np.float32)
            mel_padded[:, :end - start] = mel_norm

            cross_k, cross_v = self.run_encoder(
                mel_padded[np.newaxis, :, :].astype(np.float32)
            )

            sot_seq = list(self.sot_sequence)
            if language in self.lang2id:
                sot_seq[1] = self.lang2id[language]

            tokens = np.array([sot_seq], dtype=np.int64)
            k_cache = np.zeros((self.n_text_layer, 1, self.n_text_ctx,
                                self.n_text_state), dtype=np.float32)
            v_cache = np.zeros((self.n_text_layer, 1, self.n_text_ctx,
                                self.n_text_state), dtype=np.float32)
            offset = np.array([len(sot_seq)], dtype=np.int64)

            logits, k_cache, v_cache = self.run_decoder(
                tokens, k_cache, v_cache, cross_k, cross_v,
                np.zeros(1, dtype=np.int64),
            )

            logits_vec = logits[0, -1].copy()
            logits_vec[self.eot_id] = float("-inf")
            logits_vec[self.blank_id] = float("-inf")
            logits_vec[self.no_timestamps_id] = float("-inf")
            logits_vec[self.sot_id] = float("-inf")
            logits_vec[self.translate_id] = float("-inf")

            next_token = int(logits_vec.argmax())
            if next_token == self.no_speech_id:
                no_speech_prob = float(
                    np.exp(logits_vec[self.no_speech_id])
                    / np.sum(np.exp(logits_vec))
                )
                if no_speech_prob > no_speech_threshold:
                    continue
                logits_vec[self.no_speech_id] = float("-inf")
                next_token = int(logits_vec.argmax())

            results = []
            for _ in range(self.n_text_ctx):
                if next_token == self.eot_id:
                    break
                results.append(next_token)
                tok_in = np.array([[next_token]], dtype=np.int64)
                logits, k_cache, v_cache = self.run_decoder(
                    tok_in, k_cache, v_cache, cross_k, cross_v, offset,
                )
                offset += 1
                logits_vec = logits[0, -1].copy()
                logits_vec[self.eot_id] = float("-inf")
                logits_vec[self.blank_id] = float("-inf")
                logits_vec[self.no_timestamps_id] = float("-inf")
                logits_vec[self.sot_id] = float("-inf")
                logits_vec[self.translate_id] = float("-inf")
                next_token = int(logits_vec.argmax())

            raw = b"".join(
                base64.b64decode(self.tokens.get(tid, ""))
                for tid in results
                if self.tokens.get(tid, "")
            )
            if raw:
                text = raw.decode("utf-8", errors="replace").strip()
                if text:
                    texts.append(text)

        return " ".join(texts)


def main():
    config = load_config()
    model_name = os.environ.get("WINWHISPER_MODEL", config.get("model", "small"))
    device = os.environ.get("WINWHISPER_DEVICE", config.get("device", "cpu"))
    compute_type = os.environ.get("WINWHISPER_COMPUTE", config.get("compute_type", "float16"))
    cpu_threads = int(os.environ.get("WINWHISPER_CPU_THREADS", config.get("cpu_threads", 4)))
    num_workers = int(os.environ.get("WINWHISPER_NUM_WORKERS", config.get("num_workers", 1)))
    beam_size = int(os.environ.get("WINWHISPER_BEAM_SIZE", config.get("beam_size", 1)))
    vad_filter = os.environ.get("WINWHISPER_VAD_FILTER", str(config.get("vad_filter", False))).lower() in ("1", "true")
    vad_min_silence = int(os.environ.get("WINWHISPER_VAD_MIN_SILENCE", str(config.get("vad_min_silence_duration_ms", 300))))
    default_language = os.environ.get("WINWHISPER_LANGUAGE", config.get("default_language", "en"))
    no_speech_threshold = float(os.environ.get("WINWHISPER_NO_SPEECH_THRESHOLD", str(config.get("no_speech_threshold", 0.45))))
    log_prob_threshold = float(os.environ.get("WINWHISPER_LOG_PROB_THRESHOLD", str(config.get("log_prob_threshold", -0.8))))

    # CUDA / CPU path — faster-whisper
    if device in ("cuda", "cpu"):
        from faster_whisper import WhisperModel
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

    # DML path — onnxruntime with DirectML
    elif device == "dml":
        import onnxruntime
        available = onnxruntime.get_available_providers()

        if "DmlExecutionProvider" not in available:
            write({
                "ready": False,
                "error": "DmlExecutionProvider not available. Install onnxruntime-directml.",
            })
            sys.exit(1)

        providers = ["DmlExecutionProvider", "CPUExecutionProvider"]
        try:
            encoder_path, decoder_path, tokens_path = find_onnx_model_files(model_name)
        except FileNotFoundError as exc:
            write({"ready": False, "error": str(exc)})
            sys.exit(1)

        model = WhisperONNXModel(encoder_path, decoder_path, tokens_path, providers)
        write({
            "ready": True,
            "model": model_name,
            "device": "DmlExecutionProvider",
            "compute_type": "float16",
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
                text = model.transcribe(
                    audio_path, language,
                    no_speech_threshold=no_speech_threshold,
                    log_prob_threshold=log_prob_threshold,
                )
                write({
                    "text": text,
                    "language": language,
                    "language_probability": 1.0,
                    "segment_count": 1,
                    "avg_log_probability": None,
                    "no_speech_probability": None if text else 1.0,
                })
            except Exception as exc:
                write({"error": str(exc)})


if __name__ == "__main__":
    main()
