# Ask ADR protocol (two seats)

Oracle is `inputs/ask-copilot-brief.md`. Existing ADRs stay accepted; this
gate **amends** them and adds ADR-023.

## Seats

- **ask-auditor** (producer): writes the gap matrix, ADR-023, and append-only
  amendment footers on ADR-001, 004, 011, 018, 020. Appends INDEX.md.
- **ask-critic** (gate, read-only): checks the brief is carried, tenant RAG is
  not mixed with market catalog, no legal-advice scope, Foundry stays behind
  `IAiGateway`. Never writes files.

Producers never emit gate markers. Open each group-chat turn with your
role label (`ASK_AUDITOR:`, `ASK_CRITIC:`).

## Amendment rules

- Keep Status: accepted on ADR-001…022. Do not rewrite the original Decision.
- New content lives under `## Amendment (2026-09-08, epic-12 / ADR-023)`.
- Never weaken ADR-009 RLS or ADR-011 authz-before-retrieval.
