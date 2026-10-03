# jev-custom-connector

A Power Platform custom connector for [TypeSafe](https://typesafe.ai)'s **Jev System One** model. It ships as the solution `JevCustomConnector` (publisher `markustobler`, prefix `mto`) and contains one connector, **TypeSafe Jev v1**.

Jev answers structured questions about a piece of text and returns calibrated probabilities. In this connector, the question definition you enter in a flow also determines the shape of the action's output. Power Automate then shows a dynamic-content token for each question, such as `is_urgent yes probability` or `category probability billing`. The connector's custom C# code generates that output schema without calling TypeSafe.

## Operations

| Operation | Visibility | Description |
| --- | --- | --- |
| `Evaluate` | important | Calls `POST /v1/systemone`. Questions can come from the row-based builder or from raw JSON. The output schema is dynamic (`x-ms-dynamic-schema` and `x-ms-dynamic-properties`). |
| `EvaluateJson` | advanced | Same request as `Evaluate`, but returns the raw TypeSafe response as `responseJson` text. Use it with `ParseJSON` in Power Apps. |
| `ListModels` | advanced | Calls `GET /v1/models`. |
| `GetOutputSchema` | internal | Answered in custom code with no outbound call. Turns a question definition into a JSON schema. |
| `GetModelChoices` | internal | Supplies the model picker: the aliases `jev-latest` and `jev-preview`, then the pinned models from `/v1/models`. |

The custom code normalizes the API key into `Authorization: Bearer <key>`. It also validates questions against TypeSafe's limits before sending a request and returns TypeSafe errors (401, 422, 429, 529 with `retry-after`) unchanged. The connector does not retry; retries are left to the caller.

## Repository layout

```text
solutions/JevCustomConnector/   Dataverse solution source (pac solution project)
  src/Connectors/               OpenAPI definition, custom code (.csx), icon, connection parameters
tests/                          xUnit tests that compile the exact .csx against a ScriptBase stub
scripts/                        Validate, build, import, push-to-DEV, and sync-from-DEV scripts
docs/                           Usage and development guides
.github/workflows/validate.yml  CI: validate, test, and build managed and unmanaged packages
```

## Quick start

```bash
npm ci
pwsh ./scripts/Validate-Solution.ps1                     # contract checks, swagger-cli, dotnet test, pack
pwsh ./scripts/Build-Solution.ps1 -PackageType Both      # artifacts/JevCustomConnector_{managed,unmanaged}.zip
cp .env.example .env                                     # set POWER_PLATFORM_DEV_ENVIRONMENT_URL
pac auth create --environment <dev-url>
pwsh ./scripts/Push-ToDevelopment.ps1                    # validate, build unmanaged, import to DEV
```

- [Usage guide](docs/usage.md): connections, the builder and JSON modes, Power Automate and Power Apps examples, and errors.
- [Development guide](docs/development.md): tests, scripts, the DEV push and sync workflow, and the live verification checklist.

## References

- TypeSafe API: <https://docs.typesafe.ai/api>
- Custom connector OpenAPI extensions: <https://learn.microsoft.com/connectors/custom-connectors/openapi-extensions>
- Custom code in connectors: <https://learn.microsoft.com/connectors/custom-connectors/write-code>
- Custom connectors in solutions: <https://learn.microsoft.com/connectors/custom-connectors/customconnectorssolutions>
