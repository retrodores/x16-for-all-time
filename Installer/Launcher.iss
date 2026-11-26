#define AppName "X16 For All Time"
#define AppVersion "0.7.26"
#define BinDir "..\Launcher\bin\Release"

[Setup]
AppId=CommanderX16ForRightNow
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Retrodores
AppPublisherURL=https://github.com/retrodores/x16-for-now
DefaultDirName={localappdata}\Programs\CommanderX16ForRightNow
DefaultGroupName={#AppName}
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
CloseApplications=yes
OutputDir=..\dist
OutputBaseFilename=X16ForAllTime-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\retrodores.ico
WizardImageFile=..\Assets\retrodores-wizard.bmp
WizardSmallImageFile=..\Assets\retrodores-wizard-small.bmp
UninstallDisplayIcon={app}\CommanderX16ForRightNow.exe

[Files]
Source: "{#BinDir}\CommanderX16ForRightNow.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\CommanderX16ForRightNow.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\X16.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\CommanderX16ForRightNow.exe"

[InstallDelete]
Type: files; Name: "{userprograms}\Commander X16 For Right Now\Commander X16 For Right Now.lnk"
Type: dirifempty; Name: "{userprograms}\Commander X16 For Right Now"
Type: files; Name: "{userprograms}\Commander X16 For Now\Commander X16 For Now.lnk"
Type: dirifempty; Name: "{userprograms}\Commander X16 For Now"

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
