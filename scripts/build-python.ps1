$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$PythonDir = Join-Path $ProjectRoot "stt_engine\python"
$PythonExe = Join-Path $PythonDir "python.exe"

if (Test-Path $PythonExe) {
    $size = (Get-ChildItem $PythonDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host "Python bundle already exists at $PythonDir ($([math]::Round($size / 1MB)) MB)"
    exit 0
}

$PythonVersion = "3.12.4"
$Url = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
$TempZip = Join-Path $env:TEMP "python-embed-$PythonVersion.zip"
$TempDir = Join-Path $env:TEMP "python-embed-$PythonVersion"

Write-Host "Downloading embedded Python $PythonVersion..."
try {
    Invoke-WebRequest -Uri $Url -OutFile $TempZip -UseBasicParsing
} catch {
    Write-Host "Failed to download Python $PythonVersion from $Url"
    Write-Host "Check the URL or update the version number in build-python.ps1"
    exit 1
}

Write-Host "Extracting..."
Expand-Archive -Path $TempZip -DestinationPath $TempDir -Force

# Enable import site so pip works
$PthFile = Join-Path $TempDir "python312._pth"
(Get-Content $PthFile) -replace '^#(import site)$', '$1' | Set-Content $PthFile

# Add pip
$GetPipPy = Join-Path $env:TEMP "get-pip.py"
try {
    Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $GetPipPy -UseBasicParsing
} catch {
    Write-Host "Failed to download get-pip.py"
    exit 1
}

Write-Host "Installing pip..."
$proc = Start-Process -FilePath "$TempDir\python.exe" -ArgumentList $GetPipPy, "--no-warn-script-location" -Wait -NoNewWindow -PassThru
if ($proc.ExitCode -ne 0) {
    Write-Host "Failed to install pip"
    exit 1
}

# Pre-install setuptools and wheel so user doesn't need to download them
Write-Host "Pre-installing setuptools and wheel..."
$proc = Start-Process -FilePath "$TempDir\python.exe" -ArgumentList "-m", "pip", "install", "setuptools", "wheel" -Wait -NoNewWindow -PassThru
if ($proc.ExitCode -ne 0) {
    Write-Host "Warning: failed to pre-install setuptools/wheel"
}

# Copy to project
if (Test-Path $PythonDir) {
    Remove-Item $PythonDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PythonDir | Out-Null
Copy-Item -Path "$TempDir\*" -Destination $PythonDir -Recurse -Force

$size = (Get-ChildItem $PythonDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host "Python $PythonVersion bundled to stt_engine\python\ ($([math]::Round($size / 1MB)) MB)"

# Cleanup
Remove-Item $TempZip -Force -ErrorAction SilentlyContinue
Remove-Item $TempDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $GetPipPy -Force -ErrorAction SilentlyContinue
