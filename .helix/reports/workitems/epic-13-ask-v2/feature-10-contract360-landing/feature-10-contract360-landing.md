---
id: F10
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-10-contract360-landing — Citation landing and "Ask about it"

## Slice

Following a citation from Ask must land on the clause with its **original
wording highlighted** in Contract 360 (prototype `app.jsx` `hl`,
`citedOpened`, `back`; `screens-v2.md` §5 "Why"), and Contract 360 must
offer **Ask about it**, opening a chat scoped to that contract
(`/ask?scope=<contractId>`) with the prototype's supplier-named chips.
The full V2 answers-band / details / tracker layout of Contract 360 is
this feature's follow-up after V2 acceptance (P2, `inputs/requirements.md`
R-WEB-06); V2 acceptance requires only the landing and the scoped chat
(R-EVD-02).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | A citation opens the proof, and the proof opens a chat | 13 |

## Architecture decisions in force

- ADR-024 — citation landing, scoped conversations
- ADR-020 (amended) — screen 5 in `screens-v2.md`
- ADR-019 — tokens unchanged; spec §8.4 evidence

## Target repo

`contigo-web`
