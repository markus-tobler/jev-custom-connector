[CmdletBinding()]
param(
    [string[]]$Solution,

    [string]$Environment,

    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "EnvironmentConfig.ps1")
$Environment = Resolve-PowerPlatformEnvironmentUrl -ExplicitValue $Environment

$solutionNames = Resolve-SolutionNames -Solution $Solution
Assert-Command -Name "pac" -Description "The Power Platform CLI"
Assert-Command -Name "git" -Description "Git"

foreach ($solutionName in $solutionNames) {
    $solutionProject = Join-Path $script:RepositoryRoot "solutions/$solutionName"
    if (-not (Test-Path -LiteralPath (Join-Path $solutionProject "$solutionName.cdsproj"))) {
        throw "Solution project was not found at $solutionProject."
    }

    if (-not $AllowDirty) {
        $relativeProject = [IO.Path]::GetRelativePath($script:RepositoryRoot, $solutionProject)
        $changes = & git -C $script:RepositoryRoot status --porcelain -- $relativeProject
        if ($LASTEXITCODE -ne 0) {
            throw "Could not inspect the Git worktree."
        }
        if ($changes) {
            throw "The solution source has uncommitted changes. Commit or stash them, or pass -AllowDirty."
        }
    }

    # Sync into a staged copy first so a failed or partial export never touches the working tree.
    $stagingRoot = Join-Path ([IO.Path]::GetTempPath()) ("connector-solution-sync-" + [guid]::NewGuid())
    try {
        Copy-Item -LiteralPath $solutionProject -Destination $stagingRoot -Recurse
        foreach ($buildOutput in @("bin", "obj")) {
            $path = Join-Path $stagingRoot $buildOutput
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Recurse -Force
            }
        }

        "Synchronizing $solutionName from $Environment into a staging copy..." | Write-Host -ForegroundColor Cyan
        & pac solution sync --environment $Environment --solution-folder $stagingRoot --packagetype Both --async
        if ($LASTEXITCODE -ne 0) {
            throw "pac solution sync failed with exit code $LASTEXITCODE."
        }

        $stagedSource = Join-Path $stagingRoot "src"
        Format-SolutionJsonFiles -Path $stagedSource

        $targetSource = Join-Path $solutionProject "src"
        Remove-Item -LiteralPath $targetSource -Recurse -Force
        Copy-Item -LiteralPath $stagedSource -Destination $targetSource -Recurse
    }
    finally {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }

    & git -C $script:RepositoryRoot --no-pager diff --stat -- "solutions/$solutionName"
    "Synchronization complete. Review the diff and run scripts/Validate-Solution.ps1 before committing." | Write-Host -ForegroundColor Green
}
