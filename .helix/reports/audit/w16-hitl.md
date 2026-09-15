# Wave w16 — HITL review

**Nothing the product knows lives only in a browser tab.**

| | |
|---|---|
| Wave | `w16` · previous `w15` |
| Source | `inputs/next/w16-todo.md` (sha256 `d3518a6…`) |
| Requirements | `reports/context/waves/w16-requirements.md` |
| Council decisions | `reports/architecture/waves/w16.md` |
| Wave file | `reports/plan/slices/w16.yaml` |
| Baseline | `f0b3436` — `helix/w16` == `origin/main`, `0 0` ahead/behind |
| Caps | 20 tasks / 5 phases → **13 live tasks, 5 phases, 13 stories, 0 queued** |
| Estimate | ~16.0M tokens (`reports/plan/slices/MANIFEST.yaml`) |
| Checks | `register_wave.py --wave w16` **exit 0** · `check_single_writer.py --slice w16` **exit 0** |

---

## 1 · What to review

| File | Why |
|---|---|
| `reports/plan/slices/w16.yaml` | the 13 tasks, their phases and the DAG |
| `reports/workitems/epic-19-server-side-state/**` | the new epic: 8 features, 9 stories, 9 tasks |
| the four **promoted** epic-18 task files (below) | bodies unchanged; each carries a `## Wave w16 addendum` that supersedes stale lines |
| `reports/workitems/BACKLOG.md` | the epic-19 row, the w16 ADR coverage, the `## Wave w16` section |
| `reports/plan/slices/MANIFEST.yaml` + `INDEX-next.md` | written by `register_wave.py` |
| this file | the single-writer table, the deviations, the launch |

The four promoted files (frontmatter `status: queued → live`, everything else
appended — nothing rewritten, per raw §0.4):

- `reports/workitems/epic-18-api-authentication/feature-02-identity-residuals/us-01-conversation-user-is-the-subject/tasks/task-01-conversation-user-is-the-subject.md`
- `reports/workitems/epic-18-api-authentication/feature-02-identity-residuals/us-02-audit-read-for-a-real-admin/tasks/task-01-audit-read-for-a-real-admin.md`
- `reports/workitems/epic-18-api-authentication/feature-03-interim-posture-retirement/us-01-role-headers-retired/tasks/task-01-role-headers-retired.md`
- `reports/workitems/epic-18-api-authentication/feature-03-interim-posture-retirement/us-02-every-write-names-its-actor/tasks/task-01-every-write-names-its-actor.md`

---

## 2 · Items in the wave, with their tasks and phases

| Phase | Task | Item | Effort | What it does |
|---|---|---|---|---|
| **1** | `E18/F03/US02/T01` | NW-32 | L | a required `string` actor on nine service types across five modules; the reserved `system:<component>` principal for the two caller-less sites; the ten `UnattributedActor` declarations go |
| **1** | `E19/F03/US01/T01` | NW-13 | L | the wave's **one** new tenant table `contract_negotiation_step` + its RLS policy in the same migration; `GET`/`PUT /api/contracts/{id}/negotiation-steps` |
| **1** | `E19/F05/US01/T01` | W16-01 | S | delete the dead R0 Worker queue trio and its registration |
| **2** | `E18/F02/US02/T01` | NW-08 | M | `/api/audit` guard moves to membership; `WorkspacePrincipalAuthorization` deleted whole; the path **and** its `info.description` prose land in the contract |
| **2** | `E19/F01/US01/T01` | NW-11 | M | `GET /api/renewals/{id}/action` + `savedAction` on every list row |
| **2** | `E19/F02/US01/T01` | NW-12 | M | `GET /api/quotes`, `GET /api/quotes/{id}` with outcomes embedded; new `QuoteQueryService` |
| **2** | `E19/F04/US01/T01` | NW-21 | M | server-side supplier resolution of the savings link, or an honest unlinked record |
| **3** | `E18/F02/US01/T01` | NW-07 | M | the stale paragraph deleted; the email leg removed from both authorization comparators; normalization fixed at the source; pre-w15 rows recorded as retired |
| **3** | `E19/F06/US01/T01` | NW-11/12/13 | M | the five theme-B contract paths + `getQuote`, `getNegotiationSteps`, `putNegotiationSteps` |
| **4** | `E18/F03/US01/T01` | NW-31 | L | `X-Role` deleted from capabilities **and** the contract; `reprocess-tenant-documents.yml` → `verify-tenant-corpus.yml`; the stale-prose sweep |
| **4** | `E19/F07/US01/T01` | NW-11/13 | L | two stores retired; the reverting tick; Undo as two idempotent writes; the Savings pseudo-row retired |
| **4** | `E19/F02/US02/T01` | NW-12 | S | the quote screen renders the server's outcome; the write-only store deleted |
| **5** | `E19/F08/US01/T01` | — | L | build + test both trees, the three negatives, the sweep, `docs/waves/w16-acceptance.md` |

