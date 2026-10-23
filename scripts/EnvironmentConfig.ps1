$script:RepositoryRoot = Split-Path $PSScriptRoot -Parent
$script:DefaultDotEnvPath = Join-Path $script:RepositoryRoot ".env"

# Every solution in the repository, with its connector and the tests that cover its custom code.
$script:Solutions = [ordered]@{
    JevCustomConnector               = @{
        ConnectorName = "mto_jevv1connector"
        TestProject   = "tests/JevConnector.Tests/JevConnector.Tests.csproj"
    }
    MicrosoftDecisionCustomConnector = @{
        ConnectorName = "mto_msdecision1connector"
        TestProject   = "tests/MicrosoftDecisionConnector.Tests/MicrosoftDecisionConnector.Tests.csproj"
    }
}

# Returns the requested solution names, or every solution when none is requested.
function Resolve-SolutionNames {
    [CmdletBinding()]
    param(
        [string[]]$Solution
    )

    if ($null -eq $Solution -or $Solution.Count -eq 0) {
        return @($script:Solutions.Keys)
    }

    foreach ($name in $Solution) {
        if (-not $script:Solutions.Contains($name)) {
            throw "Unknown solution '$name'. Known solutions: $($script:Solutions.Keys -join ', ')."
        }
    }
    return @($Solution)
}

function Import-DotEnv {
    [CmdletBinding()]
    param(
        [string]$Path = $script:DefaultDotEnvPath
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return
    }

    $lineNumber = 0
    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $lineNumber++
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
            continue
        }
        if ($line.StartsWith("export ")) {
            $line = $line.Substring(7).TrimStart()
        }

        $match = [regex]::Match($line, '^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*)$')
        if (-not $match.Success) {
            throw "Invalid .env entry at ${Path}:$lineNumber. Expected NAME=value."
        }

        $name = $match.Groups["name"].Value
        $value = $match.Groups["value"].Value.Trim()
        if ($value.Length -ge 2) {
            $first = $value[0]
            $last = $value[$value.Length - 1]
            if (($first -eq '"' -and $last -eq '"') -or ($first -eq "'" -and $last -eq "'")) {
                $value = $value.Substring(1, $value.Length - 2)
            }
        }

        # Process environment variables win over .env so CI and one-off overrides behave predictably.
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, "Process"))) {
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
}

# Precedence: explicit script parameter, then process environment variable, then .env.
function Resolve-ConfigurationValue {
    [CmdletBinding()]
    param(
        [string]$ExplicitValue,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitValue)) {
        return $ExplicitValue
    }

    Import-DotEnv
    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing configuration '$Name'. Set it in .env, as a process environment variable, or pass the corresponding script parameter."
    }
    return $value
}

function Resolve-PowerPlatformEnvironmentUrl {
    [CmdletBinding()]
    param(
        [string]$ExplicitValue,

        [string]$Name = "POWER_PLATFORM_DEV_ENVIRONMENT_URL"
    )

    $value = Resolve-ConfigurationValue -ExplicitValue $ExplicitValue -Name $Name
    $uri = $null
    if (-not [Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne "https") {
        throw "Configuration '$Name' must be an absolute HTTPS URL."
    }
    return $uri.AbsoluteUri
}

function Assert-Command {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Description ('$Name') was not found on PATH."
    }
}

function Format-SolutionJsonFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Assert-Command -Name "node" -Description "Node.js"
    $jsonFiles = @(Get-ChildItem -LiteralPath $Path -Filter "*.json" -File -Recurse)
    if ($jsonFiles.Count -eq 0) {
        return
    }

    & node (Join-Path $PSScriptRoot "format-json.mjs") @($jsonFiles.FullName)
    if ($LASTEXITCODE -ne 0) {
        throw "JSON formatting failed with exit code $LASTEXITCODE."
    }
}
