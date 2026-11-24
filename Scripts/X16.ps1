param(
    [Parameter(Mandatory = $true)][string]$RequestFile,
    [Parameter(Mandatory = $true)][string]$ResultFile
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

function Get-X16Releases {
    $headers = @{ 'User-Agent' = 'CommanderX16ForRightNow'; Accept = 'application/vnd.github+json' }
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

function Install-X16Release($Release, [string]$Destination) {
    $uri = [uri]$Release.Url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or
        $uri.AbsolutePath -notmatch '^/X16Community/x16-emulator/releases/download/[^/]+/[^/]+\.zip$') {
        throw 'The selected download is not an official X16Community release ZIP.'
    }
    $destinationPath = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    $parent = [IO.Directory]::GetParent($destinationPath)
    if ($null -eq $parent) { throw 'Choose a new folder under an install parent.' }
    if (Test-Path -LiteralPath $destinationPath) {
        throw "The destination already exists. Choose another folder or register its existing x16emu.exe. $destinationPath"
    }

    [void][IO.Directory]::CreateDirectory($parent.FullName)
    $stage = Join-Path $parent.FullName ('.x16-install-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stage)
    try {
        $zip = Join-Path $stage 'release.zip'
        $files = Join-Path $stage 'files'
        Invoke-WebRequest -UseBasicParsing -TimeoutSec 120 -Uri $uri -OutFile $zip
        if ($Release.Digest) {
            if ($Release.Digest -notmatch '^sha256:([0-9a-f]{64})$') { throw 'GitHub returned an unsupported asset digest.' }
            $expected = $Matches[1]
            if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) {
                throw 'The emulator download did not match the release checksum.'
            }
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $files)
        $exes = @(Get-ChildItem -LiteralPath $files -Recurse -File -Filter 'x16emu.exe')
        if ($exes.Count -ne 1) { throw 'Expected exactly one x16emu.exe in the release ZIP.' }
        $relativeExe = $exes[0].FullName.Substring($files.Length + 1)
        # The rename fails if another process has already created the destination.
        [IO.Directory]::Move($files, $destinationPath)
        Join-Path $destinationPath $relativeExe
    }
    finally {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    $request = [IO.File]::ReadAllText($RequestFile, $utf8) | ConvertFrom-Json
    $result = switch ($request.Action) {
        'List' { @{ Releases = @(Get-X16Releases) } }
        'Install' { @{ Exe = (Install-X16Release $request.Release $request.Destination) } }
        default { throw 'Unknown launcher operation.' }
    }
    $message = $result | ConvertTo-Json -Depth 6 -Compress
    [IO.File]::WriteAllText($ResultFile, $message, $utf8)
}
catch {
    [IO.File]::WriteAllText($ResultFile, $_.Exception.Message, $utf8)
    exit 1
}
