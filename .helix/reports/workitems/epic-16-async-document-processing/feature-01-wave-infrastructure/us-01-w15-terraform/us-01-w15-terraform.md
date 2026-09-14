---
id: us-01
type: user-story
parent: feature-01
wave: w15
status: active
---

# us-01-w15-terraform — The wave's infrastructure lands in one apply per environment

## Story

As the **operator**, I want every `infra/` change this wave needs in one
reviewable pull request and one HCP apply per environment, so that a missing
directory permission degrades one feature instead of failing three, and so that
the apply is `CURRENT` before any image that reads its keys exists.

## Acceptance criteria

- [ ] AC-1 `terraform validate` passes for **both** environment roots, and `terraform fmt -check -recursive infra` is clean.
- [ ] AC-2 The `extraction-events` topic has **exactly one** subscription, named `document-processing`, with `max_delivery_count = 8`, `lock_duration = PT5M`, `default_message_ttl = P1D` and **sessions not enabled**.
- [ ] AC-3 The existing per-environment workload identity holds **two topic-scoped** role assignments (`Azure Service Bus Data Sender`, `Azure Service Bus Data Receiver`). No `RootManageSharedAccessKey`, no namespace-wide send rule, and **no Service Bus secret in Key Vault**.
- [ ] AC-4 The worker container app has a KEDA `azure-servicebus` scale rule, keeps `min_replicas = 0`, and both apps have `max_replicas = 3`. The worker receives `ConnectionStrings__Storage` as a **fail-fast** key; every `ServiceBus__*` key binds optionally.
- [ ] AC-5 `infra/modules/communication` exists and creates four resources per environment — communication service, email communication service, an Azure Managed Domain and the association — with `data_location = "Europe"`; `acs-connection` lands in Key Vault with the **same `depends_on` RBAC pair** as `postgres-connection`, surfaces as handle `acs-cs`, and reaches the **API app only**.
- [ ] AC-6 `Invitations__AcceptUrlBase` is composed inside `modules/containerapps` from `var.spa_host_name` as `https://<host>` with **no trailing slash**, and appears in no root as a typed literal.
- [ ] AC-7 `modules/identity` gains a Graph service-principal data source, a **`count`-gated** `azuread_app_role_assignment` for `User.Invite.All`, `var.guest_provisioning_enabled` (default **`false`**), a `tenant_id` output, and an `optional_claims` block requesting the `email` claim on the access token of `azuread_application.api`.
- [ ] AC-8 The API app receives `AzureAd__Authority`, `AzureAd__TenantId`, `AzureAd__ClientId` and `AzureAd__Audience` from module outputs — `ClientId` from `api_client_id` and `Audience` from `api_identifier_uri`, never swapped. The **worker receives none of the four**.
- [ ] AC-9 Per-environment flags: `dev` has mail and guest provisioning **on**; `demo` has both **`false`**. No secret and no value is copied between environments.
- [ ] AC-10 The `terraform plan` for `azuread_application.api` shows an in-place update (`~`) and **never** a replacement (`-/+`), **and** `identifier_uris` shows no diff at all.

## Definition of done

- [ ] every AC above is verified by at least one check named in the task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| — | Nothing in this wave. It is phase 1 and it has **no dependents**: its effect is not observable by the wave that wrote it (ADR-016 w14 clause 3), so a `depends_on` pointing at it would be a lie the fan-out would act on |

## Architecture decisions in force

- **ADR-005** (w15 footers §1–§5, clauses 9–14) — every resource, number and key in this story, and the `$0.00` fixed-cost confirmation.
- **ADR-007** (w15 footer §1–§2) — the new module directory and the two new edges.
- **ADR-011** (w15 §1–§2) — `acs-connection` is the wave's one new secret; Service Bus is identity + RBAC.
- **ADR-015** (w15 clauses 1–9) — the apply identity's rights; the assignment **is** the consent for a managed identity.
- **ADR-016** (w15 clauses 13–16) — the apply barrier, the flag table, the composed accept-URL base.
- **ADR-010** (w15 §2.4) — the `email` optional claim is requested in Terraform, and the design does not depend on it.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | w15 Terraform: Service Bus wiring, ACS Email, the Graph permission and the `AzureAd__*` keys | L | phase-1 |

## Council decisions carried into this story

Subscription `document-processing` — **not** `extraction-worker` (ADR-027 §C8
repaired that stale constant; one name, or the Terraform and the consumer
disagree about which subscription holds the messages). `max_delivery_count = 8`,
raised from 5 by ADR-005 clause 9 because deliveries and attempts do not advance
together: two `DeliveryCount` abandons + `MaxAttempts = 3` + scale-in eviction +
the renewal ceiling is a realistic seven against a ceiling of five, and the
broker would dead-letter **before** the handler writes `Failed`. `MaxAttempts`
stays 3 and no peer re-works anything. Sessions are deliberately **not** enabled
— they would silently cap the worker at one effective replica.
`min_replicas = 1` is **rejected**: ~$14/env/month ≈ $28 across both is a new
fixed monthly line the lock forbids.

## Open questions

- **OQ-w15-cl-01** — can the `custom_scale_rule` authenticate its queue-length probe with the workload identity under `azurerm ~> 4.0`? **Assumption in force: yes.** If `terraform validate` says otherwise, take the bounded fallback: a **topic-scoped** `listen = true, send = false, manage = false` authorization rule → Key Vault `servicebus-listen-connection` → handle `sb-listen-cs`, referenced **only** by `custom_scale_rule.authentication`, never by an `env {}` block and never by application code. `min_replicas = 1` is **not** a fallback.
- **OQ-w15-dm-01** — does `raffa-demo` apply automatically on a push to `main` touching `infra/`? **Assumption in force: it does**, which is why every `demo` flag defaults `false`.
- **OQ-w15-cl-02** — splitting the workload identity into api/worker identities is deferred, with the residual named (a compromised API host can also drain the queue). Not this wave.
