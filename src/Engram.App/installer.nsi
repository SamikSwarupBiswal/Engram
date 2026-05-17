; Engram NSIS Installer Script
; Produces a ~100MB self-contained installer
; Bundles: Tauri app + .NET runtime + LLamaSharp + sidecar
; Model (Phi-4-mini, ~2.2GB) downloaded on first launch

!include "MUI2.nsh"
!include "FileFunc.nsh"

Name "Engram"
OutFile "Engram_1.0.0_x64-setup.exe"
InstallDir "$LOCALAPPDATA\Engram"
InstallDirRegKey HKCU "Software\Engram" ""
RequestExecutionLevel user

; Pages
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Engram" SecEngram
  SetOutPath "$INSTDIR"

  ; Tauri app (main executable)
  File "engram-app.exe"

  ; .NET API sidecar (self-contained with all DLLs)
  SetOutPath "$INSTDIR\sidecar"
  File /r "publish\*.*"

  ; Create Start Menu shortcut
  CreateDirectory "$SMPROGRAMS\Engram"
  CreateShortCut "$SMPROGRAMS\Engram\Engram.lnk" "$INSTDIR\engram-app.exe"
  CreateShortCut "$DESKTOP\Engram.lnk" "$INSTDIR\engram-app.exe"

  ; Write uninstaller
  WriteUninstaller "$INSTDIR\uninstall.exe"

  ; Registry for Add/Remove Programs
  WriteRegStr HKCU "Software\Engram" "" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" \
    "DisplayName" "Engram"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" \
    "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" \
    "DisplayVersion" "1.0.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" \
    "Publisher" "Engram"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" \
    "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" \
    "NoRepair" 1

  ; Get installed size
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" \
    "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\engram-app.exe"
  RMDir /r "$INSTDIR\sidecar"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\Engram\Engram.lnk"
  RMDir "$SMPROGRAMS\Engram"
  Delete "$DESKTOP\Engram.lnk"

  DeleteRegKey HKCU "Software\Engram"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram"
SectionEnd
