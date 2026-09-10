#!/usr/bin/env python3
"""One-off, idempotent authoring helper for the Ask V2 Passata 1 (2026-09-08).

Appends the epic-13 / ADR-024 amendment footers to ADR-001/004/011/018/020,
the V2 section to reports/architecture/INDEX.md, the epic-13 rows to
reports/workitems/BACKLOG.md and the OQ-askv2 entries to
reports/open-questions.md. Safe to re-run: every write is guarded by a
presence check.

Usage (cwd = .helix):
  python scripts/_apply_ask_v2_footers.py
"""

from __future__ import annotations

from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
ARCH = HERE / "reports" / "architecture"

FOOTERS = {
    "ADR-001-scope-r0-r4.md": """
## Amendment (2026-09-08, epic-13 / ADR-024)

The **Internal Dataset** is now the **mock market-intelligence feed**
(`inputs/requirements.md` R-MKT-01…05): a checked-in, labelled
*representative* dataset of how companies close contracts (price bands,
discounts, uplift caps, notice periods, negotiated clauses) behind
`IMarketIntelligenceProvider`, projected into `IBenchmarkService` rows and a
shared read-only market index. The paid third-party API stays a later
provider behind the same seam — never a hard dependency of the first V2
`demo`, never another tenant's contracts. This footer supersedes the
epic-12 amendment above. See ADR-024.
""",
    "ADR-004-foundry-models.md": """
## Amendment (2026-09-08, epic-13 / ADR-024)

`FoundryAiGateway` implements the five roles (`ocr` on Document Intelligence,
`classify`, `extract`, `embed`, `answer`), registered when
`AiGateway:Endpoint` is set, fixture otherwise, always wrapped by
`LoggingAiGateway`. `classify` is reused for the **document admission gate**
and for the Ask **domain gate** with fixed label sets. `answer` returns
**structured JSON** (`canDetermine`, `answerMarkdown`, `citationKeys`,
`actionKeys`, `abstainReason`, `followUps`) from a versioned persona prompt,
temperature <= 0.2, with **no tools, no web grounding, no browsing** on the
deployment or the request (compliance test on the fake HTTP handler).
Per-tenant daily token and OCR-page budgets fail visibly. This footer
supersedes the epic-12 amendment above. See ADR-024.
""",
    "ADR-011-secrets-and-rag.md": """
## Amendment (2026-09-08, epic-13 / ADR-024)

Two corpora remain, never one blended index:

1. **Tenant RAG** — this workspace's validated contracts (authz →
   `tenant_id` filter → retrieval); embedding rows gain `page` / `section`.
   Unchanged from the original decision.
2. **Market** — the market-intelligence feed (mock now) with its **own
   vector index** `market_embedding`: no `tenant_id`, readable by every
   tenant, written only by the ingestion job, never containing tenant
   content, never joined with tenant tables.

**Conversations** (`conversation`, `conversation_message`) are tenant tables
under the same RLS policy, keyed by tenant + user. Off-domain turns retrieve
from neither corpus. Documents are classified **before** persistence;
rejected files are never stored (audit hash only). This footer supersedes
the epic-12 amendment above. See ADR-024.
""",
    "ADR-018-web-information-architecture.md": """
## Amendment (2026-09-08, epic-13 / ADR-024)

The V2 IA replaces the Day-1 sitemap. Pixel and behaviour reference:
`inputs/design/prototypes/Raffa V2 Prototype.html`, unpacked at
`inputs/design/prototypes/raffa-v2/` (`ia-v2.md` is the canonical route
map). Sign-in lands on **`/ask`** (`/` redirects); `/ask/:conversationId`
resumes a chat. **Two-tier rail**: Ask Raffa (⌘K, last 5 conversations,
"+ New chat") and Documents; "From your contracts": Portfolio, Renewals,
Quote check, greyed until the first validated contract. **No Home item**;
Savings lives at `/savings`, reached from actions, Renewals and Contract 360.
Review is a **state of Documents** (`/documents?review=:id`). The global Ask
bar always opens a new chat. Roles: Admin and Procurement upload; only Admin
deletes and manages members. Divergences from the prototype follow
`inputs/requirements.md` and are listed in `ia-v2.md`. This footer
supersedes the epic-12 amendment above. See ADR-024.
""",
    "ADR-020-web-screen-inventory.md": """
## Amendment (2026-09-08, epic-13 / ADR-024)

Screen inventory V2 is `inputs/design/prototypes/raffa-v2/screens-v2.md`
(authored from `Raffa V2 Prototype.html`): 1 Sign-in → Ask, 2 Ask Raffa
(home; off / new chat / conversation / abstain / redirect / refusal /
resumed), 3 Documents (onboarding, multi-file, **Not added**, attention
filter, real stages, validated hook), 4 Review as a state of Documents,
5 Contract 360 (citation landing with highlighted clause; answers band and
tracker as the P2 follow-up), 6 Portfolio, 7 Renewals, 8 Savings
(`/savings`), 9 Quote check, 10 Workspace & members. Every §16 row and §20
step still resolves to a screen (traceability table in `screens-v2.md`).
The reply body on screen 2 is the ADR-024 contract: markdown, human
citation cards with corpus badge and preview, in-app actions; the red
abstain block only for true insufficiency. This footer supersedes the
epic-12 amendment above. See ADR-024.
""",
}

