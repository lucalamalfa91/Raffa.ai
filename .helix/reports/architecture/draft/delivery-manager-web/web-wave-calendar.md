# Web delta — e06+ overnight slice calendar (proposed)

- **Status**: proposed (delivery-manager web lane)
- **Date**: 2026-09-04
- **Owner**: delivery-manager
- **Locked citations**:
  - Delta intake `inputs/web-integration-brief.md` §2 — "New execution slices are **web waves starting at wave / epic 6** (`e06`, `e07`, …). Do not splice UI tasks into `slices/e01.yaml`–`e05.yaml`."
  - `reports/context/web-integration-mandate.md` §3 — append-only; `layer: web` unless thin API gap; `target_repo: raffa-web`.
  - `reports/context/web-integration-mandate.md` §7 — new DAG goes in `wave-spec.web.yaml`; new index/manifest in `INDEX-web.md` / `MANIFEST-web.yaml`.
  - ADR-016 promotion (`demo-v*`, gated `demo` environment) — unchanged; web slices promote through the existing flow.
  - `.helix` protocol — Passata 2 stays `./run.ps1 -Max -Slice e06 -o execution-fanout` on the original artifact when the live wave is idle.

## Scope of this lane

This lane plans the **overnight cut** (how the web epics decompose into executable
slices), not the stories themselves. Adopted from both the brief §6 and the mandate §7.

It does **not**:
- write `slices/e06.yaml` … (backlog-decomposer writes those after ADR-018+ are accepted),
- edit `slices/e01.yaml`–`e05.yaml`, `wave-spec.execution.yaml`, or `slice.current.yaml`,
- write `wave-spec.web.yaml` / `MANIFEST-web.yaml` / `INDEX-web.md` (decomposer),
- renumber epics, or re-open ADR-001…017.

## Baseline: what already exists

`reports/plan/slices/MANIFEST.yaml` ends at `e05` (E05 quotes/Day-1). `e01.yaml`…`e05.yaml`
are closed as `layer: backend`/`frontend` scaffold. The `web/` OIDC shell + generated API
client + `config.json` already landed in E01/F07 — so the **auth gate and shell exist**;
what is missing is the *screen* work that consumes E02–E05 endpoints.

ADR-018 (IA), ADR-019 (design system) and ADR-020 (screen inventory) are **already
accepted on disk** and lock the left-rail sitemap, the Modernist design tokens, and the
1:1 §16/§20 → ten-screen inventory. This lane accepts those as the fixed surface the
slice calendar must put on screen; the Claude Design export is populated at
`inputs/design/prototypes/` (`ia.md`, `screens.md`, `design-system.md`, `day1-demo.html`).

## Proposed overnight cut (e06+)

The brief §6 and mandate §7 agree on six capability chunks. The cut below is the
**recommended starting point**, one slice per chunk, with slice boundaries chosen so each
slice exits to a reviewable browser state. The decomposer may split further into `e07+`
(note: the cut already yields e06…e11, so "further" exceeds eleven waves only if a single
chunk is too large for one overnight).

| Slice | Working title | layer | Exit criterion (browser, `dev`) |
| --- | --- | --- | --- |
| **e06** | Design system + app shell | web | Design-system tokens/components shipped; app shell nav + auth gate + workspace switch render against `config.json` (already real); routing/route-fallback + empty/loading states defined. **Lead-off chore: regen the TS client once against the full E02–E05 OpenAPI surface.** No capability screens yet. |
| **e07** | R0 UI — workspace, roles, upload, status, audit | web | Invite/roles (admin vs procurement), upload → document status, and audit read-back reachable in browser. |
| **e08** | R1 UI — portfolio, filters, Contract 360, evidence, correct, Ask Raffa | web | Portfolio + filters, Contract 360 with evidence/confidence (not JSON dump), review/correction, and Ask Raffa with citations/abstain. |
| **e09** | R2 UI — renewal pipeline, insight card, action | web | Renewal pipeline (threshold strip, priority table) + insight card + action reachable. |
| **e10** | R3 UI — savings KPIs + list | web | Savings KPIs (6) + opportunities table reachable. |
| **e11** | R4 UI — quote check + negotiation + Day-1 integration | web | Quote extract→assessment→target→negotiation→outcome reachable; **last story = `us-NN-final-integration`: one browser Day-1 walk on `demo`** (§20), not `dotnet test`. |

