# -*- mode: python ; coding: utf-8 -*-


from PyInstaller.utils.hooks import collect_submodules

a = Analysis(
    ['stt_engine\\whisper_worker_gpu.py'],
    pathex=[],
    binaries=[],
    datas=[],
    hiddenimports=['librosa', 'soundfile', 'numpy', 'onnxruntime', 'base64', 'math', 'struct', 'scipy._external.array_api_compat.numpy.fft'] + collect_submodules('scipy') + collect_submodules('librosa') + collect_submodules('soundfile'),
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=2,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name='whisper_worker_gpu',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
