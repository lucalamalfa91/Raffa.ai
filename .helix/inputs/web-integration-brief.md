# Raffa — Web integration intake (delta on a completed R0–R4 backend)

**Audience:** Helix docs-ingester, architecture council (including UX/UI Design), backlog decomposer  
**Status:** Engineering + product mandate for a *follow-on* design pass  
**Date:** 4 September 2026  
**Kind:** **delta intake**. Not a greenfield V1 restart.

This brief is the ground truth for a second Passata 1 whose only job is to
plan the **user-visible web experience** that consumes the already-planned
(and treated-as-done) R0–R4 backend. Mobile stays non-gating (ADR-013).

---

## 0. How this intake differs from the first Raffa intake

The first intake (`product-spec.md` + `engineering-brief.md` +
`engineering-constraints.md`) asked the council to invent the platform and
decompose R0–R4 from zero.

This intake assumes that work **already exists and is finished**:

| Already true (do not re-open) | Where |
|---|---|
| 17 accepted ADRs | `reports/architecture/INDEX.md`, `ADR-001` … `ADR-017` |
| Five backend epics, stories, tasks, slices | `reports/workitems/epic-01` … `epic-05`, `reports/plan/slices/e01.yaml` … `e05.yaml` |
| Web stack + hosting | ADR-012 — React + TypeScript + Vite SPA, OIDC PKCE, Azure Static Web Apps |
| Mobile stack (scaffold only) | ADR-013 — Expo, non-gating, no store |
| API-first contract | Clients consume the ASP.NET Core API; one generated TypeScript client |
| `web/` shell already scaffolded | E01/F07 — OIDC shell + generated API client + `config.json` |
| Day-1 promise | product-spec §20, delivered on `demo` **through the web client** |

The council and decomposer **cite** those artefacts. They do **not** replace
ADR-001…017, do **not** rewrite epic-01…05 backend tasks, and do **not**
re-pick SKUs, region, git flow, or .NET shape.

**Forbidden on this run**

- `./run.ps1 --fresh` (wipes `reports/`, including ADRs and the E01–E05 tree).
- Re-litigating “Council decides” items that already have an accepted ADR.
- Inventing a second web stack or a BFF.
- Planning a mobile store release or mobile feature waves.
- Changing `dev` / `demo` isolation, HCP workspaces, or promotion (`demo-v*`).

**Required run shape** (operator — see §8): a **non-fresh** design pass that
ingests *this* file, adds a UX/UI Design seat, and **appends** new workitems
(epic-06 onward and/or `layer: web` stories). Existing slice files `e01`–`e05`
stay as the backend waves.

---

## 1. Problem this pass exists to close

ADR-012 and the backlog already say the SPA must deliver the full user-visible
ladder as slices land (`BACKLOG.md`: ADR-012 → “epic-01 F07, **epic-02..05
web**”). The decomposer interpreted API-first as **API-only**: E02–E05 stories
are `layer: backend` (e.g. `us-01-renewal-dashboard-api`). Integration stories
mention `raffa-web (smoke)` but never author screens.

So a reviewer on `demo` after E05 can have a working API and still have no
portfolio, Contract 360, Ask Raffa, renewals, savings, or quote-check **UI**.
That fails product-spec §16 definitions of success and §20 Day-1 (those are
user outcomes, not HTTP status codes).

This pass plans the missing web integration **as if E01–E05 backend is already
on `main`**, up to the same Day-1 end state.

---

## 2. Locked for this pass (in addition to the original Locked table)

Reproduce the original Locked table from `engineering-brief.md` §1. Then add:

| Decision | Guideline |
|---|---|
| Planning baseline | Treat `reports/workitems/epic-01` … `epic-05` and ADR-001…017 as **done**. New work is additive. |
| Delivery topology | New execution slices are **web waves starting at wave / epic 6** (`e06`, `e07`, …). Do not splice UI tasks into `slices/e01.yaml`–`e05.yaml`. |
| Surface | **Web only** for this pass. `web/` in the monorepo. Mobile remains ADR-013 scaffold. |
| Stack | ADR-012 is locked. No new frontend framework ADR unless a *defect* in ADR-012 blocks the UX (then OBJECT with the file). |
| API | Web consumes existing (and E02–E05) HTTP contracts. Regen the TypeScript client from `web/openapi/`; do not hand-write divergent DTOs. If a screen needs a field the API does not expose, the story may add a **thin, named backend gap task** — that is the only allowed backend write, and it must not redesign modules. |
| Visual design method | **Claude Design is mandatory** for UX/UI (see §5). The council does not invent a pixel system in markdown prose and call it done. |
| Code authoring | Still Claude Code via Helix Passata 2. Claude Design produces the experience; Claude Code implements it in `web/`. |

---

## 3. What the council must decide (this pass only)

