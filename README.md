# TypeSafe Jev custom connector

A Microsoft Power Platform custom connector for [TypeSafe AI's Jev](https://typesafe.ai/). Jev evaluates text or structured data against questions you define and returns typed answers with probabilities and confidence values.

The connector exposes those answers as named dynamic content. For example, a question with the ID `is_urgent` produces an `is_urgent yes probability` value that a flow can use in a condition. Choice and score questions similarly produce the selected value, confidence, and individual probabilities.

## Why use Jev in automation?

Power Automate, workflows, and Copilot Studio often need to make a small, repeatable decision from natural-language input: route a request, identify an intent, assess urgency, assign a score, or decide whether a case needs review. Jev is TypeSafe AI's first **System One model**, designed specifically for these fast, focused judgments inside software.

Unlike a prompt that generates free-form text, Jev returns answers in a constrained type you define: yes/no probability, one choice from a fixed set, or a score against an ordered rubric. The connector turns those answers into named Power Platform fields, making them straightforward to use in conditions, switches, approvals, and agent workflows. Probabilities and confidence values let the automation set explicit thresholds and escalate uncertain cases instead of treating every model answer as equally reliable.

Jev complements rather than replaces prompts and frontier models:

- Use **Jev** for frequent, narrow decisions that should return predictable, machine-readable values.
- Use **prompts and frontier models** when the task requires content generation, open-ended reasoning, summarization, or a conversational response.
- Combine them by using Jev to route or qualify work before a prompt, validate a result after a prompt, or send uncertain cases to a frontier model or a person.

## Get started

1. Get a Jev API key from the [TypeSafe dashboard](https://console.typesafe.ai/keys).
2. Open the repository's [Releases page](https://github.com/markus-tobler/jev-custom-connector/releases) and download the managed `JevCustomConnector` solution from the [latest release](https://github.com/markus-tobler/jev-custom-connector/releases/latest).
3. Import the managed solution into your Power Platform environment.
4. Create a connection for **TypeSafe Jev v1** and enter the API key.
5. Add **Evaluate with Jev** to a flow, provide the state to evaluate, and define one or more questions.
6. Use the generated answer fields as dynamic content in later actions and conditions.

See the [usage guide](docs/usage.md) for a detailed Power Automate example, question types, input schemas, dynamic outputs, and error handling.

See the [development guide](docs/development.md) to build, test, import, and maintain the connector.

## Contributing and security

Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md). To report a vulnerability, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Disclaimer

This is an independent community project. It is not affiliated with, endorsed by, or supported by TypeSafe AI or Microsoft. TypeSafe, Jev, Microsoft, Power Platform, Power Automate, and Copilot Studio are trademarks of their respective owners. Use of the TypeSafe API is subject to TypeSafe's own terms.

## License

[MIT](LICENSE)
