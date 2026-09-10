# Raffa Web Integration Mandate (delta on a completed R0–R4 backend)

Source of truth: `inputs/web-integration-brief.md` (delta intake, 4 September 2026),
`inputs/product-spec.md` (v1.0), `inputs/engineering-brief.md` (v1.2),
`inputs/engineering-constraints.md`, `reports/architecture/INDEX.md`
(17 accepted ADRs), `reports/workitems/BACKLOG.md`, `reports/plan/slices/MANIFEST.yaml`.
Section numbers cite `product-spec.md` unless stated.

This file fixes the frame for every later Passata-1 seat on this pass. It is a
**delta**, not a V1 restart. Read it before drafting, decomposing, or closing.

---

## 1. This is a delta. The backend is done; do not re-open it.

| Already true (do not re-open) | Where |
|---|---|
| 17 accepted ADRs | `reports/architecture/INDEX.md`, `ADR-001` … `ADR-017` |
| Five backend epics, stories, tasks, slices | `reports/workitems/epic-01` … `epic-05`, `reports/plan/slices/e01.yaml` … `e05.yaml` |
| Backlog carried | `reports/workitems/BACKLOG.md` — R0–R4 fully decomposed, E01–E05 `layer: backend` |
| Web stack + hosting | ADR-012 — React + TypeScript + Vite SPA, OIDC PKCE, Azure Static Web Apps |
| `web/` shell scaffolded | E01/F07 — OIDC shell + generated API client + `config.json` |
| Mobile stack (scaffold only) | ADR-013 — Expo, non-gating, no store release for R0–R4 |
| API-first contract | One generated TypeScript client from `web/openapi/`; web consumes documented HTTP contracts |
| Source control / env model | Locked in `engineering-constraints.md` + ADR-014/015/016 — one public repo `lucalamalfa91/raffa`, `dev` + `demo`, promotion via `demo-v*` |

**Append-only rules (forbidden on this run):**

- Do **not** run a fresh/wiping pass over `reports/` (webs the ADRs and the E01–E05 tree).
- Do **not** rewrite `reports/workitems/epic-01` … `epic-05`, `reports/plan/wave-spec.execution.yaml`,
  `reports/plan/slices/e01.yaml` … `e05.yaml`, or `reports/plan/slice.current.yaml`.
- Do **not** replace ADR-001…017, re-pick SKUs / region / git flow / .NET shape / benchmark provider.
- Do **not** invent a second web stack or a BFF; no hand-written divergent DTOs.
- Do **not** plan a mobile store release or mobile feature waves.
- Do **not** change `dev`/`demo` isolation, HCP workspaces, or `demo-v*` promotion.

---

## 2. What this pass exists to close

ADR-012 and `BACKLOG.md` already promise the SPA will deliver the full
user-visible ladder as backend slices land. The E02–E05 decompositions are
`layer: backend` (e.g. `us-01-renewal-dashboard-api`); integration stories
mention `raffa-web (smoke)` but never author screens. So after E05 a reviewer
on `demo` could have a working API and no portfolio, Contract 360, Ask Raffa,
renewals, savings, or quote-check **UI**. That fails spec §16 definitions of
success and §20 Day-1, which are user outcomes, not HTTP status codes.

This pass plans the missing web integration **as if E01–E05 backend is already
on `main`**, up to the same Day-1 end state, delivered through the browser.

---

## 3. New work starts at wave / epic 6, `layer: web` only

| Decision | Guideline |
|---|---|
| Planning baseline | Treat epic-01…05 and ADR-001…017 as **done**. New work is additive. |
| Surface | **Web only.** New client work targets `web/` in the monorepo (`raffa-web`). |
| Delivery topology | New execution slices are **web waves starting at wave / epic 6** (`e06.yaml`, `e07.yaml`, …). Do **not** splice UI tasks into `e01.yaml`–`e05.yaml`. |
| Stack | ADR-012 is locked. No new frontend-framework ADR unless a **defect** in ADR-012 blocks the UX (then the drafting seat OBJECTs citing the file). |
| API | Web consumes the existing (and E02–E05) HTTP contracts. Regen the TS client from `web/openapi/`; never hand-write divergent DTOs. If a screen needs a field the API does not expose, the story may add a **thin, named backend gap task** — the only allowed backend write, and it must not redesign modules. |
| Env | New web work lands and deploys to `dev`, then promotes to `demo` via existing ADR-016 (`demo-v*`). No new env. |
| Done-when | Product Day-1 path (§20) works **in the browser** on Azure `demo` against the Claude Design prototype — not a localhost `config.json` shell, not a raw Swagger page. |

