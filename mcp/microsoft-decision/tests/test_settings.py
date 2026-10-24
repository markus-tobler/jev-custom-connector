from pathlib import Path

import pytest

import keyring
from keyring.errors import NoKeyringError

from microsoft_decision_mcp.settings import (
    API_KEY,
    DEPLOYMENT,
    ENDPOINT,
    KEYRING_SERVICE,
    SettingsError,
    read_keyring,
    resolve,
)

ENDPOINT_URL = "https://env.services.ai.azure.com"


def no_secrets(name: str) -> None:
    return None


def write_env(directory: Path, **values: str) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    (directory / ".env").write_text("".join(f"{name}={value}\n" for name, value in values.items()), encoding="utf-8")


def test_environment_wins_over_dotenv_files(tmp_path):
    cwd, home = tmp_path / "cwd", tmp_path / "home"
    write_env(cwd, **{ENDPOINT: "https://cwd.example.com", DEPLOYMENT: "cwd-deployment", API_KEY: "cwd-key"})
    write_env(home, **{ENDPOINT: "https://home.example.com", DEPLOYMENT: "home-deployment", API_KEY: "home-key"})

    settings = resolve(
        {ENDPOINT: ENDPOINT_URL, DEPLOYMENT: "env-deployment", API_KEY: "env-key"}, cwd, home, no_secrets
    )

    assert settings.url == "https://env.services.ai.azure.com/providers/microsoft/v1/systemone"
    assert settings.deployment == "env-deployment"
    assert settings.api_key == "env-key"


def test_each_variable_falls_through_independently(tmp_path):
    cwd, home = tmp_path / "cwd", tmp_path / "home"
    write_env(cwd, **{DEPLOYMENT: "cwd-deployment", API_KEY: '"  cwd-key  "'})
    write_env(home, **{ENDPOINT: "https://home.example.com", DEPLOYMENT: "home-deployment", API_KEY: "home-key"})

    settings = resolve({API_KEY: "   "}, cwd, home, no_secrets)

    assert settings.url == "https://home.example.com/providers/microsoft/v1/systemone"
    assert settings.deployment == "cwd-deployment"
    assert settings.api_key == "cwd-key"


def test_dotenv_supports_export_and_comments(tmp_path):
    cwd = tmp_path / "cwd"
    cwd.mkdir()
    (cwd / ".env").write_text(
        f"# comment\nexport {ENDPOINT}='{ENDPOINT_URL}'\n\n{DEPLOYMENT}=d\n{API_KEY}=k\n", encoding="utf-8"
    )

    settings = resolve({}, cwd, tmp_path / "home", no_secrets)

    assert settings.url.startswith(ENDPOINT_URL)


def test_missing_values_are_listed(tmp_path):
    with pytest.raises(SettingsError) as error:
        resolve({DEPLOYMENT: "d"}, tmp_path / "cwd", tmp_path / "home", no_secrets)

    assert ENDPOINT in str(error.value)
    assert API_KEY in str(error.value)
    assert DEPLOYMENT not in str(error.value)


def test_secret_store_is_consulted_last_and_only_for_missing_values(tmp_path):
    home = tmp_path / "home"
    write_env(home, **{DEPLOYMENT: "home-deployment"})
    requested = []

    def secret_store(name):
        requested.append(name)
        return {ENDPOINT: "https://store.example.com", DEPLOYMENT: "store-deployment", API_KEY: " store-key "}[name]

    settings = resolve({ENDPOINT: ENDPOINT_URL}, tmp_path / "cwd", home, secret_store)

    assert requested == [API_KEY]
    assert settings.url.startswith(ENDPOINT_URL)
    assert settings.deployment == "home-deployment"
    assert settings.api_key == "store-key"


def test_missing_values_name_the_credential_store(tmp_path):
    with pytest.raises(SettingsError, match=f"keyring set {KEYRING_SERVICE}"):
        resolve({}, tmp_path, tmp_path, no_secrets)


def test_read_keyring_uses_the_service_and_variable_name(monkeypatch):
    monkeypatch.setattr(keyring, "get_password", lambda service, name: f"{service}/{name}")

    assert read_keyring(API_KEY) == f"{KEYRING_SERVICE}/{API_KEY}"


def test_read_keyring_treats_an_unavailable_store_as_missing(monkeypatch, capsys):
    def unavailable(service, name):
        raise NoKeyringError("No recommended backend was available.")

    monkeypatch.setattr(keyring, "get_password", unavailable)

    assert read_keyring(API_KEY) is None
    assert "No recommended backend" in capsys.readouterr().err


@pytest.mark.parametrize(
    ("endpoint", "url"),
    [
        ("https://r.services.ai.azure.com/providers/microsoft/v1/systemone", "https://r.services.ai.azure.com"),
        ("https://r.cognitiveservices.azure.com/", "https://r.cognitiveservices.azure.com"),
        ("https://localhost:8443/api/projects/p", "https://localhost:8443"),
    ],
)
def test_endpoint_keeps_only_scheme_host_and_port(tmp_path, endpoint, url):
    settings = resolve({ENDPOINT: endpoint, DEPLOYMENT: "d", API_KEY: "k"}, tmp_path, tmp_path, no_secrets)

    assert settings.url == url + "/providers/microsoft/v1/systemone"


@pytest.mark.parametrize("endpoint", ["http://r.services.ai.azure.com", "r.services.ai.azure.com"])
def test_endpoint_must_be_https(tmp_path, endpoint):
    with pytest.raises(SettingsError, match="HTTPS"):
        resolve({ENDPOINT: endpoint, DEPLOYMENT: "d", API_KEY: "k"}, tmp_path, tmp_path, no_secrets)
