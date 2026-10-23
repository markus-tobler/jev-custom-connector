[CmdletBinding()]
param(
    [string[]]$Solution,

    [switch]$SkipTests,

    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "EnvironmentConfig.ps1")

$solutionNames = Resolve-SolutionNames -Solution $Solution
$maxBytes = 1MB
$problems = [Collections.Generic.List[string]]::new()
$openApiPaths = [Collections.Generic.List[string]]::new()

function Add-Problem([string]$Message) {
    $problems.Add($Message)
}

foreach ($solutionName in $solutionNames) {
    $solutionRoot = Join-Path $script:RepositoryRoot "solutions/$solutionName/src"
    $connectorName = $script:Solutions[$solutionName].ConnectorName
    $connectorRoot = Join-Path $solutionRoot "Connectors"
    "Checking $solutionName connector artifacts..." | Write-Host -ForegroundColor Cyan
    $requiredFiles = @(
        "$connectorName.xml",
        "${connectorName}_openapidefinition.json",
        "${connectorName}_connectionparameters.json",
        "${connectorName}_policytemplateinstances.json",
        "${connectorName}_customcodeblobcontent.csx",
        "${connectorName}_iconblob.Png"
    )
    foreach ($fileName in $requiredFiles) {
        $path = Join-Path $connectorRoot $fileName
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Missing required connector artifact: $path"
        }
    }

    Get-ChildItem -LiteralPath $solutionRoot -Recurse -Filter "*.json" | ForEach-Object {
        try { $null = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json }
        catch { Add-Problem "Invalid JSON in $($_.FullName): $($_.Exception.Message)" }
    }
    Get-ChildItem -LiteralPath $solutionRoot -Recurse -Filter "*.xml" | ForEach-Object {
        try { $null = [xml](Get-Content -LiteralPath $_.FullName -Raw) }
        catch { Add-Problem "Invalid XML in $($_.FullName): $($_.Exception.Message)" }
    }
    if ($problems.Count -gt 0) {
        throw "Validation failed:`n - $($problems -join "`n - ")"
    }

    $openApiPath = Join-Path $connectorRoot "${connectorName}_openapidefinition.json"
    $openApiPaths.Add($openApiPath)
    $scriptPath = Join-Path $connectorRoot "${connectorName}_customcodeblobcontent.csx"
    $iconPath = Join-Path $connectorRoot "${connectorName}_iconblob.Png"
    foreach ($path in @($openApiPath, $scriptPath, $iconPath)) {
        if ((Get-Item -LiteralPath $path).Length -ge $maxBytes) {
            Add-Problem "$path must be smaller than 1 MB."
        }
    }

    [xml]$connectorManifest = Get-Content -LiteralPath (Join-Path $connectorRoot "$connectorName.xml") -Raw
    $displayName = [string]$connectorManifest.Connector.displayname
    if ($displayName.Length -gt 30) {
        Add-Problem "Connector display name '$displayName' exceeds 30 characters."
    }
    if ([string]$connectorManifest.Connector.name -ne $connectorName) {
        Add-Problem "Connector XML name must be '$connectorName'."
    }

    [xml]$solutionManifest = Get-Content -LiteralPath (Join-Path $solutionRoot "Other/Solution.xml") -Raw
    $rootComponent = @($solutionManifest.ImportExportXml.SolutionManifest.RootComponents.RootComponent) |
        Where-Object { $_.type -eq "372" -and $_.schemaName -eq $connectorName } |
        Select-Object -First 1
    if ($null -eq $rootComponent) {
        Add-Problem "Solution.xml has no connector root component for $connectorName."
    }
    elseif ($rootComponent.id.Trim("{}") -ne [string]$connectorManifest.Connector.connectorid) {
        Add-Problem "Solution.xml root component id does not match the connector id."
    }

    $openApi = [Newtonsoft.Json.Linq.JObject]::Parse((Get-Content -LiteralPath $openApiPath -Raw))
    if ([string]$openApi["swagger"] -ne "2.0") {
        Add-Problem "The OpenAPI definition must be Swagger 2.0."
    }
    $title = [string]$openApi["info"]["title"]
    if ($title.Length -gt 30) {
        Add-Problem "OpenAPI info.title '$title' exceeds 30 characters."
    }

    foreach ($token in $openApi.DescendantsAndSelf()) {
        if ($token -isnot [Newtonsoft.Json.Linq.JObject]) {
            continue
        }
        foreach ($keyword in @("allOf", "anyOf", "oneOf", "uniqueItems")) {
            if ($null -ne $token.Property($keyword)) {
                Add-Problem "$($token.Path): $keyword is not supported by the Power Platform designer or validator."
            }
        }
        $collectionFormat = $token.Property("collectionFormat")
        if ($null -ne $collectionFormat -and [string]$collectionFormat.Value -ieq "multi") {
            Add-Problem "$($token.Path): collectionFormat multi is not supported."
        }
    }

    $operationIds = [Collections.Generic.List[string]]::new()
    foreach ($pathProperty in $openApi["paths"].Properties()) {
        foreach ($operation in $pathProperty.Value.Properties()) {
            if ($operation.Name -in @("get", "put", "post", "delete", "patch", "head", "options")) {
                $operationIds.Add([string]$operation.Value["operationId"])
            }
        }
    }
    $duplicates = @($operationIds | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($duplicates.Count -gt 0) {
        Add-Problem "Duplicate operationIds: $($duplicates -join ', ')"
    }

    foreach ($token in $openApi.Descendants()) {
        if ($token -isnot [Newtonsoft.Json.Linq.JProperty]) {
            continue
        }
        if ($token.Name -in @("x-ms-dynamic-values", "x-ms-dynamic-list", "x-ms-dynamic-schema", "x-ms-dynamic-properties")) {
            $target = [string]$token.Value["operationId"]
            if ($target -notin $operationIds) {
                Add-Problem "$($token.Path) references unknown operation '$target'."
            }
        }
    }

    $scriptOperations = @([Newtonsoft.Json.Linq.JArray]::Parse([string]$connectorManifest.Connector.scriptoperations) | ForEach-Object { [string]$_ })
    $missingFromScript = @($operationIds | Where-Object { $_ -notin $scriptOperations })
    $unknownInScript = @($scriptOperations | Where-Object { $_ -notin $operationIds })
    if ($missingFromScript.Count -gt 0) {
        Add-Problem "Operations not routed through custom code (scriptoperations): $($missingFromScript -join ', ')"
    }
    if ($unknownInScript.Count -gt 0) {
        Add-Problem "scriptoperations lists unknown operations: $($unknownInScript -join ', ')"
    }

    $scriptText = Get-Content -LiteralPath $scriptPath -Raw
    foreach ($operationId in $operationIds) {
        if ($scriptText -notmatch [regex]::Escape("""$operationId""")) {
            Add-Problem "The custom code does not handle operation '$operationId'."
        }
    }
    if ($scriptText -match '(?m)^\s*using\s+[A-Za-z]') {
        Add-Problem "The custom code must not declare using directives; the platform provides the supported namespaces."
    }

    if ($problems.Count -gt 0) {
        throw "Validation failed:`n - $($problems -join "`n - ")"
    }
}