INDEX_SECTION = """

## Ask Raffa V2 (wave 13 / e13, appended 2026-09-08)

ADR-001…022 keep their original Decision; ADR-001, 004, 011, 018, 020 gain
an **epic-13 amendment footer** (superseding their epic-12 footers).
**ADR-023 is superseded by ADR-024** (HITL 2026-09-08, `inputs/requirements.md`
§0 D4; epic-12 / e12 never launched). New accepted ADR from
`raffa-ask-process.yaml` (Ask V2):

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-024 | Ask Raffa V2 | product-owner + software-architect + security-architect + ux-ui-designer | Documents-only intake with an admission gate before persistence (non-contracts refused, never stored); server-side conversations under RLS; three sources of truth (validated contracts, market-intelligence feed with its own index — mock now, API later —, capability catalog); structured no-tools `answer` role with grounding + numeric guards; deterministic strategies (contract vs market, renewal strategy, portfolio criticality); V2 IA with `inputs/design/prototypes/Raffa V2 Prototype.html` (unpacked `raffa-v2/`) as the pixel reference. |
"""

BACKLOG_OLD = "| epic-12 | ask-copilot | 12 | active — decomposed (Ask savings copilot) |"
BACKLOG_NEW = (
    "| epic-12 | ask-copilot | 12 | superseded by epic-13 (never launched; ADR-023 → ADR-024) |\n"
    "| epic-13 | ask-v2 | 13 | active — decomposed (Ask Raffa V2: Documents intake + admission gate, "
    "conversations, market feed, strategies, capability catalog, V2 IA) |"
)
BACKLOG_ADR_ROWS = """
| ADR-023 | Ask savings copilot (superseded) | epic-12 (superseded by epic-13) |
| ADR-024 | Ask Raffa V2 | epic-13 (all features); design oracle `inputs/design/prototypes/Raffa V2 Prototype.html` |
"""

