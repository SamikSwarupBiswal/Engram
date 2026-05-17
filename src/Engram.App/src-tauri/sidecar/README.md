# Engram API Sidecar

This directory holds the compiled .NET API sidecar binary.

## Auto-build (recommended)

From PowerShell in `src/Engram.App/`:

```powershell
.uild-windows.ps1        # Full build + installer
.uild-windows.ps1 -Dev   # Dev mode (builds sidecar, runs tauri dev)
.\dev-windows.ps1          # Quick dev (skips sidecar rebuild)
```

## Manual build

```powershell
cd ..\..\Engram.Api
dotnet publish -c Release -r x86_64-pc-windows-msvc --self-contained false -o ..\Engram.App\src-tauri\sidecar\publish
copy ..\Engram.App\src-tauri\sidecar\publish\Engram.Api.exe ..\Engram.App\src-tauri\sidecar\engram-api-x86_64-pc-windows-msvc.exe
```

## How it works

1. User installs Engram via .msi/.exe installer
2. User opens Engram from Start Menu / Desktop shortcut
3. Tauri shell starts → spawns `engram-api` sidecar on port 5000
4. React frontend connects to `http://127.0.0.1:5000` automatically
5. User sees the chat interface — everything just works
6. When user closes Engram, sidecar is killed automatically

The user never sees localhost, never starts a server, never opens a terminal.
