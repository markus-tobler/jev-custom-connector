# TypeSafe Jev and Microsoft-Decision-1 custom connectors

**Fast AI decisions, directly in Power Automate and Copilot Studio.**

These open-source custom connectors bring [TypeSafe AI's Jev](https://typesafe.ai/) and [Microsoft-Decision-1 in Microsoft Foundry](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/use-foundry-models-microsoft-decision) to Power Platform makers without writing API integration code. Evaluate a message, route a request, or score a case, then use the answers as named, strongly typed dynamic content in your automation.

[Download the latest managed solutions](https://github.com/markus-tobler/model-one-connectors/releases/latest) · [Jev usage guide](docs/usage.md) · [Microsoft-Decision-1 usage guide](docs/microsoft-decision-usage.md) · [Contribute](CONTRIBUTING.md)

## Connectors

Both models use the same question types and return the same typed answers, so both connectors offer the same actions and generated outputs. Each connector ships as its own managed solution; import only the one you need.

| Connector | Model | Solution | Connection |
| --- | --- | --- | --- |
| **TypeSafe Jev v1** | Jev, hosted by TypeSafe AI | `JevCustomConnector` | TypeSafe API key; model selected per action |
| **Microsoft-Decision-1** | Microsoft-Decision-1, deployed in your own Microsoft Foundry resource | `MicrosoftDecisionCustomConnector` | Foundry resource endpoint, deployment name, and API key |

## Why Jev?

Jev is TypeSafe AI's first **System One model**, built for fast, focused judgments inside software: identifying intent, assessing urgency, choosing a category, or scoring against a rubric. TypeSafe [reports substantial speed and cost advantages over frontier LLMs on its System One workflow evaluations](https://typesafe.ai/blog/introducing-system-one-models-and-jev). Those results are task-specific, not a claim that Jev is better at every AI task or that a complete Power Platform flow will see the same gains.

You provide text or structured data and define the questions. Jev returns typed answers: a yes/no probability, a choice from a fixed set, or a score against an ordered rubric. Probabilities and confidence values let your automation set explicit thresholds for acting automatically and send uncertain cases for review.

Jev complements rather than replaces prompts and frontier models:

- Use **Jev** for frequent, narrow decisions that should return predictable, machine-readable values.
- Use **prompts and frontier models** when the task requires content generation, open-ended reasoning, summarization, or a conversational response.
- Combine them by using Jev to route or qualify work before a prompt, validate a result after a prompt, or send uncertain cases to a frontier model or a person.

## Why a custom connector?

Calling an API is only part of making a model useful in low-code automation. This connector makes Jev accessible to Power Platform makers, with question configuration in the designer and answers ready to use in conditions, switches, approvals, and agent workflows.

- **Named, strongly typed outputs:** Define questions in the designer and select their answer fields from dynamic content, without manually parsing API responses.
- **Text and structured JSON input:** Evaluate emails, support tickets, form responses, transcripts, or structured records.
- **Three question types:** Ask yes/no questions, choose from named options, or score against an ordered rubric.
- **Native JSON when needed:** Use the JSON-text action for integrations that need to parse the native response themselves.
- **Model selection:** List available models and select an alias or a pinned version.

## Example: route a support request

A support message arrives. One **Evaluate with Jev** action asks three questions:

| Question | Type | Dynamic content you can use |
| --- | --- | --- |
| Does this need an immediate response? | Yes/no (`noul`) | `is_urgent yes probability` |
| Should billing, engineering, or another team handle it? | Choice (`choice`) | `category choice`, `category confidence`, and probability per option |
| How severe is the problem? | Score (`score`) | `severity score`, `severity confidence`, and probability per level |

Use `is_urgent yes probability` greater than `0.8` as an example threshold for urgent routing, switch on `category choice` to select the team, and use confidence values to send uncertain cases for human review. Choose thresholds appropriate to your workflow; typed answers do not guarantee correct decisions.

![Strongly typed Jev answers in the Power Automate dynamic-content picker](docs/assets/pa-use-strongly-typed-dynamic-answers.png)

See the [usage guide](docs/usage.md) for the complete example, including question configuration and expressions.

## Get started

### TypeSafe Jev

1. Get a Jev API key from the [TypeSafe dashboard](https://console.typesafe.ai/keys).
2. Open the repository's [Releases page](https://github.com/markus-tobler/model-one-connectors/releases) and download the managed `JevCustomConnector` solution from the [latest release](https://github.com/markus-tobler/model-one-connectors/releases/latest).
3. Import the managed solution into your Power Platform environment.
4. Create a connection for **TypeSafe Jev v1** and enter the API key.
5. Add **Evaluate with Jev** to a flow, provide the state to evaluate, and define one or more questions.
6. Use the generated answer fields as dynamic content in later actions and conditions.

See the [usage guide](docs/usage.md) for a detailed Power Automate example, question types, input schemas, dynamic outputs, and error handling.

### Microsoft-Decision-1

1. [Deploy Microsoft-Decision-1](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/use-foundry-models-microsoft-decision#deploy-microsoft-decision-1) in a Microsoft Foundry resource, and copy the endpoint, deployment name, and API key from the deployment details page.
2. Download the managed `MicrosoftDecisionCustomConnector` solution from the [latest release](https://github.com/markus-tobler/model-one-connectors/releases/latest) and import it into your Power Platform environment.
3. Create a connection for **Microsoft-Decision-1** with the endpoint, deployment name, and API key.
4. Add **Evaluate with Microsoft-Decision-1** to a flow, provide the state to evaluate, and define one or more questions.
5. Use the generated answer fields as dynamic content in later actions and conditions.

See the [Microsoft-Decision-1 usage guide](docs/microsoft-decision-usage.md) for connection details, limits, and error handling.

### Microsoft-Decision-1 for local agents (MCP)

The [Microsoft-Decision-1 MCP server](mcp/microsoft-decision/README.md) offers the same evaluation to local agents such as Claude Code. It requires [uv](https://docs.astral.sh/uv/) and runs with `uvx`:

```bash
claude mcp add microsoft-decision -- uvx --from "git+https://github.com/markus-tobler/model-one-connectors#subdirectory=mcp/microsoft-decision" microsoft-decision-mcp
```

It reads `MS_FOUNDRY_DECISION_1_ENDPOINT`, `MS_FOUNDRY_DECISION_1_DEPLOYMENT`, and `MS_FOUNDRY_DECISION_1_API_KEY` from the environment, then from `.env` in the working directory, then from `~/.env`, then from the OS credential store, which is the recommended place for the API key.

See the [development guide](docs/development.md) to build, test, import, and maintain the connectors.

## Use it, share feedback, or contribute

Try the connectors in your own workflows and help shape it. [Open an issue](https://github.com/markus-tobler/model-one-connectors/issues) to report a bug or suggest an improvement. Documentation fixes and pull requests are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md) to get started.

To report a vulnerability, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Disclaimer

This is an independent community project. It is not affiliated with, endorsed by, or supported by TypeSafe AI or Microsoft. TypeSafe, Jev, Microsoft, Microsoft Foundry, Microsoft-Decision-1, Power Platform, Power Automate, and Copilot Studio are trademarks of their respective owners. Use of the TypeSafe API is subject to TypeSafe's own terms, and use of Microsoft Foundry is subject to Microsoft's terms for your Azure subscription.

## License

[MIT](LICENSE)
