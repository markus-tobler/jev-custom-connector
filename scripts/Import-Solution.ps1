[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Environment,

    [string]$SolutionFile,

    [switch]$AllowUnmanaged,

    [switch]$SkipLowerVersion
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "EnvironmentConfig.ps1")

if ([string]::IsNullOrWhiteSpace($SolutionFile)) {
    $SolutionFile = Join-Path $script:RepositoryRoot "artifacts/$($script:SolutionName)_managed.zip"
}

Assert-Command -Name "pac" -Description "The Power Platform CLI"
if (-not (Test-Path -LiteralPath $SolutionFile -PathType Leaf)) {
    throw "Solution package was not found: $SolutionFile"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $SolutionFile))
try {
    $manifestEntry = $archive.GetEntry("solution.xml")
    if ($null -eq $manifestEntry) {
        throw "The package does not contain solution.xml."
    }
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $isManaged = [string]$manifest.ImportExportXml.SolutionManifest.Managed -eq "1"
    if (-not $isManaged -and -not $AllowUnmanaged) {
        throw "Refusing to import an unmanaged package. Pass -AllowUnmanaged only for a development environment."
    }
}
finally {
    $archive.Dispose()
}

$arguments = @(
    "solution", "import",
    "--environment", $Environment,
    "--path", $SolutionFile,
    "--publish-changes",
    "--async",
    "--max-async-wait-time", "60"
)
if ($SkipLowerVersion) {
    $arguments += "--skip-lower-version"
}

"Importing $SolutionFile into $Environment..." | Write-Host -ForegroundColor Cyan
& pac @arguments
if ($LASTEXITCODE -ne 0) {
    throw "pac solution import failed with exit code $LASTEXITCODE."
}

"Solution import completed. Create or refresh the connection to use the updated connector." | Write-Host -ForegroundColor Green
