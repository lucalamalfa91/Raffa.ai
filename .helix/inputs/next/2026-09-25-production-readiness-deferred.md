# Raffa — next-waves input · Production Readiness (deferred)

Status: **not a binding input for any wave yet.** Split out on 2026-09-25
from the first draft of `inputs/next/2026-09-25-saas-readiness-and-
integrations.md` at the founders' explicit instruction: *"per ora non
voglio un ambiente di produzione, non devo vendere ora, ma devo essere
pronto a farlo"* — not now, but ready to move fast when the decision comes.

**Do not queue any item in this file into a wave without a new, explicit
founders' instruction to do so.** When that instruction comes, this file
becomes the binding input for that round exactly as any other file in
`inputs/next/` — nothing here needs to be rewritten, only picked up.

Original ids are kept from the first draft (`SR-03`, `SR-06`, `SR-08`,
`SR-10`) so cross-references in the active file and in any later report
still resolve.

| | |
|---|---|
| Split from | `inputs/next/2026-09-25-saas-readiness-and-integrations.md` (active file — SR-01, SR-02, SR-04, SR-05, SR-07, SR-09, SR-11, SR-12, SR-13) |
| Trigger to un-defer | An explicit founders' decision to sell to paying customers beyond pilots/demos, stated in a new file or as an amendment to this one |
| Product oracles | `inputs/product-spec.md` §14 (security/privacy/governance), §15.4 (backup/recovery), §16/§20 (V1 done) |
| ADRs in force | ADR-016 (dev/demo promotion — no production exists today), ADR-011 (audit, no training on customer content) |

## 0. Why these four, specifically

Every item below shares one trait: it only pays for itself once there is a
paying customer's data, a paying customer's security review, or a paying
customer's own engineers calling Raffa at scale. A pilot on `demo` with a
real prospect does not need a tested restore drill, a signed DPA, issued
API keys, or a usage dashboard tied to a pricing model that does not exist
yet. Building them now would be effort spent on an audience that is not
there yet, while the founders' stated priority — identity federation and
Teams — waits.

## 1. Binding instructions (apply once this file is un-deferred)

1. **A production environment is a prerequisite for the first paying
   customer outside the founders**, not a nice-to-have alongside other
   features — it is `must` the moment this file is activated.
2. **No feature in this file promises a compliance certificate.** SOC 2 and
   ISO 27001 are third-party audits against controls that must already
   exist; SR-08 builds the controls and the honest public statement of
   what is and is not yet true. The certificate itself is a separate
   business decision (cost, timeline) outside Helix.
3. Everything else from the active file's binding instructions still
   applies once these items are picked up: the adapter pattern discipline,
   RLS/no-cross-tenant, and — especially — the data-residency rule
   (SR-01's non-goal in the active file): SR-08's tenant export is the one
   deliberate, Admin-triggered, fully audited exception to "data never
   leaves Raffa's infra," and only because the customer themselves asked
   for their own data back.

## 2. Items (original priority order, unchanged)

### SR-03 — Production environment (must, once activated)

- **Today (evidence):** ADR-016 defines only `dev` (auto-deploy on merge)
  and `demo` (tagged promotion). No `production` Terraform workspace, no
  HA topology, no tested backup/restore, no status page.
- **What it should be:** a third environment, promoted from `demo` the same
  way `demo` is promoted from `dev` (tag + GitHub Environment approval,
  ADR-016's own pattern extended). Minimum bar: automated daily Postgres
  backups with a **tested** point-in-time restore (spec §15.4 — "periodic
  restore tests" is not optional), object storage versioning, a documented
  RPO/RTO, and a public status page separate from the app itself.
- **Acceptance:** a restore drill on `dev` data restores into a scratch
  environment and Ask answers correctly from the restored data; the
  `production` Terraform workspace applies cleanly from a fresh `demo`
  promotion.
- **Seats (hint):** cloud-architect, delivery-manager, security-architect.

### SR-06 — Public API documentation and a scoped API key (should, once activated)

- **Today:** the OpenAPI contract exists and drives the generated web
  client (ADR-012), but there is no customer-facing API documentation
  portal and no way for a customer's own engineer to call Raffa with
  anything other than an interactive browser session.
- **What it should be:** a published API reference (generated from the
  existing OpenAPI spec) and per-workspace API keys (Admin-issued, scoped
  read-only or read-write, revocable, audited) as an alternative to the
  interactive token for server-to-server calls.
- **Acceptance:** an Admin issues an API key on `dev`; a `curl` call with
  that key against `GET /api/contracts` succeeds; a revoked key fails
  immediately; the key never appears again after issuance.
- **Seats (hint):** software-architect, security-architect, client-architect.

### SR-08 — Trust package: security page, DPA, tenant export (should, once activated)

- **Today:** no security/privacy page, no standard DPA, no self-service
  tenant data export (spec §14.3 requires export **and** deletion at
  tenant level; only deletion exists).
- **What it should be:** a public `/security` or `/trust` page stating
  plainly what is already true (Azure North Europe, RLS tenant isolation,
  no training on customer content, AI Gateway logging) without overclaiming
  what is not (no SOC 2 yet — say so); a standard DPA template legal can
  hand a prospect same-day; a tenant export endpoint (Admin-only, audited,
  the one deliberate exception to the data-residency rule per instruction
  3 above) so "can we get our data back if we leave" has a real answer.
- **Acceptance:** an Admin exports a workspace on `dev` and receives an
  archive containing every validated contract's structured facts and
  original file; the `/trust` page makes no claim the codebase does not
  back.
- **Seats (hint):** product-owner (copy, what to claim), security-architect
  (what is actually true), software-architect (export endpoint).

### SR-10 — Per-tenant usage and cost visibility (could, once activated)

- **Today:** spec §15.1/§15.2 call for tenant economics telemetry; nothing
  customer-facing exists. This becomes a prerequisite the moment any
  pricing model tied to usage (contracts, pages, AI queries) is decided —
  which has not happened yet (the competitive review's open questions list
  pricing as an undecided item).
- **What it should be:** an Admin-visible usage panel (documents processed
  this period, AI queries, storage) — internal cost metrics already exist
  per the spec's observability requirements; this exposes a customer-safe
  subset.
- **Acceptance:** an Admin sees a usage summary for their workspace that
  matches the operator-side telemetry for the same period.
- **Seats (hint):** product-owner, software-architect, client-architect.

## 3. Order constraints (once activated)

- SR-03 first — everything else in this file is more credible with a real
  production environment behind it, and SR-08's honesty requirement ("no
  SOC 2 yet — say so") gets easier to eventually close once SR-03 exists.
- SR-06 and SR-08 can run in parallel after SR-03.
- SR-10 depends on a pricing decision existing, not on any item here
  technically — it can slot in whenever that decision lands, inside or
  outside this file's own wave.
