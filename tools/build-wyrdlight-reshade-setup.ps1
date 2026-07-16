param(
    [string] $Source = "tools\WyrdlightReShadeSetup.cs",
    [string] $Output = "mod\root\Wyrdlight ReShade Setup.exe"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$sourcePath = Resolve-Path -LiteralPath $Source
$outputPath = Join-Path $repoRoot $Output
$outputDirectory = Split-Path -Parent $outputPath
$fomodInfoPath = Join-Path $repoRoot "mod\fomod\info.xml"

function Convert-ToAssemblyVersion {
    param([string] $Version)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return $null
    }

    $parts = $Version.Split(".")
    if ($parts.Count -gt 4) {
        throw "Version has too many parts for an assembly version: $Version"
    }

    foreach ($part in $parts) {
        if ($part -notmatch '^\d+$') {
            throw "Version must be numeric for an assembly version: $Version"
        }
    }

    while ($parts.Count -lt 4) {
        $parts += "0"
    }

    return ($parts -join ".")
}

$displayVersion = $null
$assemblyVersion = $null
if (Test-Path -LiteralPath $fomodInfoPath) {
    [xml] $fomodInfo = Get-Content -LiteralPath $fomodInfoPath -Raw
    $displayVersion = $fomodInfo.fomod.Version.InnerText.Trim()
    $assemblyVersion = Convert-ToAssemblyVersion $fomodInfo.fomod.Version.MachineVersion
}

$sourceText = Get-Content -LiteralPath $sourcePath -Raw
if ($assemblyVersion) {
    $sourceText = $sourceText -replace '\[assembly:\s*AssemblyVersion\("[^"]*"\)\]', "[assembly: AssemblyVersion(`"$assemblyVersion`")]"
    $sourceText = $sourceText -replace '\[assembly:\s*AssemblyFileVersion\("[^"]*"\)\]', "[assembly: AssemblyFileVersion(`"$assemblyVersion`")]"
}

if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

Add-Type `
    -TypeDefinition $sourceText `
    -OutputAssembly $outputPath `
    -OutputType ConsoleApplication `
    -ReferencedAssemblies @(
        "System.dll",
        "System.Core.dll",
        "System.IO.Compression.dll",
        "System.Windows.Forms.dll",
        "System.Drawing.dll"
    )

if ($displayVersion) {
    Write-Host "Built $outputPath (version $displayVersion)"
}
else {
    Write-Host "Built $outputPath"
}
