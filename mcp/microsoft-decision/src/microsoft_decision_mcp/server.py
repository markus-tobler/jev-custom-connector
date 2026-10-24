import os
import sys
from pathlib import Path
from typing import Annotated, Any, Literal, Union

import httpx2
from mcp.server import MCPServer
from mcp.server.mcpserver.exceptions import ToolError
from pydantic import BaseModel, ConfigDict, Field

from microsoft_decision_mcp.settings import Settings, SettingsError, read_keyring, resolve

# Criteria descriptions and instructions may be text, a JSON object, a JSON array, or null.
Entry = Union[str, dict[str, Any], list[Any], None]


class _Question(BaseModel):
    model_config = ConfigDict(extra="forbid")

    instructions: Entry = Field(None, description="The question to ask about the state.")


class NoulCriteria(BaseModel):
    model_config = ConfigDict(extra="forbid")

    true: Entry = Field(None, description="Optional description of a yes answer.")
    false: Entry = Field(None, description="Optional description of a no answer.")


class NoulQuestion(_Question):
    """Answers yes or no with a probability."""

    type: Literal["noul"]
    criteria: NoulCriteria | None = None


class ChoiceQuestion(_Question):
    """Selects one option."""

    type: Literal["choice"]
    criteria: Annotated[
        dict[Annotated[str, Field(min_length=1)], Entry],
        Field(min_length=2, max_length=255, description="2 to 255 options: option key mapped to an optional description."),
    ]


class ScoreQuestion(_Question):
    """Rates against ordered levels."""

    type: Literal["score"]
    criteria: Annotated[
        list[Entry],
        Field(min_length=2, max_length=10, description="2 to 10 ordered level descriptions, starting with score 0."),
    ]


Question = Annotated[Union[NoulQuestion, ChoiceQuestion, ScoreQuestion], Field(discriminator="type")]


def create_server(settings: Settings, transport: httpx2.AsyncBaseTransport | None = None) -> MCPServer:
    server = MCPServer("microsoft-decision")

    @server.tool()
    async def evaluate(
        state: Annotated[
            Entry,
            Field(description="The state to evaluate: text, or a JSON object or array whose fields questions can refer to."),
        ],
        questions: Annotated[
            dict[Annotated[str, Field(min_length=1)], Question],
            Field(min_length=1, description="Questions keyed by question ID, for example is_urgent. Answers use the same keys."),
        ],
    ) -> dict[str, Any]:
        """Evaluate a state against yes/no (noul), choice, and score questions with Microsoft-Decision-1.

        Returns the model, typed answers keyed by question ID, and token usage. A noul answer has a
        yes probability; a choice answer has the selected option, confidence, and per-option
        probabilities; a score answer has the expected score, confidence, and per-level probabilities.
        """
        payload = {
            "model": settings.deployment,
            "state": state,
            "questions": {key: question.model_dump(exclude_unset=True) for key, question in questions.items()},
        }
        headers = {"api-key": settings.api_key, "Accept": "application/json"}
        try:
            async with httpx2.AsyncClient(transport=transport, timeout=60) as client:
                response = await client.post(settings.url, json=payload, headers=headers)
        except httpx2.HTTPError as ex:
            raise ToolError(f"Request to Microsoft-Decision-1 failed: {ex}") from ex

        if not response.is_success:
            raise ToolError(f"Microsoft-Decision-1 returned HTTP {response.status_code}: {response.text}")
        return response.json()

    return server


def main() -> None:
    try:
        settings = resolve(os.environ, Path.cwd(), Path.home(), read_keyring)
    except SettingsError as ex:
        print(f"microsoft-decision-mcp: {ex}", file=sys.stderr)
        sys.exit(1)

    create_server(settings).run()