---

## 4. Claude Design handoff lives under `inputs/design/` (HITL; already populated)

Visual design is authored in **Claude Design** (claude.ai/design), exported into
this repo, and read by later seats. The export is a **HITL gate** in Passata 1:
council chat records decisions as ADRs; it does not invent a pixel system in
markdown prose and call it done.

`inputs/design/` is **already populated** (this is a delta intake; the operator
ran the Claude Design export before launch):

```
inputs/design/
  README.md              # opens the Claude Design project; lists export layout
  _claude-design-brief.md
  prototypes/
    day1-demo.html       # clickable Day-1 path           (may grow)
    design-system.md     # tokens, type, colour, components (text dump)
    ia.md                # sitemap + user flows
    screens.md           # inventory ↔ spec §16 / §20
```

Per `inputs/design/README.md`, the export layout keeps `design-system.md`,
`ia.md`, `screens.md`, and `day1-demo.html` **inside `prototypes/`** — this is
the brief's `inputs/design/` example re-arranged for in-repo clarity. Later
seats must read the actual files present, not assume a fixed layout.

Rules:

- The **ux-ui-designer** seat (draft lane `reports/architecture/draft/ux-ui-designer/`)
  does not yet exist in `reports/architecture/draft/` — later producers create it.
  It owns IA, design system, screen inventory, interaction patterns, accessibility
  baseline — and must land ADRs that **point at files under `inputs/design/`**.
- Product-owner and council-gate OBJECT at close if `inputs/design/prototypes/`
  is empty of a working Day-1 path.
- Every screen story in the decomposer output must **cite the prototype file it
  implements** (`inputs/design/prototypes/…`).
- Claude Code implementers consume the export via `/design-sync` where available;
  the markdown dumps stay usable when the round-trip is unavailable. Enable Design
  on the Anthropic account before launching e06, or the UX seat should OBJECT.

---

## 5. Mobile stays non-gating (ADR-013)

Mobile remains ADR-013: React Native (Expo) scaffold, non-gating CI, no store
release for R0–R4. No mobile feature waves are planned in this pass. The
`mobile/` folder stays in the monorepo untouched by web-wave tasks. Web-first
carries the whole user-visible Day-1 ladder.

---

## 6. The user-visible ladder the web must finish — spec §16 and §20

The web pass exists to make §16 and §20 **browser-visible**. They are quoted as
ground truth so decomposition cannot "finish" behind an API-only bar.

### §16 — Delivery plan and implementation backlog (wave ladder + definition of success)

> | Release | Scope | Definition of success |
> | --- | --- | --- |
> | R0 — Foundation | Auth, workspace, multi-tenancy, roles, upload, storage, DB, audit baseline | A secure workspace can ingest documents |
> | R1 — Contract Intelligence | Extraction, schema, portfolio, Contract 360, Q&A, citations, validation | Customer can upload contracts and ask reliable questions |
> | R2 — Renewals | Dates, cancellation deadline, alerts, dashboard, priority, recommendations | Procurement does not miss material renewal windows |
> | R3 — Savings | Benchmark service/adapters, price comparison, savings dashboard/workflow | Raffa quantifies credible savings opportunities |
> | R4 — Quote Check | Quote extraction, benchmark, assessment, target, negotiation strategy | A new proposal can be assessed in minutes |

### §20 — Definition of V1 done (the Day-1 screen ladder)

> **Day 1:** Create a workspace and invite Procurement users. / Upload a
> portfolio of contracts. / Automatically classify, extract and structure
> supported documents.
>
> **After processing:** Ask reliable questions across the contract portfolio
> with source evidence. / See renewal and cancellation deadlines. / See relevant
> contract/commercial risks. / See market benchmarks where data is available. /
> See prioritized savings opportunities.
>
> **During a new purchase:** Upload a supplier quote. / Receive a line-level
> market assessment. / Receive a recommended target range and potential
> savings. / Receive an explainable negotiation strategy.
>
> **After negotiation:** Record the final negotiated outcome. / Track realized
> savings. / Use the outcome as permissioned proprietary learning data.
>
> **V1 customer promise** — Raffa knows what we bought, what we pay, when we
> need to act, and where we can save money.

