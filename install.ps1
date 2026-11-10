[CmdletBinding(DefaultParameterSetName='File')]
param(
    [Parameter(Mandatory=$true,ParameterSetName='File')][string]$ArchivePath,
    [Parameter(Mandatory=$true,ParameterSetName='Url')][uri]$SourceUrl,
    [string]$Destination = (Join-Path $env:LOCALAPPDATA ('Programs/KIRIHARI-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSha256,
    [switch]$Launch
)
$ErrorActionPreference='Stop'
if ($env:OS -ne 'Windows_NT' -or ![Environment]::Is64BitOperatingSystem) { throw 'Windows x64 is required.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$installTarget=[IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $installTarget) { throw "Destination already exists. Choose a new folder: $installTarget" }
$installTemp=Join-Path ([IO.Path]::GetTempPath()) ('KIRIHARI-install-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $installTemp | Out-Null
$createdTarget=$false
function Download-Https([uri]$Url,[string]$File) {
    if ($Url.Scheme -ne 'https' -or $Url.UserInfo) { throw 'An HTTPS URL without credentials is required.' }
    Invoke-WebRequest -UseBasicParsing -Uri $Url.AbsoluteUri -OutFile $File -Headers @{'User-Agent'='KIRIHARI-Installer'}
}
try {
    if ($PSCmdlet.ParameterSetName -eq 'Url') {
        if ($SourceUrl.Scheme -ne 'https' -or $SourceUrl.UserInfo) { throw 'An HTTPS URL without credentials is required.' }
        $downloadUrl=$SourceUrl
        if ($SourceUrl.Host -eq 'github.com' -and $SourceUrl.AbsolutePath -notmatch '/releases/download/') {
            $parts=$SourceUrl.AbsolutePath.Trim('/').Split('/')
            if ($parts.Length -lt 2 -or $parts[0] -notmatch '^[A-Za-z0-9_.-]+$' -or $parts[1] -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Use a GitHub repository or release URL.' }
            $repo=$parts[1] -replace '\.git$',''
            $endpoint='https://api.github.com/repos/'+$parts[0]+'/'+$repo+'/releases/latest'
            if ($parts.Length -gt 2) {
                if ($parts.Length -eq 5 -and $parts[2] -eq 'releases' -and $parts[3] -eq 'tag') {
                    $endpoint='https://api.github.com/repos/'+$parts[0]+'/'+$repo+'/releases/tags/'+[uri]::EscapeDataString([uri]::UnescapeDataString($parts[4]))
                } elseif (!($parts.Length -eq 4 -and $parts[2] -eq 'releases' -and $parts[3] -eq 'latest') -and !($parts.Length -eq 3 -and $parts[2] -eq 'releases')) {
                    throw 'Use a repository URL, releases/latest, releases/tag/VERSION, or a direct release ZIP URL.'
                }
            }
            $release=Invoke-RestMethod -Uri $endpoint -Headers @{'User-Agent'='KIRIHARI-Installer'}
            $assets=@($release.assets | Where-Object { $_.name -match '^KIRIHARI-[0-9]+\.[0-9]+\.[0-9]+-windows-x64\.zip$' })
            if ($assets.Count -ne 1) { throw 'The release must contain exactly one KIRIHARI Windows x64 ZIP.' }
            $sums=@($release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' })
            if ($sums.Count -ne 1) { throw 'SHA256SUMS.txt is missing from this release.' }
            $sumFile=Join-Path $installTemp 'SHA256SUMS.txt'
            Download-Https ([uri]$sums[0].browser_download_url) $sumFile
            $pattern='^([a-fA-F0-9]{64})\s+\*?'+[regex]::Escape($assets[0].name)+'$'
            $matchesFound=@(Get-Content -LiteralPath $sumFile | Where-Object { $_ -match $pattern })
            if ($matchesFound.Count -ne 1) { throw 'The Windows ZIP checksum is missing or ambiguous.' }
            [void]($matchesFound[0] -match $pattern)
            $releaseHash=$Matches[1]
            if ($ExpectedSha256 -and $ExpectedSha256 -ne $releaseHash) { throw 'The supplied checksum differs from the release.' }
            $ExpectedSha256=$releaseHash
            $downloadUrl=[uri]$assets[0].browser_download_url
        }
        $ArchivePath=Join-Path $installTemp 'download.zip'
        Download-Https $downloadUrl $ArchivePath
    } else {
        $ArchivePath=(Resolve-Path -LiteralPath $ArchivePath).Path
        if (!$ExpectedSha256) {
            $sumFile=Join-Path (Split-Path -Parent $ArchivePath) 'SHA256SUMS.txt'
            if (Test-Path -LiteralPath $sumFile) {
                $pattern='^([a-fA-F0-9]{64})\s+\*?'+[regex]::Escape([IO.Path]::GetFileName($ArchivePath))+'$'
                $lines=@(Get-Content -LiteralPath $sumFile | Where-Object { $_ -match $pattern })
                if ($lines.Count -ne 1) { throw 'The ZIP checksum is missing or ambiguous.' }
                [void]($lines[0] -match $pattern); $ExpectedSha256=$Matches[1]
            }
        }
    }
    $actualHash=(Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if ($ExpectedSha256 -and $actualHash -ne $ExpectedSha256) { throw 'SHA-256 mismatch. Nothing was installed.' }
    if (!$ExpectedSha256) { Write-Warning 'No expected checksum was provided. Verify the download source before launching.' }
    $staging=Join-Path $installTemp 'extracted'
    New-Item -ItemType Directory -Path $staging | Out-Null
    $archive=[IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $seen=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        $total=0L
        foreach ($entry in $archive.Entries) {
            $name=$entry.FullName.Replace('\','/')
            if (!$name.StartsWith('KIRIHARI/') -or $name -match '(^|/)(\.\.?)(/|$)|:|[\x00-\x1f]' -or $name -match '(^|/)[^/]*[. ](/|$)') { throw 'Invalid archive path.' }
            if (($entry.ExternalAttributes -shr 16 -band 0xF000) -eq 0xA000) { throw 'Archive links are not allowed.' }
            $target=[IO.Path]::GetFullPath((Join-Path $staging $name))
            if (!$target.StartsWith($staging+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or !$seen.Add($target)) { throw 'Invalid or duplicate archive path.' }
            $total+=$entry.Length
            if ($total -gt 256MB -or $archive.Entries.Count -gt 1000) { throw 'Archive exceeds the application package limit.' }
            if ($name.EndsWith('/')) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$false)
        }
    } finally { $archive.Dispose() }
    $app=Join-Path $staging 'KIRIHARI'
    foreach ($required in @('KIRIHARI.exe','ui/index.html','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll','docs/KIRIHARI-クイックスタート.pdf','docs/KIRIHARI-詳しい使い方.pdf')) {
        if (!(Test-Path -LiteralPath (Join-Path $app $required) -PathType Leaf)) { throw "Not a complete KIRIHARI application ZIP: $required" }
    }
    # Creating the exact destination fails if another installation appeared meanwhile.
    New-Item -ItemType Directory -Path $installTarget -ErrorAction Stop | Out-Null
    $createdTarget=$true
    Get-ChildItem -LiteralPath $app -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $installTarget -Recurse -ErrorAction Stop }
    Write-Output "Installed: $installTarget"
    Write-Output "Archive SHA-256: $actualHash"
    Write-Output 'Open KIRIHARI.exe and follow the first-run setup. Microsoft dialogs may require your confirmation.'
    if ($Launch) { Start-Process -FilePath (Join-Path $installTarget 'KIRIHARI.exe') -WorkingDirectory $installTarget -WindowStyle Hidden }
} catch {
    if ($createdTarget) { Write-Warning "A partial installation may remain in $installTarget. User data was not overwritten." }
    throw
} finally {
    $resolvedTemp=[IO.Path]::GetFullPath($installTemp)
    if ($resolvedTemp.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolvedTemp).StartsWith('KIRIHARI-install-')) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
