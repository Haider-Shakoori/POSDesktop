#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "."
#endif
#ifndef DeploymentMode
  #define DeploymentMode "Standalone"
#endif
#ifndef ModeLabel
  #define ModeLabel "Standalone"
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "BusinessOS-POS-Setup"
#endif

[Setup]
AppId={{C4F228C8-B208-492F-8D16-5E8F72B3848E}
AppName=BusinessOS POS
AppVersion={#MyAppVersion}
AppPublisher=BusinessOS.af
AppPublisherURL=https://businessos.af
VersionInfoCompany=BusinessOS.af
VersionInfoDescription=BusinessOS Point of Sale
VersionInfoProductName=BusinessOS POS
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={autopf}\BusinessOS\POS
DefaultGroupName=BusinessOS POS
UninstallDisplayName=BusinessOS POS ({#ModeLabel})
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\BusinessOS POS"; Filename: "{app}\BusinessOS.POS.exe"
Name: "{autodesktop}\BusinessOS POS"; Filename: "{app}\BusinessOS.POS.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BusinessOS.POS.exe"; Description: "Launch BusinessOS POS"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