Do **not** re-list git flow, SKUs, region, Terraform, .NET, Foundry IDs, or
promotion. Those are closed.

This table **is** council-owned now:

| Topic | Owner seat | Output |
|---|---|---|
| Information architecture (IA) for the Day-1 web path | **ux-ui-designer** (draft), product-owner concurs | ADR — sitemap, primary nav, roles (admin / procurement), empty/error/loading |
| Design system for Raffa web | **ux-ui-designer** | ADR + Claude Design system (tokens, type, colour, components) |
| Screen inventory mapped 1:1 to spec §16 R0–R4 + §20 | **ux-ui-designer** + product-owner | ADR or annex — every success criterion has a screen (or a named non-goal) |
| Evidence / confidence / citation UX patterns | **ux-ui-designer** + client-architect | How Contract 360, Ask Raffa citations, and review/correct render without looking like a raw JSON dump |
| Web story cut (epic-06+) | product-owner + delivery-manager | Wave calendar: e06 shell/design-system, then capability UIs aligned to R0→R4 user ladder |
| Client-architect residual | client-architect | Routing, MSAL/config, OpenAPI regen, SWA — **not** visual language |
| Thin API gaps | software-architect | Only if a locked screen cannot be built from documented endpoints |

`client-architect` stays the **stack and API-consumption** seat (ADR-012/013).
They are **not** a substitute for a UX/UI Design expert.

---

## 4. Required council seat — UX/UI Design expert

The first Raffa council had six producers and no designer. That is why
dashboards became `*-dashboard-api`.

**This pass is invalid unless a seventh producer seat exists:**

| Seat | Draft folder | Owns |
|---|---|---|
| **ux-ui-designer** | `reports/architecture/draft/ux-ui-designer/` | IA, design system, screen inventory, interaction patterns, Claude Design artefacts, accessibility baseline (keyboard, contrast, empty states) |

The seat must be a **real UX/UI Design expert** (human-backed avatar or a
dedicated lane prompted as a senior product designer — not a second
client-architect). At `council-close` they vote like every other producer.

Wire-up the operator must do *before* launching Passata 1 (see §8): agent file,
lane skill, `architecture-lanes` participant, `council-close` participant,
`max_rounds` raised from 28 (4×7) to **32** (4×8).

---

## 5. Claude Design — mandatory method (not optional flavour)