"Validating Swagger with swagger-cli..." | Write-Host -ForegroundColor Cyan
Assert-Command -Name "npm" -Description "npm"
$swaggerCli = Join-Path $script:RepositoryRoot "node_modules/.bin/swagger-cli"
if (-not (Test-Path -LiteralPath $swaggerCli)) {
    & npm ci --prefix $script:RepositoryRoot --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) {
        throw "npm ci failed with exit code $LASTEXITCODE."
    }
}
foreach ($openApiPath in $openApiPaths) {
    & $swaggerCli validate $openApiPath
    if ($LASTEXITCODE -ne 0) {
        throw "swagger-cli validation failed for $openApiPath with exit code $LASTEXITCODE."
    }
}

if (-not $SkipTests) {
    "Running custom code tests..." | Write-Host -ForegroundColor Cyan
    Assert-Command -Name "dotnet" -Description "The .NET SDK"
    foreach ($solutionName in $solutionNames) {
        & dotnet test (Join-Path $script:RepositoryRoot $script:Solutions[$solutionName].TestProject) --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed for $solutionName with exit code $LASTEXITCODE."
        }
    }
}

if (-not $SkipPack) {
    "Packing managed and unmanaged solutions..." | Write-Host -ForegroundColor Cyan
    $temporaryOutput = Join-Path ([IO.Path]::GetTempPath()) ("connector-solution-validation-" + [guid]::NewGuid())
    try {
        & (Join-Path $PSScriptRoot "Build-Solution.ps1") -Solution $solutionNames -PackageType Both -OutputDirectory $temporaryOutput
    }
    finally {
        if (Test-Path -LiteralPath $temporaryOutput) {
            Remove-Item -LiteralPath $temporaryOutput -Recurse -Force
        }
    }
}

"Validation succeeded." | Write-Host -ForegroundColor Green
