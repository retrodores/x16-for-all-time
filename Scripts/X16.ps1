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

function Download-X16File([uri]$Uri, [string]$Destination, [int]$Timeout) {
    if ($Uri.Scheme -notin @('https', 'http') -or !$Uri.Host -or $Uri.UserInfo) {
        throw 'The download requires an HTTP or HTTPS URL without credentials.'
    }
    Invoke-WebRequest -UseBasicParsing -TimeoutSec $Timeout -Uri $Uri -OutFile $Destination
}

function Install-X16Release($Release, [string]$Destination, [bool]$Overwrite, [bool]$SerialOverlay) {
    $uri = [uri]$Release.Url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or
        $uri.AbsolutePath -notmatch '^/X16Community/x16-emulator/releases/download/[^/]+/[^/]+\.zip$') {
        throw 'The selected download is not an official X16Community release ZIP.'
    }
    if ($SerialOverlay -and ($uri.AbsolutePath -cne '/X16Community/x16-emulator/releases/download/r49/x16emu_win64-r49.zip' -or
        $Release.Tag -ne 'r49' -or $Release.Asset -ne 'x16emu_win64-r49.zip' -or ![Environment]::Is64BitOperatingSystem)) {
        throw 'The Serial/Network overlay requires R49 Win64.'
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
        Download-X16File $uri $zip 120
        $step = 'Checksum verification'
        Show-Stage "Verifying $($Release.Tag)..."
        if ($SerialOverlay -and (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne
            '051047e59d86149e23471dce93f9f5511ef1e70897ac3a61b5b5a9c2cd397a1e') {
            throw 'The R49 Win64 ZIP did not match the checksum for the supported overlay base.'
        }
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
        if ($SerialOverlay) {
            $step = 'Serial/Network overlay download'
            Show-Stage "Downloading Serial/Network overlay 0.2.0..."
            $overlayZip = Join-Path $stage 'serial-network.zip'
            $overlayFiles = Join-Path $stage 'serial-network'
            Download-X16File 'https://github.com/oodler577/x16-emulator/releases/download/serial-network-v0.2.0/x16emu-serial-network-windows-x86_64.zip' $overlayZip 120
            $step = 'Serial/Network overlay verification'
            Show-Stage "Verifying Serial/Network overlay..."
            if ((Get-FileHash -LiteralPath $overlayZip -Algorithm SHA256).Hash -ne
                '89c8b02ed3bdfa9136630af133b10a568d804ad23d564e816656af3945a6b0a6') {
                throw 'The Serial/Network ZIP did not match the published checksum.'
            }
            $step = 'Serial/Network overlay extraction'
            Show-Stage "Applying Serial/Network overlay..."
            [IO.Compression.ZipFile]::ExtractToDirectory($overlayZip, $overlayFiles)
            if (!(Test-Path -LiteralPath (Join-Path $overlayFiles 'x16emu.exe') -PathType Leaf) -or
                !(Test-Path -LiteralPath (Join-Path $exes[0].DirectoryName 'rom.bin') -PathType Leaf)) {
                throw 'The overlay must contain x16emu.exe and the R49 base must contain rom.bin.'
            }
            Get-ChildItem -LiteralPath $overlayFiles -Force | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $exes[0].DirectoryName -Recurse -Force
            }
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
    if ($request.Action -eq 'SdCardTree') {
        $headers = @{ 'User-Agent' = 'X16ForAllTime'; Accept = 'application/vnd.github+json'; 'Cache-Control' = 'no-cache' }
        $repoApi = 'https://api.github.com/repos/cx16forum/sdcard'
        $revision = (Invoke-RestMethod -UseBasicParsing -TimeoutSec 30 -Uri "$repoApi/git/ref/heads/main" -Headers $headers).object.sha
        if ($revision -cnotmatch '^[0-9a-f]{40}$') { throw 'GitHub returned an invalid SD Card Kit revision.' }
        $tree = Invoke-RestMethod -UseBasicParsing -TimeoutSec 30 -Uri "$repoApi/git/trees/${revision}?recursive=1" -Headers $headers
        $listing = @{ revision = $revision; tree = $tree.tree; truncated = $tree.truncated }
        [IO.File]::WriteAllText($ResultFile, ($listing | ConvertTo-Json -Depth 5 -Compress), $utf8)
        exit 0
    }
    if ($request.Action -eq 'Catalog') {
        $catalogUrl = 'https://x16files.com/directory/data/launcher.json'
        $headers = @{ 'Cache-Control' = 'no-cache'; Pragma = 'no-cache' }
        Invoke-WebRequest -UseBasicParsing -TimeoutSec 30 -Uri $catalogUrl -Headers $headers -OutFile $ResultFile
        exit 0
    }
    $result = switch ($request.Action) {
        'Download' { Download-X16File $request.Url $request.Destination 120; @{} }
        'SdCardArchive' { Download-X16File $request.Url $request.Destination 300; @{} }
        'List' { @{ Releases = @(Get-X16Releases) } }
        'Install' { Install-X16Release $request.Release $request.Destination $request.Overwrite ([bool]$request.SerialOverlay) }
        default { throw 'Unknown launcher operation.' }
    }
    $message = $result | ConvertTo-Json -Depth 6 -Compress
    [IO.File]::WriteAllText($ResultFile, $message, $utf8)
}
catch {
    [IO.File]::WriteAllText($ResultFile, $_.Exception.Message, $utf8)
    # Exit code 3 reports HTTP 403 for a program download to the launcher.
    if ($request.Action -in @('Download', 'SdCardArchive') -and [int]$_.Exception.Response.StatusCode -eq 403) {
        exit 3
    }
    exit 1
}
