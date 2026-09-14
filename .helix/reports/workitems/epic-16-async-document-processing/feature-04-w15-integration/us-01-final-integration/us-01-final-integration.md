---
id: us-01
type: user-story
parent: feature-04
wave: w15
status: active
---

# us-01-final-integration — The wave builds, tests and has a runbook the operator can walk

## Story

As the **operator**, I want one green task that proves both trees build and test
together and hands me a numbered runbook for `dev`, so that I can decide whether
w15 is done by walking it rather than by reading eleven pull requests.

## Acceptance criteria

- [ ] AC-1 The backend builds and every test project passes.
- [ ] AC-2 The web type-checks, builds and passes vitest; the generated client is regenerated from the contract with no drift.
- [ ] AC-3 The Playwright specs that do not need live Foundry run; `invite.spec.ts` remains skipped for the reason `E17/F03/US01/T01` wrote, and **no runner is added to CI**.
- [ ] AC-4 `docs/waves/w15-acceptance.md` exists and carries, per item, the manual check on `dev` — what to click and what to expect — derived from the acceptance criteria: **A15-1, A15-2, A15-3, A15-4, A15-5, A15-6, A15-7, A15-8** and **N3b**.
- [ ] AC-5 The doc records **W15-A1** in six points, including the SHA actually merged, the **zero product-tree delta** check, and green on the `build + test` job **at the base commit**.
- [ ] AC-6 The doc carries a **known-gaps table** naming at least: `reprocess-tenant-documents.yml` out of service (401 on a read, before any write; NW-31 owns it in W16); and that **A15-4 / A15-5 / A15-7 are `dev` acceptance and are not walkable on `demo` this wave**.
- [ ] AC-7 The doc carries the **post-deploy revision-state assertion** per environment, with zero replicas at rest recorded explicitly as a **PASS**.
- [ ] AC-8 The doc carries the **dead-letter queue** as a standing condition: empty before the acceptance walk, and afterwards read with its two meanings and **routed, never drained**.
- [ ] AC-9 The doc carries the promotion sequence, including the **`demo-v4` cut before w15 starts** and w15 promoting as **`demo-v5`**.
- [ ] AC-10 The READMEs whose public surface this wave changed are swept.

## Definition of done

- [ ] every AC above is verified by at least one check named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `documents-truthful-surfaces` (`E16/F03/US01/T01`) | leaf artifact — the documents web half |
| `invite-pane-and-accept` (`E17/F02/US01/T01`) | leaf artifact — the invitation web half and the published invite contract |
| `invitation-e2e-and-seam-ban` (`E17/F03/US01/T01`) | leaf artifact — the spec's skip reason and S-T23 |

**`w15-terraform` (`E16/F01/US01/T01`) is deliberately not a dependency.** Its
effect is not observable by the wave that wrote it (ADR-016 w14 clause 3), and
delivery-manager ruled at the w15 table that the Terraform task takes **no
dependents** — a `depends_on` pointing at it would be a lie that fails real tasks
under `require_delivery: true`. Its failure is caught by a human reading the HCP
plan at the gate, and this runbook tells that human what to read.

## Architecture decisions in force

- **ADR-016** (w15 clauses 17, 19, 22–24).
- **ADR-014** (w15 clauses 1–7) — W15-A1.
- **ADR-005** (w15 clause 14) — the corrected worker assertion.
- **ADR-027 §C6** — the two meanings of a non-empty dead-letter queue.
- **ADR-001** (w15 addendum clause 12) — which acceptance steps are `dev`-only.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Build, test, sweep, and write the w15 acceptance runbook | L | phase-5 |

## Council decisions carried into this story

**Green means the `build + test` job at the base commit, not the deploy.** The
deploy job is `needs: build` (`backend.yml:83`), so a red test job means the
deploy never runs and the operator sees *no deploy* rather than *red tests* —
which is exactly how w14 closed blind to a `main` that was red on two
Testcontainers fixtures. **A red `main` is no `dev` deploy, and no `dev` deploy
is no wave**; a `127.0.0.1:5432 refused` is a fixture gap, never a flake to
re-run.

**A15-1 and A15-2 are walked on deployed `dev` with the worker scaled the way
`demo` will run it, never in CI**, which has no Service Bus, no worker and no
browser. **"The HCP run is `CURRENT`" proves none of the three Terraform gaps**
that produce the identical symptom — 201 in under 2 s and nothing ever
progressing.

**A gate check that fails when nothing is wrong gets waived, and the waiver is
what the next silent worker death hides behind.** With `min_replicas = 0` a
healthy worker has zero replicas at rest, so the assertion belongs against a
worker **given work**, and the static half must say zero replicas at rest is a
pass.

## Open questions

- **OQ-w15-dm-03** — **the one fork on the register only the operator can close.** `demo` stands at `demo-v3` of 2026-09-04 and there is no `demo-v4`, so w15's promotion would silently carry two waves. **Assumption in force**: `demo-v4` is cut on current `main` at the gate before w15 starts, and w15 promotes as `demo-v5`. Either way `demo` owes three data-plane steps belonging to the **previous** wave, and this runbook must record them so W16 does not inherit them a third time.
- **OQ-w15-ca-02** — if NW-05 slipped inside the wave, **A15-4 must be walked against a real B2B guest** before the wave is called done: a walk with an existing member's account proves nothing about that path.
