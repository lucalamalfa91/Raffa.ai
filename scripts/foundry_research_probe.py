#!/usr/bin/env python3
"""Live probe for the ADR-030 `research` role: does this Foundry account, in
this region, serve the Azure OpenAI Responses API (`openai/v1/responses`)
with the hosted `web_search` tool on the chosen deployment?

The backend's `FoundryResearchClient` (backend/src/Raffa.AiGateway/Foundry/)
sends exactly this request shape -- one `web_search` tool, strict JSON
output, no context pack -- and treats a source as real only when it comes
back as a `url_citation` annotation. This script sends the same shape once
and reports what came back, so the operator can decide whether to bind the
`research` role in `infra/environments/<env>/main.tf` (model_roles) and flip
`Chat__WebResearch__Enabled` (ai_gateway_extra_env). Nothing here touches
Terraform or the running apps.

Pass = HTTP 200, at least one `url_citation` annotation, and the model's
JSON parses. Anything else is a fail with the reason printed; the fallback
path is then Foundry Agent Service + Grounding with Bing Search (ADR-030
"considered options"), which needs its own client and infra.

Usage:
    az login
    python scripts/foundry_research_probe.py \
        --endpoint https://aisvc-raffa.cognitiveservices.azure.com/ \
        --deployment gpt-5.4-research-dev \
        [--query "typical uplift caps on enterprise saas renewals"]

Auth: the bearer token from `az account get-access-token --resource
https://cognitiveservices.azure.com` (the caller needs Cognitive Services
OpenAI User on the account), or FOUNDRY_ACCESS_TOKEN in the environment.
No key is ever read or printed (ADR-011).

Exit 0 on pass, 1 on fail, 2 on a usage error.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

RELATIVE_URL = "openai/v1/responses"
SCHEMA_NAME = "raffa_web_research"

OUTPUT_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "required": ["summaryMarkdown", "offTopic", "sources"],
    "properties": {
        "summaryMarkdown": {"type": "string"},
        "offTopic": {"type": "boolean"},
        "sources": {
            "type": "array",
            "items": {
                "type": "object",
                "additionalProperties": False,
                "required": ["n", "url", "title"],
                "properties": {"n": {"type": "integer"}, "url": {"type": "string"}, "title": {"type": "string"}},
            },
        },
    },
}

INSTRUCTIONS = (
    "You are a procurement research assistant. Use the web search tool, cite with [n] markers, "
    "list every source you cite, never write a URL in the summary, and answer with the JSON schema only."
)


def access_token() -> str:
    token = os.environ.get("FOUNDRY_ACCESS_TOKEN", "").strip()
    if token:
        return token
    result = subprocess.run(
        ["az", "account", "get-access-token", "--resource", "https://cognitiveservices.azure.com", "--query", "accessToken", "-o", "tsv"],
        check=False,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0 or not result.stdout.strip():
        print("FAIL: no bearer token -- run `az login` or set FOUNDRY_ACCESS_TOKEN.", file=sys.stderr)
        sys.exit(2)
    return result.stdout.strip()


def probe(endpoint: str, deployment: str, query: str, max_sources: int) -> int:
    url = endpoint.rstrip("/") + "/" + RELATIVE_URL
    body = {
        "model": deployment,
        "instructions": INSTRUCTIONS,
        "input": f"Language: en\nPurpose: MarketPractice\nMax sources: {max_sources}\nQuery: {query}",
        "tools": [{"type": "web_search"}],
        "text": {"format": {"type": "json_schema", "name": SCHEMA_NAME, "strict": True, "schema": OUTPUT_SCHEMA}},
        "max_output_tokens": 1200,
    }
    request = urllib.request.Request(
        url,
        data=json.dumps(body).encode("utf-8"),
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {access_token()}"},
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            status = response.status
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")[:600]
        print(f"FAIL: HTTP {error.code} from {url}\n{detail}")
        print("      A 404/400 here usually means the Responses API or the web_search tool is not served in this region/deployment.")
        return 1
    except urllib.error.URLError as error:
        print(f"FAIL: could not reach {url}: {error.reason}")
        return 1

    output = payload.get("output") or []
    message = next((item for item in output if item.get("type") == "message"), None)
    content = next((c for c in (message or {}).get("content") or [] if c.get("type") == "output_text"), None)
    annotations = [a for a in (content or {}).get("annotations") or [] if a.get("type") == "url_citation"]
    tool_calls = [item for item in output if item.get("type") == "web_search_call"]

    print(f"HTTP {status} · status={payload.get('status')} · model={payload.get('model')}")
    print(f"web_search_call items: {len(tool_calls)} · url_citation annotations: {len(annotations)}")
    for annotation in annotations[:max_sources]:
        print(f"  - {annotation.get('url')}  ({annotation.get('title') or 'untitled'})")

    text = (content or {}).get("text") or ""
    try:
        parsed = json.loads(text) if text else None
    except json.JSONDecodeError as error:
        print(f"FAIL: the output text is not the strict JSON the schema demands: {error}")
        return 1

    if parsed is None:
        print("FAIL: no output_text message in the response.")
        return 1
    if not annotations:
        print("FAIL: the model answered but the tool returned no url_citation -- the backend would treat this as 'no sources' and abstain.")
        return 1

    print(f"summaryMarkdown ({len(parsed.get('summaryMarkdown', ''))} chars), offTopic={parsed.get('offTopic')}, sources={len(parsed.get('sources') or [])}")
    print("PASS: the research role can be bound for this deployment (ADR-030).")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--endpoint", required=True, help="The AI Services account endpoint (module.foundry ai_services_account_endpoint).")
    parser.add_argument("--deployment", required=True, help="The deployment name to probe, e.g. gpt-5.4-research-dev.")
    parser.add_argument("--query", default="typical uplift caps on enterprise saas renewals", help="A procurement query with no tenant data in it.")
    parser.add_argument("--max-sources", type=int, default=3)
    args = parser.parse_args(argv)
    if "citationKey" in args.query or "packJson" in args.query:
        print("usage error: the query must never carry pack content (ADR-030).", file=sys.stderr)
        return 2
    return probe(args.endpoint, args.deployment, args.query, args.max_sources)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