### Notes on the cut

- **e06 is the visual-system shell and is a hard prerequisite** for every later slice
  (nav, auth gate, workspace switch, routing, design tokens/components, plus the client
  regen). It is the `web/` analogue of E01's platform S0 wedge — small, but everything
  else depends on it.
- **e07–e11 follow the R0→R4 user ladder**, one slice per release row, matching §16 order
  and the brief §6 "capability UIs aligned to R0→R4".
- **e11 is explicitly the integration slice** and carries the Day-1 browser walk on `demo`.
  It is `integration: true` in the manifest (mirrors the `final-integration` closure pattern
  in `e01`/`e05`).
- Slice boundaries are **capability-aligned**, not token-aligned. The decomposer re-sizes
  (splits or merges) based on story/task counts; this lane commits to the **order and the
  exit criteria**, not to a token ceiling.

## Ordering and dependency rationale

- **e06 (shell) → everything.** No capability screen ships without the auth-gated shell and
  the design system.
- **e07 (R0 UI) before e08 (R1 UI).** Upload/roles/document-status must work before portfolio
  and Contract 360 can be demonstrated meaningfully.
- **e08 (R1) before e09/e10 (R2/R3).** Renewal/savings screens read structured data produced by
  R1's extraction/correction loop (same dependency as the backend waves R1→R2→R3).
- **e10 (R3) before e11 (R4).** Quote-check reuses the savings/benchmark range UI; Home savings
  outcome is the landing target for the quote-check result.
- **e11 last** and carries the single `demo` browser Day-1 walk — it depends on every prior slice.

## Client-regen chore (repeating, not a slice boundary)

The API client already ships from E01/F07. Because E02–E05 grew the OpenAPI surface, the
**TypeScript client regen is a repeating chore**, not a standalone wave. The brief §6
mandates it. Delivery note: the decomposer should attach it as the **lead-off task in e06**
(regen once, against the full E02–E05 contract) so every later slice consumes the complete
client rather than re-triggering regen mid-stream. Any thin named API gap
(software-architect's table) must be folded into the slice that needs it, not into e06.

## Environment / promotion (unchanged)

Web slices land on `dev` on merge, and promote to `demo` via the existing ADR-016 tag +
gated `demo` environment. **No new environment, no new promotion flow.** `demo` is only
"done" when the e11 Day-1 walk passes in the browser against the Claude Design prototype.

## Assumptions (recorded for `reports/open-questions.md`)

- **OQ-DM-W01** — `inputs/design/prototypes/` (Claude Design export) remains populated and
  the Anthropic account has Claude Design enabled before e06 launches; otherwise the
  ux-ui-designer seat OBJECTs and the cut is blocked. (This is a HITL gate, not a lane decision.)
- **OQ-DM-W02** — The six-slice cut (e06…e11) is the default; the decomposer may split/merge
  based on story counts, but must preserve e06-first and e11-last ordering.
- **OQ-DM-W03** — No absolute start date exists; like the V1 calendar, waves are sequential
  from kickoff, not named calendar weeks.

## Forbidden edits (repeated for the record)

Do not modify `slices/e01.yaml`–`e05.yaml`, `wave-spec.execution.yaml`, or
`slice.current.yaml`. New web work is append-only in `slices/e06.yaml`+ and the new web
manifest/index (`MANIFEST-web.yaml` / `INDEX-web.md`) and DAG (`wave-spec.web.yaml`). This
lane writes only this draft under `reports/architecture/draft/delivery-manager-web/`.
