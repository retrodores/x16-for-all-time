param(
    [string]$MSBuild = 'msbuild.exe',
    [string]$ISCC = (Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Inno Setup 6\ISCC.exe')
)
$ErrorActionPreference = 'Stop'
& $MSBuild (Join-Path $PSScriptRoot 'Launcher\Launcher.csproj') /nologo /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'WinForms compilation failed.' }
& $ISCC (Join-Path $PSScriptRoot 'Installer\Launcher.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
