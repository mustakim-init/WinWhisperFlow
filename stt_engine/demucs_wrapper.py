import json, sys, os, torch, soundfile, librosa
from demucs.pretrained import get_model
from demucs.apply import apply_model
from demucs.audio import convert_audio

try:
    input_wav = sys.argv[1]
    output_dir = sys.argv[2]

    device = "cuda" if torch.cuda.is_available() else "cpu"
    model = get_model(name="htdemucs")
    model.to(device)

    wav, sr = librosa.load(input_wav, sr=None, mono=False)
    if wav.ndim == 1:
        wav = wav[None]
    wav = torch.from_numpy(wav)
    wav = convert_audio(wav, int(sr), int(model.samplerate), model.audio_channels)
    wav = wav.to(device)

    with torch.no_grad():
        sources = apply_model(model, wav[None], device=device, shifts=1)[0]

    sources = sources.cpu()
    stem_idx = model.sources.index("vocals")
    vocals = sources[stem_idx]

    base = os.path.splitext(os.path.basename(input_wav))[0]
    out_dir = os.path.join(output_dir, "htdemucs", base)
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, "vocals.wav")

    soundfile.write(out_path, vocals.T.numpy(), model.samplerate)
    print(out_path)

except Exception as exc:
    sys.stderr.write(json.dumps({"type": "error", "message": str(exc)}) + "\n")
    sys.stderr.flush()
    sys.exit(1)
