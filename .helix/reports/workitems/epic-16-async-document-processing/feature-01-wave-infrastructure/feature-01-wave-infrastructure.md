---
id: feature-01
type: feature
parent: epic-16
wave: w15
status: active
extends: epic-01 F02 (Azure foundation, Terraform layout)
---

# feature-01-wave-infrastructure — The one Terraform change of w15

## Slice

Every file under `infra/` that w15 touches, in **one task and one pull
request**, merged to `main` **before** the wave PR. It carries three
independent applies at once — Service Bus wiring and worker scaling (NW-27),
`infra/modules/communication` for ACS Email (NW-68), and the Graph application
permission (NW-67) — plus the four non-secret `AzureAd__*` keys and the `email`
optional claim that NW-05 cannot otherwise receive, because `backend.yml`
deploys `--image` only.

This is not a packaging preference. NW-27, NW-68, NW-67 and NW-05 all write
`modules/containerapps` and both environment roots; under
`check_single_writer.py` that is either one task or four serialized phases, and
four phases of Terraform in a five-phase wave leaves one phase for ten items
(`waves/w15.md`, NW-27 row, delivery-manager cell).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The wave's infrastructure lands in one apply per environment | w15 |

## Architecture decisions in force

- **ADR-005** (w15 footers) — the subscription and its five settings, two topic-scoped role assignments, the scale rule, both `max_replicas`, `max_delivery_count = 8`, the four ACS rows, the four `AzureAd__*` keys, the `email` optional claim, **$0.00 fixed-cost delta**.
- **ADR-007** (w15 footer) — `infra/modules/communication/` joins the layout; `servicebus → containerapps` and `identity → servicebus` become real edges; `modules/identity` gains a data source, a gated resource and an output.
- **ADR-011** — `acs-connection` is the wave's **one** new Key Vault entry and needs **no new permission**; Service Bus uses identity + RBAC, never a connection string (OQ-w15-sec-04).
- **ADR-014** (w15 clause 5) — a wave that changes `infra/` has **two merges to `main`**; this PR is the first and contains **only** `infra/**`.
- **ADR-015** (w15 clauses 1–9) — the apply identity's directory rights, granted **out of band by default**, verified at the gate.
- **ADR-016** (w15 clauses 13–16) — the first per-environment API key; `Invitations__AcceptUrlBase` composed from `var.spa_host_name`; the three named shortcuts that stay drift.
- **ADR-006** — `none`: every resource here is regional and keeps its pin; the ACS `data_location = "Europe"` case was ruled in ADR-006's w14 footer and is applied, not re-opened.

## Target repo

`raffa-infra` (the `infra/` tree of the product clone)
