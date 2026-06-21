$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $ProjectRoot

if (-not (Test-Path ".venv")) {
    Write-Host "Python .venv not found. Please run setup.ps1 first."
    exit 1
}

$PyInstaller = ".\.venv\Scripts\pyinstaller.exe"
if (-not (Test-Path $PyInstaller)) {
    Write-Host "Installing PyInstaller..."
    .\.venv\Scripts\python -m pip install pyinstaller
}

Write-Host "Building whisper_worker.exe (CPU)..."
& $PyInstaller ".\whisper_worker.spec"

Write-Host "Building whisper_worker_gpu.exe (GPU)..."
& $PyInstaller ".\whisper_worker_gpu.spec"

New-Item -ItemType Directory -Force -Path ".\stt_engine\dist" | Out-Null
Copy-Item ".\dist\whisper_worker.exe" ".\stt_engine\dist\whisper_worker.exe" -Force
Copy-Item ".\dist\whisper_worker_gpu.exe" ".\stt_engine\dist\whisper_worker_gpu.exe" -Force

Write-Host "Python workers bundled into stt_engine\dist."
