param(
    [string] $Output = "Wyrdlight ReShade - Northern Skies.zip",
    [switch] $SkipSetupBuild,
    [switch] $IncludeBundledReShadeDll,
    [switch] $KeepStaging
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$rootSource = Join-Path $repoRoot "mod\root"
$fomodSource = Join-Path $repoRoot "mod\fomod"
$fomodImageSource = Join-Path $repoRoot "docs\images\nexus-main.png"
$setupBuildScript = Join-Path $repoRoot "tools\build-wyrdlight-reshade-setup.ps1"
$staging = Join-Path $repoRoot ".codex-temp\fomod-package"
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $repoRoot $Output }
$fomodInfoPath = Join-Path $fomodSource "info.xml"

function Assert-InRepo {
    param([string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullRepo = [IO.Path]::GetFullPath($repoRoot)
    if (-not $fullPath.StartsWith($fullRepo, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside the repository: $fullPath"
    }

    return $fullPath
}

function Copy-DirectoryContents {
    param(
        [string] $Source,
        [string] $Destination,
        [string[]] $ExcludeRelativePaths = @()
    )

    $sourceFull = [IO.Path]::GetFullPath($Source)
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    Get-ChildItem -LiteralPath $Source -Recurse -Force | ForEach-Object {
        $relative = $_.FullName.Substring($sourceFull.Length).TrimStart('\', '/')
        if ([string]::IsNullOrWhiteSpace($relative)) {
            return
        }

        $normalizedRelative = $relative -replace '/', '\'
        if ($ExcludeRelativePaths -contains $normalizedRelative) {
            Write-Host "Skipping $normalizedRelative"
            return
        }

        $target = Join-Path $Destination $relative
        if ($_.PSIsContainer) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
        }
        else {
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

function Add-ZipDirectory {
    param(
        [IO.Compression.ZipArchive] $Archive,
        [string] $Directory
    )

    $base = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    Get-ChildItem -LiteralPath $Directory -Recurse -File -Force | ForEach-Object {
        $entryName = $_.FullName.Substring($base.Length).TrimStart('\', '/') -replace '\\', '/'
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $_.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}

if (-not (Test-Path -LiteralPath $rootSource)) {
    throw "Missing mod root source: $rootSource"
}

if (-not (Test-Path -LiteralPath $fomodSource)) {
    throw "Missing FOMOD source: $fomodSource"
}

[xml] $moduleConfig = Get-Content -LiteralPath (Join-Path $fomodSource "ModuleConfig.xml") -Raw
[xml] $fomodInfo = Get-Content -LiteralPath $fomodInfoPath -Raw
$fomodVersion = $fomodInfo.fomod.Version.InnerText.Trim()
$fomodMachineVersion = $fomodInfo.fomod.Version.MachineVersion

if (-not $SkipSetupBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $setupBuildScript
}

$staging = Assert-InRepo $staging
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $staging | Out-Null

$excludedRootFiles = @()
if (-not $IncludeBundledReShadeDll) {
    $excludedRootFiles += "dxgi.dll"
}

Copy-DirectoryContents -Source $rootSource -Destination (Join-Path $staging "root") -ExcludeRelativePaths $excludedRootFiles
Copy-DirectoryContents -Source $fomodSource -Destination (Join-Path $staging "fomod")

if (Test-Path -LiteralPath $fomodImageSource) {
    $imageDestination = Join-Path $staging "fomod\images\nexus-main.png"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $imageDestination) | Out-Null
    Copy-Item -LiteralPath $fomodImageSource -Destination $imageDestination -Force
}
else {
    Write-Warning "FOMOD image was not found: $fomodImageSource"
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archiveStream = [IO.File]::Open($outputPath, [IO.FileMode]::CreateNew)
try {
    $archive = New-Object IO.Compression.ZipArchive($archiveStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Add-ZipDirectory -Archive $archive -Directory $staging
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $archiveStream.Dispose()
}

$fileCount = (Get-ChildItem -LiteralPath $staging -Recurse -File -Force | Measure-Object).Count
$outputItem = Get-Item -LiteralPath $outputPath

Write-Host "Built $($outputItem.FullName)"
if ($fomodVersion) {
    Write-Host "Version $fomodVersion ($fomodMachineVersion)."
}
Write-Host "Packaged $fileCount files."
if (-not $IncludeBundledReShadeDll) {
    Write-Host "dxgi.dll was excluded; Wyrdlight ReShade Setup.exe downloads the official ReShade DLL."
}

if (-not $KeepStaging -and (Test-Path -LiteralPath $staging)) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
