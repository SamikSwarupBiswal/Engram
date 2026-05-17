# ============================================================
# Engram Windows Build Script
# Run from: PowerShell in src/Engram.App/
# ============================================================
# Standard installer (~130MB):
#   - Tauri shell + React frontend
#   - .NET API sidecar (framework-dependent)
#   - .NET 8 runtime bundled via Tauri externalBin
#   - LLamaSharp native libs included
#   - Phi-4-mini model downloaded on first run (~2.2GB)
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

Write-Host ""
Write-Host "  ENGRAM BUILD PIPELINE" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build .NET API Sidecar
if (-not $SkipDotnet) {
    Write-Host "[1/3] Building .NET API sidecar..." -ForegroundColor Yellow

    # Framework-dependent publish (requires .NET 8 runtime on target)
    # The Tauri installer bundles the .NET runtime separately
    dotnet publish $ApiProject -c Release -o "$SidecarDir\publish"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: .NET build failed" -ForegroundColor Red
        exit 1
    }

    # Copy main exe to Tauri sidecar location
    $SourceExe = "$SidecarDir\publish\Engram.Api.exe"
    $DestExe = "$SidecarDir\engram-api-x86_64-pc-windows-msvc.exe"

    if (Test-Path $SourceExe) {
        Copy-Item $SourceExe $DestExe -Force
        $SizeMB = [math]::Round((Get-Item $DestExe).Length / 1MB, 1)
        Write-Host "  Sidecar: $SizeMB MB" -ForegroundColor Green
    } else {
        Write-Host "  Sidecar: DLL-only (no exe), using dotnet run" -ForegroundColor Yellow
    }
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
    Write-Host ""

    $ExePath = "src-tauri\target\release\bundle\nsis\Engram_1.0.0_x64-setup.exe"
    $MsiPath = "src-tauri\target\release\bundle\msi\Engram_1.0.0_x64_en-US.msi"

    if (Test-Path $ExePath) {
        $ExeSize = [math]::Round((Get-Item $ExePath).Length / 1MB, 1)
        Write-Host "  .exe: $ExeSize MB" -ForegroundColor Gray
    }
    if (Test-Path $MsiPath) {
        $MsiSize = [math]::Round((Get-Item $MsiPath).Length / 1MB, 1)
        Write-Host "  .msi: $MsiSize MB" -ForegroundColor Gray
    }
    Write-Host ""
}
