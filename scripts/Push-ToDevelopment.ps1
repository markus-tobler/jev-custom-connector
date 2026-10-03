[CmdletBinding()]
param(
    [string]$Environment,

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$SkipValidation
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "EnvironmentConfig.ps1")
$Environment = Resolve-PowerPlatformEnvironmentUrl -ExplicitValue $Environment

if (-not $SkipValidation) {
    & (Join-Path $PSScriptRoot "Validate-Solution.ps1")
}

$outputDirectory = Join-Path $script:RepositoryRoot "artifacts"
$buildParameters = @{
    PackageType     = "Unmanaged"
    OutputDirectory = $outputDirectory
}
if ($Version) {
    $buildParameters.Version = $Version
}

& (Join-Path $PSScriptRoot "Build-Solution.ps1") @buildParameters
& (Join-Path $PSScriptRoot "Import-Solution.ps1") `
    -Environment $Environment `
    -SolutionFile (Join-Path $outputDirectory "$($script:SolutionName)_unmanaged.zip") `
    -AllowUnmanaged
