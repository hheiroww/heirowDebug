; Build through tools/Build-Release.ps1; every product supplies its own icon.
[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#ProductVersion}
AppPublisher=heirow
AppPublisherURL=https://github.com/hheiroww/heirowDebug
AppSupportURL=https://github.com/hheiroww/heirowDebug/issues
DefaultDirName={localappdata}\Programs\heirow\{#ProductFolder}
DefaultGroupName=heirow
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir={#OutputDirectory}
OutputBaseFilename={#ProductFolder}-{#ProductVersion}-windows-x64-Setup
SetupIconFile={#BrandIcon}
UninstallDisplayIcon={app}\{#ExecutableName}
VersionInfoVersion={#ProductVersion}
VersionInfoDescription={#ProductName} Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
Uninstallable=IsRegisteredInstall
CreateUninstallRegKey=IsRegisteredInstall
LicenseFile=..\LICENSE

[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\heirow\{#ProductName}"; Filename: "{app}\{#ExecutableName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#ExecutableName}"; Check: IsRegisteredInstall
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\{#ExecutableName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#ExecutableName}"; Tasks: desktopicon; Check: IsRegisteredInstall

[Run]
Filename: "{app}\{#ExecutableName}"; Description: "Launch {#ProductName}"; Flags: nowait postinstall skipifsilent; Check: IsRegisteredInstall

[Code]
function IsRegisteredInstall: Boolean;
begin
  // Portable mode also lets release validation exercise this exact installer
  // without registering an installation or creating user shortcuts.
  Result := ExpandConstant('{param:PORTABLE|0}') <> '1';
end;
