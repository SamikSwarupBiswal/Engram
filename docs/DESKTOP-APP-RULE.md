# ENGRAM DESKTOP APP — HARD RULES

## Rule #1: Engram is a Desktop App

Engram is NEVER a localhost web app. It is a downloadable desktop application.

- Ships as Windows installer (.msi/.exe)
- Installed via standard Windows installer
- Opened from Start Menu or Desktop shortcut
- Tauri shell auto-spawns .NET backend on launch
- User NEVER starts a server manually
- User NEVER sees localhost URLs
- Backend connects to frontend automatically
- App just works after installation

## Rule #2: Sidecar Architecture

The .NET API runs as a Tauri sidecar (child process):

- Spawned on app startup by Tauri
- Bound to 127.0.0.1:5000 (localhost only)
- Killed when app closes
- Bundled in the installer binary
- No separate installation needed

## Rule #3: Build Pipeline

```powershell
.uild-windows.ps1        # Full build + installer
.uild-windows.ps1 -Dev   # Dev mode with hot reload
.\dev-windows.ps1          # Quick dev (skip .NET rebuild)
```

## Rule #4: No Web App Mode

There is no "web mode" for production. The browser localhost approach
is for DEVELOPMENT ONLY. Production is always the desktop installer.
