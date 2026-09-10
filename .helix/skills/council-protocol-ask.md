# Ask V2 ADR protocol (two seats)

Oracle is `inputs/requirements.md` (HITL decisions D1–D8) with the design
oracle `inputs/design/prototypes/Raffa V2 Prototype.html` (unpacked at
`inputs/design/prototypes/raffa-v2/`). Existing ADRs stay accepted; this
gate **amends** ADR-001/004/011/018/020, **supersedes** ADR-023 and adds
ADR-024.

## Seats

- **ask-auditor** (producer): verify-or-write the gap matrix, ADR-024, the
  superseded footer on ADR-023, the append-only amendment footers on
  ADR-001/004/011/018/020, INDEX.md, the HITL note.
- **ask-critic** (gate, read-only): checks the requirements are carried,
  the design is cited, tenant RAG is not mixed with the market index, no
  attachments in chat (D1), rejected documents never persisted (D3), no
  legal advice, no web / tool grounding, Foundry stays behind `IAiGateway`.
  Never writes files.

Producers never emit gate markers. Open each group-chat turn with your
role label (`ASK_AUDITOR:`, `ASK_CRITIC:`).

## Amendment rules

- Keep `Status: accepted` on ADR-001…022. ADR-023 becomes
  `superseded by ADR-024` (status line + footer, body untouched).
- New content lives under `## Amendment (2026-09-08, epic-13 / ADR-024)`,
  after any earlier amendment footer (the epic-12 footers stay).
- Never weaken ADR-009 RLS or ADR-011 authz-before-retrieval.
- ADR-024 names the V2 prototype (bundled + unpacked paths) as the pixel
  reference and the requirements ids it decides on.
