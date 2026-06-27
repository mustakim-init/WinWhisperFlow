param(
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $ProjectRoot

if (-not (Test-Path ".venv\Scripts\python.exe")) {
    & $Python -m venv .venv
}

& ".\.venv\Scripts\python.exe" -m pip install --upgrade pip
& ".\.venv\Scripts\python.exe" -m pip install -r "stt_engine\requirements.txt"
& ".\.venv\Scripts\python.exe" -m pip install -r "stt_engine\requirements_gpu.txt"
& ".\.venv\Scripts\python.exe" -m pip install demucs
& ".\.venv\Scripts\python.exe" "stt_engine\preload_model.py"

dotnet restore

Write-Host ""
Write-Host "Setup complete. Start the app with: .\scripts\start.ps1"
