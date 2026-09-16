---
id: epic-25
type: epic
wave: w18
status: active
extends: [epic-13, epic-07 F02, epic-08 F03]
---

# epic-25-ask-and-quote-product-completeness — Ask/Quote product completeness + chip hiding

## Business capability
Close the product gaps in the Ask/Quote surfaces that the V2 rebuild left
behind: an admin-gated Ask chip is hidden from a non-Admin (presentation, never
a security fix); a tenant citation card deep-links to the viewer instead of
showing an empty "No page preview available" placeholder; the 360 "Ask about
it" entry briefs the contract it is scoped to instead of landing on a generic
gate; Quote check behaves as a market-benchmark job with history, not a manual
worksheet; an abstain reply carries a recovery action so Ask never dead-ends;
and the global Ask bar is suppressed on Ask screens where it duplicates the
composer.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/w18-todo.md §2 | NW-74, NW-55, NW-56, NW-57, NW-59, NW-60 |
| ADR-022 S16-11 | chip hiding is presentation, never a control |
| ADR-024 | one Ask engine, scoped entry, reply kinds |
| ADR-018/ADR-020 | viewer route deep-link, screen inventory |
| screens-v2.md §2 (Ask/abstain), §5 (360 "Ask about it"), §10 (Quote check) |

## Features
| ID | Title | Wave |
|----|------|------|
| feature-01 | ask-chip-role-gate | w18 |
| feature-02 | citation-cards | w18 |
| feature-03 | scoped-ask-brief | w18 |
| feature-04 | quote-benchmark | w18 |
| feature-05 | abstain-recovery | w18 |
| feature-06 | hide-global-ask-bar | w18 |

## Success looks like
A Procurement member sees no admin-gated chips (Admin does; the capabilities GET
is identical for both); a tenant citation opens the page the fact came from; a
scoped 360 entry briefs the contract; a Quote check presents a market benchmark
with history; every abstain/error has a clickable next step; and there is no
second Ask bar on Ask screens.

## Architecture decisions in force
- ADR-022 S16-11 + ADR-012 w17 cl 40 — chip hide is presentation; `GET /api/capabilities` stays un-gated.
- ADR-024 — one engine, scoped entry; reply `kind` decides layout; actions from the catalog only.
- ADR-018 — viewer route `/documents/:id/viewer?page&clause` (w17) is the tenant citation deep-link target.
- ADR-028 + ADR-001 — quote history is server state; fixture-adapter fence stays.

## Out of scope
- No new security control (NW-74) — `roleGate` remains presentation.
- No fabricated `previewUrl` for product (`corpus=raffa`) citations — CTA card, not a page preview.
- No paid external market API (ADR-001 §1.2 fixture fence).
