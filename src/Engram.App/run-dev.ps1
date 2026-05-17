$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64;" + $env:PATH
$env:PATH = "C:\Users\Samik\.cargo\bin;" + $env:PATH

cd C:\projects\Engram\Engram\src\Engram.App

Write-Host "=== Launching Engram Desktop App ===" -ForegroundColor Cyan
npm run tauri dev
