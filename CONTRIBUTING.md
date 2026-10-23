# Contributing

Thanks for your interest in improving the TypeSafe Jev and Microsoft-Decision-1 custom connectors. Bug reports, documentation fixes, and pull requests are welcome.

## Reporting issues

Open a GitHub issue with the steps to reproduce, the expected and actual behavior, and the connector version. Remove API keys, environment URLs, and any personal or customer data from logs and screenshots. Report security problems as described in [SECURITY.md](SECURITY.md).

## Development setup

See the [development guide](docs/development.md) for prerequisites, the source layout, and the DEV loop. In short:

```bash
npm ci
cp .env.example .env   # only needed for importing into a DEV environment
dotnet test tests/JevConnector.Tests
dotnet test tests/MicrosoftDecisionConnector.Tests
pwsh ./scripts/Validate-Solution.ps1
```

`.env` is ignored by git. Never commit real keys or environment URLs.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Keep changes focused. If you change behavior, update the tests and the documentation in `docs/`.
3. If you edited the connector in the maker portal, sync it back with `Sync-FromDevelopment.ps1` so the source in `solutions/` stays the source of truth.
4. Make sure `Validate-Solution.ps1` passes locally. CI runs the same validation on every pull request.
5. Open a pull request that describes the change and how you tested it.

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