**Nine items in, nine items decomposed.** NW-10 stays out (CLOSED-ON-MAIN,
`9c4975e`); NW-71 is not ingested (parked on `helix/w17-input`, raw §0.3).

---

## 3 · The phase shape is forced, not chosen — read this before questioning it

ADR-014 w16 clause 5 published a **corrected skeleton** as "a hint for the
decomposer, not a ruling". **That hint does not pass `check_single_writer.py`,
and the failure was reproduced before this plan was cut.** Its P1 puts NW-07,
NW-08 backend and NW-31 capabilities in one phase:

```
SINGLE-WRITER FAILED  w16.yaml
  phase 1: `backend/tests/Raffa.Api.Tests/` claimed by E18/F02/US01/T01, E18/F02/US02/T01
  phase 2: `backend/tests/Raffa.Api.Tests/` claimed by E18/F03/US01/T01, E18/F03/US02/T01
```

**All four promoted epic-18 task bodies claim the directory
`backend/tests/Raffa.Api.Tests/`**, and raw §0.4 forbids rewriting those bodies
(the protect script enforces append-only on them independently). So **no two of
the four may share a phase**. With the final-integration task alone in phase 5,
that pins them to phases 1–4, one each — and every other task is scheduled around
them.

Consequences worth naming:

1. **Three contract writers, not the two ADR-026 recommends.** `contract-A` could
   not be cut as a single task: it must land *after* both NW-08's backend and
   NW-31's capabilities deletion (delivery-manager constraint 2), and those two
   are now in phases 2 and 4, leaving only phase 5. So each promoted task carries
   **its own** contract half — which is what their (un-rewritable) bodies already
   say. The invariant ADR-026 actually protects is **one writer per phase**, and
   that holds: phase 2 `E18/F02/US02/T01`, phase 3 `E19/F06/US01/T01`, phase 4
   `E18/F03/US01/T01`.
2. **`getNegotiationSteps` / `putNegotiationSteps` land one phase before their
   first caller.** ADR-012 clause 24 wants a wrapper in the same task as its
   caller; clause 25 makes `client.ts` one-writer-per-phase and names **"the
   theme-B contract task also owns the wrapper additions"** as the cheapest
   resolution. This wave takes that branch, because the alternative — two phase-4
   web tasks both writing `client.ts` — fails the checker.
3. **NW-13's backend runs in phase 1, ahead of the rest of theme B.** It shares no
   file with NW-32 and touches none of the five services, so the theme-A-before-
   theme-B constraint (which exists *because of those five services*) is not
   engaged. Product-owner's "NW-13 last of theme B" is a **release-valve
   priority**, not a phase ordering.
4. **ADR-028 reserved `Program.cs` for NW-13; NW-13 does not need it.**
   `MapContractsEndpoints()` is already called and the module registers through
   `AddDocumentsContractsModule`, so the new routes and service arrive with no
   host edit. `Program.cs`'s stale comments are swept by NW-08 (`:379-380`) and
   NW-31 (`:323,366,427`), in different phases.
5. **Delivery-manager constraint 5 dissolves.** It required NW-12 and NW-21 to be
   in different phases "while both claim `NegotiationsEndpointExtensions.cs`".
   Under ADR-028 §D2, NW-12 publishes **no** `/api/negotiations/*` route, so it
   never opens that file; NW-21 owns it alone. They share phase 2 safely.

---

## 4 · Single writer per file, per phase

