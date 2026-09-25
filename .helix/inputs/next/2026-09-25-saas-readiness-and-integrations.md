# Raffa — next-waves input · SaaS Readiness & Customer Infra Integration

Status: **binding input** for a next-wave requirements document. Written
2026-09-25 by the founders, direct follow-up to "what is missing for Raffa
to be a real SaaS" (Vertice competitive review) and the explicit question
**"if a new customer wanted us inside their own infra today, are we
ready?"**. IDs are new and stable (`SR-nn`); they do not collide with
`NW-01…NW-97` or `CS-01…CS-10`.

| | |
|---|---|
| Companion input | `inputs/next/2026-09-25-customer-success-concierge.md` (CS-01…CS-10) — this file does not repeat support/concierge scope |
| Product oracles | `inputs/product-spec.md` §3 (tenancy), §13 (API, events, integration priority P1/P2/P3), §14 (security/privacy/governance), §15 (reliability/observability/cost) |
| ADRs in force | ADR-009 (RLS), ADR-010 (Entra OIDC — **the finding this file starts from**), ADR-011 (secrets/RAG isolation, audit), ADR-005/006 (Azure SKUs, region), ADR-016 (dev/demo promotion — **no production exists**) |
| Verified today (2026-09-25, this checkout) | `infra/modules/identity/main.tf:74,148` — `sign_in_audience = "AzureADMyOrg"` (single-tenant app registration), both environments; ADR-010 §2.2 — "Both environments use the **same** directory … for every customer workspace"; every customer user is provisioned as a **B2B guest inside Raffa's own Entra directory** (`GraphGuestProvisioner.cs`), never federated with the customer's own IdP; no SAML anywhere in the codebase; `grep` across every `backend/src/*/*.csproj` finds only Azure SDKs, Microsoft Graph, Microsoft.Identity.Web, EF Core/Npgsql/pgvector, OpenXml and Docnet — **no integration SDK of any kind** (no Slack, Teams, DocuSign, SharePoint, Jira, ServiceNow, HRIS, ERP); the domain event catalogue (spec §13.2, Appendix B) is documented but **not emitted** — no message bus topic, no webhook, no subscriber; `GET /api/audit` exists with no UI (ADR-001 w16 cl. 5); no `production` environment exists at all (ADR-016 — only `dev` and `demo`) |

## 0. The direct answer, for the record

**No.** A prospect who wants Raffa inside their own infrastructure today
cannot use their own Microsoft or Google identity, cannot have their IT
department manage Raffa users from their own directory, cannot get data in
or out through any system they already run, and cannot point their security
team at a production environment with a track record — because none of the
four exists. This file is the backlog to close that gap, ranked by what
blocks a real enterprise or mid-market sale first.

## 1. Binding instructions

1. **Identity federation is the head of this file.** It is the one gap that
   makes "put Raffa in our infra" a literal no today. Nothing here is queued
   ahead of SR-01/SR-02 unless the council finds a hard technical reason.
2. **Every new external surface goes through the AI Gateway / adapter
   pattern already proven** (ADR-004's role-based gateway, `Raffa.Benchmark`'s
   provider adapter): an integration is an adapter behind an interface the
   domain modules call, never a direct SDK call from `Raffa.Api` or
   `Raffa.Worker`. No exceptions — this is how Raffa has stayed swappable
   so far and how it stays swappable when a customer asks for a connector
   we did not plan.
3. **RLS and the no-cross-tenant rule are not renegotiated for any
   integration** (ADR-009, ADR-011). A connector reads and writes inside one
   tenant's scope; a webhook payload is tenant-scoped at the source, never
   fanned out unfiltered.
4. **A production environment is a prerequisite for the first paying
   customer outside the founders**, not a nice-to-have alongside these
   features. SR-06 is `must` for the same reason SR-01 is.
5. **No feature in this file promises a compliance certificate.** SOC 2 and
   ISO 27001 are audits performed by a third party against controls that
   must already exist; this file builds the controls (SR-08). The
   certificate itself is a business decision (cost, timeline), tracked
   outside Helix.
