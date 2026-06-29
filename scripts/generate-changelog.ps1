param(
  [string]$OutputFile = "CHANGELOG.md",
  [string]$Tag = ""
)

$ErrorActionPreference = "Stop"
$cliffUrl = "https://github.com/orhun/git-cliff/releases/download/v2.8.0/git-cliff-2.8.0-x86_64-pc-windows-msvc.zip"
$cliffDir = "$env:TEMP\git-cliff"
$cliffExe = "$cliffDir\git-cliff-2.8.0\git-cliff.exe"

if (-not (Test-Path $cliffExe)) {
  Remove-Item -Force $cliffDir -Recurse -ErrorAction SilentlyContinue
  $zip = "$cliffDir.zip"
  Write-Host "Downloading git-cliff..." -ForegroundColor Cyan
  Remove-Item -Force $zip -ErrorAction SilentlyContinue
  Invoke-WebRequest -Uri $cliffUrl -OutFile $zip -UseBasicParsing
  Expand-Archive $zip -DestinationPath $cliffDir -Force
  Remove-Item $zip -Force
}

if ($Tag) {
  & $cliffExe --tag "$Tag" --output "$OutputFile"
} else {
  & $cliffExe --unreleased --output "$OutputFile"
}

Write-Host "Changelog written to $OutputFile" -ForegroundColor Green