| File | Phase 1 | Phase 2 | Phase 3 | Phase 4 | Phase 5 |
|---|---|---|---|---|---|
| `web/openapi/raffa-api.v1.json` | — | `E18/F02/US02/T01` | `E19/F06/US01/T01` | `E18/F03/US01/T01` | — |
| `web/src/api/generated/schema.ts` | — | `E18/F02/US02/T01` | `E19/F06/US01/T01` | `E18/F03/US01/T01` | — |
| `web/src/api/client.ts` | — | `E18/F02/US02/T01` (no method added) | `E19/F06/US01/T01` (3 methods) | — | — |
| `backend/src/Raffa.Api/Program.cs` | — | `E18/F02/US02/T01` | — | `E18/F03/US01/T01` | — |
| `backend/src/Raffa.Api/ContractsEndpointExtensions.cs` | `E19/F03/US01/T01` | — | — | — | — |
| `backend/src/Raffa.Api/RenewalsEndpointExtensions.cs` | — | `E19/F01/US01/T01` | — | — | — |
| `backend/src/Raffa.Api/QuotesEndpointExtensions.cs` | — | `E19/F02/US01/T01` | — | — | — |
| `backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs` | — | `E19/F04/US01/T01` | — | — | — |
| `backend/src/Raffa.Api/NegotiationOutcomePropagationService.cs` | `E18/F03/US02/T01` | `E19/F04/US01/T01` | — | — | — |
| `backend/src/Raffa.Api/DocumentsEndpointExtensions.cs` | `E18/F03/US02/T01` | — | — | `E18/F03/US01/T01` | — |
| `backend/src/Raffa.Api/ConversationsEndpointExtensions.cs` | — | — | `E18/F02/US01/T01` | — | — |
| `backend/src/Raffa.Api/CapabilitiesEndpointExtensions.cs` | — | — | — | `E18/F03/US01/T01` | — |
| `backend/src/Raffa.Api/AskCopilotService.cs` | `E18/F03/US02/T01` | — | — | — | — |
| `backend/src/Raffa.Documents.Contracts/Migrations/Scripts/documents-contracts.sql` | `E19/F03/US01/T01` | — | — | — | — |
| `backend/src/Raffa.Worker/**` | `E19/F05/US01/T01` | — | — | — | — |
| `web/src/routes/contracts/contract360/index.tsx` | — | — | — | `E19/F07/US01/T01` | — |
| `web/src/routes/quotes/index.tsx` | — | — | — | `E19/F02/US02/T01` | — |
| `web/src/routes/savings/**`, `web/src/routes/renewals/**` | — | — | — | `E19/F07/US01/T01` | — |
| `web/e2e/v2.spec.ts` | — | — | — | `E19/F07/US01/T01` | — |
| `.github/workflows/**` | — | — | — | `E18/F03/US01/T01` **only** | — |
| `infra/**` | **no task, any phase** | | | | |
| `.github/workflows/backend.yml` | **no task, any phase** | | | | |

`web/src/components/shell/navItems.ts`, `web/src/App.tsx` and
`web/src/components/shell/WorkspaceShellApp.tsx` have **no writer this wave**
(ADR-012 w16 clause 25, re-affirmed).

Verified: `python scripts/check_single_writer.py --slice w16` →
`SINGLE-WRITER OK  w16.yaml — 13 task file(s) across 5 phase(s), no same-phase
collision`, **exit 0**.

---

## 5 · Queued tasks

**None.** Everything fits: 13/20 tasks, 5/5 phases. No item was demoted, and
W17's recorded head is unchanged. The three residuals ruled into W17 at the table
(the realized **amount** in the Savings KPI, the **bulk whole-tenant reprocess**,
and `roleGate` chip hiding) are recorded in `BACKLOG.md`'s w16 section and, for
the first two, in `docs/waves/w16-acceptance.md`'s known-gaps table.

## 6 · Superseded items

**None.** No status banner is written and no `status:` line becomes
`superseded` — `w16-requirements.md` §6 records no "cancels / replaces"
statement in the raw file. **No earlier wave file lists any w16 item as `live`,
so there is nothing for you to reconcile in a historical slice.**

Two things that are *not* supersessions but should not be misread as omissions:

- **`epic-08 .../us-01-quote-check` AC-4 is re-pointed, not cancelled**
  (ADR-001 w16 clause 2). NW-12 + NW-21 are its honest server-side
  implementation. The story stays `active`; **no banner, no `superseded:` line**.
- **`OQ-askv2-005` retires** with NW-07 — an oracle assumption, not a work item.

---

## 7 · ADR actions

- **New**: **ADR-028** — server-side state (§D1–§D6 + a w16 round-2 footer).
- **Amended by w16 footers** (bodies untouched, nothing superseded), **fifteen**:
  ADR-001, ADR-002, ADR-003, ADR-009, ADR-010, ADR-011, ADR-012, ADR-014,
  ADR-016, ADR-021, ADR-022, ADR-024, ADR-025, ADR-026, ADR-027.
- **`none`, with reasons on the record**: ADR-004, ADR-005, ADR-006, ADR-007,
  ADR-008 (cloud-architect's conditional seat resolved to *not involved*, cost
  delta **€0.00/month**), ADR-013, ADR-015 (**w16 buys CI no new credential**),
  ADR-017, ADR-018, ADR-019, ADR-020 (ux-ui-designer unseated), ADR-023.

---

## 8 · Open questions and assumptions in force

All ruled at the table; **none gates a task**. Full text in
`reports/open-questions.md` and `reports/architecture/waves/w16.md`.

