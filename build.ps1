<#
.SYNOPSIS
    Publishes SpriteForge as a self-contained single-file win-x64 executable.

.DESCRIPTION
    Produces ./bin/spriteforge.exe with all managed and native (SkiaSharp) dependencies
    bundled. No .NET runtime install is required on the target machine.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Output = "./bin"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$CliProject  = Join-Path $ProjectRoot "src/SpriteForge.Cli/SpriteForge.Cli.csproj"

Write-Host "Publishing SpriteForge ($Configuration, win-x64, self-contained)..." -ForegroundColor Cyan

dotnet publish $CliProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$Exe = Join-Path $Output "spriteforge.exe"
if (Test-Path $Exe) {
    Write-Host "Build succeeded: $Exe" -ForegroundColor Green
} else {
    Write-Error "Publish reported success but $Exe was not found."
    exit 1
}

# --- pixelart.exe: standalone pixel-art post-processing CLI ---
$PixelArtProject = Join-Path $ProjectRoot "src/PixelArt.Cli/PixelArt.Cli.csproj"

Write-Host "Publishing PixelArt CLI ($Configuration, win-x64, self-contained)..." -ForegroundColor Cyan

dotnet publish $PixelArtProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    Write-Error "PixelArt publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$PixelArtExe = Join-Path $Output "pixelart.exe"
if (Test-Path $PixelArtExe) {
    Write-Host "Build succeeded: $PixelArtExe" -ForegroundColor Green
} else {
    Write-Error "Publish reported success but $PixelArtExe was not found."
    exit 1
}
