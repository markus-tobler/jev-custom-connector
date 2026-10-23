[CmdletBinding()]
param(
    [string[]]$Solution,

    [string]$Environment,

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$SkipValidation
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "EnvironmentConfig.ps1")
$Environment = Resolve-PowerPlatformEnvironmentUrl -ExplicitValue $Environment
$solutionNames = Resolve-SolutionNames -Solution $Solution

if (-not $SkipValidation) {
    & (Join-Path $PSScriptRoot "Validate-Solution.ps1") -Solution $solutionNames
}

$outputDirectory = Join-Path $script:RepositoryRoot "artifacts"
$buildParameters = @{
    Solution        = $solutionNames
    PackageType     = "Unmanaged"
    OutputDirectory = $outputDirectory
}
if ($Version) {
    $buildParameters.Version = $Version
}

& (Join-Path $PSScriptRoot "Build-Solution.ps1") @buildParameters
foreach ($solutionName in $solutionNames) {
    & (Join-Path $PSScriptRoot "Import-Solution.ps1") `
        -Environment $Environment `
        -SolutionFile (Join-Path $outputDirectory "${solutionName}_unmanaged.zip") `
        -AllowUnmanaged
}
