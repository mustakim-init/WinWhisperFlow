$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$PublishDir = Join-Path $ProjectRoot "artifacts\publish\WinWhisperFlow"
$ZipPath = Join-Path $ProjectRoot "artifacts\WinWhisperFlow-portable.zip"

Set-Location $ProjectRoot

if (Test-Path ".\WebUI\package.json") {
    Push-Location ".\WebUI"
    npm run build
    Pop-Location
}

& ".\scripts\build-python.ps1"

dotnet publish ".\WinWhisperFlow.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $PublishDir

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath

Write-Host ""
Write-Host "Portable build created:"
Write-Host $ZipPath
