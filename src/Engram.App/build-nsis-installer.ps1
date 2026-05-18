# Build custom NSIS installer with model download
$ErrorActionPreference = "Continue"
$PSScriptRoot = "C:\projects\Engram\Engram\src\Engram.App"
$SidecarDir = "$PSScriptRoot\src-tauri\sidecar"
$PublishDir = "$SidecarDir\publish"
$ApiProject = "C:\projects\Engram\Engram\src\Engram.Api\Engram.Api.csproj"
$InstallerDir = "$PSScriptRoot\installer-staging"
$LlamaNativeDir = "$env:USERPROFILE\.nuget\packages\llamasharp.backend.cpu\0.24.0\runtimes\win-x64\native\noavx"

Write-Host "  BUILDING CUSTOM INSTALLER" -ForegroundColor Cyan

# Step 1: Self-contained .NET publish
Write-Host "[1/4] Building self-contained .NET sidecar..." -ForegroundColor Yellow
dotnet publish $ApiProject -c Release -r win-x64 --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL" -ForegroundColor Red; exit 1 }
if (Test-Path $LlamaNativeDir) { Copy-Item "$LlamaNativeDir\*.dll" $PublishDir -Force }
$Size = [math]::Round((Get-ChildItem $PublishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 0)
Write-Host "  Sidecar: $Size MB" -ForegroundColor Green

# Step 2: Build Tauri release exe
Write-Host "[2/4] Building Tauri app..." -ForegroundColor Yellow
$env:PATH = "C:\Users\Samik\.cargo\bin;$env:PATH"
Set-Location $PSScriptRoot
npm run tauri build -- --no-bundle 2>$null
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL" -ForegroundColor Red; exit 1 }
Write-Host "  Tauri app built" -ForegroundColor Green

# Step 3: Stage files
Write-Host "[3/4] Staging files..." -ForegroundColor Yellow
if (Test-Path $InstallerDir) { Remove-Item $InstallerDir -Recurse -Force }
New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null

Copy-Item "src-tauri\target\release\engram-app.exe" "$InstallerDir\engram-app.exe" -Force
Copy-Item $PublishDir "$InstallerDir\publish" -Recurse -Force
Copy-Item "download-model.ps1" "$InstallerDir\download-model.ps1" -Force
Copy-Item "installer.nsi" "$InstallerDir\installer.nsi" -Force

$TotalSize = [math]::Round((Get-ChildItem $InstallerDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 0)
Write-Host "  Staged: $TotalSize MB" -ForegroundColor Green

# Step 4: Build NSIS installer
Write-Host "[4/4] Building NSIS installer..." -ForegroundColor Yellow
Set-Location $InstallerDir
$NsisExe = "C:\Program Files (x86)\NSIS\makensis.exe"
& $NsisExe "installer.nsi"
if ($LASTEXITCODE -eq 0) {
    $InstallerPath = "$InstallerDir\Engram_1.0.0_x64-setup.exe"
    $DestPath = "C:\projects\Engram\Engram\src\Engram.App\Engram_1.0.0_x64-setup.exe"
    if (Test-Path $InstallerPath) {
        Copy-Item $InstallerPath $DestPath -Force
        $InstallerSize = [math]::Round((Get-Item $DestPath).Length / 1MB, 0)
        Write-Host ""
        Write-Host "  INSTALLER BUILT" -ForegroundColor Green
        Write-Host "  $DestPath" -ForegroundColor White
        Write-Host "  Size: $InstallerSize MB" -ForegroundColor White
    }
} else {
    Write-Host "  NSIS build failed" -ForegroundColor Red
}

Set-Location $PSScriptRoot
