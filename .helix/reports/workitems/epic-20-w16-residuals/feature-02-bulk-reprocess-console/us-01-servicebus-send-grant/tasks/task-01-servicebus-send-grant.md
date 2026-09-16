---
id: E20/F02/US01/T01
type: task
story: us-01-servicebus-send-grant
wave: w17
status: live
target_repo: raffa-infra
requires: [hcp_terraform]
---

# task-01-servicebus-send-grant — one topic-scoped Sender role for the CI deploy principal

## Context

**Closes: NW-73 (the infrastructure half).**

Decision row: `reports/architecture/waves/w17.md` — the **NW-73** row
(cloud-architect half in full), plus the rulings on **OQ-w17-008** (all three
thirds) and **OQ-w17-ca-01**.

ADRs in force: **ADR-005** w17 §15–§18, §22, §27; **ADR-007** w17 §5–§9;
**ADR-022** w17 clause 1; **ADR-009** w17 clause 1 rule 3; **ADR-015** —
**`none`** (the new right is an Azure **data-plane role** on an **existing**
principal, so no federated credential, GitHub secret, subject claim or Graph
right changes); **ADR-006 / ADR-008** — **`none`**.

⚠ **This task is PR 1 and it ships ALONE** (ADR-014 w17 clauses 1–2). Nothing
else in w17 goes in this pull request. It merges, HCP applies it, and only then
may `E20/F02/US02/T01`'s workflow be dispatched — its first run against a
missing grant is **destructive**, not merely failed (ADR-016 w17 clause 44).

Already true on `main`, so **not** work to redo: the topic
`azurerm_servicebus_topic.extraction_events`
(`infra/modules/servicebus/main.tf:20`), the subscription
`document_processing` (`:53`), and the `data "azuread_service_principal"
"ci_deploy"` lookups in both roots
(`infra/environments/dev/main.tf:223-225`,
`infra/environments/demo/main.tf:245-247`), whose `object_id` is already passed
to the keyvault module (`dev/main.tf:236`, `demo/main.tf:258`).

## Coding objective

In `raffa-infra`, grant the CI deploy principal exactly one new Azure right.

1. **Module variable.** In `infra/modules/servicebus/variables.tf` (which today
   declares exactly four variables: `environment` `:1`, `location` `:11`,
   `resource_group_name` `:17`, `workload_principal_id` `:27`), add
   `ci_deploy_principal_id` — `type = string`, **no default** (required), with a
   comment naming this task, ADR-022 w17 clause 1 and the reason the name
   matches `infra/modules/keyvault/variables.tf:34`: one grep finds every grant
   to that principal.
2. **The assignment.** In `infra/modules/servicebus/main.tf`, after the existing
   `azurerm_role_assignment.workload_servicebus_receiver` (`:89-102`), add
   `azurerm_role_assignment.ci_deploy_servicebus_sender` with:
   - `scope = azurerm_servicebus_topic.extraction_events.id` — **topic-scoped,
     never the namespace**;
   - `role_definition_name = "Azure Service Bus Data Sender"` — and nothing
     else;
   - `principal_id = var.ci_deploy_principal_id`;
   - `skip_service_principal_aad_check = true`, matching `:75` and `:93`;
   - a `lifecycle { ignore_changes = [...] }` block with the **same members** as
     `:80-86` and `:95-101`. ⚠ ARM rejects in-place updates to a role
     assignment: omitting this block plans clean today and fails a *later,
     unrelated* apply.
   - a comment naming this task, ADR-005 w17 §15 and the recorded reason the
     runner was chosen over a Container Apps Job (the workload identity holds
     **Sender *and* Receiver**, so a Job would grant the **wider** capability).
3. **Both roots, same PR.** In `infra/environments/dev/main.tf`, the
   `module "servicebus"` call (`:106-116`, which already passes
   `workload_principal_id = module.identity.workload_principal_id` at `:115`)
   gains **one line**:
   `ci_deploy_principal_id = data.azuread_service_principal.ci_deploy.object_id`.
   Do the same in `infra/environments/demo/main.tf`'s `module "servicebus"`
   block (`:128-134`). **No new data source is needed in either root.**
4. **Correct the falsified README passage.** `infra/README.md:417` states "**The
   two new V2 operator workflows need no new Azure grant.**" — this change is
   exactly that grant. Update that passage (and the grant note at `:331-337`
   where it now reads as incomplete) in **this** PR. README hygiene is standing
   implementer scope, so the path is named here rather than in the file table.
5. **Cost line.** Record **$0.00/month on both environments** — a role
   assignment is free and no SKU, replica count or capacity moves.

⚠ **Do not run `terraform apply`.** HCP VCS owns apply:
`.github/workflows/infra.yml`'s apply job is a step-summary echo named
"terraform apply skipped" (`:114-136`), deliberately (`:104-113`). This task
produces a green **plan**; the apply is an operator act at the HITL gate.

## Parent story AC covered

- AC-1 `terraform plan` on `infra/environments/dev` shows exactly **one** resource to add: an `azurerm_role_assignment` scoped to the `extraction-events` topic, role `Azure Service Bus Data Sender`, principal `data.azuread_service_principal.ci_deploy.object_id`.
- AC-2 `terraform plan` on `infra/environments/demo` shows the **same single addition**; neither plan changes the two existing assignments.
- AC-3 The new assignment carries a `lifecycle { ignore_changes = … }` block with the same members as the two existing grants.
- AC-4 No Receiver role, no `Manage`, no namespace scope, no authorization rule, no SAS key.
- AC-5 The new module variable is **required** and both env roots pass it; `terraform validate` exits 0.
- AC-6 The monthly cost delta is **$0.00** on both environments.

## Files to create or modify

