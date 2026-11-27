#define AppName "X16 For All Time"
#define AppVersion "1.0.6"
#define BinDir "..\Launcher\bin\Release"

[Setup]
AppId=CommanderX16ForRightNow
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Retrodores
AppPublisherURL=https://github.com/retrodores/x16-for-all-time
DefaultDirName={code:InstallDirectory}
UsePreviousAppDir=no
DefaultGroupName={#AppName}
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
CloseApplications=yes
OutputDir=..\dist
OutputBaseFilename=X16ForAllTime-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\retrodores.ico
WizardImageFile=..\Assets\retrodores-wizard.bmp
WizardSmallImageFile=..\Assets\retrodores-wizard-small.bmp
UninstallDisplayIcon={app}\X16ForAllTime.exe

[Files]
Source: "{#BinDir}\X16ForAllTime.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\X16ForAllTime.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\X16.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\X16ForAllTime.exe"

[InstallDelete]
Type: files; Name: "{app}\CommanderX16ForRightNow.exe"
Type: files; Name: "{app}\CommanderX16ForRightNow.exe.config"
Type: files; Name: "{userprograms}\Commander X16 For Right Now\Commander X16 For Right Now.lnk"
Type: dirifempty; Name: "{userprograms}\Commander X16 For Right Now"
Type: files; Name: "{userprograms}\Commander X16 For Now\Commander X16 For Now.lnk"
Type: dirifempty; Name: "{userprograms}\Commander X16 For Now"

[Run]
Filename: "{app}\X16ForAllTime.exe"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InstallDirectory(Param: String): String;
var
  Previous: String;
begin
  Result := ExpandConstant('{localappdata}\Programs\X16ForAllTime');
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\CommanderX16ForRightNow_is1',
      'Inno Setup: App Path', Previous) then
    if (Previous <> '') and (CompareText(Previous, ExpandConstant('{localappdata}\Programs\CommanderX16ForRightNow')) <> 0) then
      Result := Previous;
end;

function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox('Install Microsoft .NET Framework 4.8 or later, then run this setup again.', mbError, MB_OK);
end;
