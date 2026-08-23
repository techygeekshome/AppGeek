; AppGeek installer - Inno Setup script
;
; Build it with:   iscc installer\AppGeek.iss
; It expects a published build to already exist at publish\standalone\AppGeek.exe
; (run build.cmd from the repo root first, or let the Release workflow do both).
;
; Design decisions worth knowing:
;   * PrivilegesRequired=admin. AppGeek's own manifest asks for administrator rights at
;     launch, because installing and updating software needs them. An installer that put
;     the app somewhere only the current user can write would be pretending otherwise, so
;     this installs machine-wide into Program Files like any other system tool.
;   * No bundled anything. No toolbars, no offers, no third-party installers, ever.
;   * Nothing is added to startup and no service is installed. AppGeek runs when you run it.
;   * winget is not bundled or installed here. AppGeek detects the App Installer package at
;     runtime and offers to fetch it from Microsoft if it is missing.

#define AppName        "AppGeek"
#define AppVersion     "1.0.0"
#define AppPublisher   "TechyGeeksHome"
#define AppURL         "https://techygeekshome.info/appgeek/"
#define AppSupportURL  "https://github.com/techygeekshome/AppGeek/issues"
#define AppUpdatesURL  "https://github.com/techygeekshome/AppGeek/releases"
#define AppExeName     "AppGeek.exe"

[Setup]
; Unique to AppGeek. Do NOT regenerate this for future versions - Windows uses it to
; recognise "this is an upgrade of the same app" rather than a second, separate install.
AppId={{3B9C51E4-7A62-4D18-9E0C-5F4A2D6B8C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppSupportURL}
AppUpdatesURL={#AppUpdatesURL}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup

DefaultDirName={autopf}\{#AppPublisher}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
AllowNoIcons=yes

; AppGeek needs administrator rights to do its job, so it installs machine-wide.
PrivilegesRequired=admin

LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=AppGeekSetup
SetupIconFile=..\icons\appgeek.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\publish\standalone\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";   DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.md";   Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";                        Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} on the web";             Filename: "{#AppURL}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";                  Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
