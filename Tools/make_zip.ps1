# Zips a build folder so that Windows Explorer's built-in "Extract all" accepts it.
#   powershell -NoProfile -ExecutionPolicy Bypass -File make_zip.ps1 <buildDir> <zip> <topFolder>
# Every entry goes under <topFolder>/ with forward slashes and no "./" prefix (bsdtar wrote "./x",
# which Explorer rejects as an invalid archive). The Burst debug folder and build_result.txt stay out.
param([string]$BuildDir, [string]$Zip, [string]$Top)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$base = (Resolve-Path $BuildDir).Path.TrimEnd('\') + '\'
if (Test-Path $Zip) { Remove-Item $Zip -Force }
$archive = [System.IO.Compression.ZipFile]::Open($Zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -LiteralPath $base -Recurse -File | Where-Object {
        $_.FullName -notmatch '_BurstDebugInformation_DoNotShip' -and $_.Name -ne 'build_result.txt'
    } | ForEach-Object {
        $rel = $_.FullName.Substring($base.Length).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, "$Top/$rel",
            [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
