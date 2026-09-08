---
id: us-01
type: user-story
parent: feature-05
wave: 12
status: active
---

# us-01-rich-ask-ui — Articulated Ask replies

## Story

As **procurement**, I want to read Contigo as a specialist (prose, contract
preview, a button to 360 / Renewals / Savings), not as a list of document
ids.

## Acceptance criteria

- [ ] AC-1 Assistant text is rendered as markdown (paragraphs, lists,
      inline `[n]`).
- [ ] AC-2 Citation cards show a human title, page/section, snippet — never
      raw `Document:<guid>` as the only label.
- [ ] AC-3 A first-page preview (or honest placeholder) appears on the card;
      `GET /api/documents/{id}/preview` (or equivalent) is used when the API
      exposes it.
- [ ] AC-4 API `actions` render as in-app links.
- [ ] AC-5 The engineer `route` line (“Structured query…”) is not shown.
- [ ] AC-6 Greeting / off-domain turns use warm copy + optional CTA, not
      only the red “Cannot determine reliably” block.

## Definition of done

- [ ] AC verified by named tests
- [ ] honours ADR-019 / ADR-023

## Dependencies

| Depends on | Why |
|------------|-----|
| F03 | JSON contract |

## Architecture decisions in force

- ADR-018 / ADR-020 amendments
- ADR-019 — consume existing tokens
- ADR-023 — rich reply

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Rich Ask UI | L | phase-3 |

## Open questions

- none
