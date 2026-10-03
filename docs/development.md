# Development guide

## Prerequisites

- PowerShell 7 (`pwsh`)
- .NET SDK 10 (see `global.json`)
- Node.js 22 (for `swagger-cli`)
- Power Platform CLI (`pac`)

## Source layout

- `solutions/JevCustomConnector/src/Other/Solution.xml`: solution identity, version, and the connector root component (type 372).
- `solutions/JevCustomConnector/src/Connectors/mto_jevv1connector*`:
  - `.xml`: connector metadata and the `scriptoperations` list. Every operation is routed to custom code.
  - `_openapidefinition.json`: the Swagger 2.0 contract and its dynamic extensions.
  - `_customcodeblobcontent.csx`: all runtime logic. Keep it under 1 MB, with no `using` directives, and use only the namespaces that custom code supports.
  - `_connectionparameters.json`, `_policytemplateinstances.json`, `_iconblob.Png`.

## Tests

```bash
dotnet test tests/JevConnector.Tests
```

`tests/JevConnector.Script` compiles the exact `.csx` against a `ScriptBase` stub for `netstandard2.0`. Its global usings cover only the supported namespaces, so a reference to an unsupported API fails the build. The tests use a fake `IScriptContext` that records outbound requests. They cover:

- Request mapping.
- Validation limits.
- Auth normalization.
- Error passthrough.
- Schema generation with no outbound calls.
- Contract consistency between `scriptoperations`, the Swagger operation IDs, and the script's operation router.

## Scripts

| Script | Purpose |
| --- | --- |
| `Validate-Solution.ps1` | Runs these checks: files present; JSON and XML well formed; sizes; name lengths; identity consistency; Swagger 2.0 with no unsupported keywords; unique operation IDs; dynamic-extension targets exist; `scriptoperations` matches; `swagger-cli validate`; `dotnet test`; pack. Use `-SkipTests` or `-SkipPack` to skip steps. |
| `Build-Solution.ps1` | `-PackageType Both\|Managed\|Unmanaged`, `-Version x.y.z.w` (applied to a temp copy), `-OutputDirectory` (default `artifacts`). |
| `Import-Solution.ps1` | Imports a package with `pac solution import --publish-changes --async`. Unmanaged packages require `-AllowUnmanaged`. |
| `Push-ToDevelopment.ps1` | Validates, builds an unmanaged package, and imports it into DEV. |
| `Sync-FromDevelopment.ps1` | Runs `pac solution sync` into a staging copy, normalizes JSON formatting, replaces `src`, and shows the diff. Requires a clean working tree unless you pass `-AllowDirty`. |

### Environment configuration

The DEV environment URL is resolved in this order:

1. The `-Environment` parameter.
2. The `POWER_PLATFORM_DEV_ENVIRONMENT_URL` process environment variable.
3. `.env` in the repository root (copy it from `.env.example`).

Only HTTPS URLs are accepted. Authenticate first with `pac auth create --environment <url>`.

### DEV loop

1. Run `pwsh ./scripts/Push-ToDevelopment.ps1`.
2. If needed, edit the connector in the maker portal. Custom code you edit in the portal should be copied back into the `.csx` file.
3. Run `pwsh ./scripts/Sync-FromDevelopment.ps1`, review the diff, run the tests, and commit.

## CI

`.github/workflows/validate.yml` runs on pull requests, on pushes to `main`, and on manual dispatch. It:

1. Installs .NET, Node, and the Power Platform CLI.
2. Runs validation and the tests.
3. Stamps the version as `major.minor.(build + run number).attempt`.
4. Uploads the managed and unmanaged packages as an artifact.

## Live verification checklist

These checks need a DEV environment and a TypeSafe API key, so they cannot run offline:

- [ ] Import succeeds and the connector appears as **TypeSafe Jev v1** in the solution.
- [ ] Create a connection with the key, both with and without a `Bearer ` prefix.
- [ ] Power Automate: add **Evaluate with Jev** in Builder mode with noul, choice, and score rows. Confirm that dynamic tokens appear for each question and option. This shows that the nested `body/definition` reference resolves for `x-ms-dynamic-properties`.
- [ ] Switch to JSON mode and confirm the tokens refresh.
- [ ] The model picker lists the aliases and pinned versions.
- [ ] Run the flow and confirm the outputs are populated. Then try a wrong key (401) and an invalid definition (400 with `error.code`).
- [ ] Power Apps: call `EvaluateJson` and parse the result with `ParseJSON`.
- [ ] **List models** returns data.
