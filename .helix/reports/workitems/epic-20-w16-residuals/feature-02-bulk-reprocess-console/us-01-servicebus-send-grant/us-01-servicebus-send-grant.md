---
id: us-01
type: user-story
parent: feature-02
wave: w17
status: active
---

# us-01-servicebus-send-grant — the CI deploy principal may publish to extraction-events

## Story

As the **operator**, I want `raffa-sp-<env>` to hold exactly one new Azure
right — `Azure Service Bus Data Sender`, topic-scoped on `extraction-events` —
so that a CI-hosted console can publish extraction requests without a SAS key,
a connection string, or a second identity.

## Acceptance criteria

- [ ] AC-1 `terraform plan` on `infra/environments/dev` shows exactly **one**
  resource to add: an `azurerm_role_assignment` scoped to the
  `extraction-events` topic, role `Azure Service Bus Data Sender`, principal
  `data.azuread_service_principal.ci_deploy.object_id`.
- [ ] AC-2 `terraform plan` on `infra/environments/demo` shows the **same single
  addition**. Neither plan shows a change to the two existing assignments.
- [ ] AC-3 The new assignment carries a `lifecycle { ignore_changes = … }` block
  with the same members as the two existing grants, so a later unrelated apply
  does not fail on an in-place role-assignment update.
- [ ] AC-4 No `Azure Service Bus Data Receiver` role, no `Manage` right, no
  namespace-scoped assignment, no `azurerm_servicebus_namespace_authorization_rule`
  and no SAS key appears anywhere in the diff.
- [ ] AC-5 The new module variable is **required** (no default) and both env
  roots pass it; `terraform validate` exits 0 in the module and both roots.
- [ ] AC-6 The monthly cost delta is **$0.00** on both environments and the cost
  line says so.

## Definition of done

- [ ] every AC above is verified by at least one test named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| none | nothing in the wave `depends_on` this at build time. **`us-02`'s acceptance depends on its *apply*** — an operator act at the HITL gate, never a wave task (ADR-014 w17 clause 2) |

## Architecture decisions in force

- **ADR-005** w17 §15–§18, §22, §27 — one RBAC row, Sender only, topic-scoped,
  the `lifecycle` block, both roots, **$0.00/month**; zero new environment keys.
- **ADR-007** w17 §5–§9 — the module gains one variable and one resource; ⚠ one
  merge to `main` under `infra/` queues a VCS run on **`raffa-dev` and
  `raffa-demo` at the same instant**. `demo` moves on the **merge**, never on the
  promotion tag.
- **ADR-022** w17 clause 1 — a CI principal **may** hold this right: the message
  is a pointer, the Worker re-reads authority under RLS, `MessageId` collapses
  duplicates. Send crosses no confidentiality boundary; Receive would.
- **ADR-009** w17 clause 1 rule 3 — a Send right is **tenant-blind**; tenant
  scoping is the console's argument and the audit row, **never the role**.
- **ADR-015** — **`none`**: the new right is an Azure **data-plane role** on an
  **existing** principal. No federated credential, GitHub secret, subject claim
  or Graph right changes.
- **ADR-006 / ADR-008** — **`none`**: no region, Foundry account, project,
  deployment or capacity change.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | servicebus-send-grant | M | phase-1 |

## Council decisions carried into this story

- **Shape (A), the GitHub Actions runner** (OQ-w17-ca-01, ruled by
  cloud-architect). Three shapes were priced. (C), a Container Apps Job under
  the workload identity, was refused **even though it needs no new role**:
  `infra/modules/servicebus/main.tf` grants that identity **both Sender
  (`:71-87`) and Receiver (`:89-102`)**, so (C) would let the console
  **receive from `document-processing`** — the one subscription the Worker
  depends on. **The question is not how many rights the wave adds but what the
  console can do**: (C) adds zero and grants the wider capability; (A) adds one
  and grants the narrower. (B), an operator laptop, was refused because the
  human principal holds no Send right.
- **Variable name `ci_deploy_principal_id`**, matching
  `infra/modules/keyvault/variables.tf:34`, so one grep finds every grant to
  that principal. **Required**, not optional with a default.
- **Both roots in the same PR.** The reason is corrected on the record: a
  dev-only wiring does not "break the promotion path one tag later" — it queues
  a **failing plan on `raffa-demo` in the same merge**, sooner and quieter
  (ADR-016 w17 clause 43).
- **Blast radius, stated plainly**: whoever can trigger the workflow inherits
  publish rights on `extraction-events`, bounded by topic scope, Send only,
  ids-only messages and the `document.reprocessed` audit row — **and not bounded
  by tenant**.
- **The apply is an operator act at the HITL gate**, never a wave task: HCP VCS
  owns apply (`.github/workflows/infra.yml:114-136` is a step-summary echo
  named "terraform apply skipped").
- ⚠ **`infra/README.md:417`** asserts "**The two new V2 operator workflows need
  no new Azure grant**" — falsified by this change, and updated in this same PR.

## Open questions

- **OQ-w17-008** (may a CI principal hold a Send right) — **ruled yes** by all
  three seats: Send only, topic-scoped, never `Manage`, never a SAS key.
  ADR-022 w17 clause 1, ADR-005 w17 §16.
- **OQ-w17-ca-01** (which host runs the console) — **ruled (A)**, the runner.
- **OQ-w17-ca-05** (`raffa-demo`'s HCP **auto-apply** setting) — **open, an
  operator read, not a task.** Live HCP state, unreadable from this checkout.
  **Assumption in force: off** (`infra.yml:109`'s own instruction). Confirm both
  workspaces' setting at the gate and record it. ⚠ In the `off` branch `demo`
  is green with **no** Send grant, and the console's first publish there is
  destructive (ADR-011 w17 clause 26) — which is why `us-02` ships `dev` only.