**Mandate:** every §16 row has a screen (or a named non-goal against spec §1.2),
and every §20 Day-1 step is reachable **in the browser**. "The API has an
endpoint" is not a delivered screen.

---

## 7. Append-only decomposition target (for later seats, not this pass)

This file does **not** decompose. It fixes the boundaries later producers act
within:

| Artefact | Writer | Notes |
|---|---|---|
| `reports/architecture/draft/ux-ui-designer/` | ux-ui-designer seat (new) | IA, design system, interaction, accessibility; cites `inputs/design/` |
| `reports/architecture/draft/*-web/` | other producers' web-lane drafts | client-architect residual: routing, MSAL/config, OpenAPI regen, SWA — not visual language |
| `reports/architecture/ADR-018-*.md` onward | council close | accept/lock the web IA + design-system ADRs |
| `reports/workitems/epic-06-*/` onward | decomposer | web experience epics; new stories `layer: web`, `target_repo: raffa-web` |
| `reports/plan/wave-spec.web.yaml` | decomposer | new web DAG (do not touch `wave-spec.execution.yaml`) |
| `reports/plan/slices/e06.yaml`, `e07.yaml`, … | decomposer | web overnight waves; `e01`–`e05` untouched |
| `reports/plan/slices/INDEX-web.md`, `MANIFEST-web.yaml` | decomposer | new web slice index/manifest |

Approved epic-06 cut (visual-system shell → the capability UIs in R0→R4 order),
which the decomposer may split into 07+:

1. Design system + app shell (nav, auth gate, workspace switch; `config.json` already real).
2. R0 UI — invite/roles, upload, document status, audit read-back.
3. R1 UI — portfolio, filters, Contract 360, evidence, correct, Ask Raffa + citations.
4. R2 UI — renewal pipeline, insight card, action.
5. R3 UI — savings KPIs + list.
6. R4 UI — quote check + negotiation + Day-1 integration smoke.

Decomposition rules (append-only): no rewrite of epic-01…05; no renumber; new
tasks `layer: web` unless a thin named API gap; new slices `e06.yaml`… +
MANIFEST-web entries; `e01`–`e05` untouched; every §16/§20 user step covered by a
web story; TS client regen is a repeating chore; the last web-wave story is the
integration task that walks Day-1 in a browser on `demo`.

---

## 8. Files this mandate does not replace

| Do not overwrite | Role |
|---|---|
| `reports/context/product-context.md` | existing V1 product frame |
| `reports/context/locked-decisions.md` | verbatim locked table |
| `reports/context/council-open-questions.md` | council-owned open questions |
| `reports/architecture/ADR-001.md` … `ADR-017.md` | accepted decisions |
| `reports/workitems/epic-01-*/` … `epic-05-*/` | backend tree, as-done |
| `reports/plan/wave-spec.execution.yaml` | master backend DAG |
| `reports/plan/slices/e01.yaml` … `e05.yaml`, `slice.current.yaml` | backend waves |
| `inputs/product-spec.md`, `engineering-brief.md`, `engineering-constraints.md`, `web-integration-brief.md` | input ground truth |

Keep reading for WHAT (`product-spec.md`), HOW (locked engineering table in
`engineering-brief.md` §1 + this addendum), and the delta intake
(`inputs/web-integration-brief.md`). Council-owned topics for this pass only are
the IA/design-system/screen-inventory / evidence-citation-UX / web-story-cut /
client-architect-residual / thin-gap table in `web-integration-brief.md` §3.

---

## 9. Open state carried forward

- **New seat required:** `ux-ui-designer` — a real producer lane (not a second
  client-architect) whose draft folder does not yet exist; later seats create it
  and record ADRs citing `inputs/design/`.
- **Claude Design HITL:** export already present under `inputs/design/`
  (`prototypes/` populated 2026-09-04); keep it a live gate, not a one-shot.
- **Deliverable that would close this pass's problem:** a procurement user on
  Azure `demo`, after the last web slice is promoted (`demo-v*`, ADR-016),
  completes §20 **in the browser** against the prototype UI.
