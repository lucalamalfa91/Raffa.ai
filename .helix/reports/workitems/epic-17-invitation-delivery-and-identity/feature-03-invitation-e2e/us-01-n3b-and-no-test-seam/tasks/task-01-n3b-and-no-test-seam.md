---
id: E17/F03/US01/T01
type: task
story: us-01-n3b-and-no-test-seam
wave: w15
status: live
target_repo: raffa-backend
---

# task-01-n3b-and-no-test-seam — Rewrite the skip reason; add S-T23

## Coding objective

Two small, deliberate deliverables. First, rewrite the skip reason in
`web/e2e/invite.spec.ts` so it names the **one-time passcode**: its current
`SECOND_ACCOUNT_READY` guard reads *"requires a second Entra account on the pilot
tenant — set `RAFFA_E2E_SECOND_ENTRA_EMAIL` / `RAFFA_E2E_SECOND_ENTRA_PASSWORD`
(an operator prerequisite recorded in `reports/audit/w14-hitl.md`…)"*, and NW-67
removes that premise entirely — the product now creates the second account
itself. What still cannot be automated is reading the invitee's emailed one-time
passcode, and the file must say so.

Second, add **S-T23**: a repo-scanning negative test asserting that no
configuration key, environment variable or code path skips, mints or reads an
authentication credential for test purposes. That test is the whole reason this
item can close on a runbook step instead of on a seam, and it cannot be
reconstructed later from the code.

## Parent story AC covered

- AC-1 The skip reason names the one-time passcode.
- AC-2 The four tests are otherwise unchanged and none is un-skipped.
- AC-3 S-T23 exists and runs in CI.
- AC-4 S-T23 fails if such a path is introduced, naming ADR-025 §J.8.
- AC-5 No product code, workflow, Terraform resource or configuration key is added.

## Files to create or modify

| Path | Change |
|------|--------|
| `web/e2e/invite.spec.ts` | the `SECOND_ACCOUNT_REASON` constant (`:61-63`) and the `test.skip(() => !SECOND_ACCOUNT_READY, …)` guard (`:138-139`) name the **passcode**; the four tests (`:164`, `:187`, `:218`, `:235`) are otherwise untouched and none is un-skipped |
| `backend/tests/Raffa.ArchitectureTests/AuthenticationSeamAbsenceTests.cs` | **new** — S-T23, scanning `backend/src/**`, `web/src/**`, `infra/**` and `.github/workflows/**` for a configuration key, environment variable or code path that skips, mints or reads an authentication credential for test purposes; the failure message names **ADR-025 §J.8** |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-58r`.

Decision row: `reports/architecture/waves/w15.md` — **NW-58r**
(security-architect and delivery-manager cells), and **OQ-w15-006** in the
open-questions table.

- **Architecture decisions in force**: **ADR-025 §J.8–§J.9** (the refusal, the
  bounds, S-T23); **ADR-016** w15 clauses 20 and 22; **`none — ADR-014`** — a
  spec file is not CI YAML, so w15's zero-workflow-file assertion is untouched.
- **This task is unstartable before W15-01.** `web/e2e/invite.spec.ts` exists
  **only on `origin/main`**; on the pre-merge tree `web/e2e/` holds `day1.spec.ts`
  and `v2.spec.ts` only. If the file is absent, **stop and say so** — do not
  create a new spec with no history and none of the four tests. The base merge is
  an operator act at the ADR-014 w15 gate.
- **Do not un-skip anything**, and do not add a Playwright runner: a grep of all
  ten workflow files for `playwright|e2e|test:e2e|spec.ts` returns zero hits, and
  `web.yml:79-81` is `npm test` (vitest) only. Wiring one is **NW-50, W18**.
- **S-T23 must be specific enough to fail and general enough to be worth
  having.** It is not a keyword blacklist for its own sake: it exists so that a
  future wave that wants a green e2e badge cannot quietly add the bypass this
  table refused. Make the assertion name the shapes ADR-025 §J.8 names — a key that
  disables authentication, a path that returns a fixed passcode or token, a
  branch keyed on an environment name that skips a credential check — and make
  the message point at the ruling.
- **N3b itself is not delivered here.** *A task that writes only a spec file has
  not delivered the check* (ADR-016 w14 clause 9). N3b becomes a **numbered step
  in `docs/waves/w15-acceptance.md`**, walked by hand against the operator's
  external mailbox — and that document is written by `E16/F04/US01/T01`, which is
  its single writer.
- **Do not touch**: `docs/waves/w15-acceptance.md` (the final-integration task's),
  `.github/workflows/**` (w15's CI-YAML set is zero files), any product code, and
  `reprocess-tenant-documents.yml` — NW-05 takes it out of service and **NW-31
  owns it in W16**, so one wave opens it once.

## Definition of done

- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests` exit 0 — S-T23 passes on the current tree
- [ ] S-T23 is proven non-vacuous: introducing a deliberate stub bypass locally makes it **fail**, and the failure message names ADR-025 §J.8 (revert the stub before committing)
- [ ] `grep -n "SECOND_ACCOUNT" web/e2e/invite.spec.ts` shows the reason now names the passcode, not a missing second account
- [ ] `grep -rn "test.skip" web/e2e/invite.spec.ts` still shows the four tests skipped — none un-skipped
- [ ] `git diff --stat .github/` is **empty**
- [ ] `cd web && npm run build` exit 0 (the spec is type-checked by `tsc --noEmit`)

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| architecture | no configuration key, environment variable or code path mints, reads or skips an authentication credential for test purposes | `backend/tests/Raffa.ArchitectureTests/AuthenticationSeamAbsenceTests.cs` (**S-T23**) |
| manual | N3b end to end — invite, mail, passcode, accept, roster Active | `docs/waves/w15-acceptance.md`, written by `E16/F04/US01/T01` |

## Open questions blocking this task

- **OQ-w15-006** — resolved at the table in both halves. Not blocking.

## Wave-spec entry
```yaml
- id: E17/F03/US01/T01
  prompt: reports/workitems/epic-17-invitation-delivery-and-identity/feature-03-invitation-e2e/us-01-n3b-and-no-test-seam/tasks/task-01-n3b-and-no-test-seam.md
  produces: [invitation-e2e-and-seam-ban]
  depends_on: [invitation-identity-and-mail]
  effort: S
  layer: backend
  status: live
```
