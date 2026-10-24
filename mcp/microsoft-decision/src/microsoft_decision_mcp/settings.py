from __future__ import annotations

import sys
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlsplit

import keyring
from dotenv import dotenv_values
from keyring.errors import KeyringError

ENDPOINT = "MS_FOUNDRY_DECISION_1_ENDPOINT"
DEPLOYMENT = "MS_FOUNDRY_DECISION_1_DEPLOYMENT"
API_KEY = "MS_FOUNDRY_DECISION_1_API_KEY"

KEYRING_SERVICE = "microsoft-decision-mcp"

SYSTEM_ONE_PATH = "/providers/microsoft/v1/systemone"


class SettingsError(Exception):
    pass


@dataclass(frozen=True)
class Settings:
    url: str
    deployment: str
    api_key: str


def resolve(
    environ: Mapping[str, str], cwd: Path, home: Path, secret_store: Callable[[str], str | None]
) -> Settings:
    """Resolves each variable from the environment, ./.env, ~/.env, then the secret store; first non-empty wins.

    The secret store is only consulted for variables that the earlier sources leave empty.
    """
    lookups = [environ.get, dotenv_values(cwd / ".env").get, dotenv_values(home / ".env").get, secret_store]
    values: dict[str, str] = {}
    for name in (ENDPOINT, DEPLOYMENT, API_KEY):
        value = next((v.strip() for lookup in lookups if (v := lookup(name)) and v.strip()), None)
        if value:
            values[name] = value

    missing = [name for name in (ENDPOINT, DEPLOYMENT, API_KEY) if name not in values]
    if missing:
        raise SettingsError(
            f"Missing configuration: {', '.join(missing)}. Set each as an environment variable, "
            f"in {cwd / '.env'}, in {home / '.env'}, or in the OS credential store with "
            f"'uvx keyring set {KEYRING_SERVICE} <variable>'."
        )

    return Settings(url=system_one_url(values[ENDPOINT]), deployment=values[DEPLOYMENT], api_key=values[API_KEY])


def read_keyring(name: str) -> str | None:
    """Reads a value from the OS credential store; an unavailable store counts as a missing value."""
    try:
        return keyring.get_password(KEYRING_SERVICE, name)
    except KeyringError as ex:
        print(f"microsoft-decision-mcp: cannot read {name} from the credential store: {ex}", file=sys.stderr)
        return None


def system_one_url(endpoint: str) -> str:
    """Keeps only the scheme, host, and port, so a pasted target URI or project endpoint also works."""
    parts = urlsplit(endpoint)
    if parts.scheme != "https" or not parts.hostname:
        raise SettingsError(f"{ENDPOINT} must be an absolute HTTPS URL.")
    host = parts.netloc.rpartition("@")[2]
    return f"https://{host}{SYSTEM_ONE_PATH}"
