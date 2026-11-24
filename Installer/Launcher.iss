#define AppName "Commander X16 For Right Now"
#define AppVersion "0.6.3"
#define BinDir "..\Launcher\bin\Release"

[Setup]
AppId=CommanderX16ForRightNow
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\CommanderX16ForRightNow
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
CloseApplications=yes
OutputDir=..\dist
OutputBaseFilename=CommanderX16ForRightNow-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\CommanderX16ForRightNow.exe

[Files]
Source: "{#BinDir}\CommanderX16ForRightNow.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\CommanderX16ForRightNow.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\X16.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\CommanderX16ForRightNow.exe"

[Run]
Filename: "{app}\CommanderX16ForRightNow.exe"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox('Install Microsoft .NET Framework 4.8 or later, then run this setup again.', mbError, MB_OK);
end;