| OQ | Ruling |
|---|---|
| OQ-w16-001 | pre-w15 conversation rows are **recorded as retired** — never remapped, never deleted; A16-1 reworded; **DoD line `:69` of the promoted NW-07 task must not be implemented** |
| OQ-w16-002 | `/api/audit` adopts the standard shape; `X-Tenant-Id` is **declared in the contract**; the "no `?tenantId=`" property survives as *selector, never an authorization input* |
| OQ-w16-003 | **no** web audit surface; A16-2 closes through the API + the generated type |
| OQ-w16-004 | **premise withdrawn** — neither a token-bearing identity nor a credential-free re-enqueue; the API steps are deleted instead; cloud delta zero **because of that** |
| OQ-w16-005 | the **status** move closes A16-8; the realized amount is the head of W17 |
| OQ-w16-006 | `Raffa.Documents.Contracts` owns the ticks; four canonical **named** steps, per contract |
| OQ-w16-007 | required actor parameter, no default; `system:<component>` for the two caller-less sites; the green test is **rewritten, never deleted** |
| OQ-w16-008 | **operator action, no task** — see §9 (a) |
| OQ-w16-ca-01 | the Savings pseudo-opportunity row is **retired**; its product half → W17 |
| OQ-w16-sa-01 | ADR-028 §D5 clause 2 **ships in w16**, with two fences |
| OQ-w16-dm-01 | the workflow is **renamed**, not kept; delivery-manager's own fallback refused |
| OQ-w16-dm-03 | the baseline's pending infra apply — **operator**, does not gate A16-3 |

### Two things the operator must do that no task will

- **(a) `reports/plan/gates/w15.hitl-ok` does not exist** and
  `scripts/check_slice_prereqs.py:332` requires it. `run.ps1 -Slice w16` fails
  its prerequisites until you stamp it — accurately, since w15 merged as PR #103
  plus #111–#117 and every w15 area was re-verified on this checkout.
- **(b) the council table's `## Gate verdict` line in
  `reports/architecture/waves/w16.md` still reads `pending`.** Every one of the
  nine item rows is filled and all seven seats voted (five APPROVE, two PASS), so
  the council closed; only the gate's own verdict line was never stamped.
  Recorded, not worked around.
- **(c) `assert_next_plan_untouched.py verify` exits 1 on two council-phase
  files.** Reported here rather than worked around, because the checker runs the
  same command and this decomposition did not cause it:

  ```
  verify: the live plan was mutated — abort
    REWRITTEN reports/architecture/ADR-025-workspace-membership-and-invitations.md (append-only: the original text must stay as a prefix)
    REWRITTEN reports/architecture/INDEX.md (append-only: the original text must stay as a prefix)
  ```

  **The decomposer writes no ADR and no INDEX row** — the two files are the
  council seats'. The ADR-025 cause is located: its w16 amendment
  (`## Amendment (2026-09-14, wave w16 — §I's seam swap fires …)`, **line 1031**,
  §K.1–§K.4 ending at `:1096`) was **inserted ahead of the tail of the w15
  amendment** — `### §J.5 — removal, unchanged and now explicit` still begins at
  **`:1098`** and w15's §J content runs to `:1296`. A mid-file insertion breaks
  the prefix check even though the *headings* read in wave order. `INDEX.md` fails
  the same way; its w16 section is correctly last (`:1306`), so the mutation is
  earlier in the file.

  **Nothing in the decomposition depends on this**: every clause those two files
  carry was read from them and is quoted in the task files, and the content
  itself is not in dispute. The fix belongs to whoever re-enters the council lane
  — move the w16 block to the end of each file so the earlier waves' text is once
  again an unbroken prefix. **No w16 task touches either file.**

### W16-A1 — the gate checklist before `reports/plan/gates/w16.hitl-ok`

(ADR-014 w16 clause 4; **(g)** is new this wave.)

```
(a) git fetch origin                     # read the base SHA AT THE GATE, never quote it
                                         # expect origin/main == helix/w16 == f0b3436
(b) git diff --stat origin/main..HEAD -- backend web infra .github docs scripts   # empty
(c) integration re-created from the wave base, never merged into
(d) .github/workflows/backend.yml build+test green AT THE BASE COMMIT — actually run it
(e) python scripts/check_slice_prereqs.py --record-hitl w15
(f) git tag -l "demo-v*"                 # read it and write it down (demo is on demo-v3)
(g) confirm the baseline's infra apply (PR #118) landed on dev, and check the
    running image tag against main — a skipped deploy behind a red test job is silent
```

---

## 9 · Launch

```
python scripts/check_slice_prereqs.py --slice w16
./run.ps1 -Max -Slice w16 -o execution-fanout        # or ./run-next.ps1 -Launch -Wave w16
```

**One** `integration → main` PR this wave (w15 clause 5's two-merge shape does
**not** apply — the `infra/` delta is zero). **No `demo-v*` tag is cut.** The
close record is `reports/execution/wave-close-w16.md`, **never** the generic
`wave-close.md`, written after the PR merges.
