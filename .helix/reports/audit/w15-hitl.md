# w15 — HITL review page

**Wave**: `w15` — *upload feels instant, and inviting a colleague works end to end*
**Previous**: `w14` · **Cut**: 2026-09-14 by `next-decomposer`
**Caps**: 20 tasks / 5 phases → **11 live tasks in 5 phases**, 4 queued
**Registration**: `register_wave.py --wave w15` exit **0**;
`check_single_writer.py --slice w15` exit **0**

---

## 1. What to review

| File | Why |
|---|---|
| `reports/plan/slices/w15.yaml` | the wave the fan-out walks |
| `reports/workitems/epic-16-async-document-processing/**` | new epic — async processing, the truthful surfaces, the wave's Terraform, the final integration |
| `reports/workitems/epic-17-invitation-delivery-and-identity/**` | new epic — guest identity, mail transport, the pane, N3b |
| `reports/workitems/epic-18-api-authentication/**` | new epic — the token identity; **F02 and F03 are queued to W16** |
| `reports/workitems/BACKLOG.md` | three epic rows, the w15 ADR-coverage rows, the `## Wave w15` section |
| `reports/workitems/epic-13-ask-v2/feature-04-documents-v2/us-01-documents-v2/**` | **two partial banners** (story + task). `status` stays `active`/`live` deliberately |
| `reports/plan/slices/MANIFEST.yaml`, `reports/plan/slices/INDEX-next.md` | written by `register_wave.py` |
| `reports/architecture/waves/w15.md` | the decisions every task cites. **Its `## Gate verdict` at `:118` is still `pending` and only you can fill it** — the council gate is read-only and said so |

---

## 2. Items in the wave

| Item | Priority | Tasks | Phase(s) |
|---|---|---|---|
| **W15-01** wave base | must | **no task** — your act at HITL; proof recorded as W15-A1 by `E16/F04/US01/T01` | — |
| **NW-27** upload returns once stored | must | `E16/F01/US01/T01`, `E16/F02/US01/T01`, `E16/F02/US02/T01`, `E16/F02/US03/T01`, `E16/F03/US01/T01` | 1, 2, 3, 4 |
| **NW-61** instant upload, gated detail | must | `E16/F02/US03/T01`, `E16/F03/US01/T01` | 3, 4 |
| **NW-10** rail badge reads the server | should | `E16/F03/US01/T01` (co-located — OQ-w15-008 resolved in favour of riding NW-61) | 4 |
| **NW-67** Entra B2B guest at invite time | must | `E16/F01/US01/T01`, `E17/F01/US01/T01`, `E17/F02/US01/T01` | 1, 3, 4 |
| **NW-68** the invitation email (ACS) | must | `E16/F01/US01/T01`, `E17/F01/US01/T01`, `E17/F02/US01/T01` | 1, 3, 4 |
| **NW-69** honest invite pane | should | `E17/F02/US01/T01` | 4 |
| **NW-58r** N3b + the seam ban | should | `E17/F03/US01/T01` | 4 |
| **NW-05** API JWT | must | `E16/F01/US01/T01`, `E18/F01/US01/T01`, `E18/F01/US02/T01` | 1, 2 |
| **NW-06** role from membership | must | `E18/F01/US01/T01` | 1 |
| — final integration + runbook | — | `E16/F04/US01/T01` | 5 |

**Every in-wave item has at least one task citing it**, and no `must` was demoted
behind a `should`. The three release valves product-owner named — NW-10, then
NW-58r's runbook walk, then NW-69 — were **not used**: 11 live tasks against 20.

### The shape of the wave

```
phase 1   w15-terraform (no dependents)   documents-async-schema   api-jwt-identity
phase 2                                   document-queue-transport  web-bearer-token
phase 3                                   documents-async-api       invitation-identity-and-mail
phase 4   documents-truthful-surfaces     invite-pane-and-accept    invitation-e2e-and-seam-ban
phase 5   w15-integration
```

The spine is forced: schema → transport → API → web is four links, and the final
integration is the fifth phase. Everything else is packed around it.

---

## 3. Single writer per phase