6. **Out of scope (do not queue):** a full self-serve billing engine (a
   companion file will cover pricing/billing when ICP and pricing are
   decided — see the competitive review's open questions); a marketplace
   listing (AWS/Azure Marketplace) before there is a priced SKU to list;
   building bespoke connectors for a single named prospect before the
   general adapter seam (SR-04) exists.

## 2. Items (priority order)

### SR-01 — Customer IdP federation: stop minting guests in Raffa's directory (must)

- **Today (evidence):** `infra/modules/identity/main.tf:74,148` —
  `sign_in_audience = "AzureADMyOrg"`; ADR-010 §2.2 confirms every
  workspace's users live as guests in the **one** Entra directory Raffa
  owns. `tid` in the token is Raffa's own directory GUID, never read for
  any purpose (`ADR-010:218` — "No Raffa code reads `tid`"). A customer's IT
  admin has no control over who exists in Raffa; every account is
  provisioned by `GraphGuestProvisioner.cs` through `User.Invite.All`.
- **What it should be:** Raffa's Entra app registration becomes
  **multi-tenant** (`AzureADMultipleOrgs` or a v2 "any organizational
  directory" audience), so a customer's own Entra tenant can consent to
  Raffa and its users sign in with their own corporate identity — no guest
  invite, no second account. `tid` becomes the signal that distinguishes
  **which customer directory** a sign-in came from, feeding workspace
  resolution instead of the current membership-table-only lookup. Existing
  guest-provisioned workspaces keep working (migration path, not a
  breaking change on day one).
- **Acceptance:** a second, throwaway Entra tenant (test tenant) can
  consent to the Raffa app and its user signs in without ever receiving a
  B2B invite email; `tid` correctly separates two customer directories in
  the same `dev` environment; the existing guest flow still works for
  workspaces that have not migrated.
- **Seats (hint):** security-architect (owns this call — ADR-010's own
  author), cloud-architect (app registration change, region implications),
  software-architect (workspace resolution from `tid`).

### SR-02 — SAML 2.0 and SCIM for enterprise customers who are not on Entra (must)

- **What it should be:** a customer on Okta, Google Workspace, OneLogin or
  any SAML 2.0 IdP can federate without being on Microsoft Entra at all.
  SCIM 2.0 (user provisioning/deprovisioning pushed from the customer's
  IdP) covers the case Vertice already supports and Raffa's review flagged
  as a gap: an employee leaves the customer company, their Raffa access
  must be revoked without an Admin remembering to do it by hand.
- **Acceptance:** a SAML test IdP (e.g. a free Okta developer org)
  federates a sign-in; deactivating a user in that IdP revokes their Raffa
  session/access within the SCIM sync window.
- **Seats (hint):** security-architect, cloud-architect, software-architect.

### SR-03 — Production environment (must)

- **Today (evidence):** ADR-016 defines only `dev` (auto-deploy on merge)
  and `demo` (tagged promotion). No `production` Terraform workspace, no
  HA topology, no tested backup/restore, no status page.
- **What it should be:** a third environment, promoted from `demo` the same
  way `demo` is promoted from `dev` (tag + GitHub Environment approval,
  ADR-016's own pattern extended, not reinvented). Minimum bar before the
  first paying customer: automated daily Postgres backups with a **tested**
  point-in-time restore (spec §15.4 — "periodic restore tests" is not
  optional), object storage versioning, a documented RPO/RTO, and a public
  status page (even a static one) separate from the app itself.
- **Acceptance:** a restore drill on `dev` data restores into a scratch
  environment and Ask answers correctly from the restored data; the
  `production` Terraform workspace applies cleanly from a fresh `demo`
  promotion.
- **Seats (hint):** cloud-architect, delivery-manager, security-architect.

### SR-04 — Integration adapter seam + first three connectors (must)

- **What it should be:** one `Raffa.Integrations` module with a
  provider-agnostic seam (mirroring `Raffa.Benchmark`'s adapter pattern),
  and three concrete adapters that unblock the onboarding friction the
  competitive review flagged (Vertice is integration-first and criticised
  for it; Raffa's upload-first pitch needs at least a forwarding path to
  stay upload-first while removing manual drag-and-drop as the only way
  in):
  1. **Email ingestion**: a per-workspace inbound address (`workspace-slug
     @intake.raffa.ai` or similar) that runs the same admission gate as a
     manual upload — forward a contract or a supplier quote, it lands in
     Documents or Quote check exactly as if dropped by hand.
  2. **SharePoint / OneDrive connector**, read-only, per the spec's own P2
     priority (§13.4): point Raffa at a folder, new files run through the
     same admission gate on a schedule.
  3. **Google Drive connector**, same shape, same priority tier as OneDrive.
  Every connector produces a `Document` row through the existing pipeline —
  no parallel ingestion path, no special-cased extraction.
- **Acceptance:** a contract forwarded by email on `dev` appears in
  Documents within the normal processing time; a file dropped in a
  connected SharePoint folder appears within one polling cycle; a
  non-contract forwarded by email is rejected by the same admission gate
  that rejects a manually uploaded one, same audit trail.
- **Seats (hint):** software-architect, cloud-architect (Graph/Drive API,
  secrets), security-architect (scoped read-only grants, not full mailbox
  or drive access).

### SR-05 — Domain events actually emitted (should)

- **Today (evidence):** spec §13.2 and Appendix B document nine domain
  events (`document.uploaded`, `contract.created`, `renewal.approaching`,
  `savings.opportunity.created`, etc.); none is published anywhere in the
  codebase today — no Service Bus topic beyond `extraction-events`
  (ADR-027's own worker pipeline), no webhook registry, no subscriber.
- **What it should be:** the documented events are actually published on a
  Service Bus topic (reusing the infra already proven for
  `extraction-events`), and a per-workspace **outbound webhook**
  registration (Admin-configured URL + shared secret, signed payloads,
  tenant-scoped, retried with backoff) lets a customer's own systems react
  to `renewal.approaching`, `savings.opportunity.created` and
  `document.processed` without polling the API. This is the integration
  primitive that makes every future connector (customer's own Slack, their
  own ticketing, their own BI) a webhook subscription instead of a bespoke
  Raffa feature.
- **Acceptance:** registering a webhook URL on `dev` and forcing a renewal
  into the 90-day window delivers a signed `renewal.approaching` payload to
  that URL within one scheduler tick; a webhook that 500s three times is
  disabled with an Admin-visible reason, never retried forever.
- **Seats (hint):** software-architect, security-architect (payload
  signing, no tenant data leaking to a misconfigured URL), cloud-architect.

### SR-06 — Public API documentation and a scoped API key (should)

- **Today:** the OpenAPI contract exists and drives the generated web
  client (ADR-012), but there is no customer-facing API documentation
  portal and no way for a customer's own engineer to call Raffa with
  anything other than an interactive browser session.
- **What it should be:** a published API reference (generated from the
  existing OpenAPI spec — no new authoring surface) and per-workspace API
  keys (Admin-issued, scoped read-only or read-write, revocable, audited
  like every other action) as an alternative to the interactive token for
  server-to-server calls. This is also the foundation SR-05's webhook
  Admin UI and any future customer-built integration stand on.
- **Acceptance:** an Admin issues an API key on `dev`; a `curl` call with
  that key against `GET /api/contracts` succeeds; a revoked key fails
  immediately; the key never appears again after issuance (shown once,
  like a cloud provider's own secret UX).
- **Seats (hint):** software-architect, security-architect, client-architect
  (the docs portal, can reuse the generated OpenAPI JSON).

### SR-07 — Audit log, visible (should)

- **Today (evidence):** `GET /api/audit` exists, Admin-only, capped at 200
  rows, **no UI** (ADR-001 w16 cl. 5 explicitly ruled out a web audit
  screen for that wave).
- **What it should be:** a Workspace & members → Audit screen for Admins:
  filterable by actor, action, date range, paginated past the current
  200-row cap. This is table-stakes for any customer whose security team
  asks "who deleted this contract" — today the honest answer is "check the
  API by hand."
- **Acceptance:** deleting a document and inviting a member both appear in
  the Admin's audit screen within one page load, correctly attributed.
- **Seats (hint):** client-architect, ux-ui-designer, software-architect.

### SR-08 — Trust package: security page, DPA, tenant export (should)

- **Today:** no security/privacy page, no standard DPA, no self-service
  tenant data export (spec §14.3 requires export **and** deletion at
  tenant level; only deletion exists — `Raffa.Documents.Contracts`' delete-
  all cascades, nothing exports).
- **What it should be:** a public `/security` or `/trust` page stating
  plainly what is already true (Azure North Europe, RLS tenant isolation,
  no training on customer content, AI Gateway logging) without overclaiming
  what is not (no SOC 2 yet — say so, do not imply it); a standard DPA
  template legal can hand a prospect same-day; a tenant export endpoint
  (`GET /api/workspaces/{id}/export` — structured data + document blobs as
  a downloadable archive, Admin-only, audited) so "can we get our data back
  if we leave" has a real answer.
- **Acceptance:** an Admin exports a workspace on `dev` and receives an
  archive containing every validated contract's structured facts and
  original file; the `/trust` page makes no claim the codebase does not
  back.
- **Seats (hint):** product-owner (copy, what to claim), security-architect
  (what is actually true), software-architect (export endpoint).

### SR-09 — Italian and German UI (could)

- **Today:** no i18n library; UI chrome is hard-coded English
  (`web/package.json` has no i18n dependency; `index.html lang="en"`).
  Already flagged in the competitive review as inconsistent with the
  IT/DE/CH/AT target set in the workspace picker.
- **What it should be:** UI chrome (labels, buttons, empty states) in
  Italian and German alongside English, selected from the workspace's
  country or a per-user preference. Ask's own language-matching (IT/EN)
  already exists (`QuestionLanguage.cs`) and is unaffected — this item is
  the surrounding chrome, not the AI layer.
- **Acceptance:** a workspace created with country `IT` shows Italian
  chrome by default; a user can override to English; no string is left
  hard-coded English in the screens the pilot walk touches.
- **Seats (hint):** client-architect, ux-ui-designer, product-owner
  (translation review — must be accurate, not machine-literal).

### SR-10 — Per-tenant usage and cost visibility (could)

- **Today:** spec §15.1/§15.2 call for tenant economics telemetry
  (storage, documents, AI calls, benchmark calls, processing cost per
  tenant); nothing customer-facing exists. This becomes a prerequisite the
  moment any pricing model tied to usage (contracts, pages, AI queries) is
  decided — flagged here so it is not forgotten when that decision lands.
- **What it should be:** an Admin-visible usage panel (documents processed
  this period, AI queries, storage) — internal cost metrics already exist
  per the spec's observability requirements; this exposes a customer-safe
  subset of the same data.
- **Acceptance:** an Admin sees a usage summary for their workspace that
  matches the operator-side telemetry for the same period.
- **Seats (hint):** product-owner, software-architect, client-architect.

## 3. Order constraints

- SR-01 before SR-02 (federation model settled before adding a second
  protocol on top of it).
- SR-03 can run fully in parallel — it touches infra, not the app.
- SR-04 does not depend on SR-01/SR-02 (a workspace's own users can
  configure a connector regardless of how they signed in) but SR-04's
  connectors are pointless to sell into a customer's infra without SR-01,
  so ship order should keep them close together in the same wave or the
  next.
- SR-05 depends on nothing here but is much more valuable once SR-06
  exists (a webhook without a documented API around it is half a feature).
- SR-07 and SR-08 can run in parallel with anything.
- If the wave cap is reached: SR-01, SR-02, SR-03 are never pushed past the
  next wave. SR-09/SR-10 are the first to move to the following wave.

## 4. Cancels / touches

- Cancels nothing. Extends ADR-010 (federation model — the security-
  architect who owns ADR-010 owns SR-01/SR-02's decision). Extends ADR-016
  (a third environment). Touches spec §13.4's integration priority table
  (P2 SharePoint/Drive/Outlook move into this wave instead of "later";
  Outlook/Gmail as *outbound* mail stays out of this file's scope — SR-04
  is inbound ingestion only).
- Does not touch `inputs/next/2026-09-25-customer-success-concierge.md`;
  the two files are independent and can be worked in either order, though
  SR-01 (identity) has no dependency on CS-anything and could run first
  without blocking the concierge wave at all.

## 5. Acceptance walk (for `docs/waves/<wave>-acceptance.md`)

1. A throwaway external Entra tenant's user signs into a `dev` workspace
   without ever receiving a guest invite email.
2. That same workspace's Admin deactivates the user in their own IdP; the
   user's Raffa access is gone within the sync window.
3. Forward a contract PDF to the workspace's intake email address; it
   appears in Documents, processes, and is askable — same as a manual
   upload, same admission-gate rejection for a non-contract.
4. Register a webhook URL; force a contract into the 90-day renewal
   window; the URL receives a signed `renewal.approaching` payload.
5. Issue an API key; call `GET /api/contracts` with `curl`; revoke it;
   the next call fails.
6. Open the new Audit screen as Admin; delete a document; see the row.
7. Export the workspace; open the archive; the structured facts and the
   original PDF for one validated contract are both present.
8. Run a restore drill from the latest `production`-candidate backup into
   a scratch environment; Ask answers correctly from the restored data.
