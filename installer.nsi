!include "MUI2.nsh"

Name "Skjermbilde.no"
OutFile "Skjermbilde-Setup.exe"
InstallDir "$PROGRAMFILES64\Skjermbilde"
InstallDirRegKey HKCU "Software\Skjermbilde" "InstallDir"
RequestExecutionLevel admin
Unicode True

!define MUI_ICON "assets\icon.ico"
!define MUI_UNICON "assets\icon.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Velkommen til Skjermbilde.no"
!define MUI_WELCOMEPAGE_TEXT "Denne veiviseren installerer Skjermbilde.no på datamaskinen din.$\r$\n$\r$\nSkjermbilde.no lar deg ta skjermbilder, redigere og dele dem enkelt."
!define MUI_FINISHPAGE_RUN "$INSTDIR\Skjermbilde.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Start Skjermbilde.no"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Norwegian"

Section "Install"
    SetOutPath "$INSTDIR"

    ; Kill running instance
    nsExec::ExecToLog 'taskkill /f /im Skjermbilde.exe'

    ; Copy files
    File /r "publish\*.*"

    ; Save install dir
    WriteRegStr HKCU "Software\Skjermbilde" "InstallDir" "$INSTDIR"

    ; Add/Remove Programs entry
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Skjermbilde" "DisplayName" "Skjermbilde.no"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Skjermbilde" "DisplayIcon" "$INSTDIR\Skjermbilde.exe"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Skjermbilde" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Skjermbilde" "Publisher" "Skjermbilde.no"
    WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Skjermbilde" "InstallLocation" "$INSTDIR"

    ; Start menu shortcut
    CreateDirectory "$SMPROGRAMS\Skjermbilde"
    CreateShortcut "$SMPROGRAMS\Skjermbilde\Skjermbilde.lnk" "$INSTDIR\Skjermbilde.exe" "" "$INSTDIR\Skjermbilde.exe"
    CreateShortcut "$SMPROGRAMS\Skjermbilde\Avinstaller.lnk" "$INSTDIR\Uninstall.exe"

    ; Create uninstaller
    WriteUninstaller "$INSTDIR\Uninstall.exe"
SectionEnd

Section "Uninstall"
    ; Kill running instance
    nsExec::ExecToLog 'taskkill /f /im Skjermbilde.exe'

    ; Remove from startup
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Skjermbilde"

    ; Remove files
    RMDir /r "$INSTDIR"

    ; Remove start menu
    RMDir /r "$SMPROGRAMS\Skjermbilde"

    ; Remove registry
    DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Skjermbilde"
    DeleteRegKey HKCU "Software\Skjermbilde"
SectionEnd
