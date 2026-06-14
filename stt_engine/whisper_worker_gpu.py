import base64
import json
import math
import os
import struct
import sys
from pathlib import Path

import numpy as np
import onnxruntime

try:
    import librosa
except ImportError:
    librosa = None

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
    data = json.dumps(payload, ensure_ascii=False) + "\n"
    sys.stdout.write(data)
    sys.stdout.flush()


def _find_file(model_dir, pattern):
    matches = list(Path(model_dir).glob(pattern))
    return str(matches[0]) if matches else None


def find_model_files(model_name):
    models_dir = Path(os.environ.get("WINWHISPER_MODELS_DIR",
                     str(Path(__file__).parent / "models")))
    sherpa_name = SHERPA_MODEL_MAP.get(model_name, model_name)
    model_dir = models_dir / sherpa_name
    prefix = model_name

    encoder = _find_file(model_dir, f"{prefix}-encoder.onnx")
    decoder = _find_file(model_dir, f"{prefix}-decoder.onnx")
    tokens = _find_file(model_dir, f"{prefix}-tokens.txt")

    if encoder and decoder and tokens:
        return encoder, decoder, tokens, False

    encoder = _find_file(model_dir, f"{prefix}-encoder.int8.onnx")
    decoder = _find_file(model_dir, f"{prefix}-decoder.int8.onnx")
    if encoder and decoder and tokens:
        sys.stderr.write("WARN: Using int8 model (FP32 not found, falling back to CPU)\n")
        sys.stderr.flush()
        return encoder, decoder, tokens, True

    raise FileNotFoundError(
        f"Model not found in {model_dir}. "
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
    try:
        import soundfile as sf
        data, sr = sf.read(path, always_2d=True, dtype="float32")
        data = data[:, 0]
    except ImportError as exc:
        # PyInstaller packaging can sometimes miss optional/native deps.
        # Fallback to librosa loader when possible, otherwise raise a clear error.
        if librosa is None:
            raise ImportError(
                "Missing dependency 'soundfile' required to load audio in whisper_worker_gpu.py. "
                "Install soundfile>=0.12.1 and rebuild/bundle the GPU worker."
            ) from exc

        try:
            data, sr = librosa.load(path, sr=SAMPLE_RATE, mono=True)
            data = data.astype(np.float32, copy=False)
        except Exception as exc2:
            raise ImportError(
                "Failed to load audio without 'soundfile'. "
                "Ensure 'soundfile>=0.12.1' is available in the GPU worker environment "
                "(rebuild PyInstaller bundle)."
            ) from exc2

    if sr != SAMPLE_RATE:
        if librosa is not None:
            data = librosa.resample(data, orig_sr=sr, target_sr=SAMPLE_RATE)
        else:
            old_len = len(data)
            new_len = int(old_len * SAMPLE_RATE / sr)
            data = np.interp(np.linspace(0, old_len - 1, new_len),
                             np.arange(old_len), data)

    return np.ascontiguousarray(data), SAMPLE_RATE


def compute_features(audio, sr, n_mels, expected_frames):
    if librosa is None:
        raise ImportError(
            "Missing required dependency 'librosa' for feature computation. "
            "Install librosa>=0.10.0 in the GPU worker environment."
        )

    frames = librosa.frames_to_time(
        np.arange(expected_frames + expected_frames // 2),
        sr=SAMPLE_RATE, hop_length=HOP_LENGTH, n_fft=N_FFT
    )

    spec = librosa.stft(
        audio, n_fft=N_FFT, hop_length=HOP_LENGTH,
        win_length=N_FFT, window="hann", center=True,
        pad_mode="reflect"
    )
    power = np.abs(spec) ** 2

    mel_basis = librosa.filters.mel(
        sr=SAMPLE_RATE, n_fft=N_FFT, n_mels=n_mels,
        fmin=0, fmax=SAMPLE_RATE / 2,
        htk=True, norm="slaney"
    )
    mel = mel_basis @ power

    log_spec = np.log10(np.clip(mel, a_min=1e-10, a_max=None))
    log_spec = np.maximum(log_spec, log_spec.max(axis=0, keepdims=True) - 8.0)
    mel_norm = (log_spec + 4.0) / 4.0

    T = mel_norm.shape[1]
    if T >= expected_frames:
        mel_norm = mel_norm[:, :expected_frames]
    else:
        mel_norm = np.pad(mel_norm, ((0, 0), (0, expected_frames - T)), mode="constant")

    return mel_norm[np.newaxis, :, :].astype(np.float32)


class WhisperModel:
    def __init__(self, encoder_path, decoder_path, tokens_path, providers):
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
        self.expected_mel_frames = encoder_input_shape[2] if len(encoder_input_shape) >= 3 and encoder_input_shape[2] is not None else 3000
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
        outputs = self.encoder.run(None, {self.encoder.get_inputs()[0].name: mel})
        return outputs[0], outputs[1]

    def get_cache(self):
        return (
            np.zeros((self.n_text_layer, 1, self.n_text_ctx, self.n_text_state),
                     dtype=np.float32),
            np.zeros((self.n_text_layer, 1, self.n_text_ctx, self.n_text_state),
                     dtype=np.float32),
        )

    def run_decoder(self, tokens, k_cache, v_cache, cross_k, cross_v, offset):
        feed = {
            self.decoder.get_inputs()[0].name: tokens,
            self.decoder.get_inputs()[1].name: k_cache,
            self.decoder.get_inputs()[2].name: v_cache,
            self.decoder.get_inputs()[3].name: cross_k,
            self.decoder.get_inputs()[4].name: cross_v,
            self.decoder.get_inputs()[5].name: offset,
        }
        logits, k_cache, v_cache = self.decoder.run(None, feed)
        return logits, k_cache, v_cache

    def suppress_tokens(self, logits, is_initial):
        if is_initial:
            logits[self.eot_id] = float("-inf")
            logits[self.blank_id] = float("-inf")
        logits[self.no_timestamps_id] = float("-inf")
        logits[self.sot_id] = float("-inf")
        logits[self.translate_id] = float("-inf")

    def transcribe(self, audio_path, language="en", no_speech_threshold=0.72, log_prob_threshold=-0.8):
        audio, sr = load_audio(audio_path)
        mel = compute_features(audio, sr, self.n_mels, self.expected_mel_frames)

        cross_k, cross_v = self.run_encoder(mel)

        sot_sequence = list(self.sot_sequence)
        if language in self.lang2id:
            sot_sequence[1] = self.lang2id[language]

        tokens = np.array([sot_sequence], dtype=np.int64)
        k_cache, v_cache = self.get_cache()
        offset = np.array([len(sot_sequence)], dtype=np.int64)

        logits, k_cache, v_cache = self.run_decoder(
            tokens, k_cache, v_cache, cross_k, cross_v,
            np.zeros(1, dtype=np.int64),
        )
        logits_vec = logits[0, -1].copy()
        self.suppress_tokens(logits_vec, is_initial=False)
        next_token = int(logits_vec.argmax())

        if next_token == self.no_speech_id:
            no_speech_prob = float(np.exp(logits_vec[self.no_speech_id]) / np.sum(np.exp(logits_vec)))
            if no_speech_prob > no_speech_threshold:
                return ""
            logits_vec[self.no_speech_id] = float("-inf")
            next_token = int(logits_vec.argmax())

        results = []
        for i in range(self.n_text_ctx):
            if next_token == self.eot_id:
                break

            results.append(next_token)
            tok_in = np.array([[next_token]], dtype=np.int64)
            logits, k_cache, v_cache = self.run_decoder(
                tok_in, k_cache, v_cache, cross_k, cross_v, offset,
            )
            offset += 1
            logits_vec = logits[0, -1].copy()
            self.suppress_tokens(logits_vec, is_initial=False)
            next_token = int(logits_vec.argmax())

        raw = b""
        for tid in results:
            token_str = self.tokens.get(tid, "")
            if token_str:
                try:
                    raw += base64.b64decode(token_str)
                except Exception:
                    pass

        text = raw.decode("utf-8", errors="replace").strip()
        return text


def main():
    config = load_config()
    model_name = os.environ.get("WINWHISPER_MODEL", config.get("model", "small"))
    default_language = os.environ.get("WINWHISPER_LANGUAGE", config.get("default_language", "en"))
    no_speech_threshold = float(os.environ.get("WINWHISPER_NO_SPEECH_THRESHOLD", str(config.get("no_speech_threshold", 0.45))))
    log_prob_threshold = float(os.environ.get("WINWHISPER_LOG_PROB_THRESHOLD", str(config.get("log_prob_threshold", -0.8))))

    try:
        encoder_path, decoder_path, tokens_path, is_int8 = find_model_files(model_name)
    except FileNotFoundError as exc:
        write({"ready": False, "error": str(exc)})
        sys.exit(1)

    available = onnxruntime.get_available_providers()

    if is_int8:
        providers = ["CPUExecutionProvider"]
        sys.stderr.write("WARN: Using int8 quantized model on CPU (FP32 model not available)\n")
        sys.stderr.flush()
    else:
        providers = ["CPUExecutionProvider"]
        if "CUDAExecutionProvider" in available:
            providers = ["CUDAExecutionProvider", "CPUExecutionProvider"]
            sys.stderr.write("INFO: Using CUDAExecutionProvider (NVIDIA GPU)\n")
            sys.stderr.flush()
        elif "DmlExecutionProvider" in available:
            providers = ["DmlExecutionProvider", "CPUExecutionProvider"]
            compute_type = "float16"
            sys.stderr.write("INFO: Using DmlExecutionProvider (DirectML GPU)\n")
            sys.stderr.flush()

    model = WhisperModel(encoder_path, decoder_path, tokens_path, providers)

    write({
        "ready": True,
        "model": model_name,
        "device": providers[0],
        "compute_type": "float16" if providers[0] != "CPUExecutionProvider" else "float32",
        "n_mels": model.n_mels,
        "sot_sequence": model.sot_sequence,
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
                audio_path,
                language,
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
