; LaserWorks Manager — Windows installer (NSIS 3)
; Build: makensis -DVERSION=1.0.0 -DDISPLAYVERSION=1.0.0-rc1 -DSOURCE=<publish folder> -DOUTFILE=<path>\LaserWorksManagerSetup.exe LaserWorksManager.nsi

Unicode true
!include "MUI2.nsh"
!include "x64.nsh"

!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef DISPLAYVERSION
  !define DISPLAYVERSION "${VERSION}"
!endif
!ifndef SOURCE
  !define SOURCE "..\artifacts\publish\win-x64"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\artifacts\release\LaserWorksManagerSetup.exe"
!endif

!define APPNAME "LaserWorks Manager"
!define EXENAME "LaserWorksManager.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\LaserWorksManager"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\${APPNAME}"
InstallDirRegKey HKLM "Software\LaserWorksManager" "InstallDir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "${APPNAME} ${DISPLAYVERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "ProductVersion" "${DISPLAYVERSION}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "LegalCopyright" "Copyright (c) 2026 LaserWorks"

!define MUI_ICON "..\src\LaserWorks.Desktop\Assets\app.ico"
!define MUI_UNICON "..\src\LaserWorks.Desktop\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APPNAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE-NOTICES.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Arabic"

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${APPNAME} requires 64-bit Windows 10 or later."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

Section "Install"
  SetOutPath "$INSTDIR"
  File "${SOURCE}\${EXENAME}"
  File "..\LICENSE-NOTICES.txt"
  File "..\README.md"
  SetOutPath "$INSTDIR\docs"
  File "..\docs\*.md"
  SetOutPath "$INSTDIR\demo"
  File "..\artifacts\demo\LaserWorksDemo.lwbak"

  ; application data (database, attachments, backups, logs) lives in %LOCALAPPDATA%\LaserWorksManager and is never removed by setup
  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\User Guide.lnk" "$INSTDIR\docs\USER_GUIDE.md"
  CreateShortcut "$SMPROGRAMS\${APPNAME}\Uninstall ${APPNAME}.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\LaserWorksManager" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayVersion" "${DISPLAYVERSION}"
  WriteRegStr HKLM "${UNINSTKEY}" "Publisher" "LaserWorks"
  WriteRegStr HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXENAME}"
  WriteRegStr HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetRegView 64
  Delete "$INSTDIR\${EXENAME}"
  Delete "$INSTDIR\LICENSE-NOTICES.txt"
  Delete "$INSTDIR\README.md"
  RMDir /r "$INSTDIR\docs"
  RMDir /r "$INSTDIR\demo"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$DESKTOP\${APPNAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APPNAME}"
  DeleteRegKey HKLM "${UNINSTKEY}"
  DeleteRegKey HKLM "Software\LaserWorksManager"
  ; the company database in %LOCALAPPDATA%\LaserWorksManager is intentionally kept
SectionEnd
