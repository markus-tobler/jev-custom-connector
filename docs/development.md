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

## Live verification

### Verified on DEV (`mto-training-management`, 2026-10-03)

| Check | Result |
| --- | --- |
| `Push-ToDevelopment.ps1` import and publish | Passed |
| `Sync-FromDevelopment.ps1` round trip | Passed. Only platform normalization changed: BOMs, the lowercased publisher name, and the re-encoded icon. |
| Connection with a plain key and with a `Bearer `-prefixed key | Both connected, and both called TypeSafe successfully. |
| Wrong key | TypeSafe's 401 `authentication_error` was passed through unchanged. |
| `ListModels`, `GetModelChoices` (`/_connector/model-choices`) | 200 |
| `GetOutputSchema`, using the full body or the `definition` only | 200 with identical schemas. Works even when the key is invalid, so no outbound call is made. |
| `Evaluate` in Builder mode | 200. The live response matches the generated schema exactly. `x-typesafe-request-id` is returned. |
| `Evaluate` in JSON mode with a JSON state | 200 |
| `EvaluateJson` | 200. `responseJson` parses. |
| Invalid definition (duplicate question ID) | 400 `duplicate_question_id`. The schema endpoint falls back to the generic schema. |
| Power Automate flow: Button trigger, then Evaluate, then Compose of `answers.is_urgent.noul`, `category.choice`, `severity.score` | Run succeeded: `0.97`, `engineering`, `3.0` |

To reproduce these checks:

1. Get tokens with Azure CLI for the DEV tenant.
2. Create a connection with `PUT https://api.powerapps.com/providers/Microsoft.PowerApps/apis/<api>/connections/<name>`.
3. Call `<primaryRuntimeUrl><connection>/<path>` with a token for the `https://apihub.azure.com` audience. Leave out the `/v1` base path, for example `/systemone` or `/_connector/output-schema`.

### Still requires a manual designer check

The designer resolves the dynamic schema client-side, so you need to check it in the browser:

- [ ] In the Power Automate designer, add **Evaluate with Jev** in Builder mode with noul, choice, and score rows. Confirm that dynamic tokens appear for each question and option. This shows that the nested `body/definition` reference resolves for `x-ms-dynamic-properties`.
- [ ] Switch to JSON mode and confirm the tokens refresh.
- [ ] Power Apps: call `EvaluateJson` and parse the result with `ParseJSON`.
