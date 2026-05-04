; Gausslite — Inno Setup script
; Per-user install (no admin / UAC). Output: GaussliteSetup-{version}.exe

#define AppName "Gausslite"
#define AppVersion "0.3.5"
#define AppPublisher "Mohamed Assem"
#define AppPublisherURL "https://github.com/mohamedasem318/Gausslite"
#define AppExeName "Gausslite.App.exe"

[Setup]
; AppId is the upgrade identity. Generated once and never changed so future installer
; versions auto-upgrade old installs in place.
AppId={{4BEC16D5-721D-4C2B-9B71-03CBF83653C6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppPublisherURL}
AppSupportURL={#AppPublisherURL}/issues
AppUpdatesURL={#AppPublisherURL}/releases
VersionInfoVersion=0.3.5.0
VersionInfoCompany={#AppName}
VersionInfoCopyright=(c) 2026 Mohamed Assem
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion=0.3.5.0

; Per-user install: no admin elevation, no UAC prompt.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22621

OutputDir=Output
OutputBaseFilename=GaussliteSetup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

WizardStyle=modern
SetupIconFile=..\src\Gausslite.App\Assets\tray-icon.ico
WizardSmallImageFile=wizard-small.bmp
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

; Skip the SxS finished-page nag and the "Run as administrator?" stuff.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
; All publish output (self-contained single-file + native sidecars) goes into {app}.
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Registry]
; Auto-start registry value (HKCU\...\Run\Gausslite) is *written* by the app at runtime
; via the in-app toggle. This entry exists only to remove the value on uninstall —
; deletevalue ensures the value is dropped, uninsdeletevalue removes any value the app
; wrote there.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#AppName}"; \
    Flags: dontcreatekey deletevalue uninsdeletevalue

[UninstallDelete]
; Per-user state directory holding settings.json (and any future per-user config).
; filesandordirs recursively deletes contents and the directory itself; if File
; Explorer is browsing the directory at uninstall time, the contents get deleted
; but the empty directory may remain — harmless cosmetic leftover.
Type: filesandordirs; Name: "{localappdata}\{#AppName}"
; Runtime-created log files in the install directory. Inno's [Files] cleanup only
; removes files installed by the .iss; logs created at runtime by the app are
; explicitly listed here so the install directory ends up empty and can be removed.
Type: files; Name: "{app}\gausslite-startup.log"
Type: files; Name: "{app}\gausslite-crash.log"
