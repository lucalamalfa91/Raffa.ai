---
id: us-01
type: user-story
parent: feature-03
wave: w15
status: active
---

# us-01-n3b-and-no-test-seam — The spec tells the truth, and no test seam can be added quietly

## Story

As a **reviewer of the next wave**, I want the invitation spec to name the real
reason it cannot run and a test in CI that fails if anyone adds an authentication
bypass "just for tests", so that the refusal made at the w15 table survives
contact with a future wave that wants a green e2e badge.

## Acceptance criteria

- [ ] AC-1 `web/e2e/invite.spec.ts`'s skip reason names the **one-time passcode** as the blocker. Its current `SECOND_ACCOUNT_READY` premise — "requires a second Entra account on the pilot tenant" — is **removed by NW-67**, so leaving it would make the file assert something false.
- [ ] AC-2 The spec's four tests are otherwise unchanged, and no test is un-skipped: there is no Playwright runner in CI to execute them.
- [ ] AC-3 **S-T23** exists and runs in CI: a negative test asserting that no configuration key, environment variable or code path skips, mints or reads an authentication credential for test purposes.
- [ ] AC-4 S-T23 fails if such a path is introduced, and its failure message names ADR-025 §J.8 so the next author reads the ruling rather than deleting the test.
- [ ] AC-5 No product code, workflow, Terraform resource or configuration key is added by this story. w15's CI-YAML set stays **zero files**.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-invite-provisions-and-mails (`E17/F01/US01/T01`) | the new skip reason is only *true* once the flow creates the second account itself |
| **W15-01 (operator act, not a wave task)** | `web/e2e/invite.spec.ts` exists **only on `origin/main`**. On this checkout `web/e2e/` holds `day1.spec.ts` and `v2.spec.ts` only, so before the base merge this task would create a new file with no history and none of the four tests |

## Architecture decisions in force

- **ADR-025 §J.8–§J.9** — the refusal, its bounds, and S-T23 as the thing that makes it durable.
- **ADR-016** (w15 clauses 20, 22) — *a task that writes only a spec file has not delivered the check*; browser-expressed assertions land in the acceptance doc, which the final-integration task owns.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Rewrite the skip reason; add S-T23 | S | phase-4 |

## Council decisions carried into this story

**Three refusals, three separate reasons, and only one of them is security's.**
(1) A Playwright runner is NW-50's job and NW-50 is W18 — a grep of all ten
workflow files for `playwright|e2e|test:e2e|spec.ts` returns **zero** hits, and
*a wave does not acquire a new CI capability as a side effect of a test it
wanted*. (2) A test-only passcode seam is an authentication bypass present in the
shipped image **everywhere**, on a directory `dev` and `demo` share, in the wave
that finally closes impersonation — and it would ship with **no consumer**,
because there is no runner to call it. (3) A `dev` mail-catcher is a **fourth
apply**: a new resource and module that can error the single HCP run already
carrying Service Bus, ACS and Graph, for a test nothing can execute.

**The acceptable future shape is recorded so W18 does not invent another**: a
`dev` mail-catcher is test infrastructure rather than a product code path, and if
NW-50 builds it — `dev` only, never provisioned in `demo`; only addresses on a
dedicated test alias or domain, never a real person's mailbox; not reachable from
the API and holding no Raffa credential; contents treated as secrets in CI with
no artifact upload of a mailbox dump. And if e2e directory credentials ever
exist: GitHub **environment** secrets scoped to `dev`, never repository-wide and
never a workflow literal; a dedicated test principal whose only workspace grant
is the test tenant; and because Playwright traces, videos and screenshots capture
typed input, the sign-in step runs with tracing off or the field masked and no
trace artifact from a signed-in run is uploaded — **a leaked trace is a leaked
directory password**.

## Open questions

- **OQ-w15-006** — resolved in both halves: no passcode seam, no runner, no mail-catcher this wave; the skip reason names the passcode and **N3b is walked by hand** as a numbered step in `docs/waves/w15-acceptance.md`, which `E16/F04/US01/T01` writes.
