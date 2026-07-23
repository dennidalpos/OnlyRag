; OnlyRag NSIS Installer Script
; Target Architecture: Windows x64 (win-x64)

!define APP_NAME "OnlyRag"
!define APP_PUBLISHER "OnlyRag"
!define APP_URL "https://github.com/dennidalpos/OnlyRag"
!define APP_EXE "OnlyRag.App.exe"

!ifndef APP_VERSION
  !define APP_VERSION "0.1.0"
!endif

!ifndef RUNTIME_IDENTIFIER
  !define RUNTIME_IDENTIFIER "win-x64"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\artifacts\publish\OnlyRag\win-x64"
!endif

!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "..\artifacts\installer"
!endif

!ifndef APP_ICON
  !define APP_ICON "..\src\OnlyRag.App\Assets\OnlyRag.ico"
!endif

; General Configuration
Name "${APP_NAME}"
OutFile "${OUTPUT_DIR}\OnlyRag-Setup-${APP_VERSION}-${RUNTIME_IDENTIFIER}.exe"
InstallDir "$LOCALAPPDATA\Programs\OnlyRag"
InstallDirRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "InstallLocation"
RequestExecutionLevel user
Unicode true

; Icon Configuration
Icon "${APP_ICON}"
UninstallIcon "${APP_ICON}"

; Pages
Page directory
Page instfiles

UninstPage uninstConfirm
UninstPage instfiles

; Language
LoadLanguageFile "${NSISDIR}\Contrib\Language files\English.nlf"

Section "MainSection" SEC01
    SetOutPath "$INSTDIR"
    File /r "${PUBLISH_DIR}\*.*"

    ; Create Start Menu and Desktop Shortcuts
    CreateDirectory "$SMPROGRAMS\OnlyRag"
    CreateShortCut "$SMPROGRAMS\OnlyRag\OnlyRag.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
    CreateShortCut "$DESKTOP\OnlyRag.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

    ; Write Uninstaller
    WriteUninstaller "$INSTDIR\uninstall.exe"

    ; Write Windows Add/Remove Programs Registry Entries
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "DisplayName" "${APP_NAME}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "Publisher" "${APP_PUBLISHER}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "URLInfoAbout" "${APP_URL}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "DisplayIcon" "$INSTDIR\${APP_EXE}"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "UninstallString" '"$INSTDIR\uninstall.exe"'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "InstallLocation" "$INSTDIR"
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "NoModify" 1
    WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag" "NoRepair" 1

    ; Post-install PaddleOCR initialization script run (if script exists)
    IfFileExists "$INSTDIR\scripts\ocr\install_ocr_runtime.ps1" 0 +2
        ExecWait 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\scripts\ocr\install_ocr_runtime.ps1" -RuntimeTarget auto'
SectionEnd

Section "Uninstall"
    ; Stop running process if any
    ExecWait 'powershell.exe -NoProfile -Command "Get-Process -Name OnlyRag.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue"'

    ; Remove Shortcuts
    Delete "$SMPROGRAMS\OnlyRag\OnlyRag.lnk"
    RMDir "$SMPROGRAMS\OnlyRag"
    Delete "$DESKTOP\OnlyRag.lnk"

    ; Remove Registry Keys
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\OnlyRag"

    ; Remove installed files and directory
    RMDir /r "$INSTDIR"
SectionEnd
