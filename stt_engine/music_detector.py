import librosa
import numpy as np
import sys


def is_music(audio_path, threshold=0.5):
    y, sr = librosa.load(audio_path, duration=30, mono=True)

    zcr = float(np.mean(librosa.feature.zero_crossing_rate(y)))
    zcr_std = float(np.std(librosa.feature.zero_crossing_rate(y)))
    centroid = float(np.mean(librosa.feature.spectral_centroid(y=y, sr=sr)))
    bandwidth = float(np.mean(librosa.feature.spectral_bandwidth(y=y, sr=sr)))
    contrast = float(np.mean(librosa.feature.spectral_contrast(y=y, sr=sr)))
    mfccs = librosa.feature.mfcc(y=y, sr=sr, n_mfcc=13)
    mfcc_std = float(np.mean(np.std(mfccs, axis=1)))
    rms = float(np.mean(librosa.feature.rms(y=y)))
    tempo, _ = librosa.beat.beat_track(y=y, sr=sr)

    score = 0.0

    # ZCR mean: speech has rapid polarity flips (consonants), music is smoother
    if zcr < 0.06:
        score += 1.5
    elif zcr < 0.09:
        score += 0.5
    elif zcr > 0.12:
        score -= 0.5

    # ZCR std: speech has varying rates, music is more consistent
    if zcr_std < 0.02:
        score += 1.0
    elif zcr_std < 0.04:
        score += 0.5

    # Spectral centroid: music tends to have brighter sustained timbre
    if centroid > 2000:
        score += 0.5

    # Spectral bandwidth: wider for music (multiple instruments)
    if bandwidth > 1800:
        score += 0.5

    # Spectral contrast: music has sharper harmonic peaks vs noise floor
    if contrast is not None and contrast > 25:
        score += 1.0
    elif contrast is not None and contrast > 15:
        score += 0.5

    # MFCC variance: speech varies more (different phonemes), music is more consistent
    if mfcc_std < 15:
        score += 1.0
    elif mfcc_std < 25:
        score += 0.5
    else:
        score -= 0.5

    # RMS energy: music tends to have more consistent loudness
    rms_std = float(np.std(librosa.feature.rms(y=y)))
    if rms_std < 0.04:
        score += 0.5

    # Tempo: only if beat is clear AND consistent
    if tempo is not None:
        t = float(tempo.item() if hasattr(tempo, 'item') else tempo)
        if t > 80:
            score += 0.5

    max_score = 7.0
    return (max(score, 0) / max_score) >= threshold


if __name__ == "__main__":
    result = is_music(sys.argv[1])
    sys.stdout.write("true" if result else "false")
