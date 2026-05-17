# ============================================================
# Engram Windows Build Script
# Run from: PowerShell in src/Engram.App/
# ============================================================
# Standard installer: self-contained, ~100-130MB
# Phi-4-mini model downloaded on first run (~2.2GB)
# ============================================================

param(
    [switch]$Dev,
    [switch]$SkipDotnet
)

$ErrorActionPreference = "Stop"
$env:PATH = "C:\Users\Samik\.cargo\bin;$env:PATH"
$Root = Split-Path -Parent $PSScriptRoot
$SidecarDir = "$PSScriptRoot\src-tauri\sidecar"
$ApiProject = "$Root\Engram.Api\Engram.Api.csproj"
$LlamaNativeDir = "$env:USERPROFILE\.nuget\packages\llamasharp.backend.cpu\0.24.0\runtimes\win-x64\native\noavx"

Write-Host ""
Write-Host "  ENGRAM BUILD PIPELINE (Standard Installer)" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build .NET API Sidecar (self-contained)
if (-not $SkipDotnet) {
    Write-Host "[1/3] Building .NET API sidecar (self-contained, win-x64)..." -ForegroundColor Yellow

    $PublishDir = "$SidecarDir\publish"

    # Self-contained publish (without LLamaSharp backend packages to avoid DLL conflicts)
    dotnet publish $ApiProject -c Release -r win-x64 --self-contained true -o $PublishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: .NET build failed" -ForegroundColor Red
        exit 1
    }

    # Copy LLamaSharp native DLLs (noavx variant only — safest CPU fallback)
    if (Test-Path $LlamaNativeDir) {
        Copy-Item "$LlamaNativeDir\*.dll" $PublishDir -Force
        Write-Host "  Copied LLamaSharp native DLLs (noavx)" -ForegroundColor Gray
    }

    # Copy publish output to Tauri sidecar location
    # Tauri expects: sidecar/engram-api-{target_triple}[.exe]
    $DestExe = "$SidecarDir\engram-api-x86_64-pc-windows-msvc.exe"
    $SourceExe = "$PublishDir\Engram.Api.exe"

    if (Test-Path $SourceExe) {
        Copy-Item $SourceExe $DestExe -Force
    }

    $TotalSize = [math]::Round((Get-ChildItem $PublishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 0)
    Write-Host "  Sidecar: $TotalSize MB (self-contained + LLamaSharp)" -ForegroundColor Green
} else {
    Write-Host "[1/3] Skipping .NET build" -ForegroundColor Gray
}

# Step 2: npm dependencies
Write-Host "[2/3] Checking npm dependencies..." -ForegroundColor Yellow
Set-Location $PSScriptRoot
if (-not (Test-Path "node_modules")) {
    npm install
}
Write-Host "  Dependencies ready" -ForegroundColor Green

# Step 3: Build Tauri App
Write-Host "[3/3] Building Tauri desktop app..." -ForegroundColor Yellow
Set-Location $PSScriptRoot

if ($Dev) {
    npm run tauri dev
} else {
    npm run tauri build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Tauri build failed" -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host "  BUILD COMPLETE" -ForegroundColor Green
    Write-Host "  Variant: Standard (self-contained, no .NET install needed)" -ForegroundColor White
    Write-Host "  Model:   Downloaded on first run (~2.2GB)" -ForegroundColor White
    Write-Host ""

    $ExePath = "src-tauri\target\release\bundle\nsis\Engram_1.0.0_x64-setup.exe"
    $MsiPath = "src-tauri\target\release\bundle\msi\Engram_1.0.0_x64_en-US.msi"

    if (Test-Path $ExePath) {
        $ExeSize = [math]::Round((Get-Item $ExePath).Length / 1MB, 0)
        Write-Host "  .exe installer:  $ExeSize MB" -ForegroundColor Gray
    }
    if (Test-Path $MsiPath) {
        $MsiSize = [math]::Round((Get-Item $MsiPath).Length / 1MB, 0)
        Write-Host "  .msi installer:  $MsiSize MB" -ForegroundColor Gray
    }
    Write-Host ""
}
