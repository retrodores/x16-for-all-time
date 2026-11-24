param(
    [Parameter(Mandatory = $true)][string]$RequestFile,
    [Parameter(Mandatory = $true)][string]$ResultFile
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

function Get-X16Releases {
    $headers = @{ 'User-Agent' = 'X16ForAllTime'; Accept = 'application/vnd.github+json' }
    $catalog = Invoke-RestMethod -UseBasicParsing -TimeoutSec 30 -Headers $headers -Uri 'https://api.github.com/repos/X16Community/x16-emulator/releases?per_page=100'
    foreach ($release in $catalog) {
        if ($release.draft -or $release.prerelease) { continue }
        foreach ($asset in $release.assets) {
            if ($asset.name -notmatch '^x16emu[_-]win(32|64)[_.-].+\.zip$') { continue }
            [pscustomobject]@{
                Tag = $release.tag_name
                Asset = $asset.name
                Url = $asset.browser_download_url
                Digest = $asset.digest
            }
        }
    }
}

function Show-Stage([string]$Stage) {
    [Console]::WriteLine("X16-STAGE:$Stage")
}

function Install-X16Release($Release, [string]$Destination, [bool]$Overwrite) {
    $uri = [uri]$Release.Url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or
        $uri.AbsolutePath -notmatch '^/X16Community/x16-emulator/releases/download/[^/]+/[^/]+\.zip$') {
        throw 'The selected download is not an official X16Community release ZIP.'
    }
    $destinationPath = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    $parent = [IO.Directory]::GetParent($destinationPath)
    if ($null -eq $parent) { throw 'Choose a new folder under an install parent.' }
    if (Test-Path -LiteralPath $destinationPath) {
        $item = Get-Item -LiteralPath $destinationPath -Force
        if (!$Overwrite -or !$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "The destination already exists and cannot be replaced. $destinationPath"
        }
    }

    [void][IO.Directory]::CreateDirectory($parent.FullName)
    $stage = Join-Path $parent.FullName ('.x16-install-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stage)
    $step = 'Download'
    try {
        $zip = Join-Path $stage 'release.zip'
        $files = Join-Path $stage 'files'
        Show-Stage "Downloading $($Release.Tag)..."
        Invoke-WebRequest -UseBasicParsing -TimeoutSec 120 -Uri $uri -OutFile $zip
        $step = 'Checksum verification'
        Show-Stage "Verifying $($Release.Tag)..."
        if ($Release.Digest) {
            if ($Release.Digest -notmatch '^sha256:([0-9a-f]{64})$') { throw 'GitHub returned an unsupported asset digest.' }
            $expected = $Matches[1]
            if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) {
                throw 'The emulator download did not match the release checksum.'
            }
        }
        $step = 'ZIP extraction'
        Show-Stage "Extracting $($Release.Tag)..."
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $files)
        $exes = @(Get-ChildItem -LiteralPath $files -Recurse -File -Filter 'x16emu.exe')
        if ($exes.Count -ne 1) { throw 'Expected exactly one x16emu.exe in the release ZIP.' }
        $relativeExe = $exes[0].FullName.Substring($files.Length + 1)
        $step = 'DLL validation'
        Show-Stage "Checking emulator files..."
        if ($Release.Asset -ieq 'x16emu_win32-r49.zip' -and
            !(Test-Path -LiteralPath (Join-Path $exes[0].DirectoryName 'libgcc_s_dw2-1.dll') -PathType Leaf)) {
            throw 'R49 Win32 ZIP lacks libgcc_s_dw2-1.dll. Use R49 Win64 on 64-bit Windows or select a different Win32 build.'
        }
        $step = 'Installation'
        Show-Stage "Installing $($Release.Tag)..."
        $backup = $null
        if (Test-Path -LiteralPath $destinationPath) {
            $item = Get-Item -LiteralPath $destinationPath -Force
            if (!$Overwrite -or !$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "The destination already exists and cannot be replaced. $destinationPath"
            }
            $backup = $destinationPath + '.previous-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
            [IO.Directory]::Move($destinationPath, $backup)
        }
        try { [IO.Directory]::Move($files, $destinationPath) }
        catch {
            if ($backup) {
                try { [IO.Directory]::Move($backup, $destinationPath) }
                catch { throw "Could not restore the previous installation from $backup. $($_.Exception.Message)" }
            }
            throw
        }
        @{ Exe = (Join-Path $destinationPath $relativeExe); Backup = $backup }
    }
    catch { throw "$step failed. $($_.Exception.Message)" }
    finally {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    $request = [IO.File]::ReadAllText($RequestFile, $utf8) | ConvertFrom-Json
    $result = switch ($request.Action) {
        'List' { @{ Releases = @(Get-X16Releases) } }
        'Install' { Install-X16Release $request.Release $request.Destination $request.Overwrite }
        default { throw 'Unknown launcher operation.' }
    }
    $message = $result | ConvertTo-Json -Depth 6 -Compress
    [IO.File]::WriteAllText($ResultFile, $message, $utf8)
}
catch {
    [IO.File]::WriteAllText($ResultFile, $_.Exception.Message, $utf8)
    exit 1
}
