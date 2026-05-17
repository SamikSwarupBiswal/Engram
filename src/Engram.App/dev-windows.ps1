# Quick dev mode — builds sidecar once, then runs tauri dev
param([switch]$SkipDotnet)

$PSScriptRootuild-windows.ps1 -Dev -SkipDotnet:$SkipDotnet
