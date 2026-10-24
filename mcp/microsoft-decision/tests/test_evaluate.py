import json

import httpx2
import pytest
from mcp.client import Client

from microsoft_decision_mcp.server import create_server
from microsoft_decision_mcp.settings import Settings

URL = "https://my-resource.services.ai.azure.com/providers/microsoft/v1/systemone"
SETTINGS = Settings(url=URL, deployment="triage-decision", api_key="foundry-secret-key-123")

QUESTIONS = {
    "is_urgent": {"type": "noul", "instructions": "Does this need an immediate response?"},
    "category": {"type": "choice", "criteria": {"billing": None, "engineering": "Bugs and outages."}},
    "severity": {"type": "score", "criteria": ["None", "Minor", "Major"]},
}

ANSWER = {
    "model": "triage-decision",
    "answers": {"is_urgent": {"type": "noul", "noul": 0.95}},
    "usage": {"input_tokens": 10, "output_tokens": 3},
}


@pytest.fixture
def anyio_backend():
    return "asyncio"


def recording_transport(status=200, body=ANSWER):
    sent = []

    def handler(request):
        sent.append(request)
        return httpx2.Response(status, json=body)

    return httpx2.MockTransport(handler), sent


async def call_evaluate(transport, arguments):
    async with Client(create_server(SETTINGS, transport)) as client:
        return await client.call_tool("evaluate", arguments)


@pytest.mark.anyio
async def test_evaluate_sends_native_request_and_returns_answers():
    transport, sent = recording_transport()

    result = await call_evaluate(transport, {"state": {"subject": "Site down"}, "questions": QUESTIONS})

    assert not result.is_error
    assert result.structured_content == ANSWER
    [request] = sent
    assert str(request.url) == URL
    assert request.headers["api-key"] == "foundry-secret-key-123"
    assert json.loads(request.content) == {
        "model": "triage-decision",
        "state": {"subject": "Site down"},
        "questions": QUESTIONS,
    }


@pytest.mark.anyio
async def test_upstream_error_is_passed_through_as_tool_error():
    transport, _ = recording_transport(401, {"error": {"code": "401", "message": "Access denied"}})

    result = await call_evaluate(transport, {"state": "text", "questions": {"q": {"type": "noul"}}})

    assert result.is_error
    assert "HTTP 401" in result.content[0].text
    assert "Access denied" in result.content[0].text


@pytest.mark.anyio
@pytest.mark.parametrize(
    "questions",
    [
        {},
        {"category": {"type": "choice", "criteria": {"billing": None}}},
        {"severity": {"type": "score", "criteria": [str(level) for level in range(11)]}},
        {"q": {"type": "unknown"}},
        {"q": {"type": "noul", "criteria": {"maybe": "x"}}},
    ],
)
async def test_invalid_questions_are_rejected_without_a_call(questions):
    transport, sent = recording_transport()

    result = await call_evaluate(transport, {"state": "text", "questions": questions})

    assert result.is_error
    assert sent == []
