# Builds a download package in dist\: a self-contained single-file build that needs no .NET install,
# with the manual as a PDF, zipped. Tag the commit first so the package and the About box carry the
# version:
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1            (release, for GitHub)
#   powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Tester    (play-test package)
# -Tester uses the tester manual, START HERE and the list of things to verify, which are kept on the
# maintainer's machine and aren't in the repository.
# -SoftwareRender builds a diagnostic copy that draws without the graphics card; -Suffix adds to the
# package name, e.g. -Suffix diag-gpu.
# Requires: python with the markdown package (pip install markdown), and Microsoft Edge.
param([switch]$Tester, [switch]$SoftwareRender, [string]$Suffix)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = git -C $root describe --tags --always --dirty 2>$null
if (-not $version) { $version = 'dev' }
$name = "Run8-OCU-II-$version"
if ($Suffix) { $name += "-$Suffix" }
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
$publish = Join-Path $dist 'publish'

$manual = Join-Path $root 'docs\OCU-II-Manual.md'
if ($Tester) {
    $manual = Join-Path $root 'docs\OCU-II-Manual-Tester.md'
    foreach ($f in $manual, (Join-Path $root 'docs\tester\START-HERE.md'), (Join-Path $root 'docs\tester\Things-To-Verify.txt')) {
        if (-not (Test-Path $f)) { throw "-Tester needs $f, which isn't in the repository" }
    }
}
Remove-Item $stage, $publish, "$stage.zip" -Recurse -Force -ErrorAction SilentlyContinue

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge is needed to make the PDFs' }

# The documents go out as PDFs, which show formatted anywhere (including Google Drive's preview): the
# Markdown becomes a web page, and Edge prints that to PDF with a bookmark for each heading.
function ConvertTo-Pdf([string]$markdown, [string]$pdf) {
    $html = Join-Path $dist 'doc.html'
    $edgeData = Join-Path $dist 'edge-pdf'
    python (Join-Path $PSScriptRoot 'md_to_html.py') $markdown $html
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Start-Process $edge -Wait -ArgumentList '--headless', '--disable-gpu', '--no-pdf-header-footer',
        '--generate-pdf-document-outline', "--user-data-dir=`"$edgeData`"", "--print-to-pdf=`"$pdf`"", ([Uri]$html).AbsoluteUri
    Remove-Item $html, $edgeData -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $pdf)) { throw "Edge did not write $pdf" }
}

dotnet publish (Join-Path $root 'src\Beltpack.Overlay') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none "-p:SoftwareRender=$([bool]$SoftwareRender)" -o $publish --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory $stage | Out-Null
Copy-Item (Join-Path $publish 'Beltpack.Overlay.exe') (Join-Path $stage 'Run8 OCU II.exe')
# The talker recordings go out as one talker.pak (a zip, stored rather than compressed since they're
# MP3s), with their licence note inside it and beside it.
$talker = Join-Path $root 'talker'
if (Test-Path $talker) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $pak = [IO.Compression.ZipFile]::Open((Join-Path $stage 'talker.pak'), 'Create')
    foreach ($f in Get-ChildItem $talker -Recurse -File | Where-Object { $_.Extension -in '.mp3', '.txt' }) {
        $entry = $f.FullName.Substring($talker.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($pak, $f.FullName, $entry, 'NoCompression')
    }
    $pak.Dispose()
    Copy-Item (Join-Path $talker 'README.txt') (Join-Path $stage 'talker.pak README.txt')
}
ConvertTo-Pdf $manual (Join-Path $stage 'OCU-II-Manual.pdf')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $stage
if ($Tester) {
    ConvertTo-Pdf (Join-Path $root 'docs\tester\START-HERE.md') (Join-Path $stage 'START HERE.pdf')
    # Changes-since-beta2.md goes out as "What's changed since beta2.pdf".
    foreach ($changes in Get-ChildItem (Join-Path $root 'docs\tester') -Filter 'Changes-since-*.md') {
        $since = $changes.BaseName -replace '^Changes-since-', ''
        ConvertTo-Pdf $changes.FullName (Join-Path $stage "What's changed since $since.pdf")
    }
    Copy-Item (Join-Path $root 'docs\tester\Things-To-Verify.txt') (Join-Path $stage 'Things to verify.txt')
} else {
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
}

Compress-Archive -Path $stage -DestinationPath "$stage.zip"
Remove-Item $publish -Recurse -Force
Write-Host ("Packaged {0} ({1:N0} MB)" -f "$stage.zip", ((Get-Item "$stage.zip").Length / 1MB))
