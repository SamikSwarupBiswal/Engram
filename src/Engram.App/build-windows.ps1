# ============================================================
# Engram Windows Build Script
# Run from: PowerShell in src/Engram.App/
# ============================================================
# This script:
#   1. Builds the .NET API sidecar (self-contained exe)
#   2. Copies it into Tauri's sidecar directory
#   3. Runs `npm run tauri build` to produce the installer
# ============================================================

param(
    [switch]$Dev,       # Use `tauri dev` instead of `tauri build`
    [switch]$SkipDotnet # Skip .NET build (if already built)
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$SidecarDir = "$PSScriptRoot\src-tauri\sidecar"
$ApiProject = "$Root\Engram.Api\Engram.Api.csproj"

Write-Host ""
Write-Host "  ╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "  ║        ENGRAM BUILD PIPELINE         ║" -ForegroundColor Cyan
Write-Host "  ╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Build .NET API Sidecar ──
if (-not $SkipDotnet) {
    Write-Host "[1/3] Building .NET API sidecar..." -ForegroundColor Yellow
    
    # Detect target triple
    $TargetTriple = "x86_64-pc-windows-msvc"
    
    # Build self-contained single-file exe
    dotnet publish $ApiProject `
        -c Release `
        -r $TargetTriple `
        --self-contained false `
        -o "$SidecarDir\publish" `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: .NET build failed" -ForegroundColor Red
        exit 1
    }
    
    # Copy to Tauri sidecar location with target triple suffix
    # Tauri expects: sidecar/engram-api-{target_triple}[.exe]
    $SourceExe = "$SidecarDir\publish\Engram.Api.exe"
    $DestExe = "$SidecarDir\engram-api-$TargetTriple.exe"
    
    Copy-Item $SourceExe $DestExe -Force
    Write-Host "  -> Sidecar built: $DestExe" -ForegroundColor Green
    Write-Host "  -> Size: $([math]::Round((Get-Item $DestExe).Length / 1MB, 1)) MB" -ForegroundColor Gray
} else {
    Write-Host "[1/3] Skipping .NET build (--SkipDotnet)" -ForegroundColor Gray
}

# ── Step 2: Install npm dependencies ──
Write-Host "[2/3] Checking npm dependencies..." -ForegroundColor Yellow
if (-not (Test-Path "node_modules")) {
    Write-Host "  -> Running npm install..." -ForegroundColor Gray
    npm install
}
Write-Host "  -> Dependencies ready" -ForegroundColor Green

# ── Step 3: Build Tauri App ──
Write-Host "[3/3] Building Tauri desktop app..." -ForegroundColor Yellow

if ($Dev) {
    Write-Host "  -> Starting Tauri dev mode..." -ForegroundColor Cyan
    npm run tauri dev
} else {
    npm run tauri build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Tauri build failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "  ║         BUILD COMPLETE               ║" -ForegroundColor Green
    Write-Host "  ╚══════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Installers at:" -ForegroundColor White
    Write-Host "    src-tauri	argeteleaseundle" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  .msi installer  — for enterprise/IT" -ForegroundColor Gray
    Write-Host "  .exe installer  — for end users" -ForegroundColor Gray
    Write-Host ""
}
