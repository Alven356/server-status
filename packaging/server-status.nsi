!ifndef PublishDir
  !error "PublishDir is required"
!endif
!ifndef OutputFile
  !error "OutputFile is required"
!endif
!ifndef IconFile
  !error "IconFile is required"
!endif

!include "MUI2.nsh"

!define PRODUCT_NAME "server-status"
!define PRODUCT_VERSION "2.1.0"
!define PRODUCT_PUBLISHER "server-status"
!define PRODUCT_EXE "server-status.exe"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\server-status"
!define MUI_ICON "${IconFile}"
!define MUI_UNICON "${IconFile}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${PRODUCT_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch server-status"

Name "${PRODUCT_NAME}"
OutFile "${OutputFile}"
InstallDir "$LOCALAPPDATA\Programs\server-status"
InstallDirRegKey HKCU "Software\server-status" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "server-status"

VIProductVersion "2.1.0.0"
VIAddVersionKey /LANG=1033 "ProductName" "server-status"
VIAddVersionKey /LANG=1033 "FileDescription" "Minecraft server availability monitor"
VIAddVersionKey /LANG=1033 "FileVersion" "2.1.0"
VIAddVersionKey /LANG=1033 "ProductVersion" "2.1.0"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright 2026 server-status"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "server-status" MainSection
  SetOutPath "$INSTDIR"
  File /r "${PublishDir}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall server-status.exe"
  WriteRegStr HKCU "Software\server-status" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "server-status"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${PRODUCT_EXE}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" "$\"$INSTDIR\Uninstall server-status.exe$\""
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\server-status"
  CreateShortcut "$SMPROGRAMS\server-status\server-status.lnk" "$INSTDIR\${PRODUCT_EXE}"
  CreateShortcut "$SMPROGRAMS\server-status\Uninstall server-status.lnk" "$INSTDIR\Uninstall server-status.exe"
  CreateShortcut "$DESKTOP\server-status.lnk" "$INSTDIR\${PRODUCT_EXE}"
SectionEnd

Section "Uninstall"
  ExecWait 'taskkill /IM "${PRODUCT_EXE}" /F'
  Delete "$DESKTOP\server-status.lnk"
  RMDir /r "$SMPROGRAMS\server-status"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "Software\server-status"
  RMDir /r "$INSTDIR"
SectionEnd
