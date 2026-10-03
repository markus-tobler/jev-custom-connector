[CmdletBinding()]
param(
    [ValidateSet("Both", "Managed", "Unmanaged")]
    [string]$PackageType = "Both",

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts")
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "EnvironmentConfig.ps1")

$sourcePath = Join-Path $script:RepositoryRoot "solutions/$script:SolutionName/src"
Assert-Command -Name "pac" -Description "The Power Platform CLI"
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath "Other/Solution.xml"))) {
    throw "Solution source was not found at $sourcePath."
}

$packSource = $sourcePath
$temporaryRoot = $null
try {
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        # Version overrides are applied to a temporary copy so the committed source stays unchanged.
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("jev-solution-pack-" + [guid]::NewGuid())
        Copy-Item -LiteralPath $sourcePath -Destination $temporaryRoot -Recurse
        $packSource = $temporaryRoot

        $manifestPath = Join-Path $packSource "Other/Solution.xml"
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
        $manifest.ImportExportXml.SolutionManifest.Version = $Version
        $settings = [Xml.XmlWriterSettings]::new()
        $settings.Encoding = [Text.UTF8Encoding]::new($false)
        $settings.Indent = $true
        $writer = [Xml.XmlWriter]::Create($manifestPath, $settings)
        try { $manifest.Save($writer) } finally { $writer.Dispose() }
    }

    $null = New-Item -ItemType Directory -Path $OutputDirectory -Force
    $types = if ($PackageType -eq "Both") { @("Managed", "Unmanaged") } else { @($PackageType) }
    foreach ($type in $types) {
        $suffix = $type.ToLowerInvariant()
        $zipPath = Join-Path $OutputDirectory "$($script:SolutionName)_$suffix.zip"
        & pac solution pack --zipfile $zipPath --folder $packSource --packagetype $type
        if ($LASTEXITCODE -ne 0) {
            throw "pac solution pack failed for $type with exit code $LASTEXITCODE."
        }
        "Created $zipPath" | Write-Host -ForegroundColor Green
    }
}
finally {
    if ($temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
