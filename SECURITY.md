# Security policy

## Supported versions

Only the [latest release](https://github.com/markus-tobler/jev-custom-connector/releases/latest) receives fixes.

## Reporting a vulnerability

Do not open a public issue for security problems. Instead, report them privately through GitHub: go to the repository's **Security** tab and select **Report a vulnerability**.

Include the affected version, steps to reproduce, and the potential impact. You can expect an initial response within a few days.

## Handling credentials

Never include TypeSafe or Foundry API keys, Power Platform environment URLs, connection IDs, or access tokens in issues, pull requests, logs, or screenshots. If you accidentally expose a TypeSafe API key, revoke it immediately in the [TypeSafe dashboard](https://console.typesafe.ai/keys). If you expose a Foundry API key, regenerate it on the resource's **Keys and Endpoint** page.

Vulnerabilities in the TypeSafe API itself should be reported to TypeSafe AI, and vulnerabilities in Microsoft Foundry or Power Platform to Microsoft.
