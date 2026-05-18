# ============================================================
# Engram Model Download Script
# Downloads Phi-4-mini GGUF (~2.3GB) to %LOCALAPPDATA%/Engram/models/
# ============================================================

param(
    [switch]$Silent
)

$ErrorActionPreference = "Stop"
$ModelDir = "$env:LOCALAPPDATA\Engram\models"
$ModelFile = "$ModelDir\phi-4-mini-q4_k_m.gguf"
$ModelUrl = "https://huggingface.co/unsloth/Phi-4-mini-instruct-GGUF/resolve/main/Phi-4-mini-instruct-Q4_K_M.gguf"
$ExpectedSize = 2300000000

# Skip if already downloaded
if (Test-Path $ModelFile) {
    $ExistingSize = (Get-Item $ModelFile).Length
    if ($ExistingSize -gt $ExpectedSize * 0.9) {
        if (-not $Silent) {
            Write-Host "  Model already downloaded ($([math]::Round($ExistingSize / 1GB, 2)) GB)" -ForegroundColor Green
        }
        exit 0
    }
}

New-Item -ItemType Directory -Path $ModelDir -Force | Out-Null

if (-not $Silent) {
    Write-Host ""
    Write-Host "  DOWNLOADING ENGRAM MODEL" -ForegroundColor Cyan
    Write-Host "  Phi-4-mini GGUF Q4_K_M (~2.3 GB)" -ForegroundColor White
    Write-Host "  This may take a few minutes depending on your internet speed." -ForegroundColor Gray
    Write-Host ""
}

try {
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $ModelUrl -OutFile "$ModelFile.downloading" -UseBasicParsing

    if (Test-Path "$ModelFile.downloading") {
        Move-Item "$ModelFile.downloading" $ModelFile -Force
        $FinalSize = (Get-Item $ModelFile).Length
        if (-not $Silent) {
            Write-Host "  Model downloaded: $([math]::Round($FinalSize / 1GB, 2)) GB" -ForegroundColor Green
        }
        exit 0
    }
} catch {
    if (-not $Silent) {
        Write-Host "  Download failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Model will be downloaded when you first open Engram." -ForegroundColor Yellow
    }
    if (Test-Path "$ModelFile.downloading") {
        Remove-Item "$ModelFile.downloading" -Force -ErrorAction SilentlyContinue
    }
    exit 1
}
