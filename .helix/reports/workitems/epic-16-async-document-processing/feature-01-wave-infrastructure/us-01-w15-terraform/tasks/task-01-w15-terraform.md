---
id: E16/F01/US01/T01
type: task
story: us-01-w15-terraform
wave: w15
status: live
target_repo: raffa-infra
requires: [hcp_terraform]
---

# task-01-w15-terraform — Service Bus wiring, ACS Email, the Graph permission and the `AzureAd__*` keys, in one PR

## Coding objective

Write every `infra/` change wave w15 needs, in **one** pull request containing
**only** `infra/**`. Wire the already-provisioned Service Bus namespace to both
container apps (one `document-processing` subscription, two topic-scoped role
assignments on the existing workload identity, a KEDA scale rule, `max_replicas`
1 → 3 on both apps, and the worker's missing `ConnectionStrings__Storage`).
Create `infra/modules/communication` and instantiate it in both roots, with
`acs-connection` in Key Vault, handle `acs-cs`, and the four `Invitations__*`
keys on the API app. Add the Graph pieces to `modules/identity`: a service
principal data source, a `count`-gated `azuread_app_role_assignment` for
`User.Invite.All`, `var.guest_provisioning_enabled` (default `false`), a
`tenant_id` output and an `optional_claims` block requesting `email`. Finally
publish the four non-secret `AzureAd__*` keys on the API app from
`module.identity` outputs that already exist and have **zero consumers** today.

Nothing here builds a new resource type for Service Bus: the namespace
(`sbns-raffa-${var.environment}`, Standard) and the topic `extraction-events`
already exist at `infra/modules/servicebus/main.tf:11-18` and `:20-23`; that
file ends at `:24` with **no subscription resource of any name**, which is why a
producer shipped against today's Terraform would report success and silently
lose every document.

## Parent story AC covered

- AC-1 … AC-10 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `infra/modules/servicebus/main.tf` | one `azurerm_servicebus_subscription` **`document-processing`** on `extraction-events` (`max_delivery_count = 8`, `lock_duration = "PT5M"`, `default_message_ttl = "P1D"`, `requires_session = false`, dead-lettering on expiry); two **topic-scoped** `azurerm_role_assignment` (`Azure Service Bus Data Sender`, `Azure Service Bus Data Receiver`) on the workload principal |
| `infra/modules/servicebus/variables.tf` | new `workload_principal_id` |
| `infra/modules/servicebus/outputs.tf` | fully-qualified namespace + topic and subscription names, as **non-secret** config |
| `infra/modules/communication/main.tf` | **new** — `azurerm_communication_service` `acs-raffa-<env>` with `data_location = "Europe"`, `azurerm_email_communication_service` `acsemail-raffa-<env>`, an Azure Managed Domain, and the service↔domain association |
| `infra/modules/communication/variables.tf` | **new** — environment, resource group, tags |
| `infra/modules/communication/outputs.tf` | **new** — the primary connection string (sensitive) and the **sender address**, which callers must consume rather than hand-compose |
| `infra/modules/keyvault/main.tf` | `acs-connection` secret, with the **same `depends_on` RBAC pair** already used by `postgres-connection` / `storage-connection` at `:81-101` |
| `infra/modules/keyvault/variables.tf` / `outputs.tf` | the ACS connection input and its secret id output |
| `infra/modules/identity/main.tf` | Graph `data "azuread_service_principal"`; `count`-gated `azuread_app_role_assignment` for `User.Invite.All` on the workload identity; `optional_claims { access_token { name = "email" } }` on `azuread_application.api` (`:57`); **replace the stale comment at `:41-42`** ("the deploy job does not need Microsoft Graph") with the ADR-015 w15 distinction between the deploy plane and the apply plane |
| `infra/modules/identity/variables.tf` | new `guest_provisioning_enabled` (default **`false`**) |
| `infra/modules/identity/outputs.tf` | new `tenant_id` output (`issuer`, `api_client_id`, `api_identifier_uri` already exist at `:7-28` and gain their first consumers here) |
| `infra/modules/containerapps/main.tf` | worker: `acs`-free, but a `st-cs` secret handle + `ConnectionStrings__Storage` env var, the KEDA `azure-servicebus` `custom_scale_rule`, `max_replicas = 3`; API: `max_replicas = 3`, `acs-cs` handle, `Invitations__Mail__Enabled` / `__SenderAddress` / `__ConnectionString`, `Invitations__AcceptUrlBase` composed from `var.spa_host_name`, `Invitations__GuestProvisioning__Enabled` / `__TenantId`, and the four `AzureAd__*` keys; **both** hosts get the optional `ServiceBus__*` keys |
| `infra/modules/containerapps/variables.tf` | new inputs for the ACS secret id + sender address, the Service Bus namespace/topic/subscription, the four `AzureAd__*` values, and the two invitation flags. `var.spa_host_name` (`:59`) and `var.storage_connection_secret_id` already exist and are **reused** |
| `infra/environments/dev/main.tf` | instantiate `module "communication"`; pass `workload_principal_id` to `module "servicebus"`; wire the new `containerapps` inputs; `guest_provisioning_enabled = true`; `invitation_mail_enabled = true` |
| `infra/environments/demo/main.tf` | the same wiring with **`guest_provisioning_enabled = false`** and **`invitation_mail_enabled = false`** |
| `infra/environments/dev/variables.tf`, `infra/environments/demo/variables.tf` | the two per-environment flags, mirroring the existing `ai_gateway_wired` shape |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root.

## Context the implementer needs

`Closes: NW-27 (infrastructure half), NW-67 (infrastructure half), NW-68
(infrastructure half), NW-05 (infrastructure half)`.

Decision rows: `reports/architecture/waves/w15.md` — **NW-27** (cloud-architect
and delivery-manager cells), **NW-67** (cloud-architect cell), **NW-68**
(cloud-architect and delivery-manager cells), **NW-05** (cloud-architect cell).

- **Architecture decisions in force**: **ADR-005** w15 footers (§1–§5 and
  clauses 9–14) — every number below is theirs; **ADR-007** w15 footer §1–§2;
  **ADR-011** w15 §1–§2 (`acs-connection` is the wave's one new secret; Service
  Bus is identity + RBAC, **never** a connection string — OQ-w15-sec-04 ruled
  against ADR-027 §D12's earlier hand-off wording); **ADR-015** w15 clauses 1–9;
  **ADR-016** w15 clauses 13–16; **ADR-010** w15 §2.4; **ADR-006** — no footer,
  because its w14 footer `:82` already ruled `data_location = "Europe"` is not a
  second region.
- **This PR contains `infra/**` and nothing else**, and it merges to `main`
  **before** the wave PR (ADR-014 w15 clause 5). It is safe by construction:
  `infra/**` is in neither `backend.yml`'s nor `web.yml`'s path filter
  (`backend.yml:16-23`, `web.yml:17-23`), so that merge deploys no image.
- **`ClientId` and `Audience` are both published on purpose.** At
  `requested_access_token_version = 2` (`identity/main.tf:75`) the `aud` claim
  **is** the client id, while the SPA requests scopes against the identifier
  URI. Wiring `api_identifier_uri` where the client id belongs applies cleanly,
  deploys cleanly, and then 401s every request in the browser.
- **Read the plan before approving it.** `azuread_application.api` must show
  `~`, never `-/+`: a replacement mints a new client id and takes out
  `AzureAd__ClientId`, the `aud`, both Static Web Apps and `web.yml`'s scope
  literals **behind a green CI run**, and it destroys the service principal
  (`:107`) and the pre-authorization (`:155`) with it. Separately and just as
  binding, **`identifier_uris` must show no diff at all** — `web.yml:204-205` is
  a hand-copied duplicate of `identity/main.tf:70` with no test and no build
  step comparing them, so an in-place change to that string passes the first
  check and silently breaks every login (ADR-012 w15 §16).
- **The `count` gate is what lets all three applies ride one PR.** If the apply
  identity lacks the directory right, `guest_provisioning_enabled` stays
  `false`, `count = 0`, and the whole w15 apply still succeeds — NW-67 degrades
  to a later one-line flip instead of blocking Service Bus and mail.
- **For a managed identity there is no "Grant admin consent" click** — the
  `azuread_app_role_assignment` **is** the consent. What needs a one-time grant
  is the **apply** identity, out of band by default (ADR-015 w15 clause 9),
  verified at the gate.
- **`infra/README.md` carries a second layout tree at `:18-29`** and must gain
  `communication/` in this change, with the module count corrected from nine to
  twelve (`staticwebapp/` and `foundry/` landed without it). That file is not
  listed above because README hygiene is standing implementer scope; it is still
  owed, and AC-1's `fmt`/`validate` will not catch it.
- **Do not touch**: anything outside `infra/`. Not `backend.yml`, not `web.yml`
  — w15's planned CI-YAML set is **zero files** (ADR-014 w15 clause 6). Not
  `--set-env-vars` and not the single `dynamic "env"` block as a shortcut for a
  new key: both are **named drift** (ADR-016 w15 clause 16) and the next apply
  reverts them anyway. Do not enable Service Bus sessions. Do not raise
  `min_replicas` above 0.

## Definition of done

- [ ] `terraform fmt -check -recursive infra` exit 0
- [ ] `terraform -chdir=infra/environments/dev init -backend=false` then `terraform -chdir=infra/environments/dev validate` exit 0
- [ ] `terraform -chdir=infra/environments/demo init -backend=false` then `terraform -chdir=infra/environments/demo validate` exit 0
- [ ] `grep -R "RootManageSharedAccessKey" infra/` returns **no match**; `grep -R "servicebus" infra/modules/keyvault/` returns **no match** (NW-27 adds no Key Vault secret)
- [ ] `grep -c "azurerm_servicebus_subscription" infra/modules/servicebus/main.tf` is exactly **1**, and its name is `document-processing`
- [ ] `grep -R "min_replicas" infra/modules/containerapps/main.tf` still shows `0` for the worker
- [ ] `infra/README.md`'s layout tree lists `communication/` and the module count matches `ls infra/modules | wc -l`
- [ ] The HCP `plan` output for both workspaces is attached to the PR, and a human confirms: `azuread_application.api` is `~` and not `-/+`; `identifier_uris` has no diff; exactly one subscription is created; two role assignments are created; no Key Vault secret named `servicebus-*` unless OQ-w15-cl-01's fallback was taken

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| static | the configuration parses and every identifier resolves under the pinned providers | `terraform validate` in both roots (`.github/workflows/infra.yml` runs fmt/validate/plan) |
| plan review | the app registration is updated in place and its identifier URI is untouched | the HCP plan, read by a human at the ADR-014 w15 gate |
| acceptance | the resources actually work — A15-1/A15-2 with the worker scaled as `demo` will run it, and one real invite on deployed `dev` | `docs/waves/w15-acceptance.md`, written by `E16/F04/US01/T01` |

There is deliberately **no unit test**: HCP runs plan and apply remotely and
`infra.yml` is fmt/validate/plan only. "The HCP run is `CURRENT`" proves none of
the four gaps this task closes, which is why the acceptance walk and the
post-deploy revision-state assertion exist (ADR-016 w15 clauses 17, 23).

## Open questions blocking this task

- **OQ-w15-cl-01** — scale-rule authentication. **Assumption in force: workload identity.** Bounded fallback recorded in the story; `min_replicas = 1` is not a fallback. Proved by `terraform validate`, which is where identifiers are proved rather than asserted.
- **OQ-w15-dm-01** — `raffa-demo` may apply at this merge. **Assumption in force: it does**, which is why both `demo` flags are `false`.
- None blocks the task.

## Wave-spec entry
```yaml
- id: E16/F01/US01/T01
  prompt: reports/workitems/epic-16-async-document-processing/feature-01-wave-infrastructure/us-01-w15-terraform/tasks/task-01-w15-terraform.md
  produces: [w15-terraform]
  depends_on: []
  effort: L
  layer: backend
  status: live
```

**This task has no dependents, deliberately.** Its effect is not observable by
the wave that wrote it (ADR-016 w14 clause 3), so a `depends_on` pointing at it
would be a lie — and under `require_delivery: true` with
`task_failure_markers: ["HALTED:"]` it is a lie that fails real tasks. Its
failure is caught by a human reading the slice and the HCP plan, not by the
graph (`waves/w15.md`, NW-27 row, delivery-manager cell).
