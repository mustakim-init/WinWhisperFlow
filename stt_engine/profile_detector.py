import librosa, numpy as np
from music_detector import is_music


def profile(path, label):
    y, sr = librosa.load(path, duration=30, mono=True)
    zcr = float(np.mean(librosa.feature.zero_crossing_rate(y)))
    zcr_std = float(np.std(librosa.feature.zero_crossing_rate(y)))
    centroid = float(np.mean(librosa.feature.spectral_centroid(y=y, sr=sr)))
    bandwidth = float(np.mean(librosa.feature.spectral_bandwidth(y=y, sr=sr)))
    contrast = float(np.mean(librosa.feature.spectral_contrast(y=y, sr=sr)))
    mfccs = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=13)
    mfcc_std = float(np.mean(np.std(mfccs, axis=1)))
    rms_std = float(np.std(librosa.feature.rms(y=y)))
    tempo, _ = librosa.beat.beat_track(y=y, sr=sr)
    result = is_music(path)

    print(f"\n{label}:")
    print(f"  zcr={zcr:.5f}  zcr_std={zcr_std:.5f}  centroid={centroid:.1f}")
    print(f"  bandwidth={bandwidth:.1f}  contrast={contrast:.2f}  mfcc_std={mfcc_std:.3f}")
    print(f"  rms_std={rms_std:.5f}  tempo={tempo}")
    print(f"  => {'MUSIC' if result else 'SPEECH'}")


if __name__ == "__main__":
    import sys
    print("Usage: python profile_detector.py <path_to_audio> <label>", file=sys.stderr)
