# ============================================================
# Engram Standard Installer Build Script
# Produces ~100MB self-contained installer
# ============================================================

param(
    [switch]$SkipDotnet,
    [switch]$SkipTauri
)

$ErrorActionPreference = "Stop"
$env:PATH = "C:\Users\Samik\.cargo\bin;$env:PATH"
$Root = Split-Path -Parent $PSScriptRoot
$SidecarDir = "$PSScriptRoot\src-tauri\sidecar"
$PublishDir = "$SidecarDir\publish"
$ApiProject = "$Root\Engram.Api\Engram.Api.csproj"
$InstallerDir = "$PSScriptRoot\installer-staging"
$LlamaNativeDir = "$env:USERPROFILE\.nuget\packages\llamasharp.backend.cpu\0.24.0\runtimes\win-x64\native\noavx"

Write-Host ""
Write-Host "  ENGRAM STANDARD INSTALLER BUILD" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build .NET self-contained sidecar
if (-not $SkipDotnet) {
    Write-Host "[1/4] Building .NET API sidecar (self-contained)..." -ForegroundColor Yellow
    dotnet publish $ApiProject -c Release -r win-x64 --self-contained true -o $PublishDir
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL" -ForegroundColor Red; exit 1 }

    # Copy LLamaSharp native DLLs
    if (Test-Path $LlamaNativeDir) {
        Copy-Item "$LlamaNativeDir\*.dll" $PublishDir -Force
        Write-Host "  + LLamaSharp native DLLs (noavx)" -ForegroundColor Gray
    }

    $Size = [math]::Round((Get-ChildItem $PublishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 0)
    Write-Host "  Sidecar: $Size MB" -ForegroundColor Green
}

# Step 2: Build Tauri app (release exe only, no installer)
if (-not $SkipTauri) {
    Write-Host "[2/4] Building Tauri app..." -ForegroundColor Yellow
    Set-Location $PSScriptRoot
    npx tauri build --no-bundle
    if ($LASTEXITCODE -ne 0) { Write-Host "FAIL" -ForegroundColor Red; exit 1 }
    Write-Host "  Tauri app built" -ForegroundColor Green
}

# Step 3: Stage files for NSIS
Write-Host "[3/4] Staging installer files..." -ForegroundColor Yellow
if (Test-Path $InstallerDir) { Remove-Item $InstallerDir -Recurse -Force }
New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null

# Copy Tauri app exe
Copy-Item "src-tauri\target\release\engram-app.exe" "$InstallerDir\engram-app.exe" -Force

# Copy .NET publish directory
Copy-Item $PublishDir "$InstallerDir\publish" -Recurse -Force

$TotalSize = [math]::Round((Get-ChildItem $InstallerDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 0)
Write-Host "  Staged: $TotalSize MB" -ForegroundColor Green

# Step 4: Build NSIS installer
Write-Host "[4/4] Building NSIS installer..." -ForegroundColor Yellow
Set-Location $InstallerDir

$NsisExe = "C:\Program Files (x86)\NSIS\makensis.exe"
if (-not (Test-Path $NsisExe)) {
    # Try to find NSIS in PATH
    $NsisExe = (Get-Command makensis -ErrorAction SilentlyContinue).Source
}

if ($NsisExe) {
    & $NsisExe "$PSScriptRoot\installer.nsi"
    if ($LASTEXITCODE -eq 0) {
        $InstallerPath = "$InstallerDir\Engram_1.0.0_x64-setup.exe"
        if (Test-Path $InstallerPath) {
            $InstallerSize = [math]::Round((Get-Item $InstallerPath).Length / 1MB, 0)
            Copy-Item $InstallerPath "$PSScriptRoot\Engram_1.0.0_x64-setup.exe" -Force
            Write-Host ""
            Write-Host "  INSTALLER BUILT" -ForegroundColor Green
            Write-Host "  Engram_1.0.0_x64-setup.exe = $InstallerSize MB" -ForegroundColor White
            Write-Host ""
        }
    }
} else {
    Write-Host "  NSIS not found. Install NSIS or use Tauri bundler." -ForegroundColor Yellow
    Write-Host "  Falling back to Tauri bundler..." -ForegroundColor Yellow
    Set-Location $PSScriptRoot
    npm run tauri build
}

Set-Location $PSScriptRoot
Write-Host "  Done." -ForegroundColor Green