[Claude Design](https://claude.com/product/design) (Anthropic, `claude.ai/design`)
is the **authoring environment for the experience**. Helix council chat is
where decisions are recorded as ADRs. Those are different jobs.

### 5.1 What Claude Design must produce

Before decomposition is allowed to finish, the UX/UI seat (with HITL) must
leave on disk a handoff the decomposer and later implementers can open:

```
inputs/design/                    # or web/design/handoff/ if already in-repo
  README.md                       # how to open / refresh the Design project
  design-system.md                # tokens, type, colour, components (text dump)
  ia.md                           # sitemap + user flows (Day-1 path)
  screens.md                      # inventory ↔ spec §16 / §20
  prototypes/                     # exported HTML / interactive prototype
    day1-demo.html                # clickable Day-1 path on demo
    r0-workspace.html
    r1-contract-360.html
    r1-ask-raffa.html
    r2-renewals.html
    r3-savings.html
    r4-quote-check.html
```

Minimum prototype coverage (one clickable path, not every edge):

1. Sign-in (Entra / MSAL) → workspace
2. Invite / role (admin vs procurement)
3. Upload contract → document status
4. Portfolio list + filters
5. Contract 360 (clauses, evidence, confidence)
6. Review / correction
7. Ask Raffa + citations / abstain
8. Renewal pipeline + insight card + action
9. Savings KPIs + list
10. Quote extract → assessment → target → negotiation

Empty, error, and loading states for the Day-1 path are in scope. Marketing
landing pages are out of scope.

### 5.2 Who runs Claude Design (Helix cannot)

**Today Helix cannot open Claude Design by itself.**

| Actor | Provider | Can call Claude Design? |
|---|---|---|
| docs-ingester, six original council seats, council-gate, decomposer | DeepSeek chat | **No.** No browser, no MCP, no `claude.ai/design`. |
| Passata 2 implementer / reviewer | Claude Code | **Yes, if enabled** — `/design`, `/design-sync` (see §5.3). |
| Human operator / UX HITL | claude.ai/design | **Yes — this is the primary author.** |

So the visual work is a **HITL gate inside Passata 1**, not a DeepSeek tool
call. The UX/UI seat’s lane instructions must say:

1. Do not “design” the product as a bullet list of colours in an ADR and stop.
2. Require the operator to run Claude Design (or confirm an existing project).
3. Ingest the export under `inputs/design/` (or `web/design/handoff/`).
4. Write ADRs that **point at those files** (paths, not screenshots-only).
5. `VOTE: OBJECT` if the handoff is missing when the table tries to close.

Product-owner and council-gate must OBJECT if `inputs/design/prototypes/` is
empty at close.

### 5.3 Claude Code `/design` — what you must change to use it

Claude Design ↔ Claude Code is a **subscription + toolchain** feature, not
something already mounted in this artifact (`config.md`: no MCP; council is
DeepSeek; implementer is Claude Code without a Design skill).

To actually use it:

1. **Plan:** Claude Pro, Max, Team, or Enterprise with **Claude Design enabled**
   (beta; default-off on some Enterprise). Work at
   [claude.ai/design](https://claude.ai/design) or Claude Desktop.
2. **Claude Code login** on the implementer path (already how Passata 2 runs).
   In a Claude Code session on `web/`:
   - `/design-sync` — pull the design system from the repo / Design project
     so implementation uses the same tokens/components.
   - `/design` — iterate a screen without leaving the coding agent.
3. **Helix patches** (operator, before Passata 2 web slices):
   - Add a standing skill (e.g. `skills/claude-design.md`) mounted on
     **implementer** (and reviewer as read-check): *web `layer: web` tasks
     must consume `inputs/design/` / `web/design/handoff/`; must not invent a
     parallel visual language; prefer `/design-sync` when the CLI offers it.*
   - Task template: for `layer: web`, require a row
     `inputs/design/…` or `web/design/handoff/…` under “Files to create or
     modify” / “depends on design handoff”.
   - Do **not** expect DeepSeek council turns to run `/design`.

If Design is not enabled on the Anthropic account that `claude login` uses,
Passata 2 will only see markdown handoffs — still usable, but you lose the
round-trip. Enable Design **before** launching e06, or the UX seat should
OBJECT.

---

## 6. Decomposition rules (append-only)

The decomposer reads INDEX **plus** the new UX ADRs **plus** this brief.

| Rule | Detail |
|---|---|
| Do not rewrite | `reports/workitems/epic-01` … `epic-05` stay. No renumber. |
| New epic(s) | Start at **epic-06** (e.g. `epic-06-web-experience`). Further epics 07+ if the tree needs more than one overnight slice. |
| Layer | New tasks are `layer: web` unless a thin API gap (§2). |
| `target_repo` | `raffa-web` / folder `web/`. |
| Slices | New files `reports/plan/slices/e06.yaml`, `e07.yaml`, … and MANIFEST entries. Do not edit `e01`–`e05`. |
| Mapping | Every spec §16 row and §20 Day-1 step has at least one web story. “API exists” is not enough. |
| Client regen | A repeating chore: regenerate TS client when backend OpenAPI grew in E02–E05. |
| Integration | Last story of the last web wave is `us-NN-final-integration`: one task, browser Day-1 on `demo`, not only `dotnet test`. |
| Claude Design | Every screen story cites the prototype file it implements. |

Suggested epic-06 cut (council may split):

1. Design system + app shell (nav, auth gate, workspace switch, config.json already real).
2. R0 UI — invite/roles, upload, document status, audit read-back.
3. R1 UI — portfolio, filters, Contract 360, evidence, correct, Ask Raffa.
4. R2 UI — renewal pipeline, insight, action.
5. R3 UI — savings KPIs + list.
6. R4 UI — quote check + negotiation + Day-1 integration smoke.

---

## 7. Success looks like

A procurement user on Azure **`demo`**, after the last new web slice is
promoted (`demo-v*`, existing ADR-016), can complete product-spec §20 **in the
browser**, with a UI that matches the Claude Design prototype (not a
localhost `config.json` shell and not a raw Swagger page).

Mobile is unchanged: scaffold builds, non-blocking CI, no store.

---

## 8. How to run this intake

A **separate** Helix artifact owns this pass. Do **not** patch or relaunch
`raffa-process.yaml` (that file drives the live R0–R4 fan-out).

| Artifact | Launcher | Target |
|---|---|---|
| `raffa-web-process.yaml` | `./run-web.ps1` | `raffa-web-design` (default) |

See `WEB-PROCESS.md`. `--fresh` is refused. Decomposition starts at **epic-06 /
e06**. Claude Design HITL: fill `inputs/design/prototypes/` before the web
council can close. Passata 2 stays `./run.ps1 -Max -Slice e06 -o execution-fanout`
on the original artifact, only when that wave is idle.

---

## 9. Documents this brief does not replace

| Keep using | Role |
|---|---|
| `inputs/product-spec.md` | WHAT — jobs, §16, §20, non-goals |
| `inputs/engineering-brief.md` | HOW — original locked platform |
| `inputs/engineering-constraints.md` | hard constraints |
| `reports/architecture/ADR-*.md` | accepted decisions |
| `reports/workitems/epic-0[1-5]/**` | backend tree, as-done |

End of intake.
