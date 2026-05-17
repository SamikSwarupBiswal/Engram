$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64;" + $env:PATH
$env:PATH = "C:\Users\Samik\.cargo\bin;" + $env:PATH

cd C:\projects\Engram\Engram\src\Engram.App

Write-Host ""
Write-Host "  Building Engram Installer..." -ForegroundColor Cyan
Write-Host ""

npm run tauri build

Write-Host ""
Write-Host "  Build complete! Check src-tauri\target\release\bundle\" -ForegroundColor Green
