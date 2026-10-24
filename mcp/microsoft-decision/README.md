# Microsoft-Decision-1 MCP server

A local [MCP](https://modelcontextprotocol.io/) server that gives agents such as Claude Code the same decisions as the Microsoft-Decision-1 custom connector. It has one tool, `evaluate`, which sends a state and a set of yes/no (`noul`), `choice`, and `score` questions to your Microsoft-Decision-1 deployment in Microsoft Foundry and returns the typed answers. It communicates over stdio.

## Prerequisites

- [uv](https://docs.astral.sh/uv/getting-started/installation/), which provides `uvx`. uv installs the required Python version itself.
- A [Microsoft-Decision-1 deployment](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/use-foundry-models-microsoft-decision#deploy-microsoft-decision-1), with its endpoint, deployment name, and API key.

## Configuration

| Variable | Value |
| --- | --- |
| `MS_FOUNDRY_DECISION_1_ENDPOINT` | Endpoint of the Foundry resource, for example `https://my-resource.services.ai.azure.com`. Only the scheme, host, and port are used, so a full target URI also works. |
| `MS_FOUNDRY_DECISION_1_DEPLOYMENT` | Deployment name, for example `Decision-1`. |
| `MS_FOUNDRY_DECISION_1_API_KEY` | API key of the Foundry resource. |

The server looks up each variable in this order and uses the first non-empty value:

1. The process environment.
2. `.env` in the current working directory.
3. `.env` in your user profile (`~/.env`).
4. The OS credential store: Windows Credential Manager, macOS Keychain, or the Secret Service on Linux. The server reads it through [keyring](https://pypi.org/project/keyring/), with the service name `microsoft-decision-mcp` and the variable name as the user name.

If a value is still missing, the server writes an error to stderr that names the missing variables and exits.

### Store the API key securely

`.env` files and the `env` settings of MCP clients store values in plaintext. Keep the API key in the credential store instead:

```bash
uvx keyring set microsoft-decision-mcp MS_FOUNDRY_DECISION_1_API_KEY
```

The command prompts for the key without echoing it. Run it again to rotate the key, or remove it with `uvx keyring del microsoft-decision-mcp MS_FOUNDRY_DECISION_1_API_KEY`. The endpoint and deployment name are not secret, so set them in the client configuration below. You can also store them in the credential store in the same way.

Make sure the key is not also set in the environment or a `.env` file, because those take precedence.

## Run

From GitHub:

```bash
uvx --from "git+https://github.com/markus-tobler/model-one-connectors#subdirectory=mcp/microsoft-decision" microsoft-decision-mcp
```

From a local clone:

```bash
uvx --from ./mcp/microsoft-decision microsoft-decision-mcp
```

### Claude Code

```bash
claude mcp add microsoft-decision --scope user \
  -e MS_FOUNDRY_DECISION_1_ENDPOINT=https://my-resource.services.ai.azure.com \
  -e MS_FOUNDRY_DECISION_1_DEPLOYMENT=Decision-1 \
  -- uvx --from "git+https://github.com/markus-tobler/model-one-connectors#subdirectory=mcp/microsoft-decision" microsoft-decision-mcp
```

Run `/mcp` in Claude Code to check that the server is connected.

### Claude desktop app

Open **Settings > Developer > Edit Config** and add the server to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "microsoft-decision": {
      "command": "C:\\Users\\you\\.local\\bin\\uvx.exe",
      "args": [
        "--from",
        "git+https://github.com/markus-tobler/model-one-connectors#subdirectory=mcp/microsoft-decision",
        "microsoft-decision-mcp"
      ],
      "env": {
        "MS_FOUNDRY_DECISION_1_ENDPOINT": "https://my-resource.services.ai.azure.com",
        "MS_FOUNDRY_DECISION_1_DEPLOYMENT": "Decision-1"
      }
    }
  }
}
```

The desktop app does not load your shell's `PATH`, so set `command` to the full path of `uvx`. Find it with `where uvx` on Windows or `which uvx` on macOS. Quit and restart the app to load the server. The desktop app runs local servers only; claude.ai on the web and the mobile apps cannot start them.

## The `evaluate` tool

`questions` uses the native format of the decision API and is keyed by question ID:

```json
{
  "state": { "subject": "Checkout fails", "body": "Every payment returns error 500 since this morning." },
  "questions": {
    "is_urgent": { "type": "noul", "instructions": "Does this need an immediate response?" },
    "category": {
      "type": "choice",
      "instructions": "Which team should handle it?",
      "criteria": { "billing": null, "engineering": "Bugs, outages, and errors.", "other": null }
    },
    "severity": { "type": "score", "instructions": "How severe is the problem?", "criteria": ["None", "Minor", "Major", "Critical"] }
  }
}
```

A choice question needs 2 to 255 options, and a score question needs 2 to 10 levels. The server rejects invalid questions before it calls Foundry. Foundry errors, such as a wrong key or an unknown deployment, are returned as tool errors with the HTTP status and response body.

## Development

```bash
cd mcp/microsoft-decision
uv run pytest
```
