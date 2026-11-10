param(
    [switch]$Test,
    [switch]$Package,
    [switch]$VideoTest,
    [switch]$WindowTest,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$AppFolder = 'KIRIHARI'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$versionText = Get-Content -LiteralPath (Join-Path $root 'src/AssemblyInfo.cs') -Raw
if ($versionText -notmatch 'AssemblyInformationalVersion\("([0-9]+\.[0-9]+\.[0-9]+)"\)') { throw 'Missing release version.' }
$version = $Matches[1]
$fixtures = Join-Path $root 'samples'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $csc)) { throw '.NET Framework 4.8 x64 compiler is required.' }

$dist = Join-Path $root 'dist'
if ($AppFolder -like 'release-*') { throw 'AppFolder names starting with release- are reserved for distribution packages.' }
New-Item -ItemType Directory -Path $dist -Force | Out-Null
try {
    $buildLock = [IO.File]::Open((Join-Path $dist '.build.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
}
catch {
    if (($_.Exception.GetBaseException().HResult -band 0xffff) -notin @(32, 33)) { throw }
    throw 'Another build is using this workspace. Wait for it to finish before building again.'
}
try {
    $app = Join-Path $dist $AppFolder
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    $exe = Join-Path $app 'KIRIHARI.exe'
    $sdk = Join-Path $root '.tooling/webview2'
    if (!(Test-Path -LiteralPath (Join-Path $sdk 'lib/net462/Microsoft.Web.WebView2.Core.dll'))) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $sdkArchive = Join-Path $root '.tooling/webview2.nupkg'
        New-Item -ItemType Directory -Path (Split-Path -Parent $sdkArchive) -Force | Out-Null
        if (!(Test-Path -LiteralPath $sdkArchive)) {
            Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.2903.40/microsoft.web.webview2.1.0.2903.40.nupkg' -OutFile $sdkArchive
        }
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $sdkArchive).Hash -ne 'EF128016DD1E51C59178C827ED5B8AA3322C57AFA8675D930F8109505542AD74') {
            throw 'The WebView2 SDK package checksum does not match.'
        }
        [IO.Compression.ZipFile]::ExtractToDirectory($sdkArchive, $sdk)
    }
    $refs = @('System.Web.Extensions.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Xaml.dll',
        'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll',
        'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll') |
        ForEach-Object { '/reference:' + (Join-Path $framework $_) }
    $refs += @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll') |
        ForEach-Object { '/reference:' + (Join-Path $sdk "lib/net462/$_") }
    $sources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File | ForEach-Object FullName
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /utf8output /warn:4 /langversion:5 "/out:$exe" "/win32manifest:$root\src\app.manifest" "/win32icon:$root\src\assets\KIRIHARI.ico" "/resource:$root\src\assets\KIRIHARI.png,Kirihari.Icon.png" @refs @sources
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

    $sampleImages = @('samples/lightweight/images/transparent-badge.png', 'samples/lightweight/images/color-card.jpg')
    $bundle = @('README.md', 'LICENSE', 'THIRD-PARTY.md', 'CHANGELOG.md', 'INSTALL.md', 'AI_HANDOFF.md', 'AI_EDITING.md', 'AGENTS.md', 'install.ps1',
        'docs/SETUP.md', 'docs/KIRIHARI-クイックスタート.pdf',
        'docs/KIRIHARI-詳しい使い方.pdf',
        'docs/verification.md', 'docs/startup-verification.md', 'docs/verification-classic.md',
        'docs/design.md', 'docs/PUBLISHING.md', "docs/RELEASE-NOTES-$version.md",
        'samples/lightweight/cat-motion-short.mp4', 'samples/lightweight/insert-motion.mp4',
        'samples/lightweight/optical-motion.mp4', 'samples/lightweight/サンプル編集.kirihari') + $sampleImages
    foreach ($name in $bundle) {
        $from = Join-Path $root $name
        $to = Join-Path $app $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Copy-Item -LiteralPath $from -Destination $to -Force
    }

    $components = [ordered]@{
        'Microsoft.Web.WebView2.Core.dll' = 'lib/net462/Microsoft.Web.WebView2.Core.dll'
        'Microsoft.Web.WebView2.Wpf.dll' = 'lib/net462/Microsoft.Web.WebView2.Wpf.dll'
        'WebView2Loader.dll' = 'runtimes/win-x64/native/WebView2Loader.dll'
        'licenses/WebView2-LICENSE.txt' = 'LICENSE.txt'
        'licenses/WebView2-NOTICE.txt' = 'NOTICE.txt'
    }
    foreach ($entry in $components.Keys) {
        $to = Join-Path $app $entry
        New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $sdk $components[$entry]) -Destination $to -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $app 'ui') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'src/ui/index.html') -Destination (Join-Path $app 'ui/index.html') -Force
    $runtimeBundle = @($components.Keys) + @('ui/index.html')

    if ($Test) {
        $qa = Join-Path $dist 'qa'
        New-Item -ItemType Directory -Path $qa -Force | Out-Null
        $result = Start-Process -FilePath $exe -ArgumentList @('--qa', ('"' + $qa + '"'), ('"' + $fixtures + '"')) -PassThru -WindowStyle Hidden
        if (!$result.WaitForExit(60000)) {
            $result.Kill()
            $progress = Join-Path $qa 'progress.txt'
            if (Test-Path -LiteralPath $progress) { Get-Content -LiteralPath $progress -Tail 10 -Encoding UTF8 }
            throw 'The QA run timed out.'
        }
        $result.Refresh()
        if ($result.ExitCode -ne 0) {
            $failure = Join-Path $qa 'failed.txt'
            if (Test-Path -LiteralPath $failure) { Get-Content -LiteralPath $failure -Encoding UTF8 }
            throw "QA failed ($($result.ExitCode))."
        }
        Get-Content -LiteralPath (Join-Path $qa 'results.txt') -Encoding UTF8
    }

    if ($VideoTest) {
        $qa = Join-Path $dist 'video-qa'
        New-Item -ItemType Directory -Path $qa -Force | Out-Null
        $failure = Join-Path $qa 'video-failed.txt'
        if (Test-Path -LiteralPath $failure) { Remove-Item -LiteralPath $failure }
        $result = Start-Process -FilePath $exe -ArgumentList @('--video-qa', ('"' + $qa + '"'), ('"' + $fixtures + '"')) -PassThru -WindowStyle Hidden
        if (!$result.WaitForExit(120000)) { $result.Kill(); throw 'Video QA timed out.' }
        if ($result.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $qa 'video-failed.txt') -Encoding UTF8; throw 'Video QA failed.' }
        Get-Content -LiteralPath (Join-Path $qa 'video-results.txt') -Encoding UTF8
    }

    if ($WindowTest) {
        $qa = Join-Path $dist ('window-qa-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Path $qa | Out-Null
        $failure = Join-Path $qa 'window-failed.txt'
        $result = Start-Process -FilePath $exe -ArgumentList @('--window-qa', ('"' + $qa + '"'), ('"' + $fixtures + '"')) -PassThru -WindowStyle Hidden
        if (!$result.WaitForExit(240000)) { $result.Kill(); throw 'Window QA timed out.' }
        if ($result.ExitCode -ne 0) {
            if (Test-Path -LiteralPath $failure) { Get-Content -LiteralPath $failure -Encoding UTF8 }
            throw "Window QA failed (exit $($result.ExitCode))."
        }
        Write-Output "Window QA: $qa"
        Get-Content -LiteralPath (Join-Path $qa 'window-results.txt') -Encoding UTF8
    }

    if ($Package) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $sampleFiles = @('samples/README.md', 'samples/cat-motion.mp4', 'samples/insert-motion.mp4', 'samples/optical-motion.mp4')
        $sourceFiles = $bundle + $sampleFiles + @(
            '.gitignore', '.github/workflows/windows.yml', 'build.ps1',
            'src/app.manifest', 'src/assets/KIRIHARI.ico', 'src/assets/KIRIHARI.png', 'src/ui/index.html',
            'docs/setup-design.md', 'docs/make-manual.py', 'docs/make-quickstart.py', 'docs/manual_pdf.py',
            'docs/images/manual-output.png', 'docs/images/manual-palette.png', 'docs/images/manual-proxy.png',
            'docs/images/manual-rotation.png', 'docs/images/manual-times.png', 'docs/images/manual-trim.png',
            'docs/images/new-project-dialog.png', 'docs/images/quick-cat-frame.png', 'docs/images/quick-cut.png',
            'docs/images/quick-play.png', 'docs/images/setup-window.png',
            'samples/make-cat.py', 'samples/make-test-images.py', 'samples/optical-reference.png',
            'samples/光学ステージ_編集見本.kirihari'
        )
        $sourceFiles += $sources | ForEach-Object { 'src/' + [IO.Path]::GetFileName($_) }
        $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $root 'docs') -Filter 'RELEASE-NOTES-*.md' -File |
            Where-Object { $_.Name -match '^RELEASE-NOTES-\d+\.\d+\.\d+\.md$' -and $_.Name -ne "RELEASE-NOTES-$version.md" } |
            ForEach-Object { 'docs/' + $_.Name }
        $packages = @(
            @{ Name = "KIRIHARI-$version-windows-x64.zip"; Prefix = 'KIRIHARI'; Root = $app; Files = @('KIRIHARI.exe') + $bundle + $runtimeBundle }
            @{ Name = "KIRIHARI-$version-samples.zip"; Prefix = 'KIRIHARI-samples'; Root = $root; Files = $sampleFiles + $sampleImages + @('LICENSE') }
            @{ Name = "KIRIHARI-$version-source.zip"; Prefix = "KIRIHARI-$version-source"; Root = $root; Files = $sourceFiles }
        )
        $release = Join-Path $dist "release-$version"
        $sumName = 'SHA256SUMS.txt'
        $newVersion = 'Update src/AssemblyInfo.cs and build a new version to distribute changes.'

        function Get-PackageChecksums([string]$Directory) {
            foreach ($description in $packages) {
                $file = Join-Path $Directory $description.Name
                if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Incomplete release: $file" }
                '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), $description.Name
            }
        }

        if (Test-Path -LiteralPath $release) {
            $checksums = @(Get-PackageChecksums $release)
            $sumFile = Join-Path $release $sumName
            if (!(Test-Path -LiteralPath $sumFile -PathType Leaf) -or
                ([IO.File]::ReadAllLines($sumFile) -join "`n") -ne ($checksums -join "`n")) {
                throw "Release $version is incomplete or its ZIP checksums do not match. Existing files were kept: $release"
            }
            foreach ($description in $packages) {
                $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $release $description.Name))
                try {
                    if ($archive.Entries.Count -ne $description.Files.Count) {
                        throw "Release $version already exists with a different file list in $($description.Name). $newVersion"
                    }
                    foreach ($name in $description.Files) {
                        $entry = $archive.GetEntry($description.Prefix + '/' + $name.Replace('\', '/'))
                        if ($null -eq $entry) {
                            throw "Release $version already exists without '$name' in $($description.Name). $newVersion"
                        }
                        # The Framework compiler can vary its generated EXE between builds.
                        # Reuse the checksum-verified original if the source and bundled files match.
                        if ($description.Root -eq $app -and $name -eq 'KIRIHARI.exe') { continue }
                        $stream = $entry.Open()
                        try { $archivedHash = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash }
                        finally { $stream.Dispose() }
                        if ($archivedHash -ne (Get-FileHash -LiteralPath (Join-Path $description.Root $name) -Algorithm SHA256).Hash) {
                            throw "Release $version already exists with different contents for '$name' in $($description.Name). $newVersion"
                        }
                    }
                }
                finally { $archive.Dispose() }
            }
            Write-Output "Reusing the existing release: $release"
        }
        else {
            foreach ($description in $packages) {
                $legacy = Join-Path $dist $description.Name
                if (Test-Path -LiteralPath $legacy) { throw "Release $version already has a legacy ZIP at '$legacy'. $newVersion" }
            }
            $stage = Join-Path $dist ('.package-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $stage | Out-Null
            try {
                foreach ($description in $packages) {
                    $file = Join-Path $stage $description.Name
                    $archive = [IO.Compression.ZipFile]::Open($file, [IO.Compression.ZipArchiveMode]::Create, [Text.Encoding]::UTF8)
                    try {
                        foreach ($name in $description.Files) {
                            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $description.Root $name),
                                ($description.Prefix + '/' + $name.Replace('\', '/')), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
                        }
                    }
                    finally { $archive.Dispose() }
                }
                $checksums = @(Get-PackageChecksums $stage)
                [IO.File]::WriteAllLines((Join-Path $stage $sumName), $checksums, (New-Object Text.UTF8Encoding($false)))
                for ($attempt = 0; ; $attempt++) {
                    try { [IO.Directory]::Move($stage, $release); break }
                    catch {
                        if ($attempt -ge 5 -or ($_.Exception.GetBaseException().HResult -band 0xffff) -notin @(32, 33)) { throw }
                        Start-Sleep -Milliseconds (100 * ($attempt + 1))
                    }
                }
                Write-Output "Created release: $release"
            }
            finally {
                if (Test-Path -LiteralPath $stage -PathType Container) {
                    foreach ($file in Get-ChildItem -LiteralPath $stage -File) { Remove-Item -LiteralPath $file.FullName }
                    [IO.Directory]::Delete($stage)
                }
            }
        }
        $checksums
    }
    Write-Output $exe
}
finally { $buildLock.Dispose() }
