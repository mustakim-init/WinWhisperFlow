$ErrorActionPreference = "Stop"
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $ProjectRoot

& ".\scripts\publish.ps1"

# Read version from csproj
$csproj = [xml](Get-Content ".\WinWhisperFlow.csproj")
$version = $csproj.Project.PropertyGroup.VersionPrefix
if (-not $version) { $version = "0.1.0" }

# Pack Velopack delta packages for in-app updates
Write-Host "Packing Velopack release..."
dotnet tool install -g vpk --quiet 2>$null
vpk pack -u WinWhisperFlow -v $version -p "artifacts\publish\WinWhisperFlow" -o "artifacts\vpk" -i Icon.ico -e WinWhisperFlow.exe

$IsccCommand = Get-Command iscc -ErrorAction SilentlyContinue
$CandidatePaths = @(
    $IsccCommand.Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$IsccPath = $CandidatePaths | Select-Object -First 1
if (-not $IsccPath) {
    Write-Host "Inno Setup compiler was not found."
    Write-Host "Install it with: winget install JRSoftware.InnoSetup"
    Write-Host "If it is already installed, add its folder to PATH or edit scripts\build-installer.ps1."
    Write-Host "Then run this script again."
    exit 1
}

& $IsccPath ".\installer\WinWhisperFlow.iss"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Installer created under artifacts\installer."