`check_single_writer.py --slice w15` exits **0**. It rejected two real collisions
on the first run and both were fixed rather than worked around: a shared
`Raffa.Api.Tests/` directory in phase 3, and a path one task named in a "do not
touch" note that a sibling created in the same phase.

| File that states current state | Phase | Sole writer |
|---|---|---|
| `backend/src/Raffa.Api/Program.cs` | 1 | `E18/F01/US01/T01` |
| | 2 | `E16/F02/US02/T01` |
| | 3 | `E17/F01/US01/T01` |
| every `backend/src/Raffa.Api/*EndpointExtensions.cs` | 1 | `E18/F01/US01/T01` |
| `DocumentsEndpointExtensions.cs`, `ContractsEndpointExtensions.cs`, `PortfolioEndpointExtensions.cs` | 3 | `E16/F02/US03/T01` |
| `WorkspaceInvitesEndpointExtensions.cs` | 3 | `E17/F01/US01/T01` |
| `backend/Raffa.slnx` | 2 | `E16/F02/US02/T01` |
| `backend/tests/Raffa.AiGateway.Tests/SdkAllowListTests.cs` | 2 | `E16/F02/US02/T01` (`Azure.Identity` → `Raffa.Worker`) |
| | 3 | `E17/F01/US01/T01` (`Microsoft.Graph` → `Raffa.Api`) |
| `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs` | 3 | `E17/F01/US01/T01` |
| `backend/src/Raffa.Api/appsettings*.json` | 1 | `E18/F01/US01/T01` |
| `backend/src/Raffa.Worker/appsettings.json` | 2 | `E16/F02/US02/T01` |
| `Raffa.Identity.Workspace/Infrastructure/ServiceCollectionExtensions.cs` | 3 | `E17/F01/US01/T01` |
| `documents-contracts.sql` + the EF migrations | 1 | `E16/F02/US01/T01` |
| `web/openapi/raffa-api.v1.json` + `web/src/api/generated/**` | 3 | `E16/F02/US03/T01` |
| | 4 | `E17/F02/US01/T01` |
| `web/src/api/client.ts` | 2 | `E18/F01/US02/T01` |
| | 3 | `E16/F02/US03/T01` |
| | 4 | `E17/F02/US01/T01` |
| `web/src/components/shell/navItems.ts`, `RailNav.tsx`, `AppShell.tsx` | 4 | `E16/F03/US01/T01` |
| `web/src/routes/documents/**`, `web/src/styles/semantics.ts` | 4 | `E16/F03/US01/T01` |
| `web/src/routes/workspace/members/**`, `web/src/routes/invite/accept/index.tsx` | 4 | `E17/F02/US01/T01` |
| `web/e2e/invite.spec.ts` | 4 | `E17/F03/US01/T01` |
| every file under `infra/` | 1 | `E16/F01/US01/T01` |
| `.github/workflows/**` | — | **nobody. w15's planned CI-YAML set is ZERO files** |
| `docs/waves/w15-acceptance.md` | 5 | `E16/F04/US01/T01` |

**Three client-side constraints the council set for me, all honoured**: NW-05's
client task shares its phase with no other `client.ts` writer (phase 2); at most
one task per phase regenerates the contract and the generated client; and the
router table is not opened at all — no route is added, moved or removed.

### One deliberate irregularity you should see rather than discover

**`E16/F01/US01/T01` (Terraform) has no dependents, including the final
integration task.** Delivery-manager ruled it at the table: an HCP apply is
triggered *by* the merge and is **not observable by the wave that wrote it**
(ADR-016 w14 clause 3), so a `depends_on` pointing at it would be a lie — and
under `require_delivery: true` with `task_failure_markers: ["HALTED:"]` it is a
lie that fails real tasks. **Its failure must be caught by a human reading the
HCP plan at this gate, not by the graph.** That is the trade; §6 below tells you
exactly what to read.

This is the one place where I deliberately did not attach the final-integration
task to every leaf artifact. The other three leaves —
`documents-truthful-surfaces`, `invite-pane-and-accept`,
`invitation-e2e-and-seam-ban` — are all attached.