| Path | Change |
|------|--------|
| `infra/modules/servicebus/variables.tf` | modify — add required `ci_deploy_principal_id` |
| `infra/modules/servicebus/main.tf` | modify — add `azurerm_role_assignment.ci_deploy_servicebus_sender`, topic-scoped, Sender only, with the `lifecycle { ignore_changes }` block |
| `infra/environments/dev/main.tf` | modify — one line in the `module "servicebus"` call |
| `infra/environments/demo/main.tf` | modify — one line in the `module "servicebus"` call |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-005 w17 §15** — the wave's entire Azure delta is **one RBAC row**.
  - **ADR-005 w17 §16** — (A) the runner, not (C) a Container Apps Job. Recorded so it is not re-opened as "the cheaper option".
  - **ADR-005 w17 §17** — **both roots in the same PR**; "wiring is not applying".
  - **ADR-005 w17 §18, §26** — **zero new environment keys**. ⚠ **`AZURE_CLIENT_ID` must not be set on a runner**: it selects the user-assigned managed identity and aims `DefaultAzureCredential` at an identity absent from that host.
  - **ADR-007 w17 §9** — `scripts/hcp_vcs_wiring.py:104-106` wires **both** workspaces to `EXPECTED_BRANCH = "main"` with `EXPECTED_TRIGGER_PREFIX = "infra/"`. **`demo` moves at the merge, not at the promotion tag.**
  - **ADR-009 w17 clause 1 rule 3** — a Send right is **tenant-blind**. Tenant scoping belongs to the console and the audit row, never to this role.
  - `infra/modules/servicebus/main.tf:64-70` is the module's own standing rule: "identity + RBAC, never a connection string and never the namespace's default full-access shared key… Topic-scoped (not namespace-wide)". This addition obeys it.
- **The module's existing shape, verified**: two assignments today, both to
  `var.workload_principal_id` — Sender `:71-87`, Receiver `:89-102`. The CI
  principal has **no** Service Bus role anywhere in `infra/`; its only
  data-plane grant is Key Vault Secrets User.
- **Do not touch**: the two existing role assignments; the namespace, topic or
  subscription blocks; `infra/modules/keyvault/**`; `infra/modules/identity/**`;
  `infra/modules/containerapps/**`; anything under `.github/workflows/**` (that
  is `E20/F02/US02/T01`'s, and only its); `infra/modules/storage/**`.
- **Do not** create a purpose-made publisher service principal. ADR-015 keeps
  SPs out of band, so that would cost an out-of-band creation, a federated
  credential, a GitHub Environment variable and a **third** literal client id in
  source — and both roots warn that a stale literal **fails the plan outright**
  (`dev/main.tf:218-222`). w16 already ruled "no new identity, credential, app
  role or secret".

## Definition of done

- [ ] `terraform -chdir=infra/modules/servicebus init -backend=false` then `terraform -chdir=infra/modules/servicebus validate` exits 0
- [ ] `terraform -chdir=infra/environments/dev init` then `terraform -chdir=infra/environments/dev validate` exits 0
- [ ] `terraform -chdir=infra/environments/demo init` then `terraform -chdir=infra/environments/demo validate` exits 0
- [ ] `terraform -chdir=infra/environments/dev plan` shows **`1 to add, 0 to change, 0 to destroy`**
- [ ] `terraform -chdir=infra/environments/demo plan` shows **`1 to add, 0 to change, 0 to destroy`**
- [ ] `terraform fmt -check -recursive infra/` exits 0
- [ ] `grep -n "Azure Service Bus Data Receiver\|Manage\|authorization_rule\|shared_access\|primary_connection_string" infra/modules/servicebus/main.tf` shows only the pre-existing Receiver grant at `:89-102` and nothing new
- [ ] `grep -c "ignore_changes" infra/modules/servicebus/main.tf` returns **3**
- [ ] `grep -n "ci_deploy_principal_id" infra/modules/servicebus/variables.tf infra/modules/servicebus/main.tf infra/environments/dev/main.tf infra/environments/demo/main.tf` returns a hit in all four files
- [ ] `grep -n "default" infra/modules/servicebus/variables.tf` shows **no** default on `ci_deploy_principal_id`
- [ ] `grep -n "need no new Azure grant" infra/README.md` returns nothing
- [ ] the PR body states the cost delta as **$0.00/month on dev and demo** and says "apply is HCP VCS on merge; do not run terraform apply from Actions"

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| plan | exactly one resource added, no change to the two existing grants, on **both** roots | `terraform plan` output in the PR (AC-1, AC-2) |
| static | the module and both roots parse and type-check with a required variable | `terraform validate` in three directories (AC-5) |
| grep | no Receiver, no `Manage`, no namespace scope, no authorization rule, no SAS key; three `lifecycle` blocks | the DoD greps above (AC-3, AC-4) |
| static | CI YAML is untouched by this task, so `backend.yml`'s workflow scanner stays green | `dotnet test backend/tests/Raffa.ArchitectureTests --configuration Release` exits 0 |

## Open questions blocking this task

- **none blocking.** OQ-w17-008 is ruled (Send only, topic-scoped) and
  OQ-w17-ca-01 is ruled ((A), the runner).
- **OQ-w17-ca-05 is open but does not block this task**: `raffa-demo`'s HCP
  **auto-apply** setting is live state unreadable from this checkout.
  **Assumption in force: off.** It is an **operator read at the gate**, not work
  here; this task's output is a green plan either way.

## Wave-spec entry

```yaml
- id: E20/F02/US01/T01
  prompt: reports/workitems/epic-20-w16-residuals/feature-02-bulk-reprocess-console/us-01-servicebus-send-grant/tasks/task-01-servicebus-send-grant.md
  produces: [servicebus-ci-send-grant]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