OQ_SECTION = """

## Ask V2 lane (epic-13 / ADR-024, 2026-09-08)

Source: `inputs/requirements.md` §13. Every entry has an assumption in force; none gates a task.

- **OQ-askv2-001** — The mock market-intelligence record shape (`MarketDeal`, R-MKT-01) is Raffa's own normalized contract; the third-party API will be mapped onto it. **Status**: `assumed-confirmed`. **Assumption in force**: build `IMarketIntelligenceProvider` around `MarketDeal`; the live client maps into it. Ref: ADR-024.
- **OQ-askv2-002** — Admission threshold (0.6) and minimum readable text (200 chars) are right for the golden set. **Status**: `assumed-confirmed`. **Assumption in force**: both are configuration (`Documents:AdmissionThreshold`, `Documents:MinReadableChars`), tuned on `Raffa.AiEval`. Ref: R-DOC-03.
- **OQ-askv2-003** — Savings KPIs / opportunities are not a rail item in V2. **Status**: `assumed-confirmed`. **Assumption in force**: route `/savings` (renamed from Home), reached from Ask actions, Renewals and Contract 360. Ref: R-WEB-02, `raffa-v2/ia-v2.md`.
- **OQ-askv2-004** — Conversation retention. **Status**: `assumed-confirmed`. **Assumption in force**: unlimited in V2; deletion by the owner only. Ref: R-CONV-01.
- **OQ-askv2-005** — Per-user identity for conversations under the ADR-022 header posture. **Status**: `assumed-confirmed`. **Assumption in force**: `X-User-Id` = MSAL account username, non-authoritative; the task that lands the API JWT (ADR-010) replaces it with the token subject. Ref: R-CONV-03.
- **OQ-askv2-006** — Answer language. **Status**: `assumed-confirmed`. **Assumption in force**: follows the question's language; fixtures and golden set cover Italian and English. Ref: ADR-024.
- **OQ-askv2-007** — Upload processing stays synchronous in the request. **Status**: `assumed-confirmed`. **Assumption in force**: bounded by 50 MB / file and the OCR page budget; worker-queued upload is a later task. Ref: R-DOC-01, A7.
- **OQ-askv2-008** — A Quote admitted in Documents. **Status**: `assumed-confirmed`. **Assumption in force**: no automatic `Quote` record; the result card and Ask route to Quote check (`/quotes`) where the user uploads the quote. Ref: R-DOC-03 AC-4.
- **OQ-askv2-009** — Live Foundry on `dev` / `demo`. **Status**: `assumed-confirmed`. **Assumption in force**: acceptance A2–A8 runs against the Foundry-backed gateway (Container Apps inject `AiGateway__*`); CI proves the same paths on the fixture gateway. Ref: R-AI-01.
"""


def main() -> int:
    for name, footer in FOOTERS.items():
        p = ARCH / name
        t = p.read_text(encoding="utf-8")
        if "epic-13 / ADR-024" in t:
            print("skip (exists)", name)
            continue
        p.write_text(t.rstrip("\n") + "\n" + footer, encoding="utf-8")
        print("footer", name)

    idx = ARCH / "INDEX.md"
    t = idx.read_text(encoding="utf-8")
    if "ADR-024" not in t:
        idx.write_text(t.rstrip("\n") + INDEX_SECTION, encoding="utf-8")
        print("INDEX appended")

    bl = HERE / "reports" / "workitems" / "BACKLOG.md"
    t = bl.read_text(encoding="utf-8")
    if BACKLOG_OLD in t:
        t = t.replace(BACKLOG_OLD, BACKLOG_NEW, 1)
        print("BACKLOG epic rows updated")
    elif "epic-13" not in t:
        print("WARN: epic-12 row not found verbatim; epic-13 row appended after the epics table")
        t = t.replace("| epic-12 | ask-copilot | 12 |", "| epic-13 | ask-v2 | 13 | active — decomposed (Ask Raffa V2) |\n| epic-12 | ask-copilot | 12 |", 1)
    if "ADR-024" not in t:
        t = t.rstrip("\n") + "\n" + BACKLOG_ADR_ROWS.lstrip("\n")
        print("BACKLOG ADR rows appended")
    bl.write_text(t, encoding="utf-8")

    oq = HERE / "reports" / "open-questions.md"
    t = oq.read_text(encoding="utf-8")
    if "OQ-askv2-001" not in t:
        oq.write_text(t.rstrip("\n") + OQ_SECTION, encoding="utf-8")
        print("open-questions appended")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