---

## 4. Queued tasks (decomposed, no entry in `w15.yaml`)

Four `should` items, the **head of W16** in this order. Each has a full task file
with real paths, real DoD commands and its evidence, so the W16 intake picks them
up without re-auditing.

| Task | Item | Note |
|---|---|---|
| `E18/F02/US01/T01` | NW-07 — conversation `user_id` is the token subject | carries a live normalization bug independent of NW-05 |
| `E18/F02/US02/T01` | NW-08 — `GET /api/audit` for a real Admin | also absent from the published contract today |
| `E18/F03/US01/T01` | NW-31 — retire the dual role headers | **owns `reprocess-tenant-documents.yml`** in W16 |
| `E18/F03/US02/T01` | NW-32 — every write names its actor | nine service-layer defaults survive NW-05 untouched |

---

## 5. Superseded items

**No work item is superseded whole, and no `status:` line becomes `superseded`.**
Two **partial** banners were written, on product-owner's explicit instruction
correcting §6 of the intake:

| File | What the banner says |
|---|---|
| `epic-13-ask-v2/feature-04-documents-v2/us-01-documents-v2/us-01-documents-v2.md` | **AC-6 entirely** and **AC-1's 422 / nothing-persisted clause only**; AC-1's 415 clause and AC-2–AC-5 **stand**. `status` stays `active` |
| `.../tasks/task-01-documents-admission.md` | its two required-test rows' "nothing persisted on 422" clause, and its `OQ-askv2-007` line. Everything else stands. `status` stays `live` |

The reason `status` stays `active`/`live`: the story is **mostly still wanted**,
and flipping it to `superseded` would make the next intake drop four ACs the
product still holds. The banner is the fix, not the status change.

**No earlier wave file lists either item as `live`**, so there is nothing for you
to reconcile in a historical slice — unlike w14, which had to flag `e01.yaml`.

What *is* superseded is an oracle assumption, on the record only (`inputs/**` is
never written by this process): `inputs/requirements.md` **A7**,
**`OQ-askv2-007`** and **R-DOC-05 AC-1** are `assumed-wrong` from this wave on.

---

## 6. Operator prerequisites — read before you approve

These are **not tasks**. Four of them are new this wave and the council named
each one because it is a way the wave fails quietly.

1. **The wave base.** Merge `origin/main` into `helix/w15` **before** fan-out.
   Read the SHA from the **loose ref** `.git/refs/remotes/origin/main`, never from
   `packed-refs`, which is stale for **every** branch ref this wave touches. The
   merge is proven by `git diff --stat origin/main..HEAD -- backend web infra
   .github docs scripts` being **empty** — three of the five differing files are
   *shorter* on the process branch, so "the two cited files resolve" proves two of
   five and a careless resolution silently reverts w14's README and e2e work.
2. **Green means the `build + test` job at the base commit, not the deploy.** The
   deploy is `needs: build`, so a red test job reads as *no deploy* rather than
   *red tests* — which is how w14 closed blind to two red Testcontainers fixtures.
3. **The apply identity's directory rights**, verified **before** the first apply
   rather than discovered from a red run (ADR-015 w15 clauses 6–10). Note that
   security-architect **struck one of the three options** the first draft offered:
   *Cloud Application Administrator excludes Microsoft Graph application
   permissions, and `User.Invite.All` is one* — it is the narrowest-sounding entry
   and it **fails at the apply**. The default is now the **out-of-band grant** by a
   Global Administrator, with the resource left at `count = 0` and imported later.
4. **An external mailbox the tenant has never seen.** A15-4 and A15-5 cannot be
   walked without one, and the `demo` walk needs a **second** one.
5. **`integration` is re-created from the wave base, never merged into** — it has
   already diverged on this clone, and `helix/w17-input` exists, so confirm no
   second wave is live against it.
