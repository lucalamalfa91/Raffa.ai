---
id: feature-02
type: feature
parent: epic-17
wave: w15
status: active
extends: epic-15 F02 (invitation web — the pane and the accept screen)
---

# feature-02-invite-pane-and-accept — The invite pane and the accept screen tell the truth

## Slice

The invite pane stops branching on one boolean and starts rendering **one server
outcome string**, with the link shown only when it is *usable*. The 502's reason
reaches the pane as a **typed** field rather than as server prose the client
string-matches. And the accept screen stops asking the invitee to press a second
button they have no reason to understand: the popup's own promise carries
straight into the join.

This feature also publishes the invite half of the API contract —
`identityProvisioned`, `deliveryOutcome` and the **declared** 502 — because
`renderOperation` types every status key under `responses`, so a declared 502 is
typed for free and an undeclared one forces exactly the hand-written DTO
ADR-012 `:53` prohibits.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The pane renders one server fact, and accept completes in one click | w15 |

## Architecture decisions in force

- **ADR-020** (w15 §3) — screen 10's corrected **three**-outcome set, the withdrawn fourth state, the **non-shipping** "Try sending again" affordance, the 502 reason copy with its catch-all row, the identity `.micro-meta` on three surfaces, and the confirmation that **no member status is added**.
- **ADR-012** (w15 §7, §8, §13.1–§13.3) — the discriminant is the server's outcome string; the typed `failureReason` off a declared 502; `identityProvisioned` is a non-nullable boolean; the accept auto-continues on its own promise, not on a `useEffect` over `accounts`.
- **ADR-026** — the field names, values and nullability are the server's; the client constrains only `enum`-ness and nullability, which are generator facts.
- **ADR-019** — `.btn-secondary`, `.micro-meta`, `.input` and paragraphs are all catalogued: **NW-69 adds no semantic row, no token and no component.**
- **ADR-018** — no route and no IA state; the pane is one screen's affordance.

## Target repo

`raffa-web` (plus `web/openapi/raffa-api.v1.json`, the published contract)
