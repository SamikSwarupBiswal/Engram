!include "MUI2.nsh"
!include "FileFunc.nsh"

Name "Engram"
OutFile "Engram_1.0.0_x64-setup.exe"
InstallDir "$LOCALAPPDATA\Engram"
InstallDirRegKey HKCU "Software\Engram" ""
RequestExecutionLevel user

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Engram Application" SecEngram
  SetOutPath "$INSTDIR"
  File "engram-app.exe"
  File "download-model.ps1"

  SetOutPath "$INSTDIR\sidecar"
  File /r "publish\*.*"

  CreateDirectory "$SMPROGRAMS\Engram"
  CreateShortCut "$SMPROGRAMS\Engram\Engram.lnk" "$INSTDIR\engram-app.exe"
  CreateShortCut "$DESKTOP\Engram.lnk" "$INSTDIR\engram-app.exe"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr HKCU "Software\Engram" "" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" "DisplayName" "Engram"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" "DisplayVersion" "1.0.0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram" "Publisher" "Engram"
SectionEnd

Section "Download AI Model (2.3 GB)" SecModel
  DetailPrint "Downloading Phi-4-mini AI model (2.3 GB)..."
  DetailPrint "This may take a few minutes depending on your internet speed."
  nsExec::ExecToLog 'powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File "$INSTDIR\download-model.ps1"'
  Pop $0
  ${If} $0 == "0"
    DetailPrint "Model downloaded successfully."
  ${Else}
    DetailPrint "Model download will complete when you first open Engram."
  ${EndIf}
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecEngram} "Install Engram application and API sidecar."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecModel} "Download the Phi-4-mini AI model (2.3 GB). Required for local chat. Can be skipped and downloaded later."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  Delete "$INSTDIR\engram-app.exe"
  Delete "$INSTDIR\download-model.ps1"
  RMDir /r "$INSTDIR\sidecar"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\Engram\Engram.lnk"
  RMDir "$SMPROGRAMS\Engram"
  Delete "$DESKTOP\Engram.lnk"

  DeleteRegKey HKCU "Software\Engram"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Engram"
SectionEnd

Function .onInstSuccess
  MessageBox MB_YESNO "Installation complete! Open Engram now?" IDNO NoLaunch
    Exec "$INSTDIR\engram-app.exe"
  NoLaunch:
FunctionEnd