6. **`demo-v4`** (OQ-w15-dm-03 — the one fork only you can close). The highest tag
   is `demo-v3` of **2026-09-04**: w14 was never promoted, so `demo` is a whole
   wave behind and w15's promotion would carry two. The assumption in force is
   that you cut `demo-v4` on current `main` **at this gate**, and w15 promotes as
   **`demo-v5`**. Either way, `demo` owes three data-plane steps belonging to
   **w14** — the w14+w15 schema apply, `seed-demo-fixture.yml`,
   `backfill-workspace-membership.yml`, then `w14-acceptance.md` N1–N9 + W14-A2.
7. **Read the HCP plan for phase 1's PR**, because the graph will not catch it:
   `azuread_application.api` must show `~` and **never** `-/+`, **and**
   `identifier_uris` must show **no diff at all**. The second is the inversion of
   the first — the SPA binds to the URI *string*, so the shape the first check
   calls safe is the one that silently breaks every login.

### The two-merge structure

A wave that changes `infra/` has **two merges to `main`**: the infrastructure-only
PR first, then the wave PR. It is safe by construction — `infra/**` is in neither
`backend.yml`'s nor `web.yml`'s path filter, so that merge deploys no image.
**Confirm PR 1 contains only `infra/**`.**

---

## 7. ADR actions carried into the tasks

- **New**: **ADR-027** — async document processing. The wave's only new ADR.
- **Amended** (bodies untouched, all `Status: accepted`, nothing superseded):
  ADR-001, ADR-002, ADR-005 ×2, ADR-007, ADR-009, ADR-010, ADR-011 ×2,
  ADR-012 ×2, ADR-014, **ADR-015** (its first amendment since 2026-09-01),
  ADR-016 ×2, ADR-018 ×2, ADR-019 ×2, ADR-020 ×2, ADR-022, ADR-024,
  **ADR-025** (a new §J), ADR-026.
- **`none`, with reasons on the record**: ADR-003, ADR-004, ADR-006, ADR-008,
  ADR-013, ADR-017, **ADR-021** (`processing_status` is `varchar(30)` with no
  CHECK, so `Rejected` needs no DDL and neither `backend.yml` array moves),
  ADR-023.

---

## 8. Open questions and assumptions in force

All are already on `reports/open-questions.md` and in `waves/w15.md`; none gates a
task. The ones that touch your decision:

| OQ | Assumption in force | Yours to close? |
|---|---|---|
| OQ-w15-dm-03 | `demo-v4` cut at this gate; w15 promotes as `demo-v5` | **yes** |
| OQ-w15-001 | the base is `origin/main` **as read at the gate**; W15-A1 records the SHA actually merged | **yes** |
| OQ-w15-cl-01 | the KEDA scale rule authenticates with the workload identity; a **topic-scoped** `listen`-only fallback is bounded and pre-approved. `min_replicas = 1` is **not** a fallback | proved by `terraform validate` |
| OQ-w15-dm-01 | `raffa-demo` **does** apply at the infrastructure merge — which is why every `demo` flag defaults `false` | confirm the run state |
| OQ-w15-ca-01 | a client-owned upload deadline of **120 s**; no ingress ceiling is pinned | no |
| OQ-w15-sec-03 | the live-invitation cap is **100** per tenant | you may set the number |
| OQ-w15-D2 | a `Rejected` row with no next step for a Procurement user is acceptable for the pilot | revisit only on a complaint |
| OQ-w15-ca-02 | if NW-05 slips, **A15-4 is walked against a real B2B guest** before the wave is called done | numbered in the runbook |
| OQ-w15-008 | NW-10 **rides** NW-61 — decided by me, as the register says it is the decomposer's call | no |

---

## 9. Launch

```
python scripts/check_slice_prereqs.py --slice w15
./run.ps1 -Max -Slice w15 -o execution-fanout        # or ./run-next.ps1 -Launch -Wave w15
```

`check_slice_prereqs.py` reads the MANIFEST row and requires
`reports/plan/gates/w14.hitl-ok`. Create `reports/plan/gates/w15.hitl-ok` when
you have reviewed this page — and after §6's prerequisites are true, not before.

**Before fan-out, the infrastructure PR (`E16/F01/US01/T01`) must already be
merged and its HCP applies confirmed**, because inside the wave nothing depends on
it and nothing will tell you if it did not land.
