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

# Use --unreleased so git-cliff shows commits not yet tagged (or ALL if no tags exist)
& $cliffExe --unreleased --output "$OutputFile"

if ($Tag) {
  # Stamp the version header since --unreleased outputs "## [unreleased]"
  $content = Get-Content $OutputFile -Raw
  $today = Get-Date -Format "yyyy-MM-dd"
  $ver = $Tag -replace "^v", ""
  $content = $content -replace '^## \[unreleased\]', "## [$ver] - $today"
  Set-Content $OutputFile $content
}

Write-Host "Changelog written to $OutputFile" -ForegroundColor Green
