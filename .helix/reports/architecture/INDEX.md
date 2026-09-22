# Raffa V1 — Architecture Decision Records (INDEX)

Accepted ADRs promoted at council-close (2026-09-01/02), plus ADR-017 (OCR in V1). Source drafts live under
`reports/architecture/draft/<seat>/`; accepted copies below carry the canonical `ADR-NNN` number and
`Status: accepted`. Supporting (non-ADR) lane artefacts are listed separately and remain under their
seat's draft folder.

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-001 | V1 scope R0–R4 | product-owner | User-visible wave ladder; §1.2 non-goals out of scope; R3/R4 on fixture adapter, never a paid API for first `demo`. |
| ADR-002 | .NET solution shape | software-architect | Modular monolith: one project per bounded context + shared kernel + thin API/worker hosts. |
| ADR-003 | Relational store | software-architect | PostgreSQL Flexible Server + pgvector via EF Core/npgsql; RLS tenancy; single system of record. |
| ADR-004 | Foundry model roles | software-architect | Role-split (ocr/classify/extract/embed/answer) behind AI Gateway, config-selected cheapest IDs. |
| ADR-005 | Azure services + SKUs | cloud-architect | Container Apps (consumption) + Postgres Burstable + Storage + Service Bus Standard + Key Vault + Entra ID Free. |
| ADR-006 | Region | cloud-architect | North Europe (`northeurope`) for both `dev` and `demo` (West Europe ineligible for new customers on this tenant). |
| ADR-007 | Terraform layout | cloud-architect | Reusable modules + two env roots; remote state per env; no secrets in source. |
| ADR-008 | Foundry account shape | cloud-architect | One hub, two projects (`dev`/`demo`), one pay-as-you-go AI services account. |
| ADR-009 | Tenancy / RLS | security-architect | Postgres RLS on every tenant table; app passes `tenant_id`; RLS is the non-bypassable backstop. |
| ADR-010 | Entra ID / OIDC | security-architect | Per-env pair (public client + API registration), Authorization Code + PKCE, four registrations total. |
| ADR-011 | Key Vault + RAG isolation | security-architect | Per-env Key Vault + managed identity + OIDC federation; authz-before-retrieval; no-training; input-hash logging. |
| ADR-012 | Web stack | client-architect | React + TypeScript + Vite SPA, OIDC PKCE, static bundle on Static Web Apps free tier. |
| ADR-013 | Mobile stack | client-architect | React Native (Expo) + TypeScript, non-gating lane, no store release for R0–R4. |
| ADR-014 | Git flow | delivery-manager | Trunk-based, protected `main`, PR required; `main`→`dev` auto-deploy; tag + env approval for `demo`. |
| ADR-015 | CI → Azure auth | delivery-manager | OIDC federated credentials, per-env least-privilege service principals; no stored secrets. |
| ADR-016 | Promotion dev→demo | delivery-manager | Tag + `demo` GitHub Environment with required reviewers; code/artifacts only, never data. |
| ADR-017 | OCR in V1 | software-architect | Hybrid native-text + Azure AI Document Intelligence (`prebuilt-read`/`prebuilt-layout`) behind the AI Gateway; full document; not deferred. |

## Supporting artefacts (not ADRs)

| File (draft) | Seat | Purpose |
| --- | --- | --- |
| `draft/product-owner/scope-notes.md` | product-owner | Personas, non-goals, day-1 promise, acceptance hooks (seed for user stories). |
| `draft/software-architect/module-map.md` | software-architect | Module boundaries, entity ownership, dependency direction, worker responsibilities. |
| `draft/client-architect/api-consumption.md` | client-architect | One versioned OpenAPI contract → one generated TS client; OIDC; config-not-code. |
| `draft/delivery-manager/wave-calendar.md` | delivery-manager | Wave order + calendar (S0 → R0–R4, ~18 weeks) and environment plan. |

## Required-ADR coverage check

All fourteen required topics from the council-protocol brief are covered by ADR-001 through ADR-016
(scope/R0–R4, git flow, Azure SKUs, region, Terraform layout, .NET solution, web, mobile, Foundry
models, CI→Azure auth, promotion, relational store, tenancy/RLS, Key Vault+RAG). ADR-017 closes the
CQ-008 sub-item "OCR vs native document parse": OCR is in V1, not deferred. No required topic
remains unaddressed.

## Web delta (wave 6+, appended 2026-09-05)

ADR-001…017 are unchanged. New accepted ADRs from `raffa-web-design`:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-018 | Web information architecture | ux-ui-designer | Left-rail Day-1 sitemap; Admin vs Procurement; cites `inputs/design/prototypes/ia.md`. |
| ADR-019 | Web design system | ux-ui-designer | Adopt Claude Design Modernist export verbatim; no forked tokens. |
| ADR-020 | Web screen inventory | ux-ui-designer | 1:1 §16/§20 → ten screens in `prototypes/screens.md` + `day1-demo.html`. |

## Schema-apply (wave 9 / e09, appended 2026-09-05)

ADR-001…020 are unchanged. New accepted ADR from `raffa-schema-design`:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-021 | Schema apply on Azure Postgres | software-architect | CI applies checked-in idempotent EF SQL after container update; no `MigrateAsync` in the API; Terraform injects Savings/Quotes connection strings. |

## Demo-readiness (wave 10 / e10, appended 2026-09-05)

ADR-001…021 are unchanged. New accepted ADR from `raffa-readiness-design`:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-022 | Day-1 demo auth + fixture seed | security-architect | First `demo-v*` may keep `X-Tenant-Id` on the API; savings numbers come from a seeded fixture on `raffa_demo`; ADR-010 remains the post-Day-1 host target. |

## Ask savings copilot (wave 12 / e12, appended 2026-09-08)

ADR-001, 004, 011, 018, 020 keep their original Decision and gain an
**amendment footer**. New accepted ADR from `raffa-ask-design`:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-023 | Ask Raffa savings copilot | product-owner + software-architect | Foundry-hosted copilot over tenant RAG + `IBenchmarkService` (fixture now); narrates P25–P75 / negotiation numbers; no legal advice; rich `/ask` reply. |

## Ask Raffa V2 (wave 13 / e13, appended 2026-09-08)

ADR-001…022 keep their original Decision; ADR-001, 004, 011, 018, 020 gain
an **epic-13 amendment footer** (superseding their epic-12 footers).
**ADR-023 is superseded by ADR-024** (HITL 2026-09-08, `inputs/requirements.md`
§0 D4; epic-12 / e12 never launched). New accepted ADR from
`raffa-ask-process.yaml` (Ask V2):

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-024 | Ask Raffa V2 | product-owner + software-architect + security-architect + ux-ui-designer | Documents-only intake with an admission gate before persistence (non-contracts refused, never stored); server-side conversations under RLS; three sources of truth (validated contracts, market-intelligence feed with its own index — mock now, API later —, capability catalog); structured no-tools `answer` role with grounding + numeric guards; deterministic strategies (contract vs market, renewal strategy, portfolio criticality); V2 IA with `inputs/design/prototypes/Raffa V2 Prototype.html` (unpacked `raffa-v2/`) as the pixel reference. |

## Live Foundry (appended 2026-09-09)

ADR-001…024 keep their original Decision. ADR-004, ADR-005, ADR-008 and
ADR-017 gain an **amendment footer** dated 2026-09-09: the shared Azure AI
Services account `aisvc-raffa` (kind `AIServices`, account-native Foundry
projects, no hub), its per-environment projects, model deployments and RBAC
are created by Terraform (`infra/modules/foundry`; owned by the `dev` root,
attached by `demo`; two-phase wiring behind `ai_gateway_wired`); confirmed
per-environment model ids (dev: gpt-5.4-nano + text-embedding-3-small; demo:
gpt-5.4 / gpt-5.4-nano + text-embedding-3-large at 1536 dimensions); OCR is
Document Intelligence `prebuilt-read` for every PDF and image with the page
map from `pages[].spans`, native parsing only for DOCX/XLSX. No new ADR.

## Workspace is real (wave w14, appended 2026-09-10)

ADR-001…024 keep their original Decision and their existing footers.
**ADR-001 gains a w14 amendment footer** (product-owner): the invitation
lifecycle (token → accept → sign in → join that workspace → Admin removal →
re-invite) is in scope at R0 under spec §16/§20; a **mail transport is
deferred, not a §1.2 non-goal** (§13.4 and §16.1 rank email **P1**, and
`percorso-pilota-v1.md` §2/§5/§7 exclude it from the pilot while keeping it in
the full V1 spec), so w14 delivers a copyable single-use accept link behind an
`IInvitationMailer` seam; an unaccepted invitation grants nothing; the UI must
not assert a fact the system does not hold; the workspace profile is
`name` + `industry` + `country` as closed lists with currency and business
region **derived from country**, and a workspace currency never overrides a
contract's own extracted currency; the workspace-domain invite restriction is
deferred to the wave that lands ADR-010.

New ADRs written at this wave's table are listed by the seats that author them
(next free number at the time of this row: **ADR-025**).

**ADR-003 gains a w14 amendment footer** (software-architect, owner): three
nullable columns on `workspace` (`industry`, `country`, `currency` — currency
derived from country, never typed, and never overriding a contract's own
extracted currency); one new ordinary tenant-scoped table
`workspace_invitation` whose RLS policy ships in the same migration as the
table; the record that `workspace_user` gains one `SELECT`-only identity
policy owned by ADR-009; and the migration mechanics for the three migrations
that regenerate a single byte-compared `.sql` script. ADR-003's Decision
outcome is unchanged.

New ADR from the w14 table:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-026 | Workspace discovery, roster and invitations — API contract, data model, module composition | software-architect (+ security-architect co-sign) | `GET /api/workspaces` is two-phase (identity discovery → per-tenant scoped projection) and takes no tenant input; inclusion is gated on a live membership row, so removal takes effect on the next request; the validated-contract count is the definition already in `PortfolioQueryService` and is composed in the host, never by widening a module's dependencies; the roster is live memberships ∪ live invitations with a derived `Invited`/`Active` status, never a scan of `workspace_user`; `workspace_invitation` is an ordinary tenant table and the token `{tenantId}.{secret}` removes the last unscoped read; `IInvitationMailer` + `mailDelivered` make the wave transport-independent. |

ADR-026 is the **contract and data-model** half of this wave; **ADR-025**
(security-architect) is the **authorization, token-strength, RLS-policy and
audit** half. They are written to be read together and neither restates the
other. Where they touch the same mechanism, ADR-025 governs.

**ADR-005 and ADR-006 gain w14 amendment footers** (cloud-architect, owner of
both). Their Decision outcomes are unchanged and **no new ADR is written by
this seat**.

- **ADR-005 — mail transport: decided, not applied.** Wave w14's Azure delta
  is **zero**: no new resource, no SKU, no Terraform change, no runtime
  configuration key, no Key Vault secret, in either environment, and no new
  fixed monthly cost line. The footer records for the wave that *does* ship
  invitation mail: Azure Communication Services Email with an Azure Managed
  Domain, one resource per environment, global types with no SKU and no idle
  charge (metered per message, order $0.00025/email — for the infra cost
  researcher to confirm at retail); the rejected alternatives with their
  reasons; the module/wiring/secret shape; the `Invitations__*` config keys;
  and the per-environment `invitation_mail_enabled` flag mirroring
  `ai_gateway_wired`. Two contract properties keep w14's delta at zero and are
  normative for the decomposition: **`acceptUrl` is site-relative in w14**
  (an absolute URL would need an API env var, which `backend.yml` cannot
  deliver — it deploys `--image` only — hence Terraform plus an
  operator-confirmed HCP apply mid-wave), and **no server-held invitation
  signing key in w14** (ADR-026 §D4's random-and-hashed token needs none; this
  seat's lane proposal of `Invitations__TokenSigningKey` is withdrawn).
- **ADR-006 — global-only resource types are not a second region.** One rule,
  two cases, no task: a resource type that takes no `location` carries
  `data_location = "Europe"`; every regional resource keeps the `northeurope`
  pin. This records the **Static Web Apps** deviation that has been in force
  since the first apply (`infra/modules/staticwebapp/main.tf:6-9`) but
  contradicted ADR-006 `:55`, and pre-decides the ACS case for the transport
  wave. `northeurope` is not re-opened.

**ADR-009, ADR-010, ADR-011 and ADR-022 gain w14 amendment footers**
(security-architect, owner of all four). Every Decision outcome is unchanged and
every `Status:` stays `accepted`; each footer **narrows** the posture it amends
and none weakens RLS or authorization-before-retrieval.

- **ADR-009 — discovery, scope discipline, bootstrap.** One new
  identity-keyed policy, `FOR SELECT` only and fail-closed, on `workspace_user`
  alone; discovery is gated on a **live membership row**, never on user
  existence; the identity GUC is set with `SELECT set_config(…, @p, false)` and
  never by copying the interpolating `SET` helper; **two named exceptions** to
  "one scope per request" (multi-scope discovery, and the accept path's scope
  from a caller-supplied tenant id, each with its bounding rules) and no third;
  a scope change takes effect only on a connection opened after it; bootstrap is
  one scope and one save; verify-then-scope-then-read, with **404 for a
  non-member** because 403 is a tenant-existence oracle;
  `workspace_invitation` is an ordinary tenant table whose policy ships in the
  same migration. `BYPASSRLS` stays forbidden.
- **ADR-010 — the token carries identity only.** Tenant and role are resolved
  from the database on every request; a `tenant_id` or `roles` claim is never the
  authorization source. This **amends a shipped contract** —
  `WorkspacePrincipalAuthorization` reads both from claims today — at the single
  seam its own comment names, so W15 cannot ship a stale-authorization window or
  a last-Admin bypass. No w14 task edits it. Also records that the Entra scopes
  are live and hardcoded in `web.yml:204-205`, an identity-plane rename risk that
  fails at token acquisition rather than at deploy.
- **ADR-011 — no new secret; authz-before-retrieval strengthened.** The
  invitation token needs **no Key Vault entry** (random-and-hashed, not signed),
  which is what keeps w14's infrastructure delta at zero; a later mail transport
  still takes a per-env vault secret via managed identity; the tenant used for
  retrieval becomes a *verified membership* fact rather than a client-asserted
  header; nine new audit actions with no schema change, and the token, its hash
  and raw identity headers are never written to an audit row or a log.
- **ADR-022 — the interim posture is narrowed, not extended.** `X-Role` /
  `X-Workspace-Role` are **demoted now** to UI shaping only (zero call sites in
  `web/src`, so no client changes); membership beats the header in both
  directions; `X-Tenant-Id` is not an input to a membership route; `X-User-Id`
  is read in one seam; and each interim mechanism gets a named retirement item.

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-025 | Workspace membership authorization and the invitation lifecycle | security-architect (+ six co-deciders) | An invitation is an **offer, not a grant** — invite writes `workspace_user` with no membership plus the invitation row, and membership lands only at accept, which keeps "sign-in never provisions a user" intact; the invite endpoint becomes Admin-gated, closing a path that today lets anyone who knows a tenant GUID grant themselves `Admin`; a 256-bit CSPRNG token stored only as a hash, 7-day expiry, single use, in a URL **fragment** and an `X-Invitation-Token` header — never a query string, a log, an audit row or a client store, and needing **no server-held key**; one rejection contract where non-membership is always **404** and never 403; removal is immediate because nothing caches authorization, revokes the email's live invitations in the same transaction, and can never remove the last Admin. |

ADR-025 is the **authorization, token-strength, RLS-policy and audit** half of
this wave; **ADR-026** (software-architect) is the **contract, data-model and
composition** half. They are written to be read together and neither restates the
other. **Where they touch the same mechanism, ADR-025 governs.**

**ADR-012 and ADR-018 gain w14 amendment footers** (client-architect, owner of
both). Both Decision outcomes are unchanged, both keep `Status: accepted`, and
**no new ADR is written by this seat** — the client half of this wave is a
provenance rule and one route, not a new decision surface.

- **ADR-012 — provenance: a client store never stands in for a missing GET.**
  ADR-012's **first** amendment footer. `:53` already forbade divergent DTOs by
  *shape*; this adds the *provenance* half, because four client stores exist
  only because an endpoint did not: the known-workspaces `localStorage` list,
  the members `sessionStorage` roster and the `sessionStorage` role mirror are
  **deleted**, and the current-workspace key is **demoted to a hint**
  revalidated against the server list on every mount (so a removed member
  loses the workspace on their next mount with no endpoint, no polling and no
  cache to invalidate). A failed read renders ADR-018 `:117`'s error state,
  never a stale local answer, so **there is no "fall back to the cache" task**.
  Also: the four binding constraints the client generator imposes on every new
  OpenAPI entry (`operationId` or it throws; flat scalar rows, no `$ref`;
  enums → literal unions; only `responses` are parsed, so bodies, path
  parameters and headers stay hand-written) and the single-writer rule that
  follows from `schema.ts` being regenerated wholesale; the SPA sends
  `X-Tenant-Id` + `X-User-Id` and **never a role header**, and sends **no
  `X-Tenant-Id` on a route that already carries `{tenantId}`**; the client's
  role is presentation-only, server-derived and **defaults to least privilege,
  never `admin`**; the invitation token lives **in memory for one mount** and
  is never put in a client store nor in MSAL's `state` (which would route it
  through the identity provider's query string and logs); and once
  `contractCount` is a server field, **no screen computes a count from the
  client-side status predicate**.
- **ADR-018 — one public route joins the V2 map.** `/invite/accept` is added
  to the locked route map: reachable **signed out and with no workspace**,
  rendered **outside `AppShell`**, carrying the token in a URL **fragment**.
  This footer **adds to the epic-13 V2 footer and does not supersede it**. It
  forces `BrowserRouter` up from `WorkspaceShellApp` into `App.tsx` (no router
  exists in the invitee's state), which is why NW-03's async gate and NW-58's
  route hoist are one task or strictly sequenced. It **resolves** the ADR-018
  Assumption that Entra claims would gate `/workspace/members` client-side —
  the assumption's own stated alternative, a **server-driven** role, is what
  ships — and records that `/workspace/members` gains **two** destructive
  affordances (revoke an `Invited` row, remove an `Active` one) with the
  last-Admin guard as a disabled control with a visible reason.

**ADR-018, ADR-019 and ADR-020 gain w14 amendment footers** (ux-ui-designer,
owner of all three). All three Decision outcomes are unchanged, all three keep
`Status: accepted`, and **no new ADR is written by this seat** — w14 adds
screens and states to an existing system, not a new design surface. ADR-018
carries **two** w14 footers: client-architect's (the route) and this seat's (the
states that route creates); the second **adds to** the first and supersedes
nothing.

- **ADR-019 — a status vocabulary for people, and one rule generalised.** The
  Semantic mapping gains three rows for members and invitations, of which only
  `Expired → .tag-outline` is new — and it is **derived, not invented**, since
  `.tag-outline` is already this system's "needs your decision" treatment
  (`needs_review`, `Review · N%`). The disabled-CTA-with-a-visible-reason rule
  is recorded as a **system rule about gated destructive actions**, not a rule
  about the one Review button it was written for. **No new token and no new
  component**: `.micro-meta` and `.hint` are noted as implemented utilities
  rather than catalogue entries, so the locked catalogue is not extended, and
  destructive confirmation is **inline in the row** because `--shadow-*` is
  "dialogs only (avoid in-app)". One role vocabulary spans two key-space maps
  that must not diverge, and an **unmodelled role's label passes through**:
  permissions degrade to least privilege, labels do not degrade at all. The
  three confidence rows are deliberately untouched and **not** ratified — they
  are already stale against a HITL ruling of the same date, which is NW-65 in
  W17.
- **ADR-020 — screens 1 and 10 gain their real states; screen 11 is new.**
  Numbered in the **ADR-024 V2 numbering**, not the body's R0 numbering, which
  the footer states in its first lines because the same file numbers screens
  twice. Screen 1: the export's three-field create form (Company · Industry ·
  Country, options verbatim, `Other` as the "not specified" case), currency
  **shown and never asked**, a pick-row segment list that **drops** null
  segments and ships zero/singular forms, a role tag that renders the **server's**
  role instead of a hardcoded "Workspace Admin", and a state inventory that adds
  loading/error/resolving plus the previously unrecorded interstitial. Screen 10:
  the invite result as the server's `mailDelivered` fact in **two** strings whose
  `false` copy is written to be true for **both** of its causes (with the
  delivery-failure copy pre-decided for the transport wave so it is not
  re-opened); the workspace-domain rule **demoted from a block to a warning**;
  `Active`/`Invited`/`Expired`; **two** destructive affordances with different
  consequence copy, because revoking an invitation never removed anyone's access;
  the last-Admin disabled control; the member row keeping the **email** as its
  primary line and the **D8** role summaries rather than the export's superseded
  ones. **Screen 11** is the invitation accept flow in ten states, of which
  "open your invitation link again" is a **normal outcome, not an error**, and
  the wrong-account state **cannot echo the invited address**. Six design
  exports are recorded as owed, none blocking.
- **ADR-018 — the Procurement roster state, and where the states contract
  binds.** Records that `:107-108`'s "read-only" reading is the one in force and
  that the V2 export **contradicts its own IA doc** by hiding the table
  outright — a contradiction inside the design oracle, resolved in favour of the
  ADR, and buildable for the first time because NW-04 ships the endpoint whose
  absence generalised that copy. `Request access` becomes a real `mailto:` to
  the workspace's Admins rather than an inert button. The IA-level
  empty/error/loading contract is bound to the three new read surfaces, with the
  create form recorded as screen 1's **empty state** and "open your link again"
  recorded as a normal outcome. Screen 1's undocumented interstitial is named as
  a divergence; the routing decision on it stays client-architect's.

**ADR-014 and ADR-016 gain w14 amendment footers** (delivery-manager, owner of
both). Both Decision outcomes are unchanged, both keep `Status: accepted`, and
**no new ADR is written by this seat** — the delivery half of this wave is a
wave-base rule and a promotion ordering, not a new mechanism. **ADR-015 records
`none`**: the seed and backfill jobs reuse the existing per-environment deploy
service principal and its existing Key Vault Secrets User role — no new OIDC
subject claim, no new federated credential, no new stored secret.

- **ADR-014 — the wave base is a reconciliation, and it is proven green before
  task 1.** The body fixed "every task branch is created from `main`" and
  stopped there. The footer adds what the next-wave process actually needs:
  the Helix artefact branch diverges from `main` **by construction** (`.helix/`
  lives in the repo), so a wave base is produced by **merging**, never by
  branching afresh — neither side is a subset of the other. **`integration` is
  named for the first time** (the word does not occur in the body) as the
  wave-scoped branch that reaches `main` through one PR. A wave base is
  **proven green before its first task**: the rename sweep across
  `.github/`, `infra/`, `scripts/` and `backend/scripts/`, including **both**
  ADR-021 schema arrays and the brand-asserting `check_demo_swa_config.py`;
  one throwaway `dev` deploy; and **one interactive sign-in**, because a
  renamed Entra scope fails at token acquisition in the browser *after* CI is
  green, and no build-level check can see it. Finally: a feature wave does not
  edit CI YAML its wave plan did not name.
- **ADR-016 — w14 adds no per-environment key, and that is a property of the
  contract, not an accident.** The zero Terraform delta cloud-architect confirms
  holds **because** `acceptUrl` is site-relative: an absolute URL would force an
  API env var, and `backend.yml` deploys `--image` only, so that key could
  arrive only through Terraform plus an operator-confirmed HCP apply — a
  human-gated barrier introduced mid-wave through a *contract* rather than
  through infrastructure. The two shortcuts (`--set-env-vars`, or folding a
  value into the one `dynamic "env"` block) are named as **drift**, because the
  next HCP apply reverts anything CI sets behind Terraform's back. Also:
  **seeds and backfills are data-plane acts and are never promoted** — the
  `demo` membership rows are produced by running the job against `demo`, never
  by copying `dev` rows; a future mail transport's promotion ordering (apply
  `CURRENT` → `promote-backend` → `promote-web`) with the reason it is a HITL
  gate and not a `depends_on`; the **standing** `check_demo_swa_config.py`
  check on every promotion, with the warning that it lives in the **repo-root**
  `scripts/` and not `.helix/scripts/`; and the w14 sequence — acceptance on
  deployed `dev` **before** the `demo-v4` tag, four separate approvals, and
  `seed-demo-fixture.yml` against `demo` only after `promote-backend` has
  applied the schema its guard requires. Tag-triggered promotion is not
  re-opened; `workflow_dispatch` stays rejected.

**ADR-026 gains a w14 amendment footer** (software-architect, owner) closing the
one inconsistency the council-gate verified after the close and assigned to this
seat. **No new ADR, no new row, and `waves/w14.md` is not edited** — the approved
decision record already carried the right outcome; the ADR was the outlier.

- **The members route sends `X-User-Id` only.** D3's fenced block specified
  `X-Tenant-Id + X-User-Id` on `GET /api/workspaces/{tenantId}/members`, which
  **ADR-022's own accepted w14 footer forbids** in two places (clause 3 and the
  retirement table): on every route in this wave the tenant comes from the route
  path and membership is verified. Two accepted ADRs cannot instruct one
  operation in opposite directions. The header would have been a second,
  unvalidated tenant input on an authorization-bearing route, forcing the handler
  to arbitrate between it and `{tenantId}` — the exact ambiguity the header was
  demoted to remove — and would have split one route prefix, since the sibling
  invite operation already takes the tenant from the route. Verified as **one
  line**: `Grep` returns only this hit and D1's prohibition, which stands.
- **`role` is a string on the wire, never an OpenAPI `enum`** — the
  client-architect's open ask, answered from the schema. Role names are
  **per-tenant rows**: `workspace_role.name` is `character varying(30)` with no
  CHECK and no Postgres enum type, unique only per tenant, and `backend/src`
  holds no role-name literal at all. An `enum` would generate a closed client
  union the server can legally violate on the next seeded row. This agrees with
  the generator clause already in the ADR (nullable enums lose their `null`).
  Consequently the client's least-privilege parse is **mandatory, not
  defensive**, matching the server, which returns `null` for an unrecognised role
  rather than a default; and per ux-ui-designer, **permissions degrade, labels do
  not** — an unmodelled role is shown by its own name.

**ADR-005 gains a second w14 amendment footer** (cloud-architect, owner)
correcting one string in this seat's own first w14 footer. **No new decision, no
new ADR, no new row, no Terraform delta and no new task.**

- **The accept link is `/invite/accept#<token>`, never `/invite/accept?token=…`.**
  The first footer illustrated its site-relative rule at `:126` with a **query
  string**. The rule (site-relative, never absolute) is unchanged; the example is
  superseded. **ADR-025 Rule C9 governs the link shape and cites *this* footer as
  its authority for the site-relative half — in the same sentence that forbids
  the query string the example showed.** The cited authority was the outlier:
  ADR-012 `:151`, ADR-016 `:106`, ADR-018 `:185`, ADR-025 `:257` and this INDEX
  at `:251` all already carry the fragment.
- **The consequence is in this seat's lane, which is why it is a defect and not a
  typo.** It is the failure this seat raised as OQ-w14-cl-01 and did not own:
  `staticwebapp.config.json` rewrites every non-asset path to `/index.html`, so
  the **Static Web Apps platform logs the accept path**. A query-string token is
  logged with it, rides `Referer` to every third-party asset the accept page
  loads, and persists in browser history — a live single-use invitation token in
  platform logs, on infrastructure this seat owns.
- **`waves/w14.md:20` was corrected in one literal** (`?token=…` → `#<token>`)
  and the edit is disclosed in the ADR footer. It was an internal contradiction
  *within one row*: this seat's `CA` half said `?token=…` while the `DM` half of
  the same row already read "`/invite/accept#<token>` — site-relative (C2) *and*
  fragment (Rule C9)". No other byte, row, vote, verdict or ADR action changed.
- **Zero-delta confirmation unaffected; both conditions still hold.** A fragment
  is site-relative by construction, and the token remains CSPRNG-and-hashed with
  no server-held key. **$0.00 cost delta in both environments.**
- **Open, and not this seat's file**: ADR-026 §D5 defines the 201 body carrying
  `acceptUrl` but states **no shape** for it, while §D6 `:255` calls it "the
  copyable `acceptUrl`" — a phrase that reads as absolute. The wave is governed
  by Rule C9 and the ADR-005 footer, but the ADR owning the endpoint contract is
  silent. Raised for **software-architect** as a note, not a demand.

**ADR-025 gains a w14 amendment footer** (security-architect, owner) — the
mechanism that enforces Rule C10, and a rule-id concordance. **No new decision, no
new ADR, no new row, no new task, no schema, endpoint or infrastructure change.**

- **Rule C10 is violated by the auth library, not by our code.** C10 forbids the
  token in `sessionStorage`. Verified on disk: `msalConfig.ts:19-33` sets no
  `navigateToLoginRequestUrl` and `:31` sets `cacheLocation: SessionStorage`;
  `App.tsx:91-106` renders `SignInRoute` **in place** with no router at that level
  (`routes/signin/index.tsx:19-21`), so the address bar still reads the accept URL;
  `routes/signin/index.tsx:34` is the only `loginRedirect` call site in `web/src`.
  A signed-out invitee opening `/invite/accept#<token>` therefore initiates the
  auth redirect **from that URL**, and MSAL's documented default records the
  current href — the token — into the store C10 names.
- **The accepted remedy cannot run.** Clearing the fragment on mount (ADR-012 w14,
  ADR-020 `:411`) never executes, because `App.tsx:91` short-circuits before the
  accept route mounts — and the signed-out case is exactly the case that redirects.
  The rule and the gate are in the wrong order. Same class as F.2, where the agreed
  "bind the parameter" was literally unachievable and the statement form had to be
  named.
- **Rule C10a** — `navigateToLoginRequestUrl: false`, set **explicitly** (a rule
  resting on a library default is not a rule). **Rule C10b** — the fragment is
  captured and cleared **before** any path that can initiate authentication renders
  a control. Both, because they fail differently: C10a survives a route refactor,
  C10b survives a default change or a second `loginRedirect` call site.
- **T15 joins §H** — after landing on `/invite/accept#<token>` while signed out and
  initiating sign-in, no key in `sessionStorage` or `localStorage` holds the token.
  It asserts the property, so it is correct whatever the library default is.
- **Ownership**: `msalConfig.ts` and `App.tsx` are **client-architect's**. This
  footer states the rule and the test and constrains two tasks NW-03 and NW-58
  already own. Cloud-architect's zero-delta confirmation is untouched — no key, no
  environment variable, no secret.
- **Rule-id concordance**: ADR-026 `:239` cites "ADR-025 Rule 5.2f/5.2g", the ids
  of this seat's **lane draft** (`:694`, `:704`), promoted into the accepted ADR as
  **Rule C9**. Every other seat cites `C9` correctly; ADR-026 was written from the
  draft before ADR-025 was on disk. **Read `5.2f`/`5.2g` as `C9`.** Recorded on
  this seat's side rather than by editing an ADR it does not own; ADR-026's clause
  is substantively identical and needs no change.
- **`waves/w14.md` was not edited.** The record is not wrong here, only silent, and
  appending to the artifact the gate read back would mean the approved record is
  not the one that was checked.

**ADR-012 gains a second w14 amendment footer** (client-architect, owner) — the auth
library's store, and the screen the redirect actually lands on. **Supersedes nothing
in the first footer. No new ADR, no new row, no new task, no endpoint, no schema, no
infrastructure.**

- **Rules C10a and T15 are adopted without qualification.** They name this seat's
  files. Verified independently: `navigateToLoginRequestUrl` has **zero** occurrences
  in all of `web/src` (the library default governs), `msalConfig.ts:31` is
  `SessionStorage`, `routes/signin/index.tsx:34` is the only `loginRedirect` call
  site. One line of config plus one e2e assertion, inside tasks NW-03/NW-58 own.
- **C10b's routing half is already accepted — do not schedule it twice.** ADR-025
  `:784-789` reads `App.tsx:91` and concludes the remedy "cannot run". True of the
  code **today**; not true of the accepted w14 design: **ADR-018's w14 footer
  `:182-204` already hoists `BrowserRouter` into `App.tsx` with a public branch for
  `/invite/accept` above the `!account || !workspace` gate**, and states the route is
  reachable signed out and with no workspace. The seam is closed — but only if the
  decomposer reads both footers. C10a stays necessary: it is what fails closed if the
  hoist is ever refactored.
- **What C10b genuinely adds** is the *intra-route* ordering this ADR had not stated:
  the fragment is cleared **before the sign-in control is interactive**, not merely
  "on mount" — the accept screen renders its own auth-initiating control (ADR-020
  state 2), so both are one screen's problem. Adopted.
- **New, and neither footer reached it: the redirect lands on a screen that says
  nothing about the invitation.** `oidcRedirectUri` is a **single** configured value
  per environment, used for login and post-logout alike (`msalConfig.ts:24-25`;
  `appConfig.ts:28,41,90`), so a signed-out invitee **cannot** return to
  `/invite/accept`. They fall through `*` to `App.tsx:91` with an account but no
  picked workspace (`workspaceStore.ts:117`, session-scoped) → `SignInRoute:41-52` →
  `WorkspacePickerScreen` → under NW-01, `GET /api/workspaces` returns `[]` for an
  unaccepted invitee → **the create-a-workspace form** (ADR-012 w14 clause 2).
  ADR-020 state 5 ("Open your invitation link again") is the right copy on a screen
  the user has just left. **Newly broken by this wave**: before NW-01 the list came
  from a cache that could be non-empty.
- **Remedy — one unconditional sentence on the picker's empty-list state**, beside
  the create form: "Invited to a workspace? Open your invitation link again." No
  endpoint, no route, no stored token, and no knowledge of whether an invitation
  exists — which is what keeps it clear of ADR-020 `:422-425`'s leak guard. **Copy is
  ux-ui-designer's**; placement and trigger are client-architect's; it belongs to the
  NW-01 picker task that already owns that state. ADR-020 is unchanged and needs no
  edit — state 5 stays correct and reachable for the reload and popup-blocked paths.
- **`loginPopup` is unwritten code** (grep: zero call sites in `web/src`) and stays an
  optimisation — **no `must` item may depend on it**. The guaranteed flow is still
  sign in, then open the link again; the picker sentence is what makes that flow
  discoverable rather than a dead end.
- **`waves/w14.md` was not edited**, for the reason the product-owner gave and the
  software and security seats followed: this seat's record entry is not wrong, only
  silent. The decomposer reaches this footer through the NW-01, NW-03 and NW-58 rows,
  which already name ADR-012 as governing.

**ADR-020 gains a second w14 amendment footer** (ux-ui-designer, owner) — the empty
picker's sentence, and the one affordance re-issue has. **No new ADR, no new row, no
new task, no new endpoint, no new component, no new token.** Supersedes nothing in the
first footer **except 10.3's row-action wording**, corrected below.

- **A correction the decomposer must not miss: `Send a new invitation` has no legal
  path as written.** ADR-020's first w14 footer gives an `Expired` row that action;
  ADR-026 D4 `:209-211`'s partial unique index `(tenant_id, lower(email)) WHERE
  accepted_at IS NULL AND revoked_at IS NULL` rejects it — the predicate excludes
  **accepted** and **revoked**, and a lapsed invitation is neither (`:203-204`), so it
  still holds the slot. The index cannot be widened to exclude expiry: a partial-index
  predicate must be immutable and "expired" is a clock comparison (`:202`). The ADR's
  intent ("re-invites cannot accumulate **valid** links") does not cover the lapsed
  case; its predicate does.
- **Second face of the same gap: an invitation link cannot be shown twice.**
  `acceptUrl` is on exactly one response in the contract — the invite 201 (ADR-026
  `:227`); `GET /api/invites` returns "nothing else, ever" and only to the token holder
  (`:228-230`); storage is a SHA-256 hash (ADR-025 C2 `:214`, T10 `:635`). An Admin who
  leaves screen 10 can never see the link again and **the server cannot produce it** —
  correct by design. Nor can they re-invite (`WorkspaceMembershipService.cs:52-54`
  "fails cleanly" on a repeat of the same role).
- **Decision: "lost the link" and "the invitation lapsed" are one need — re-issue** —
  and w14 models only invite and revoke. One affordance on both rows meaning
  revoke-then-invite; the `Invited` variant must say **"The link you already shared
  stops working."** before the click. Prevention in an existing string: the invite
  pane's `false`-state meta becomes "It expires {date}, can be used once, and **is not
  shown again**." **Mechanism is software-architect's and security-architect's** (two
  existing endpoints, ADR-026 `:234`/`:226`, or one server-side op; audit is §G) — no
  new endpoint on the accepted shape.
- **The picker's empty-list copy, handed to this seat by ADR-012 w14 clause 3:**
  "**Invited to a workspace? Open the invitation link your workspace admin shared with
  you.**" The proposed "again" is removed — the create form **is** the empty state
  (ADR-020 1.5), so the line is read by every first-time Admin who never held an
  invitation, and a screen that must not know an invitation exists cannot claim one was
  opened. **State 5 keeps "again" (its user demonstrably held a token); the picker does
  not.** Treatment is `.micro-meta`, not `.hint` — ADR-019 w14 clause 2 reserves `.hint`
  for a **gated** control.
- **Standing rule: that sentence stays unconditional.** Showing it only to users who
  hold an invitation requires the picker to ask "does this address hold one?" — an
  invitation-probing oracle ADR-025 closes (states 7/9 worded identically) and
  `GET /api/invites` refuses. Unconditional is both cheaper and safer; **conditioning it
  requires a leak.**
- **`waves/w14.md` was not edited**, per the product-owner's principle. Its `:361`
  carries the superseded 10.3 wording; the NW-58 row already names ADR-020 as governing,
  and this footer is where that row is now built from.

**ADR-016 gains a second w14 amendment footer** (delivery-manager, owner) — post-close
verification of the wave's workflow set, and where a browser-expressed test is actually
proven. **No new ADR, no new row, no new task, no new workflow, no new environment key.**
Clause numbers continue the first footer (clauses 8–12).

- **The two-file set holds, verified at the source.** The workflow set is **nine files**;
  the planned w14 change is still exactly `backfill-workspace-membership.yml` (new) plus
  `seed-demo-fixture.yml`'s guard. The three migrations need no workflow edit: ADR-003
  `:122-123` and ADR-026 `:360-362` both assert it from one source, so this seat checked
  the source — `backend.yml:277` **and** `:309` each already list
  `identity-workspace.sql`, the file all three regenerate. ADR-014 w14 clause 4 stands.
- **A browser-expressed mandatory test is not a CI gate in w14.** `backend.yml:72-74`
  runs `dotnet test` unfiltered, so ADR-025 §H's Postgres/RLS tests genuinely gate.
  `web.yml:79-81` runs `npm test` → `vitest run` (`package.json:15`), while
  `"test:e2e": "playwright test"` (`:16`), `@playwright/test` (`:27`) and two checked-in
  specs (`web/e2e/v2.spec.ts`, `day1.spec.ts`) exist and **no workflow invokes them**. So
  **T15** (ADR-025 `:807-813`) and the `v2.spec.ts` prerequisite have no runner: **a task
  that writes only a spec file has not delivered the check.**
- **Do not wire Playwright into CI in w14.** It is a third workflow file (ADR-014 w14
  clause 4 → defect), it needs a non-interactive Entra identity this wave does not have,
  and **NW-50 is queued W18**.
- **The acceptance doc gains three steps** — T15 (the only proof of Rule C10a's one-line
  diff), the picker empty-state sentence (ADR-012 `:189`, ADR-020 `:451`), and the
  re-issue affordance if it lands. The decomposer must read this footer to build that task.
- **Re-issue adds no writer, task or phase**: client-side DELETE-then-POST touches no
  server file, and a server-side operation lands in `InvitationsEndpointExtensions.cs`,
  which ADR-026 `:363-367` already confines to one task. **No fifth writer** on
  `WorkspaceEndpointExtensions.cs`.
- **`waves/w14.md` was not edited**, per the product-owner's principle. Its acceptance
  step list at `:508-514` is incomplete, not wrong; the NW-58 and W14-01 rows already
  name ADR-016 as governing.

## Upload feels instant, and inviting a colleague works end to end (wave w15, appended 2026-09-13)

ADR-001…026 keep their original Decision outcomes. **No ADR is superseded by
this wave.** Items served: NW-27, NW-61, NW-10, NW-67, NW-68, NW-69, NW-05,
NW-06, NW-58r, W15-01 (`reports/architecture/waves/w15.md`).

New ADR from the w15 table:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-027 | Asynchronous document processing — the extraction queue, the rejection state and the completeness contract | software-architect (owner; cloud-, security-, client-architect, ux-ui-designer, product-owner, delivery-manager co-decide) | `POST /api/documents` returns 201 once the blob and rows are durable and the AI pipeline runs on `Raffa.Worker`; the message is **published before the commit** because `extraction_job` carries `FORCE ROW LEVEL SECURITY`, which makes every poller and every sweeper a cross-tenant read ADR-009 forbids; idempotency is a conditional `UPDATE` claim on the already-written `ExtractionJob` row, never a broker feature, and the **database owns the terminal state** (`max_delivery_count` strictly above the app's `MaxAttempts`), which is what makes "every row terminal" a property of the schema; the pipeline must **replace rather than append**, because at-least-once delivery otherwise duplicates every extracted fact; content classification moves to the worker and a refusal becomes a terminal `Rejected` row (code-only enum value) whose blob is deleted, listed but **never counted** and never askable; and one server-computed completeness contract (`counts`, `readiness`, `processingDocumentCount`) replaces **four** incompatible definitions of "ready", one of which already tells users a fabricated number. |

**ADR-001 gains a w15 amendment footer** (product-owner, owner). It *completes*
its w14 clauses 1–2, *resolves* clause 7, and **supersedes nothing**; the R0–R4
ladder and the §1.2 non-goal list quoted verbatim are unchanged. The
**asynchronous pipeline is restored as the product rule and grants no new scope
authority** — spec §7.1 is literally headed "Asynchronous pipeline", `uploaded`
is defined there as "processing **not started**" and §16.1 ranks the async
worker **P0**, so an undelivered P0 is being delivered late, and
`requirements.md` **A7 / OQ-askv2-007** are `assumed-wrong` from this wave on.
The **admission gate splits** (format and size in the request, content
classification on the Worker) and a refused file becomes a **persistent,
visible, terminal record whose content Raffa does not keep** — never askable,
**never counted**, removable by the existing `DELETE` — which supersedes
**R-DOC-05 AC-1** and R-DOC-04's *session-only / not stored* clause **on the
record** (`inputs/**` is never edited) while R-DOC-04's ***never counted* clause
stands**, because async classification **plus** a session-only refusal is the
one combination that loses a user's file without telling them. **"Still
processing" outranks "empty"**, *askable = validated, not merely `completed`*,
and screens are **gated, never blocked**. Inviting a colleague **provisions a
guest identity and never anything more**: Raffa creates a guest and **never
deletes or blocks one** (answering OQ-w15-007), one guest per address, no
directory reads beyond provisioning — not §1.2's "supplier onboarding", because
the invitee is a **colleague** joining the customer's own workspace. The
deferred **workspace-domain invite restriction is resolved as NOT enforced**,
since *a deferral does not convert into an obligation when its trigger fires*.
The **mail deferral ends with scope unchanged**: exactly one mail type, the
pilot script gains no inbox step, `mailDelivered` is a **server fact**, and **a
mail carrying a site-relative accept link means NW-68 does not ship**. The
general rule under all six clauses is w14 clause 3's mirror — **the system must
not silently drop a fact the user is entitled to.** An **addendum** from a later
table round corrects **A15-6's acceptance wording** (no "Try sending again"
ships, withdrawing this seat's own lane draft against ADR-020 w15 §3.4, because
the token is stored only as a SHA-256 hash so any retry is a **re-issue** that
kills the link on screen — revoke + re-invite under §J.2b keeps the capability),
confirms **OQ-w15-D2 unoverruled** on a role-boundary ground (widening Admin-only
`DELETE` to Procurement is a **spec §3.1** change, not a list tidy-up), sets the
live-invitation cap at **100** as a safety bound rather than a quota
(OQ-w15-sec-03), and rules that **if the 20-task cap binds the two `should`s
yield before either `must`** — NW-10 first, then NW-58r to its runbook walk,
then NW-69 — while NW-27's durability, NW-67's failure contract and NW-68's
absolute accept link are **never** narrowed. A re-entry round adds **clause 12**,
which rules what `demo` actually shows this wave: both new capabilities ship
behind per-environment flags defaulting **false** there, and ADR-026 §2's fourth
value **`NotConfigured`** makes flag-off behave *link-only, exactly as `main`
does today*, so the "a failure leaves nothing behind" rule binds **`Failed`**
alone and **`demo` loses nothing w14 delivered**; **A15-4 / A15-5 / A15-7 are
therefore `dev` acceptance and are not walkable on `demo`**, and no task may
enable either flag on `demo` to make them pass. The same clause **ratifies the
`demo-v4` cut as a product requirement** — `demo` stands a whole wave behind at
`demo-v3`, so the workspace-and-invite half of `percorso-pilota-v1.md` has never
existed on the environment the client demo is run from.

**ADR-002 gains its first amendment footer** (software-architect, owner). The
modular monolith, the module map and "no microservices split in V1" are
unchanged. The queue stops being a diagram and becomes a real port
(`SharedKernel/Messaging/`), with handlers confined to the worker host as
ADR-002's own Implications section already required; **one new project,
`Raffa.Storage`**, carries the blob adapter that is `internal` to `Raffa.Api`
today but is needed by both hosts — the `Raffa.AiGateway` shape, not a bounded
context, so the spec §5.1 module map does not move; `Microsoft.Graph` joins
`ForbiddenSdkPrefixes` so ADR-002's provider-SDK rule becomes enforced rather
than merely stated; and the assumption "no durable outbox/messaging middleware
beyond the queue at R0" retires — it is a **broker, not an outbox**, deliberately,
because a transactional outbox would need the cross-tenant sweep ADR-009 forbids.

**ADR-024 gains its first amendment footer** (software-architect, owner). Ask
Raffa V2's three sources, the no-tools `answer` role, the grounding and numeric
guards, conversations, strategies, the capability catalog and the V2 IA are all
untouched. Superseded in their **ordering half only**: "the admission gate runs
before `IDocumentStorage.SaveAsync`" and the intake row's "before any blob or row
is written" / 422 clauses — the gate now **splits**, format and size staying in
the request (so nothing is stored for a non-document, and the 415 clause stands
verbatim) while content classification moves to the worker. D3's substance —
a non-contract's **content** never enters the tenant corpus — is preserved and
restated as binding. The footer also records `requirements.md` **A7 /
OQ-askv2-007** and **R-DOC-05 AC-1** as superseded **on the record** (`inputs/**`
is never edited), R-DOC-04's *never counted* clause as standing, and fixes a live
defect: Ask's `validatedContractCount` is fed an **unfiltered** portfolio count
and `AskCopilotService.cs:294` renders it into user-facing copy as "N validated
contract(s)" — from w15 there is exactly one definition of validated, ADR-026
§D2's.

**ADR-026 gains a second amendment footer** (software-architect, owner). D1–D4
and every implication are untouched; no schema change and no regenerated
`.sql`. The invite 201 gains `identityProvisioned` (a server fact beside
`mailDelivered`, so the pane never infers identity state from delivery state);
`IGuestProvisioner` joins D6's seam family with a fourth `NotConfigured` value
that keeps the wave landable if the directory permission does not ship;
provisioning runs inside the request **before the mail** and a failure leaves
**no invitation row** — forced by D4's own partial unique index, which would
otherwise let a failed invite hold the address slot; `acceptUrl` becomes
**absolute** when a transport is configured, closing the D5 shape gap the ADR-005
second w14 footer raised for this seat, with the fragment form unchanged; and the
footer records that the server **cannot re-send the original link** (the token is
stored only as a hash), so "Try sending again" is necessarily a re-issue.

**ADR-005 gains a third amendment footer** (cloud-architect, owner). Every SKU,
the scale-to-zero rule, the shared-`aisvc-raffa` exception, the ACS design with
its rejected alternatives and the fragment accept link are unchanged. Service
Bus moves from **provisioned** to **wired** and the ACS rows from **decided** to
**applied**. Three independent Terraform gaps produce the *same* symptom — 201 in
under 2 s and no document ever progresses: `modules/servicebus` creates a topic
with **no subscription** (a topic with zero subscriptions **discards** every
message, so the producer reports success and the documents are lost), the worker
has `min_replicas = 0` with no ingress and **zero `scale_rule` matches anywhere
in `infra/`**, and **no Service Bus RBAC or authorization rule exists**. w15 adds
**exactly one** subscription (`max_delivery_count = 5`, strictly above the app's
`MaxAttempts` so the database owns the terminal state; `lock_duration = PT5M`,
the Service Bus maximum, coupled to `ServiceBus__MaxAutoLockRenewalMinutes`;
sessions deliberately **not** enabled), two **topic-scoped** role assignments on
the existing workload identity (**no secret at all**), a KEDA scale rule whose
authentication shape must be **proved by `terraform validate`, never asserted**,
and `max_replicas` 1 → 3 on both apps. **`min_replicas = 1` is rejected** with
its ~$14/env/month arithmetic, recorded so it is not re-proposed as a
simplification. The worker's **absent `ConnectionStrings__Storage`** is the
quiet blocker — it needs **no new module variable and no root change**, and it
must stay **fail-fast** while all nine `ServiceBus__*` / `Invitations__*` keys
bind **optionally**, the shape `Raffa.Worker/Program.cs:44` already uses for
`Market`. The connection-string form for ACS **stands for w15** and the
`TokenCredential` migration security-architect proposed is recorded as the
preferred later step (an ADR-011 amendment and one role assignment). **Fixed-cost
delta: $0.00 in both environments** — `:56` still names Service Bus Standard and
ACR Basic as the only two non-trivial fixed lines, and w15 adds no third.

**ADR-007 gains its first amendment footer** (cloud-architect, owner). Option 1,
remote state per environment, no state in git, no secrets in source and the
mandatory tagging are all unchanged. The layout block at `:36-61` is reconciled
with the tree: it listed **nine** modules while `staticwebapp/` and `foundry/`
had already landed without a footer, so the corrected count is **eleven, and
twelve** with w15's new `communication/` — and `infra/README.md:18-29` is named
as a **second, parallel layout tree** that gains the same directory in the same
task. Two module edges the layout implied and the tree did not have become real
(`servicebus → containerapps`, `identity → servicebus`), with the note that
`module.servicebus.fqdn` derives from the namespace **name** and so introduces
**no plan-time unknown**. One Terraform PR owns all of `infra/**` this wave,
which is what `check_single_writer.py` requires.

**ADR-010 gains its second amendment footer** (security-architect, owner). The
four registrations, PKCE, no client secrets and per-environment `iss`/`aud`
validation are unchanged, as is the w14 footer. w15 records the **validation
parameters**: `ValidateAudience = true` against **`api_client_id`** — not the
`api://` identifier URI, because `requested_access_token_version = 2` makes the
client id the `aud` a token actually carries, and a task that wires the URI sees
every token rejected and reaches for `ValidateAudience = false`; `ValidateIssuer`
pinned to the concrete tenant issuer, never `common`/`organizations`/`consumers`;
`ClockSkew ≤ 2 minutes`; a missing scope denies and a present scope **grants
nothing**. `oid` is the identity and `email` only ever a first-bind aid; **`tid`
is the directory, never the Raffa tenant**; the `#EXT#` guest UPN is never parsed
into an email for an authorization decision. The footer also carries the finding
that makes **NW-06 urgent rather than tidy**: `WorkspaceRoleResolver`'s claims
branch returns a role **without ever referencing its `tenantId` parameter**, so
wiring JWT would turn one directory-wide app role into Admin **in every workspace
the caller can name**, bypassing `workspace_membership` on the path that gates
document deletion and reprocess. The branch is **deleted, together with the doc
comment at `:20-22` that instructs the next implementer to keep it**, and no
authentication kill-switch is added in any environment.

**ADR-022 gains its second amendment footer** (security-architect, owner). The
Day-1 posture stands as history; its w14 retirement schedule **fires for its first
two rows**. `X-User-Id` stops being read at all — the class and its constant are
deleted, not kept for compatibility. **`X-Tenant-Id` is demoted, not deleted**, a
deliberate departure from the intake's wording: a caller may belong to several
workspaces, so the token subject alone names no tenant and only 6 of 36 routes
carry `{tenantId}`; it becomes an **authorized selector** verified against the
token subject's live membership before scope entry, **404** on failure. Sizing is
measured, not estimated — **74 occurrences across 17 files**, one task, or two
identity regimes run live at once. The w14 "recorded limitation" (a caller can
still assert another `X-User-Id`) is **closed**: from w15 it is a defect, not a
residual.

**ADR-025 gains a second amendment footer — a new §J** (security-architect,
owner). §A–§I are untouched. §J governs the wave in which an invitation becomes a
**write into the customer's company directory** and the accept link becomes an
**email**: `User.Invite.All` as a Graph **application** permission on the existing
workload identity rather than the Guest Inviter directory role (a fixed, auditable
grant versus a Microsoft-owned bundle that can widen without our Terraform
changing); guest **before** row, so a failure leaves an inert directory object
rather than a live 256-bit token for an identity that cannot sign in — the same
answer software-architect reached from D4's partial unique index; **bind the
Graph-returned guest object id into `workspace_user.ExternalSubjectId` at invite
time**, without which NW-67 and NW-05 together break Rule D.3b's email equality and
A15-4 fails at its last step; the Graph `inviteRedeemUrl` is credential-shaped and
is never mailed, returned, stored, logged or audited; exactly two externally
visible provisioning outcomes, so the invite form is not a directory-enumeration
oracle; removal **never** deletes or blocks the guest; the accept base is
configuration and never a request header, or Raffa mails a live token to an
attacker-controlled origin; Rule C9's fragment survives the transport and is now
load-bearing against link-rewriting mail gateways; **no test-only authentication
seam ships, in any wave**; five additive audit verbs; and T14/T15 activated beside
eight new tests. §J also corrects the guard-test half of ADR-002's w15 footer:
`DependencyDirectionTests` covers only the ADR-002 **domain-module** list, so it
cannot enforce "one Graph call site" in a **host** — that belongs in
`SdkAllowListTests`, which covers every project, and the amendment must be
**package-scoped**, since widening its single `AllowedProjectName` skip would make
`Azure.AI.*` legal in `Raffa.Api` as well.

**ADR-011 gains its fourth amendment footer** (security-architect, owner). One Key
Vault per environment, managed identity, no secrets in source or bundle,
authorization before retrieval, audit, no training on customer content — all
unchanged. **w15 adds exactly one Key Vault secret (`acs-connection`) and no new
Key Vault permission**; Graph, Service Bus and the API JWT add **none**. The
footer resolves a **contradiction between two ADRs written at this same table**:
ADR-027 §D12 (`:347`, `:395`) specifies the Service Bus connection as a Key Vault
secret on both hosts "exactly as `pg-cs` and `st-cs` already do", while ADR-005's
w15 footer specifies topic-scoped RBAC and no secret at all. **This ADR owns the
secret-versus-identity question and rules for identity + RBAC** (`Data Sender` /
`Data Receiver`, never `RootManageSharedAccessKey`), leaving ADR-027's other
requirements untouched. Cloud-architect's `listen`-only KEDA fallback is accepted
**because** the message carries ids only — a leaked listen key would disclose
identifiers, not contract content. The `TokenCredential` migration for ACS is
recorded as accepted-later, not re-argued. And the ADR-024 footer's "classified
before persistence; rejected files are never stored" is superseded **in its
ordering half only**: the *rule* — a refused file's content never becomes
retrievable, no embedding rows in either corpus — is preserved exactly.

**ADR-009 gains its second amendment footer** (security-architect, owner). RLS on
every tenant table, no `BYPASSRLS`, and the w14 footer's eight clauses are
unchanged. w15 gives the Implications section's background-worker bullet its
mechanism, because this is the first wave that builds that path: a queue message is
**untrusted input** (`TryParseExact` before `BeginScope`, the job-row match as the
first statement inside the scope, one scope per message, the Worker is **not** a
third exception to clause 4). The footer's sharpest clause is one neither ADR shows
alone — **the storage path must never come from the message**, because
`DocumentStoragePath.EnsureWithinTenant` compares the path against the tenant *the
caller passes*, so a message supplying both sides of that comparison would pass the
guard. It also names the RLS guard correctly — **`TenantRlsMigrationCheckTests`** —
and records that its coverage is **conditional**: it discovers tables from
`TenantScopedEntity` subclasses of `DocumentsContractsDbContext` **alone**, which is
why w14 had to hand-write `WorkspaceInvitationRlsTests`; and that a hand-written RLS
test run against Testcontainers' default **superuser** is green and worthless.

**ADR-012 gains its third amendment footer** (client-architect, owner). The
Decision outcome (React + TypeScript + Vite, OIDC PKCE, a static bundle on Static
Web Apps) and both w14 footers are unchanged. w15 adds the client half of five
items in **one** footer: the **authorized-fetch choke point** — `client.ts`'s
identity accessor is synchronous and its header literal is spread into **37
separate `await fetch(` call sites**, so NW-05 cannot be a find-and-replace and
`X-User-Id` is **deleted, not made conditional**; the SPA never stores the access
token and **never reads a role claim from it**; `X-Tenant-Id` **stays a header**,
demoted to a membership-verified selector by security-architect's ruling, so the
w14 workspace hint and the `v2.spec.ts` e2e seam survive; the NW-61 provenance
rule — **a client must not infer a server state it can be told** (Ask's off-state
branches on `totalCount > 0` today and therefore lies to a tenant whose only
document failed); the optimistic upload row is handed off **on the server row's
arrival, never a timer**, because NW-27 turns a one-RTT gap into a visible
flicker on every file of a 15-file batch; NW-69's outcome discriminant becomes a
**server string** and the accept link renders **only when it is usable**; and
NW-67's `loginPopup` **continues into the accept on its own resolution** rather
than requiring a second click. The footer's sharpest clause **corrects this
ADR's own w14 clause 3**: the claim that "a nested object renders `unknown`" is
**false** — `renderSchemaType` recurses at `generate-api-client.mjs:110`, and the
contract already nests three deep (`raffa-api.v1.json:3103-3105`) with the
rendered result on disk (`schema.ts:646`). What genuinely renders `unknown` is
**`$ref`/`oneOf`**, which the contract itself works around by duplicating shapes
inline (`:3910`). This matters because ADR-027 §D7–§D9 promoted `counts` and
`readiness` believing they would render `unknown`, whose likeliest repair is a
**hand-written DTO in `client.ts`** — the one prohibition ADR-012 `:53` has held
since it was accepted. It also records that `processingStatus` lives in **eight**
contract sites, the eighth being a **query parameter** that no build or `tsc`
check can catch. **ADR-018, ADR-013 and ADR-020 take no action** — no route is
added, moved or removed this wave, mobile is untouched, and the states' copy is
the designer's. A **round-two clause 13** adopts ux-ui-designer's two corrections
to this footer and supplies the mechanisms they need: the invite outcome is
**three** values, not four (a provisioning failure aborts the invitation, so no
201 can carry it); the 502's closed `reason` set reaches the pane as a **typed
`failureReason` off a declared 502 response**, because the envelope carries only
prose today (`client.ts:160-170`) and string-matching it is the inference clause 4
forbids; and **`UploadResultCard.tsx` may not simply be deleted** — the refusal
has four producers and only the Worker-side content gate becomes a row, while the
browser-side oversize check and the pre-storage 413/415 never can, so they move to
a **local `"rejected"` row** rather than being silently dropped.

**ADR-018 gains its fifth amendment footer** (ux-ui-designer, owner). The Route
map, the Roles section and the three original states of
`## Empty / error / loading (IA-level contract)` are unchanged, and **no route is
added, moved or removed this wave** — recorded from this seat as well as from
client-architect's so no task re-opens the router table from either side. w15
adds two states to that contract: **not ready yet** (something is in flight) and
**nothing made it through** (Raffa.ai holds documents, none in flight, none
validated). Both use the *empty* block and never the error treatment, both are
**told by the server and never inferred**, and both exist to enforce one rule —
*an empty state must never tell a user to do a thing they have already done*.
The footer binds Ask, Portfolio, Contract 360, Renewals and Quote check in one
statement. It also settles **what each Documents number counts**, and in doing so
**withdraws a ruling of this seat's own lane draft**: `All documents · N` does
*not* count refused files. Instead every number reads exactly one server field —
`counts.needsAttention`, `counts.all`, and a **third filter chip `Not added · K`**
reading `counts.rejected`, which had no consumer in ADR-027 §D7 until now. That
is the only arrangement in which product-owner's "persistent, visible, terminal"
refusal row is actually **reachable**: with two chips and `counts.all` excluding
`Rejected`, the row exists on the server and no filter shows it. A third finding
is recorded as an ask rather than assumed: `kbSummary`'s **"M askable"** is
page-scoped *and* equates askable with `Completed`, which ADR-026 §D2 rules is not
the definition — the same fabricated-fact class software-architect found at
`AskCopilotService.cs:294`, one screen earlier.

**ADR-019 gains its second amendment footer** (ux-ui-designer, owner) and it is
deliberately the smallest of the wave: **one row** joins the locked Semantic
mapping — *document refused at admission (`Rejected`) → `.tag-outline`, label
"Not added"* — **derived, not invented**, since that treatment and that label
already ship together on the card w15 retires (`UploadResultCard.tsx:24`), and
`.tag-accent` is reserved for a Raffa.ai-side failure the user can retry. **No
token, no type-scale row, no component, no confidence threshold changes.** The
footer records the one place in this wave's design surface where the type system
does not protect the change: `documentTable.ts:97-99` is an **unchecked cast**,
so adding `"rejected"` to `RowStatus` while forgetting `semantics.ts:52`
**compiles clean** and falls through an exhaustive switch to `undefined`. It also
confirms no component is needed for three surfaces that look like they need one —
the third `.seg` button is a catalogue *use*; and the invitation email consumes
token **values as inline literals**, because no custom property and no web font
(`Archivo`) survives an email client.

**ADR-020 gains its fifth amendment footer** (ux-ui-designer, owner). Both w14
footers stand and are quoted where touched. It rules on four surfaces. **Screen 3**:
a refused file becomes a **row** with a terminal status, the reason moves verbatim
into the existing hint slot, `UploadResultCard.tsx` is deleted, the row offers no
next step but stays removable by the existing `DELETE`, and `stage: null` reads
**"Queued…"** — which costs nothing, because `documentTable.ts:76-78` already
states that only the stage text distinguishes queued from processing. **Screens 2,
5 and 6**: Ask's off-state gains a **third** variant (OQ-w15-ca-04, routed here by
client-architect) because its predicate today tells a tenant whose only document
*failed* that it "is still processing"; Portfolio gains a third variant with **no
new string**; Contract 360 gains a fifth state with two readings. **Screen 10**:
the pane's outcome set is corrected against what the backend seats promoted — a
provisioning failure **aborts the invitation**, so it is a blocking **502**, not a
fourth success state; this seat's "suppress the link" and client-architect's
fourth outcome both yield to a mechanism that is strictly stronger, and the rule
*a link renders only when it is usable* is satisfied **by construction**. 10.1
therefore gains **one** value, and its pre-decided **"Try sending again" affordance
does not ship**, because the server cannot re-send the original link (the token is
a SHA-256 hash) so any retry is a re-issue that kills the link the Admin is
looking at — the precedent being this ADR's own w14 §2, *an affordance with no
legal mechanism is not shipped*. Copy is supplied for software-architect's closed
502 reason set, with a fourth row guaranteeing a raw wire enum can never reach the
screen. **Surface 12 is new — the invitation email**, which has no design owner in
any export: plain-text body of record, no image, no web font, the accept link as a
visible absolute URL, and lines lifted verbatim from screen 11 so the mail and the
landing page say the same thing. One rule binds the whole wave: the V2 export is
**pre-rebrand** ("Raffa") while every shipped string says **"Raffa.ai"**, so any
task retyping copy out of the export silently reverts the rebrand.

**ADR-027, ADR-002 and ADR-026 each gain a second w15 amendment footer**
(software-architect, owner of all three) — the reconciliation round. **No new ADR,
no new row, no new endpoint, no new table, no migration, no infrastructure and no
Terraform delta.** Three peer corrections are adopted after re-verification at the
source, one cross-item hazard no lane had named is recorded, and two questions
routed to this seat are answered.

- **ADR-027 (C1–C5).** Implication 5 is **withdrawn**: `renderSchemaType` **recurses**
  (`generate-api-client.mjs:109-111`) and returns `unknown` only for a missing
  `properties` map or a `$ref`/`oneOf`, so `counts` and `readiness` land **inline** —
  the generator is not extended, nothing is flattened, and nothing is hand-written
  (client-architect's correction; the wrong repair in front of an `unknown` is the
  hand-written DTO ADR-012 `:53` forbids). The case's own doc comment still claims
  *"flat property maps only"*, which its next two lines falsify — the same staleness
  class as OQ-w15-003's, retired by the task that first exercises the recursion. D12's
  worker key list is **false and would fail the apply**: the worker already carries
  `AZURE_CLIENT_ID` and all three `AiGateway__*` keys, so acting on it emits duplicate
  `env` names; only `ConnectionStrings__Storage` is absent (cloud-architect's
  correction). D12's remaining bullets **yield to ADR-005's w15 footer**, which is the
  ruling D12 asked for: `min_replicas = 1` **rejected** in favour of a KEDA scale rule,
  the subscription is `document-processing`, and **NW-27 adds no Key Vault secret** —
  topic-scoped RBAC — so ADR-011's "`acs-connection` is the one new entry" is exact.
  D10's selection predicate inherits (namespace, not connection string). And **`counts`
  does not gain `askable`**: a document envelope cannot carry a contract fact without
  minting a fifth definition of *ready* in the wave whose D8 collapses four into one —
  the segment renders §D2's existing `contractCount`, already fetched by the shell, or
  it is removed (ux-ui-designer's copy call), while `counts` gains **`needsReview`**,
  a free projection of the one grouped query, because `needsAttention` is the wider
  number and rendering it under narrower words is the same defect D8 removes.
  **Round 3 (C6–C11)** adds one correction this seat found in its own design and five
  discharges. **C6: D3 completed a message that may not be a phantom.** A commit slower
  than D2's 2 s delay makes the row invisible at delivery, D3 completed the message, the
  commit then landed, and — with no sweeper possible under `FORCE` RLS (§0.1) — the
  document sat at `Uploaded` **forever on a 201**. The three cases D3 merged are split by
  `DeliveryCount`: row present ⇒ complete; absent and `< 2` ⇒ **abandon**; absent and
  `≥ 2` ⇒ **dead-letter `job-not-found`**. D2's "nothing is lost" is corrected — the
  residual stranding is bounded, alarmed and recoverable by D5's re-enqueue, and closing
  it fully would need the cross-tenant sweep ADR-009 forbids; a non-empty DLQ now also
  means *"an upload's commit failed"*. **C7: `MaxAttempts = 3`**, the number `ADR-005:358`
  assigns to this seat — strictly-below is insufficient because deliveries and attempts do
  not advance together, and `4` would let the broker dead-letter before the handler wrote
  `Failed`; `MaxAutoLockRenewalMinutes = 30` binds to `lock_duration = PT5M`. **C8:** C3
  renamed the subscription in prose but left D2's constant `extraction-worker`, which
  fails at first receive inside a **green** CI run — it is `document-processing`; the
  topic is re-verified correct and the module carries **no subscription resource at all**.
  **C9:** D7's `needsAttention` takes ADR-018 clause 2b (*not `Completed` and not
  `Rejected`*), its old premise being false at `documentTable.ts:141-143`; `counts` is
  therefore **five overlapping projections, not a partition** (no sum holds), and
  `all − needsAttention` must never render as *askable*. **C10:** `Raffa.Storage` carries
  **no `DbContext`, migration or `.sql`**, so `backend.yml` is untouched and w15's CI-YAML
  set stays **zero** (delivery-manager's ask). **C11:** the message stays **ids-only** —
  a path beside its own tenant id would make `EnsureWithinTenant` self-referential — and
  `AzureAd__` is confirmed unclaimed anywhere in `backend/src`.
- **ADR-002 (clause 4 corrected).** Adding `Microsoft.Graph` to
  `DependencyDirectionTests.ForbiddenSdkPrefixes` closes a real hole — nothing in that
  list matches it — but **cannot** enforce "one Graph call site", because that test
  scans only the fixed ADR-002 **domain-module** list and the adapter lives in a
  **host**. The enforcing test is **`SdkAllowListTests`** (every project, "hosts and
  tests included"), amended **package-scoped**: widening its single `AllowedProjectName`
  would make `Azure.AI.*` legal in `Raffa.Api` (security-architect's §J.1). The
  wave-wide map is stated once — `Azure.AI.*` → `Raffa.AiGateway`; `Azure.Identity` →
  `Raffa.AiGateway` + `Raffa.Api` + **`Raffa.Worker`**; `Microsoft.Graph` → `Raffa.Api`.
  **The `Raffa.Worker` entry is the hazard no lane named**: cloud-architect's RBAC
  transport puts `DefaultAzureCredential` in the worker, so **NW-27 turns that test red
  on a file NW-67 already edits** — `SdkAllowListTests.cs` is a single-writer file
  contended by two items, like `Program.cs`.
- **ADR-026 (§8–§10).** The invite 201 gains **`deliveryOutcome`**, a **non-nullable
  `enum`** of `"sent" | "mail_failed" | "no_transport"` — the field client-architect and
  ux-ui-designer both routed to this seat to name, with three values because a
  provisioning failure is a **502**, not a fourth success state. `mailDelivered` is
  **kept** (three seats ruled on its meaning this wave) under a normative biconditional
  — `deliveryOutcome == "sent"` **iff** `mailDelivered == true` — so the pane branches on
  one string and never combines two booleans. §6's deferral **closes**: ADR-025 §J.2b
  ruled re-issue **by replacement**, so `POST …/invites` replaces a live invitation in
  one transaction — no new endpoint, the `alreadyMember` 409 and the unique-violation
  concurrency backstop both **stay**, and the client-composed `DELETE`+`POST` fallback is
  **withdrawn**. This is what finally makes ADR-020's "Send a new invitation" affordance
  legal against D4's index, which is itself unchanged.

**ADR-014 gains its second amendment footer** (delivery-manager, owner). The
trunk-based model, the protected `main`, the tag-plus-approval promotion and the
whole w14 footer are unchanged. w15 sharpens the wave base — **the base SHA is
read at the gate, never quoted from a wave document** (the requirements name
`3c89d35` while `origin/main` on disk was already `6ae21b9`, and `packed-refs`
is stale for **every** branch ref this wave touches, so the loose ref wins) — and
adds the check w14 lacked: a behind-base wave's merge must leave a **zero
product-tree delta**, because three of the five differing files were *shorter*
on the process branch and a wrong resolution would have silently reverted the
previous wave's README and e2e work with no build, test or deploy noticing. It
also records that green means the **`build + test` job at the base commit**, not
the deploy (a red `main` is no `dev` deploy, which is no wave — the trap w14
closed blind to), that `integration` is **re-created** from the base rather than
merged into (it has diverged: `8ed3af1a` vs `271c3ae1`), and that w15's planned
CI-YAML set is **zero files**. Its one structural change extends w14 clause 2:
**a wave that changes `infra/` has two merges to `main`**, the infrastructure-only
PR first, because an HCP apply is triggered *by* the merge and therefore a single
merge event cannot satisfy "the apply is `CURRENT` before the image that reads
it deploys" — which w15 needs twice over, for the worker's fail-fast storage key
and the API's four `AzureAd__*` keys.

**ADR-015 gains its first amendment footer since 2026-09-01** (delivery-manager,
with cloud-architect's text and security-architect's permission ruling — the
same joint authorship the body records). OIDC federation, the two per-environment
service principals and the subject-claim pinning are untouched, and **no GitHub
secret, federated credential or environment is added**. NW-67 extends the body's
`:82-83` runtime-identity sentence to a **directory** API for the first time:
the existing per-environment workload managed identity receives Graph
`User.Invite.All` as an **application** permission, with no secret and no client
credential. The change that is genuinely this ADR's is the **apply plane**:
`infra/modules/identity/main.tf:41-42` records that the deploy job does not need
Graph, which stays true for *deploy* and stops being true for the identity that
runs the HCP apply — creating an `azuread_app_role_assignment` is itself a
directory write, so that identity needs `AppRoleAssignment.ReadWrite.All` +
`Application.Read.All` (or Privileged Role Administrator), granted **once, out
of band, before the first apply** and verified at the gate rather than
discovered from a red run. For a managed identity the assignment **is** the
consent, so no separate consent click is scheduled; and because one PR carries
Service Bus, ACS and Graph into a single state per environment, the resource is
`count`-gated behind `guest_provisioning_enabled` (default `false`) so a missing
directory right **degrades NW-67** instead of blocking the wave's other two
features.

**ADR-016 gains its third amendment footer** (delivery-manager, reconciled with
cloud-architect and security-architect), clauses continuing at **13**. w15 is the
wave that breaks its own w14 clause 1 — the **first per-environment API
configuration key**, precisely the case that clause predicted — so the barrier is
restated and the ordering built: **one infrastructure PR owning all of
`infra/**`, merged before the wave PR**, with `demo`'s keys flag-gated `false` at
that merge because both HCP workspaces watch the same `infra/` prefix and
`demo`'s infrastructure therefore moves while it is still serving the previous
wave's images. Every new key declares its absent-value behaviour and **none may
fail open**: the `ServiceBus__*` and `Invitations__*` keys are optional, the
worker's `ConnectionStrings__Storage` is fail-fast (the soft alternative marks
*every* document `Failed` while A15-2 still passes), and `AzureAd__*` is
**fail-closed but never crash-closed** — 401 with no header fallback, because a
boot-crash loop costs the wave its acceptance environment. Three findings shape
the rest: **a green `backend.yml` run does not prove the worker started** (no
revision-state assertion exists anywhere in the workflow, and three independent
Terraform gaps produce the identical silent symptom), so the gate gains one
post-deploy revision-state assertion and A15-2 is walked on deployed `dev`;
**w14 was never promoted** — the highest tag is `demo-v3` of 2026-09-04, so
`demo-v4` is cut at the gate to restore one-promotion-one-wave and w15 promotes
as `demo-v5`, with the three data-plane steps `demo` owes recorded as the
*previous* wave's debt; and the **web-before-backend ordering three seats asked
for is neither enforceable nor useful** — the outage window is symmetric, and the
direction that was requested is the one that can land writes attributed to
nobody. Clause 21 records that NW-05 takes `reprocess-tenant-documents.yml` out
of service at the code merge, failing loudly on a read before any write, and
that w15 neither repairs nor edits it because NW-31 owns that file in W16.

**ADR-005 gains a second w15 amendment footer** (cloud-architect, owner), clauses
**9–14**, written at the re-entry round. No resource, SKU or module changes and
the **$0.00** fixed-cost delta is unchanged; ADR-007 needs nothing this round
(`modules/identity` gains a block, not a module or an edge). **Clause 9 corrects
this seat's own number**: `max_delivery_count` moves **`5` → `8`** and the
value quoted at `:709` above, at `ADR-027:609` and at `:732`, is superseded by it.
ADR-027 §C7 discharged the app-side half as `MaxAttempts = 3` and closed its
arithmetic *exactly* — 2 abandons + 3 attempts = 5 — but a delivery is also spent
by failures that advance no attempt: the transient pre-claim error §C7 itself
names, §C6's abandons, and two this seat created and must therefore budget for —
**scale-in eviction** under `min_replicas = 0` and the 30-minute **renewal
ceiling** against `lock_duration = PT5M`. Realistic worst case is **7 deliveries
against a ceiling of 5**, so the broker dead-letters before the handler writes
`Failed`, which is the exact failure §C7 exists to prevent. `MaxAttempts = 3`
**stands unchanged** and the inequality holds *a fortiori*; the bound on a
looping message is **time, not count** (`P1D` + dead-letter on expiry), so the
headroom cannot loop and costs **$0.00**. **Clause 10** accepts §C6's hand-off —
a non-empty DLQ now also means *"an upload's commit failed"* — and lands it on
**two new operator checks** rather than a resource, noting that the dead-letter
queue is **durable and swept by nothing**, which is what makes "recoverable"
true. **Clause 11 fills a gap in this seat's own §4**: the `email` **optional
claim** (ADR-010 §2.4 / S15-9) is a real Terraform addition — verified absent
from all of `infra/` — on `azuread_application.api`
(`modules/identity/main.tf:57`) only, ungated, and **the plan must show an
in-place update (`~`), never a replacement (`-/+`)**, because replacing that
registration mints a new client id and takes out `AzureAd__ClientId`, the `aud`,
both SWAs and `web.yml`'s scope literals behind a green CI run. **Clause 12**
accepts ADR-011 §2c's three conditions verbatim and offers one narrowing —
a **topic-scoped** authorization rule instead of the namespace-wide one this
seat's own draft named, shrinking condition (iii)'s residual to the single DLQ
ADR-009 §5 already bounds to ids, subject to proof. **Clause 13** answers
OQ-w15-ca-01: **no ingress ceiling is pinned and client-architect's 120 s
stands**, because NW-27 makes the API's longest synchronous request *shorter*.
**Clause 14** confirms delivery-manager's two asks (the infra PR is **only**
`infra/**`, which `infra/README.md` is inside; `demo`'s two flags default
`false`) and constrains the gate's new revision-state assertion: with
`min_replicas = 0` a healthy worker has **zero replicas at rest**, so "a replica
is running" fails on a healthy environment and "the revision exists" passes
through a crash-loop — the assertion belongs against a worker **given work**,
which is A15-2's walk.

**ADR-011 gains a second w15 amendment footer** (security-architect, owner),
clauses **6–13**, written at the re-entry round. No new Key Vault secret, no new
Key Vault permission and no new CI credential: w15 still adds exactly one
(`acs-connection`). **Clause 6 corrects this seat's own ruling** — the first-round
`none — ADR-015` reasoned about the **deploy** identity (`raffa-sp-dev` /
`raffa-sp-demo`) and never about the **apply** identity, and creating an
`azuread_app_role_assignment` is itself a **directory write**, so the new privilege
lands where this seat did not look. **Clause 7** prices what nobody priced:
`AppRoleAssignment.ReadWrite.All` grants **any** application permission of **any**
API — Graph's own `Directory.ReadWrite.All` and `RoleManagement.ReadWrite.Directory`
included — to any service principal, so a standing grant would let whoever can merge
`infra/**` mint arbitrary directory privilege in the **customer's** tenant, for a
need that is one assignment, once. **Clause 8** finds that of the three options at
`ADR-015:126-128`, **Cloud Application Administrator cannot perform this assignment
at all** (that role excludes Microsoft Graph application permissions, and
`User.Invite.All` is one), so the two that work are both tenant-wide escalation and
the narrow-sounding one fails — routing an operator to a red apply and then to an
escalation. **Clause 9 rules `ADR-015` clause 4's fallback the default**: a Global
Administrator makes the single assignment out of band, the resource stays
`count = 0`, the later `import` leaves the apply identity needing only **read**, and
revocation stays a human act; the standing grant survives as a fallback under four
conditions (never Privileged Role Administrator; exactly one assignment and never one
whose principal is the apply identity; re-reviewed each `infra/**` wave; recorded in
the ADR-016 runbook). No ADR body changes and no seat re-works anything — both
options were already on delivery-manager's page. **Clause 10** makes it a gate check
with a named expected outcome per shape, discharging delivery-manager's ask.
**Clause 11 co-signs ADR-005 clause 11's `~`-not-`-/+` rule as an *authorization*
requirement** — the API application's client id **is** the `aud` ADR-010 w15 §1.2
pins, so a replacement fails every token at once and the tempting repair is the
`ValidateAudience = false` that §1.2 forbids — and corrects its premise: ADR-010 §2.4
already says the design does **not** depend on the `email` claim, because `oid` is
bound at invite time, and the stronger reading invites an **email match** on a mutable
identifier. **Clause 12** binds ADR-027 §C6's `DeliveryCount` split to the message's
own tenant scope (a `job-not-found` is never a cross-tenant lookup) and accepts
ADR-005 clause 12's topic-scoped narrowing of ADR-011 §2c(iii), with the namespace
rule standing if the narrowing proves inexpressible.

**ADR-012 gains a second w15 amendment footer, §15–§20** (client-architect, owner),
written at the re-entry round. **§15 is the finding**: the SPA's identity
configuration is not in the bundle and not in the repo — `web.yml:144-205` writes
`dist/config.json` during the **deploy** job from four live Azure lookups plus two
hardcoded scope literals, and `web.yml` fires only on `web/**`,
`.github/workflows/web.yml`, `.github/actions/azure-login/**` and
`scripts/write_web_runtime_config.py` (`:9-23`), with **no `workflow_dispatch`**.
`infra/**` is in neither workflow's filter, so the wave's infrastructure-only PR
(ADR-014 w15 clause 5) applies with **no web run at all**: anything `config.json`
carries that the apply moves stays stale on the deployed SPA until an unrelated
`web/**` push. Not "behind a green CI run" — behind **no run**, and on `dev` there
is no manual redeploy; nor is it loud at boot, since `AppConfigError`
(`appConfig.ts:44-50`) rejects only *missing or malformed* config and a stale
client id is neither. The rule is a **gate step**, not a task, and w15's own answer
is **checked and negative**: the one registration touched is
`azuread_application.api` (`identity/main.tf:57`), not the public client (`:116`),
and a Container Apps FQDN is per app, not per revision. **§16 adopts ADR-005 clause
11's `~`-not-`-/+` rule and corrects its client-half collateral**: both SWAs record
the **public-client** id (`web.yml:202` ← `tags.oidcPublicClientId` ←
`identity/main.tf:43-44`, and `appConfig.ts:25` names the field), not the API's, so
that item is struck — clause 11's own next bullet says the public client is not
touched; the scope literals resolve against `identifier_uris` (`:70`), a
**name-based** string, so a replacement re-creates them intact and what it actually
takes out is the service principal (`:107`) and the pre-authorization (`:155-161`),
turning every sign-in into a consent prompt. **And the inversion**: the SPA binds by
**URI string**, so the shape clause 11 declares safe is the one that breaks it — an
in-place `~` changing `identifier_uris` passes the check and silently breaks every
login, because `web.yml:204-205` is a hand-copied duplicate of `identity/main.tf:70`
with no test, no build step and no plan assertion comparing them. **The plan gains a
second assertion: `identifier_uris` shows no diff at all.** **§17 is the second
finding, against a property withdrawn at this round**: ADR-027 §C6 honestly retires
D3's "the database owns the terminal state", which is what made the 2 s poll's
termination a theorem — `useDocumentsList.ts:105-108` polls while any row is
`Uploaded`/`Processing` and `:110-119` clears the interval only when that boolean
flips, so §C6's permanently-stranded row means an open tab polls **every 2 s
forever** (~1,800 requests/hour/tab) while the screen shows a *not ready yet* state
that is true and never resolves. The poll gains a **no-change budget** with an
explicit resume, under three prohibitions — never re-label the row `Failed`, never a
client timer feeding the bar, never a second definition of terminal —
copy owed by ux-ui-designer as **OQ-w15-ca-05**. **§18** closes the counts clauses:
§C9 adopted this seat's `isAttentionStatus` ask in full, §C5 and ADR-018 clause 3
resolve §13.6 to its second branch, and the one mechanism still missing is a
**signature** — `buildKbSummary` (`documentTable.ts:154-160`) becomes a function of
`counts` (`all`, `needsReview`), after which no page-derived number survives screen
3; §C9.1 lands as a client prohibition (five overlapping projections, so no chip is
computed from another). **§19** discharges OQ-w15-ca-01 (120 s stands, ADR-005 clause
13), -ca-02 (scheduled as a numbered walk), -ca-03 and -ca-04, and **accepts
delivery-manager's refusal of "web before backend"** on evidence. No route changes
this wave, no new ADR from this seat, and no CI-YAML file added to a zero-file set.

**ADR-020, ADR-018 and ADR-019 gain second w15 footers** (ux-ui-designer,
re-entry round, 2026-09-14) — three findings, two of them against this seat's own
round-1 text. **ADR-020 §6** corrects §1.3: a refusal has **four producers** and
only the content gate becomes a server row, so the oversize check
(`uploadPipeline.ts:105-108`, no HTTP call at all) and **413/415** (`:122-125`,
kept in-request by the split gate) render as a **local row** — client-architect's
mechanism (ADR-012 §13.4), adopted, which is what makes "one file, one row on
every path" true rather than aspirational. Same `.tag-outline` **Not added** on
both, lead-in dropped on both, and the oversize sentence loses its interpolated
filename because the row already has a filename cell — licensed by provenance:
that sentence is app-authored, while `getRejectionReasonCopy`'s two are
requirements copy (`requirements.md:186`). **§6.3 is the finding**: `:123` renders
`result.error ?? "Not added: this file could not be added."`, a user-facing
sentence **the API authors** and no oracle contains, on the one refusal path that
survives NW-27 — replaced by two designed sentences keyed on the status code,
under the rule *a server field may select the sentence a user reads, never supply
it* (the same convention NW-67 already uses). **§7**: ADR-027 §D6 persists the
rejection reason but §D7's `items[]` and §11's contract list **carry no reason
field**, so after the reload §1.1 exists to survive, a terminal **Not added** row
renders with an **empty hint** — ask to software-architect for a reason **code**
from the closed set (`AdmissionDecision.cs:23-32`), copy staying client-side;
assumption in force is tag-and-no-hint, never a remembered 422. **§8 answers
OQ-w15-ca-05**: the stopped poll is a **list-level notice**, not a row state and
not an error — *"Nothing has changed for five minutes, so this page stopped
checking for updates."* + **"Check again"**, `.hint` + `.btn-secondary`, the actor
being the page and never Raffa.ai, and never implying failure. **§8.3 generalises
it, and this is the half ADR-012 §17 could not see from one hook**: NW-61 turns
Ask's one-shot gate fetch (`ask/index.tsx:100-105`, verified) into a 2 s poll, so
the wave adds a **second** unbounded re-read on the surface where "not ready yet"
is the whole promise. **ADR-018 clause 6** therefore states it at IA level — *a
"not ready yet" state that depends on a repeating re-read stops on a bounded
no-change budget and offers an explicit resume; a surface that cannot offer the
resume must not claim it is waiting* — binding five surfaces with one sentence and
one label; **clause 7** fences the counting table (a pre-storage refusal has no id
and no client-side increment may "fix" the chip). **ADR-019 clause 4** adds **no
semantic row** — the treatment is keyed on the reading — but names the third edit
site: `DocumentStatusTable.tsx:94-96` is a ternary, so a forgotten branch renders
a refused file as **Processing** forever, compiling clean; clause 5 confirms the
paused notice needs no component and no token. No new ADR from this seat.

**ADR-015 and ADR-016 gain second w15 footers** (delivery-manager, re-entry
round, 2026-09-14) — four corrections, **three of them against this seat's own
promoted clauses**, and none of them reachable by re-reading its own lane.
**ADR-015 clause 6** strikes one of the three options its own clause 2 offered
for the apply identity: **Cloud Application Administrator excludes Microsoft
Graph *application* permissions**, and `User.Invite.All` is one
(security-architect, `ADR-011` §8) — it is the narrowest-sounding entry on the
list, therefore the one a least-privilege operator picks first, and it fails **at
the apply**, producing the red HCP run clause 2 exists to prevent. **Clause 7
inverts the default**: clause 4's out-of-band Global Administrator grant becomes
the preferred shape and the standing grant the fallback, on security-architect's
pricing (`ADR-011` §9) — `AppRoleAssignment.ReadWrite.All` grants any
application permission of any API to any service principal **including itself**,
so held by an automation identity triggered by a merge to `infra/`, whoever can
merge Terraform can mint arbitrary directory privilege in the customer's tenant,
for a need that is one assignment, once. **Clause 8** gives the gate check a
named expected outcome **per shape**, so it proves the answer either way rather
than becoming a shrug. **ADR-016 clause 23 rewords clause 17's revision-state
assertion, which as written fails on a healthy environment**: cloud-architect's
`min_replicas = 0` means a healthy worker has **zero replicas at rest**, so *"a
replica is running"* fails when nothing is wrong and *"the revision exists"*
passes through a crash-loop — the check splits into a static half where **zero
replicas at rest is a PASS** and a dynamic half that is A15-2's walk, on the
rule that *a gate check that fails when nothing is wrong gets waived, and the
waiver is what the next silent worker death hides behind* (wording this seat's
at cloud-architect's request, `ADR-005` §14). **Clause 24** puts the
dead-letter queue into the promotion sequence as a **standing condition to be
read** — empty before the acceptance walk, and after it read with §C6's two
meanings and **routed, never drained**, since nothing sweeps it and no
cross-tenant sweep may exist. **Clause 25** adds two *different* plan assertions
on one resource: `azuread_application.api` shows `~` and never `-/+`
(`ADR-005` §11), **and** `identifier_uris` shows no diff at all (`ADR-012` §16)
— the inversion, because the SPA binds by URI string, so the shape the first
assertion declares safe is the one that breaks every login. **Clause 26** names
the other edge of this seat's own two-merge structure: `web.yml` builds
`dist/config.json` in its deploy job and has **no `workflow_dispatch`**
(verified: `pull_request`/`push`/`workflow_call` only), so a PR 1 that ever
changed a config-borne value would leave an outage **behind no run at all** —
**checked negative for w15** and recorded as a negative result, to be asked by
the next wave that touches `infra/` rather than inherited. No new ADR from this
seat; w15's CI-YAML set stays **zero files**.

**ADR-027 (C12–C13) and ADR-020 (10–12) gain a further w15 footer** (built by
hand, 2026-09-14, after the first real twenty-file batch on `dev` measured
~100 s/document against ~30 s cold start and found a UX failure no council
lane had modelled: twenty rows reading "Processing" with a bar stuck at 0% for
minutes, honest and still indistinguishable from broken). **ADR-027 §C12**
adds priority by claim — one nullable column (`extraction_job.prioritised_at`),
`POST /api/documents/{id}/prioritise`, and four lines inside the Worker's
existing handler that let one delivery claim a prioritised job in its own
tenant ahead of its own, bounded to one per delivery — with **no second Service
Bus subscription** (OQ-w15-012 stays exactly one) and **no new settlement
outcome** (a superseded message's own delivery loses its claim to a row that
already exists, which is C6's `ClaimLost` unchanged). **ADR-020 §10–12** moves
the fix to where the batch-drop failure actually lives: a document reads
**"Uploaded"**, never a stalled bar, from the instant its row appears (a local
pre-201 row included, the one declared and bounded exception to "screens never
infer"); the six-stage checklist and the "Queued…" reading move into a fourth
state of Documents (`?progress=<id>`, the same "state of the screen, never a
route" idiom as `?review=`) opened from the row's own filename, which also
fires the priority call. **ADR-012 gains a short fourth footer** (§21) for the
one new client method: `prioritiseDocument` through the same one
`Authorization` choke point as the other 36, the completeness gate now
counting 37. Task E16/F03/US02/T01 (raffa-backend) and T02 (raffa-web); no
council round, no new ADR, no infrastructure change.

`waves/w15.md` carries the per-item rows and the votes.

## Nothing the product knows lives only in a browser tab (wave w16, appended 2026-09-14)

ADR-001…027 keep their original Decision outcomes and their existing footers.
**No ADR is superseded by wave w16 and no work item is cancelled** (the raw file
carries no "cancels / replaces" statement; `w16-requirements.md` §6 records
"none"). Items at this table: NW-07, NW-08, NW-31, NW-32, NW-11, NW-12, NW-13,
NW-21, W16-01 (`reports/architecture/waves/w16.md`). Baseline `f0b3436`.

**ADR-001 gains a w16 amendment footer** (product-owner, owner of the ADR),
clauses 0–6, serving NW-07, NW-12, NW-13 and NW-21 with two acceptance rulings
on NW-32 and NW-08. **Clause 0** records that all four items expose facts the
product already stores, so **no §1.2 non-goal is touched and no capability is
added** (NW-52's paid market API stays DEFERRED), and that none of the four
surfaces is on the pilot script — so none may claim pilot priority or grow a new
screen. **Clause 1** reduces NW-07 from *migrate* to *record*: pre-w15
conversation rows keyed by the MSAL username are **left in place, never remapped,
never deleted**, because a wrong email→`oid` remap would hand one user's threads
to another — worse than the loss it repairs — and **A16-1 is reworded in four
clauses** because the published wording (same user, different UPN casing) is
vacuous against an `oid` key; the promoted task's DoD line `:69` **must not be
implemented**, since satisfying it re-introduces the normalization ADR-010
rejected. **Clause 2** fences NW-12 to the read-back of the outcome on the quote
in front of the user, leaving the history / levers UX to NW-57 (W18), and rules
that `E08/F03/US01` AC-4 — today discharged by a write-only `sessionStorage`
mirror, the exact pattern ADR-012 §1 forbids — is **re-pointed** to NW-12 + NW-21
rather than cancelled: the story stays `active`, with no status banner and no
`superseded:` line. **Clause 3** makes the four *named* negotiation steps of
`screens-v2.md:106-108` canonical, stores them by **stable key and never by array
index** with the parameterized label staying client-side (w14 clause 3), and
scopes ticks **per contract**, not per renewal cycle. **Clause 4** rules
OQ-w16-005 on the **status** move — no surface renders a realized money figure,
the per-opportunity `realizedAmount` is already on the wire, so nothing is
silently dropped — fences w16 against rendering `SavingsKpiSummary.Realized` as
money, re-classifies the realized-amount gap as the unmet AC-1 of the `active`
story `E04/F03/US01` ruled **head of W17** (new **OQ-w16-po-01**) rather than an
ADR-001 capability change, and rules that an outcome may be *resolved* to an
opportunity but **never guessed** — no unambiguous match means recorded
**unlinked**, nothing moved. **Clause 5** ratifies A16-4 for NW-32 (whose
published wording could not fail: an unsigned POST is already 401) and rules
**OQ-w16-003 `no`** — the audit read closes through the API and a typed client,
so **ux-ui-designer stays unseated** and ADR-018 / ADR-020 are untouched.
**Clause 6** records that every in-wave item is `should` except W16-01 (`could`),
so no must/should inversion exists and ADR-001 w15 clause 11 does not fire, and
fixes the release-valve order — W16-01, then NW-12 narrowed, then NW-13 — with
NW-11, NW-07's isolation clause and NW-21's fence **never narrowed**. No new ADR
from this seat.

**Clauses 7–8 were appended to the same footer at the table's second round**, for
two questions that reached this seat after clauses 0–6 were written; nothing above
them is rewritten. **Clause 7** rules **OQ-w16-sa-01**: ADR-028 §D5's *clause 2*
(deterministic supplier-name resolution) **ships in w16**, because with
zero client change the explicit-id path alone is unreachable from the UI, so an
API-only close would leave the product in exactly the state NW-21 names — with
two fences: A16-8's deterministic close stays the **explicit-id** path (a decline
is a pass), and **no surface may present an outcome with `savingsPropagated: null`
as a realized saving**. **Clause 8** re-words **A16-3**'s workflow half, because
the table declined *both* of its published branches: the close becomes an **Admin
resubmitting a document on deployed `dev` through the product's own path**, and
what w16 does **not** restore — the **bulk whole-tenant** reprocess — is recorded
as deferred to **W17** with its shape designed, rather than dropped. No capability
is added, no §1.2 non-goal touched, and no `must` sits behind a `could`.

`waves/w16.md` carries the per-item rows and the votes.

**Six ADRs gain a w16 amendment footer from software-architect (owner of all
six); their Decision outcomes are unchanged and none is superseded.**

- **ADR-002** — `contract_negotiation_step` is owned by
  `Raffa.Documents.Contracts` (`Raffa.Renewals`'s allow-list makes it
  impossible); NW-21's savings resolution is composed in `Raffa.Api`, the only
  project allowed to reference every module; `Raffa.Suppliers.Products` gains
  one **read-only** name → id lookup; NW-32's actor is a required positional
  parameter on nine service types across five modules (no ambient accessor, and
  caller-less writes take a reserved `system:<component>` principal); two
  deletions authorised — `WorkspacePrincipalAuthorization` whole, and the R0
  queue trio in `Raffa.Worker`. No project, host or allow-list moves.
- **ADR-003** — **one** new tenant table, `contract_negotiation_step`, unique
  `(tenant_id, contract_id, step)` with the row's **presence as the tick** and
  its RLS policy in the same migration; plus the three schema changes this wave
  **refuses**, each with its reason (no `savings_opportunity.quote_id`, no
  widening of `renewal_action`, no new `negotiation_outcome` column).
- **ADR-021** — the single regenerated script is `documents-contracts.sql`, and
  **no CI array moves**: `backend.yml`'s two arrays list modules and that module
  is already in both, so a `backend.yml` diff this wave is a defect.
- **ADR-024** — `OQ-askv2-005` **retires** with NW-07 (the conversation key is
  the token subject; the stale `ConversationsEndpointExtensions.cs:23-37`
  paragraph is deleted; pre-w15 rows are recorded as retired, never re-keyed);
  and the capability catalog is **served whole** — the role filter is deleted
  with `X-Role`, `roleGate` stays on the wire as a presentation signal, with the
  non-Admin suggestion-chip consequence recorded (**OQ-w16-sa-02**, W17).
- **ADR-026** — `/api/audit` joins the contract together with the
  `info.description` sentence that denies it; the route's tenant derivation
  moves from a claim to a **membership-verified `X-Tenant-Id`** (OQ-w16-002
  answered, security's 401→400→404→403→200 ladder); the `X-Role` parameter and
  the stale `X-Workspace-Role` prose leave while **`X-Tenant-Id` stays**; the
  parameter edit's gate is a **grep, not a green build**; five theme-B
  read-backs are published (NW-21 adds none); the stale `200`-with-`pagesParsed`
  reprocess prose is corrected. **Two contract tasks, never six.**
- **ADR-027** — `:198`'s "reprocess collapses into re-enqueue" is a property of
  the **handler**, not a licence for a credential-free operator path
  (OQ-w16-004's zero-cost premise is withdrawn, converging with
  delivery-manager's D1 and security's identity-plane answer); the **R0
  placeholder queue is deleted** (W16-01) while `InMemoryExtractionQueue`
  stays; the reprocess response is **202**, and every stale record must match it.

New ADR from the w16 table:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-028 | Server-side state for renewal actions, quote outcomes and negotiation steps, and how an outcome finds its savings opportunity | software-architect (+ client-architect on shape, product-owner on scope, security-architect on RLS) | Three browser stores retire against the rows that already exist: the renewal action is read back by `GET /api/renewals/{id}/action` and embedded in list rows as **`savedAction`** (never `action`, which is a calculator's output); the quote becomes a resource (`GET /api/quotes`, `GET /api/quotes/{id}` with its outcomes embedded) without overloading the assessment endpoint; negotiation step ticks become **one** new tenant table owned by `Raffa.Documents.Contracts`, keyed by the four **named** steps and never by array index, with Undo as two idempotent writes rather than a hidden cross-module side effect; and the savings link is **named or deterministically resolved, never guessed** — the product's own supplier-identity rule, exactly one open opportunity or none at all — with **no column, no migration, no contract delta and no client change**. |

ADR-028 is the **state and shape** half of this wave; ADR-026's w16 footer is
its contract half, and ADR-003's is its schema half.

**ADR-028 gains a round-2 footer** (`## Amendment (2026-09-14, wave w16 — table
round 2)`, software-architect), after product-owner ruled **OQ-w16-sa-01** in
ADR-001 w16 clause 7. **§D5 is unchanged** — it already shipped clause 2 — so the
footer adds mechanism, not a decision: product-owner's *must not* (an outcome
with `savingsPropagated: null` is never a realized saving, no total absorbs it)
is made **structural** — a declining resolution never invokes `PropagateAsync`,
so no opportunity row, status or `RealizedSavings` row is written and the KPI
**cannot** absorb it, with a byte-identical-KPI test to prove it. Two checks run
at the table: clause 2 costs **no contract delta** (`savingsPropagated` is
already a required `["boolean","null"]` property with no description —
`raffa-api.v1.json:4356,4415-4419`, `schema.ts:504`), so the wave stays at
**five contract paths and two contract tasks**; and clause 2 **falsifies**
`backend/README.md:2296-2304`, a correction folded into NW-21's own
single-writer file so it adds no task — left undone it would re-create, inside
w16, the stale-record defect NW-31 exists to delete. The footer also records
that NW-31's header deletion **cannot break** the acceptance path ADR-001 clause
8 names: `reprocessDocument` sends only `X-Tenant-Id` and the bearer
(`web/src/api/client.ts:1943-1950`), so the two round-2 rulings are compatible
and no client task is smuggled in. No ADR is superseded; no Decision outcome is
touched.

**Five ADRs gain a w16 amendment footer from security-architect (owner of all
five). No new ADR, none superseded, no Decision outcome or existing clause
edited** — the wave's identity, authorization and audit delta is entirely in
footers:

- **ADR-009** — the w15 §6 forward bullets for the W16 head are **discharged**,
  and the NW-07 bullet's evidence line is **corrected by the seat that wrote it**
  (`TryResolveUserId` does not exist on this tree; the conclusion stands, re-derived
  from the sites that do). The wave's **one new tenant table**
  (`contract_negotiation_step`, NW-13) is bound to w14 clause 8 / w15 clause 4a —
  `ENABLE` + **`FORCE`** + `tenant_isolation` with `USING` **and** `WITH CHECK`, in
  the table's own migration — and to the part that is **not** automatic: w15 clause
  4c's CI guard covers only `TenantScopedEntity` subclasses of
  `DocumentsContractsDbContext`, which is where SA's module ruling puts it, **so
  the free branch is available and must be taken deliberately**; placed anywhere
  else the table owes a hand-written RLS test with its **own unprivileged Postgres
  role** (clause 4d — Testcontainers hands you a superuser and the obvious test is
  green and worthless). No policy text is rewritten this wave.
- **ADR-010** — **one comparison rule for the identity column.** The email leg is
  deleted from both authorization comparators (`CallerContext.cs:143`,
  `WorkspaceRoleResolver.cs:80`), which today can grant **membership and role on a
  match against `workspace_user.Email`** — latent, not exploitable, and one
  GUID-shaped `Email` row away from being a grant. Normalization is fixed **at the
  source** so all four implementations of the predicate agree by construction, and
  the shipped `identity_self` policy is untouched. Pre-w15 conversation rows may be
  recorded or retired but **never re-keyed by an email match** — that would move
  ownership through the very leg being deleted.
- **ADR-011** — clauses 14–19. **Who may read a tenant's audit trail: a live
  `Admin` membership in that tenant and nobody else**, on the standard ladder with
  no bespoke version; the "no `?tenantId=`" property survives **strictly stronger**
  as *authorized selector, never an authorization input*. On the write side, **an
  audit row that cannot name its actor is not written** (required parameter, no
  default), non-human writes take the reserved **`system:<component>`** principal
  already live in the codebase — provably non-colliding with an `oid` and greppable
  — and the **append-only trigger is never dropped**, so the existing
  `"unattributed"` rows stay permanent and the gap stays honest and bounded.
- **ADR-022** — the interim **identity** posture is fully retired; only the fixture
  seed (data, not identity) remains. The capabilities gate drop is accepted **with
  the rule it now depends on**: the catalog is served whole to an unauthenticated
  caller, so it must carry **no tenant data of any kind**, proven by an identical
  response for no token / non-Admin / Admin. `roleGate` is **presentation, never
  authorization**. OQ-w16-004's token option is refused on identity-plane grounds
  independent of the infrastructure evidence. **`X-Tenant-Id` does not retire** and
  the sweep's grep is **paired** so it cannot overrun.
- **ADR-025** — **§K**: §I's seam swap fires on the last claims-reading route, and
  `WorkspacePrincipalAuthorization` is **deleted whole rather than stripped of its
  constant** (what survives the minimal repair is a working, fail-closed,
  helpfully-named claims authorizer for the next endpoint to pick up — which is how
  this defect arrived). §K.3 adds the assertion a green ladder test cannot give:
  a token carrying `tenant_id` **and** `roles` for a non-member tenant must get
  **404**, proving the claims path is deleted rather than merely unreachable.

**One ADR gains a w16 amendment footer from client-architect** (owner of ADR-012,
ADR-013 and ADR-018's route map), seated on NW-08, NW-31, NW-11, NW-12, NW-13 and
NW-21. **ADR-012's fourth footer, clauses 21–31**; body and §1–§20 unchanged.
**ADR-013, ADR-018 and ADR-020 are `none`** this wave — no route added, moved or
removed, no screen, state or copy, mobile scaffold untouched.

- **The three `sessionStorage` stores retire** and §1's disposition table gains
  their rows: `raffa.renewals.actions`, `raffa.quotes.negotiationOutcomes` and
  `raffa.contract360.steps.<id>` — all **deleted**, each **with** its read-back
  and never before it (a deleted store with no GET is a regression, not a step
  toward one).
- **When a route gets an `ApiClient` wrapper — re-grounded rather than asserted.**
  The lane draft claimed a caller-less wrapper is always dead code; the codebase
  carries a **named counter-convention** (`getQuoteAssessment` is wrapped with no
  caller "for API-contract completeness", `client.ts:811-813`) alongside the
  opposite one (`raffa-api.v1.json:6` documents two routes "for completeness, no
  `client.ts` wrapper"). Restated as a test: **a wrapper is added in the same task
  as its first caller**; a caller-less route is published and left unwrapped, the
  omission recorded in `info.description`. Unwrapped in w16: `/api/audit`,
  `GET /api/renewals/{id}/action`, `GET /api/quotes`.
- **`web/src/api/client.ts` is a one-writer-per-phase file** — the footer's own
  correction of the draft, which recorded it as having no writer at all. It gains
  three hand-written methods across NW-12 and NW-13, it is hand-written glue
  (`:1-8`) rather than generated, and two tasks editing it in one phase is the
  defect §3 already prevents for the contract file. ADR-028's single-writer list
  is backend-only; this file is this seat's.
- **Two evidence corrections to the seat's own draft**, recorded rather than
  re-worded: Contract 360 **already** fetches `getRenewals`
  (`contract360/index.tsx:115`), so NW-11 needs **no new wrapper** and the
  embedded `savedAction` reaches all three surfaces; and the quote screen's mount
  call is the **recalculate POST**, not the assessment GET the draft's shape
  preference assumed (`quotes/index.tsx:91`) — so that preference would have
  shipped a read-back the screen cannot see, and ADR-028 §D2's decline of it was
  correct on stronger grounds than it was given.
- **Fabricated facts and floods.** A tick the server rejected **reverts** and never
  survives on screen; §D4's two-write Undo is accepted with its ordering ratified
  (ticks first — an action with no ticks is honest, `NotStarted` with four ticks is
  a contradiction). **OQ-w16-ca-01 ruled** by this seat because product-owner is
  unseated on NW-11: Savings' pseudo-opportunity rows are **retired**, since a
  session-scoped handful becomes every renewal action in the tenant permanently
  once it is read back. The product half — whether Savings shows tracked actions at
  all — is deferred to W17 as a **designed** section.
- **Contract-file discipline.** NW-08's path must land **with** the
  `info.description` sentence that currently denies documenting it, or the contract
  contradicts itself — the same stale-record class NW-31 deletes. NW-31's deletion
  changes **no generated byte** (the generator parses only `responses`), so its
  gate is a **paired grep**, not a green build. Product-owner's "plus a typed
  client" is satisfied by the **generated type**, not by a method with no caller.

**Two ADRs gain a w16 amendment footer from delivery-manager** (owner of
ADR-014, ADR-015 and ADR-016), seated on **NW-31** plus the process question
**OQ-w16-008**. **ADR-016 clauses 27–35** (numbering continuous, per that ADR's
own convention) and **ADR-014 w16 clauses 1–5** (numbering restarts, per *that*
ADR's convention — the lane draft applied ADR-016's rule to ADR-014 and proposed
"continue at 8"; corrected before promotion). **ADR-015 is `none`** — no
federated credential, GitHub secret, subject claim or Graph right changes this
wave. No ADR is superseded and no body or earlier footer is rewritten.

- **OQ-w16-004 answered "neither option", because its premise was false.** The
  re-enqueue path was assumed to need no identity, secret or Terraform. Both
  Service Bus roles are granted to the **container-apps workload identity only**,
  topic-scoped (`modules/servicebus/main.tf:71-102`); CI's `raffa-sp-<env>` holds
  exactly one Terraform-granted role, `Key Vault Secrets User`. ADR-027 `:198` is
  a property of the **handler**, not a licence for an operator path. **There is no
  zero-cost repair** ⇒ **w16 buys CI no new credential**, and the wave's cloud
  delta is zero **because the API steps are deleted, not because re-enqueue was
  free**. Three seats converged from independent evidence.
- **The reprocess workflow is broken on three axes, and a credential repairs
  one** — 401 since NW-05, a POST loop that accepts only `200` where NW-27 now
  returns `202`, and a `%PDF` assertion that races asynchronous work. **The repair
  is a rewrite, and the rewrite is what does not fit this wave.**
- **Disposition: `reprocess-tenant-documents.yml` deleted,
  `verify-tenant-corpus.yml` added** — the credential-free half (OIDC login, Key
  Vault, `psql` with the tenant GUC, the `%PDF` and supplier assertions) kept, the
  API steps and all four `X-*` headers deleted, a reporting worklist added. **The
  job reports; it does not mutate.** This seat's own keep-the-filename fallback is
  **refused**: a file named `reprocess-*` that never reprocesses is a new instance
  of the stale-record defect NW-31 exists to delete. **The bulk whole-tenant job is
  designed and deferred to W17**, and must appear in the wave's known-gaps record.
- **Three shortcuts named so they are refused rather than discovered** — a SAS
  key in Key Vault, re-adding any `X-*` header, and (the dangerous one) inserting
  `extraction_job` rows via `psql`: **nothing sweeps them**, so a row with no
  message is a permanently-`Queued` job the UI renders as "processing" forever.
- **The close record becomes wave-scoped.** `wave-close.md`'s **generic filename**
  is what made it a trap — stamped pre-merge, listing 9 of 11 delivered w15 tasks
  as undelivered, and permanently the newest file with that name. Rule:
  **`wave-close-<w>.md`, written after the `integration → main` PR merges**, and
  it states the promotion outcome even when there is none. **OQ-w16-008** has the
  same root cause and is ruled: the operator stamps the missing `w15.hitl-ok` at
  the w16 HITL gate — accurate, not a rubber stamp — and **no task is created**.
- **The baseline carries an infra delta this wave did not write** (PR #118 —
  `worker_max_replicas 3→5`, `MaxConcurrentCalls = 4`). It is an **operator
  apply**, not a wave task and not ADR-014 w15 clause 5 firing; it **cannot roll
  the running image back** (`ignore_changes` on both container apps); it **does not
  gate A16-3**; and the wave's negative assertion must be a **two-dot diff**, never
  "no infra commits in the range". `demo` is now **three promotions deep** and its
  next one is the first that is **not flag-only**.
- **One PR, two CI-YAML files, and a corrected wave order.** The
  final-integration task asserts the empty `infra/` diff, the exact two-file
  workflow diff, and a **paired** retirement grep (`X-Role`/`X-Workspace-Role`
  gone **and `X-Tenant-Id` present**, so the sweep cannot overrun). **The lane
  draft's recommended skeleton was wrong and is corrected here**: `NW-11 · NW-12 ·
  NW-13` in one phase fails `check_single_writer.py` twice — on `client.ts` and on
  `contract360/index.tsx` — neither collision being in the intake's constraint
  list. Both came from client-architect, and both live inside the one artefact this
  seat owns.

## The product officializes what it knows (wave w17, appended 2026-09-15)

ADR-001…028 keep their original Decision outcomes and their existing footers.
**No ADR is superseded by wave w17 and no work item is cancelled** — the raw
file carries no "cancels / replaces" statement and `w17-requirements.md` §6
records "none"; `E04/F03/US01` is *completed in part*, not cancelled, and keeps
`status: active` with no banner. Items at this table: NW-72, NW-73, NW-71,
NW-20, NW-22, NW-62, NW-26, NW-63, NW-64, NW-65, NW-66
(`reports/architecture/waves/w17.md`). Baseline `d3d2d24`. New ADRs written at
this table are listed by the seats that author them (next free number at the
time of this row: **ADR-029**).

**ADR-001 gains a w17 amendment footer** (product-owner, owner of the ADR),
clauses 0–7, serving NW-72, NW-71, NW-20, NW-22, NW-62, NW-64 and NW-66, plus
rulings on NW-75, NW-23 and NW-25, which are not at this table. **Clause 0**
records that every in-wave item renders or decides a fact the product already
extracts and stores, so **no §1.2 non-goal is touched and NW-52 stays
DEFERRED**; it **discharges three w16 forward promises** — clause 4's "head of
W17" (NW-72 is row 1), clause 8's deferred bulk reprocess (NW-73 is row 2) and
clause 6 valve 1's W16-01 slot, which is **not owed** because W16-01 shipped in
w16 (`d3d2d24`, PR #129, verified rather than assumed) — and notes that, unlike
w16, **this wave stands on the pilot script**. **Clause 1** lifts *and replaces*
w16 clause 4's money fence: realized money renders only from `RealizedSavings`
rows **grouped by currency, never summed**, the estimate-summed range stays a
defect to fix rather than to render, and an unlinked outcome enters no total.
**Clause 2** sets **one bar at 90 % for every field including the five critical
ones, with no always-review list**, supersedes **five** oracle lines on the
record (spec `:333-335` and `:341`'s threshold half; pilot `:32`, `:46`, `:55`,
`:122`) while preserving the *criticality* of those fields and the
threshold-relative rules at pilot `:65`/`:55`, moves the consequential-use fence
from "< 80 %" to **"not accepted"**, adds the display rule that **a percentage
beside a decision is floored, never rounded up across the bar**, corrects
**ADR-019's w14 footer §6** (its cited evidence for the 90 % ruling is not on
`main`, so W17 is the wave that puts it on the record), and fences the pilot
fixture — **the fixture changes, never the threshold**. **Clause 3** defines
NW-20's `activity` as the provenance timeline of already-persisted events,
never an extracted value, with the honest alternative of deleting the member
rather than shipping an array that can never fill. **Clause 4** gives a market
claim exactly **two** honest shapes — a representative position with adapter,
sample size and as-of date, or an explicit "insufficient market data" — rules
OQ-w17-004's product half (geography = workspace country, labelled
representative), and rules that **"Not yet available" is a placeholder, not an
answer**. **Clause 5** makes the five critical fields **always shown, recovered
or not**, as empty fillable rows, and records termination and price uplift as a
bounded gap this wave does not invent. **Clause 6** removes confidence from the
Why row as a **deliberate divergence** from `screens-v2.md:101`, binds NW-65 and
NW-66 with one rule (**confidence lives in Review; Contract 360 never renders
it**), and supersedes Appendix C **`product-spec.md:954`** in its **rendering
half on 360 only** — the rule stands verbatim in Review and Ask and its
**storage half (`:127`) is untouched**. **Clause 7** rules **NW-75 OUT for V1**
with a re-entry condition, queues NW-23 / NW-25 with OQ-w17-007's product half
recorded so W18 does not re-litigate it, **ratifies the NW-63 split** only
because the remainder is already the head of W18 ahead of the overflow, and
fixes the release-valve order. No new ADR from this seat.

**Clauses 8–9 were appended to the same footer at the table**, for two questions
that reached this seat from other seats rather than from its lane; nothing above
them is rewritten. **Clause 8** (raised by ux-ui-designer) rules that after
NW-71 a field is in one of **three** states — auto-accepted, accepted by a
human, pending — carrying three different authorities, and that they **must stay
distinguishable in words and not by colour alone**, because painting the first
two alike tells a user they signed something they did not (w15 clause 7 and w14
clause 3 together); the consequence this seat owns is that **the contract may
not make the three states indistinguishable**, while the wire shape stays
software-architect's and a decision stays server-computed and read-only to the
client. **Clause 9** (raised by delivery-manager, who records declining to defer
it a fourth time) rules that **`demo` is not dormant** — the pilot path is the
product's client-facing acceptance surface and w15 clause 12 already ratified
the `demo-v4` cut as a product requirement — and notes that **w17 is the wave
where the gap stops being a satellite question**, since both flagship items are
pilot-script surfaces. The backlog is cleared by an **operator promotion
decision at the w17 HITL gate**; **no wave task promotes**; and if that decision
is not taken, **W18 opens with the promotion as its head item, ahead of every
feature**, because a deferral that never costs a slot is what let this reach a
fourth wave.

**One new ADR at this table — ADR-029** (software-architect, owner; deciders also
cloud-architect, client-architect, ux-ui-designer, product-owner):

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-029 | Document page rendering + the preview contract | software-architect | Rasterise per page **in the Worker at pipeline time, never on the API request path**; the stage runs after admission and is independent of extraction success, so a failed document is still viewable; pages persist per-page under the tenant prefix behind `EnsureWithinTenant`; the route gains 1-based `?page=n` bounded by the persisted `page_count`, out of range **404**; `pageCount` joins the document read model; the page budget is a stated cap, never a silent truncation. Records the **NW-63 split** and defers bounding boxes, the widened `AiOcrPage` and the phrase-edit write path to W18. |

**Seven ADRs gain a w17 amendment footer from this seat**; every body and every
earlier footer is untouched and **none is superseded**. **ADR-002** — `Raffa.Tools`
is a **third composition root** holding no business rule (it *calls*
`DocumentReprocessService`, never re-implements its requeue), it joins the
all-projects array and **not** the domain-module allow-list, and a calculator
stays pure: `IBenchmarkService` is **not** injected into `RenewalPipelineBuilder`
(the host resolves the band and passes it in on the DTO), plus the rule that the
360 composes in `Raffa.Api` because `Raffa.Documents.Contracts` is fenced to
`[SharedKernel, AiGateway]`. **ADR-003** — the per-field decision lands as two
nullable columns on the **existing** `extraction_evidence` (no new table, so zero
new isolation surface; three seats reached that table independently), no SQL
enum, and **NW-63's geometry columns are refused this wave**, which leaves
`documents-contracts.sql` a single writer. **ADR-017** — `prebuilt-layout` stays
uncalled, and page-level anchoring is shown to be derivable from the spans
already stored, so **bounding boxes are the only gap** and they are W18.
**ADR-021** — one writer again (so the wave record's single-writer constraint 5
**dissolves**), plus a corrected citation: the arrays are ranges `:277-285` /
`:309-317`, and the compressed `:277`/`:309` point at *identity-workspace.sql*
while `documents-contracts.sql` is `:278`/`:310`; two stale CI prose counts
("eight", "six" against **nine** scripts) are assigned to NW-73's sweep, the only
task allowed to open CI YAML. **ADR-024** — one review bar decided server-side on
the **raw** double (`>= 0.90`, no rounding before the compare), one
`ExtractionConfidencePolicy` consumed by both deciders so the badge cannot
desync from `needs_review`, an explicit retire/stay table for the other five
thresholds, the **`OpenWeakFacts` collision** (filtered at 0.8 while Review moves
to 0.90) ruled, a decision that is **read-only on the wire and not a boolean**
(three states, answering ADR-001 w17 clause 8), and the market claim's single
wire shape with **one resolution per screen**. **ADR-027** — rasterisation is
pipeline work, **re-derivation never overrides a human correction** (the rule
that stops the three states collapsing on reprocess), and the bulk console calls
the pipeline rather than reimplementing it. **ADR-028** — realized money is read
from the `RealizedSavings` rows that already exist but had no reader, as a new
per-currency shape that is a **deliberate wire break on one field**, with the
calculator kept pure and no cross-currency total.

**Two ADRs gain a w17 amendment footer from cloud-architect**, and **two record
`none` as a decision rather than as silence**. **The wave's entire Azure delta is
one RBAC row, and its fixed-cost delta is $0.00 on both `dev` and `demo`** — no
resource created or destroyed, no SKU, capacity or model deployment moved, no
region change, no Key Vault secret, no new container app and no new environment
key. **ADR-005** (clauses **15–22**, continuing from w15 round 3's §14) adds a
**third topic-scoped `azurerm_role_assignment`** on `modules/servicebus` granting
the CI deploy principal **`Azure Service Bus Data Sender` and nothing else**, with
the `lifecycle { ignore_changes = … }` block both existing grants carry — ARM
rejects in-place updates to a role assignment, so omitting it plans clean and
fails a *later, unrelated* apply. It rules **OQ-w17-ca-01** in favour of the
**GitHub runner (A)** on a ground that **inverts the "zero new rights" argument
for a Container Apps Job**: the workload identity holds **Sender *and* Receiver**,
so running the console under it would grant the **wider** capability — the power
to receive from the one subscription the Worker depends on — while (A) grants
**Send only** to a principal that exists only inside CI. It also fixes the NW-26
envelope without moving a SKU: **render page-by-page and dispose**, because both
apps share **one** 0.25 vCPU / 0.5 GiB pair and Consumption's ladder means memory
cannot be raised alone; a **pre-authorised contingency with a named ceiling**
(Worker only, 0.5 / 1.0 GiB) removes the need for a further council round; and it
prices the constraint **no seat had seen — the Worker *image***, whose
`dotnet/runtime:10.0` base carries no fontconfig/freetype, so a native rasteriser
needs an `apt-get` layer **above `USER $APP_UID` and above `COPY --from=build`**
or it fails permission-denied inside ACR Tasks and re-stores itself on every
commit. **ADR-007** (clauses **5–8**) records that `modules/servicebus` gains a
**second principal input** under the same per-root isolation rule as the first —
**required**, both roots wired in the same PR, named `ci_deploy_principal_id` to
match `modules/keyvault` — that a role assignment **cannot carry tags**, so the
mandatory-tagging implication is not violated by its absence, and that
**`infra/README.md`'s role inventory joins the w15 "two files, one edit" rule**:
`:417-421` currently asserts that operator workflows "need no new Azure grant",
which NW-73 falsifies exactly. **ADR-006 `none`** (North Europe, both
environments) and **ADR-008 `none`** (no Foundry change). **No new ADR from this
seat — ADR-029 is ratified, not competed with.**

**Three ADRs gain a w17 amendment footer from security-architect**, and **two
record `none` as a decision rather than as silence**. **No ADR body is rewritten,
none is superseded, and this seat writes no new ADR.** The wave's entire identity
delta is **one topic-scoped Send assignment**, and its entire RLS delta is a
binding rule for a new kind of host — **no new tenant table, no policy edit, no
new secret**.

**ADR-009** (w17 clauses **1–4**) rules that a host with **no HTTP caller** must
bind the tenant explicitly, and names the trap that makes the wrong way the easy
way: `DocumentsContractsDbContextOptions.Configure` wires the RLS interceptor
**only when its optional third argument is supplied**, so the two-argument form —
the one tests and migrations use — leaves `app.tenant_id` unset, which makes
**every query return zero rows**. It fails *closed*, but on NW-73's path that is
**indistinguishable from an empty worklist**, so the console would **exit green
having done nothing**. Five rules follow (three-argument `Configure` inside
`BeginScope`; never a raw connection, hand-written `SET` or `psql`; **one tenant
per run with no all-tenants mode**; **zero rows under a valid tenant exits
non-zero**; the application's own credential, never a superuser or `BYPASSRLS`),
plus the test that pins it — assert `app.tenant_id` **is set**, because a correct
binding and a missing one differ only in row count. Clause 2 records that NW-71's
per-field decision as a **column on the already-`FORCE`d `extraction_evidence`**
adds **zero new isolation surface**, and sharpens w16 clause 2a with the failure
mode it defends against: the RLS guard discovers tables **from the EF model by
`TenantScopedEntity` subclass**, and its "guards the guard" assert catches only an
**empty** list, never a **missing member** — so a non-deriving entity is never
checked **and the suite stays green**. Clause 3 ratifies ADR-029's per-page
objects under the same tenant prefix and adds that `?page=n` is **caller input
entering a storage path**: parsed as a positive integer, bounded by the persisted
`page_count`, **never string-concatenated** into a blob path.

**ADR-011** (clauses **20–24**, continuing w16's 19) fixes the console's actor as
`system:bulk-reprocess` and forbids **any CI-controlled string in
`AuditEvent.Actor`** — the table's UPDATE/DELETE trigger makes a wrong actor a
**falsified trail forever** — while ruling **OQ-w17-sec-01**: human attribution is
required and rides in `Detail` (`requestedBy=…; run=…`), because `Detail` asserts
no identity and `Actor` does. NW-71's auto-accept writes **one row per document**,
actor `system:extraction`, carrying **field names and confidence numbers, never a
field value** (append-only means a value written once cannot be removed), and the
trail must always distinguish an auto-accept from a human acceptance — the audit
half of ADR-001 w17 clause 8. **Clause 22 is the re-review clause 14c reserved**,
and it rules **OQ-w17-sa-03**: the 360 `activity` member is **permitted as a
contract-scoped provenance projection and refused as an audit reader**, because
`/api/audit` is **Admin-only** while `GET /api/contracts/{id}` is gated by
**membership with no Admin check** — so a naive projection moves an Admin-only
read onto an any-member surface. Five conditions make it a different read
(contract-scoped and never tenant-wide; a **default-deny allow-list** of actions,
never a blocklist; names never values; no actor identifier beyond what the member
list already shows; the Admin ladder untouched), pinned by one test — a **non-Admin
member sees the timeline and still gets 403 from `/api/audit`**. Clause 23 extends
the never-logged list to **GitHub Actions logs**, a retained sink readable by an
audience that is not the tenant.

**ADR-022** (w17 clauses **1–5**) **discharges the "Owed to W17" left at w16 §4**:
a CI principal **may** hold a topic-scoped **Send** right, with a six-row refusal
table, because the message is a **pointer, not content** — the Worker re-reads all
authority under RLS and `MessageId` collapses duplicates — so **Send crosses no
confidentiality boundary while Receive would**. Clause 2 records that this ruling
**corrects this seat's own lane**: the lane preferred a Container Apps Job on
"zero new rights", and the workload identity holds **Sender *and* Receiver**
(`modules/servicebus/main.tf:71-87`, `:89-102`, verified first-hand), so (C) would
grant the **wider** capability — the error was counting rights added instead of
measuring what the principal can do. Clause 3 rules **OQ-w17-sa-02**: the Admin
gate is **relocated to the CI plane, not bypassed**, with the four-plane chain
named, the security property stated as *no wider than the Admins it replaces*, and
the finding that **NW-73 is the product's first *mutating* operator workflow** —
its predecessor's own comment says *"this job reports; it does not mutate"* — so
its controls must be **at least** those of the read-only one it copies. Clause 4
records that queuing **NW-74** to W18 carries **no security debt** by this ADR's
own §3, re-verified on the code (`GetCapabilities()` takes no parameters and
returns the catalog unfiltered), with its two conditions travelling unchanged and
the rule that it **must never be written as a security fix**. **ADR-010 `none`** —
no token, claim, app registration or federated credential changes; the console
reuses the deploy principal's existing federated credential. **ADR-025 `none`** —
no membership, role or invitation change, and the service principal explicitly
gets **no `workspace_membership` row**.

**Two ADRs gain a w17 amendment footer from client-architect**, and **one records
`none` as a decision**. No body is rewritten and nothing is superseded.

**ADR-012** (w17 clauses **32–41**, continuing the w16 footer's 21–31) rules that
**the web renders the server's persisted decision and never recomputes it**. It
retires `acceptedThisSession` (`useReviewSession.ts:92`) against the finding that
`accept()` already has **two unequal branches** — `:177-182` writes and reads
back, `:184` is React state with **no network** — so NW-71 would have made **one
screen carry three durabilities under one paint**. It records a **correction
against this seat's own lane**: the lane proposed a two-value wire enum, which
would have forced the retired session store to survive in order to represent a
human acceptance; software-architect's **three** states are adopted. Clause 32
rules that **NW-63 adds no runtime dependency** — because ADR-029 rasterises
server-side the viewer renders PNG pages, so `web/package.json` stays at **five**
runtime dependencies and the dependency fence is never breached, this wave or in
the W18 remainder. Clause 33 binds the **object-URL lifecycle** on its first-ever
consumer (`client.ts:418-426` documents it; a grep finds **zero callers today**).
Clause 35 rules **one answer source per screen**; clause 36 finds that the 360's
attention gate infers a decision from a **tag colour**
(`contract360ViewModel.ts:575`) and will flip meaning when NW-71 lands with nobody
editing the line; clause 37 records that a declared wire break on a **generated**
type cannot land silently because `package.json:13` regenerates then typechecks;
clause 39 resolves the `client.ts` and `web/e2e/v2.spec.ts` writer contention;
clause 40 records the client decision for the three items **queued to W18** so W18
re-deliberates nothing.

**ADR-018** (w17 clauses **9–11**) adds **one route** — the first since w14 — to
the locked map: `/documents/:documentId/viewer?page=<n>&clause=<clauseId>`. The
page lives **in the URL, not React state**, so a reload and a deep link land on
the same page; the `?clause=&page=` pair **already is** this product's
citation-landing convention (`contract360/index.tsx:52,65-67,113`) and is reused
rather than re-invented; and the viewer is a **citation-reached state, not a rail
row**, so **`navItems.ts` gains no row and has zero writers** — the same call the
V2 IA already made for Review. This ADR is **shared**: client-architect owns its
route half and wrote this footer; **ux-ui-designer owns its IA, states and copy
half, and clauses 1–8 are untouched**.

**ADR-013 `none`** — the mobile scaffold is not touched, remains non-gating, and
the ADR is unamended.

### w17 — ux-ui-designer (2026-09-15)

**ADR-019** (w17 clauses **7–12**) carries the wave's design core. Clause 7 is the
**first change to the Semantic mapping's confidence rows since this ADR was
accepted**: the three bands (`:100-102`) become **two treatments over three
server-persisted decisions** — `auto_accepted` "Accepted automatically · NN%" and
`human_accepted` "Accepted by you" both `.tag-neutral`, `review_required`
"Review · NN%" `.tag-outline` — with the two accepted states distinguished **by
the label, never by the variant**, because a variant that encodes a decision is
the defect OQ-w17-cl-02 has just found live at `contract360ViewModel.ts:575`.
**`.tag-accent` leaves confidence entirely** and stays reserved for `failed`, High
risk and the critical markers; it was the retired middle band's treatment on four
surfaces, so the one-accent rule gets stronger. Clause 8 rules that **every
displayed confidence floors, never rounds** — `Math.round` at `semantics.ts:33`
renders a `review_required` field at `0.895` as "Review · 90 %", the bar's own
number beside the word denying it. Clause 9 relabels the clause risk enum into
product-owner's three words as a **label-only** change: mapping row `:106` is
unchanged, `getClauseRiskTag` already computes the variants, and the relabel is
**fenced to `contract360ViewModel.ts`** so `semantics.ts:getRiskTag` — a different
function on Portfolio and Renewals — is untouched and `semantics.ts` stays
NW-71's. Clause 10 **corrects the w14 footer §6**: its conclusion was right and
its cited evidence is not on `d3d2d24`. Clause 11 fences where each treatment may
appear now that two screens share `.tag-neutral` for two meanings.

**ADR-020** (w17 sections **13–19**) is the screen half. §13 — Review's legend
drops to **two** server-fed items, the title stops naming a threshold, and the
field list gains a fourth row state plus the **"Not found in the document"**
section, which renders **only fields that have a correctable target** and **does
not render at all** when nothing is missing. §14 — Contract 360: the "facts you
still need to decide" block is deleted and replaced by one count line; the
officialized gate **keeps every row** and shows the em-dash placeholder, because
the sparse-column risk is the *filter*, not the deletion; the Why row loses the
quote and the confidence tag, with the quote moving into the **existing**
`ClauseHighlight`; the answers band gets three real states per cell. §15 — a
**fourth** KPI cell, "Savings verified", with no new component and no new token.
§16 — the document viewer's chrome, decided from the locked catalogue because no
export exists, and the **copy rule that it must not promise a bounding box** the
wave cannot draw. §17 ratifies **five** deliberate divergences from the prototype
and forbids a task from "restoring" the export. §18 records **five** design
exports owed, none of which blocks the wave.

**ADR-018** (w17 clauses **12–14**) is this seat's **states and copy** half,
written as a separate footer so client-architect's route clauses 9–11 stay
byte-identical. Clause 12 binds the viewer to the states contract and gives it
**four** states — including a **not-found** state that the route owns rather than
the shell's `*` catch-all, which would otherwise turn a broken citation into the
Ask screen with no error at all. Clause 13 records a **correction against this
seat's own lane**: the lane ruled the viewer "a state, not a route";
client-architect owns that half and ruled otherwise, and is right.

**No new ADR and no supersession from this seat.** **No new token and no new
component** in the wave: the "specchietto" is the existing `ClauseHighlight`, the
fourth KPI cell is the existing cell shape, and the viewer is composed from
`.btn-ghost`, `.table` and surface tokens.

### w17 — delivery-manager (2026-09-15)

**Two ADRs gain a w17 amendment footer from delivery-manager** (owner of ADR-014,
ADR-015 and ADR-016), seated on **NW-73** plus the wave's **order**. **ADR-016
clauses 36–42** (numbering continuous, per that ADR's own convention) and
**ADR-014 w17 clauses 1–7** (numbering restarts, per *that* ADR's convention).
Both conventions are restated inside the footers because this seat applied them
the wrong way round in w16; the correction is recorded once and not re-earned.

**ADR-016** discharges clause 31's "owed at W17". **Clause 36** puts the console
on the **GitHub runner** (`dotnet run`): `backend.yml:66-74` already builds and
tests `Raffa.slnx`, and `:130-144` builds exactly two Dockerfiles, so the wave
adds **no image, no deploy path and no environment key**. The Container Apps Job
is refused with its attraction inverted — the workload identity holds Sender
**and Receiver**, so the Job would grant the **wider** capability. **Clause 37**
fixes the grant as a **four-file** change with a **required** variable and **both
roots wired in the same PR**, because `demo-promote.yml:128-136` calls `infra.yml`
for `demo` and a dev-only wiring breaks the promotion path a tag later; it adopts
cloud-architect's **`lifecycle { ignore_changes }`**, absent from this seat's
draft. **Clause 38** records the gate no other seat carried:
`AuthenticationSeamAbsenceTests` scans **`.github/workflows/**`** from inside
`backend.yml`'s `dotnet test`, so **this seat's file set sits inside a backend
test** and a careless workflow line turns `main` red — which is no `dev` deploy
and therefore no wave; the workflow copies `verify-tenant-corpus.yml`'s
credential idiom **verbatim** because that idiom is proven green against the
scanner today. **Clause 39** makes the two operator jobs share **one** worklist
predicate, which is what makes A17-S2 self-proving.

**Clause 40 rules the `demo` promotion** product-owner routed back here, and
carries the wave's sharpest delivery finding: **the promotion job does not
apply.** `demo-promote.yml` → `infra.yml`, whose apply job is a **step-summary
echo** named *"terraform apply skipped (demo)"* (`:114-136`), with
`promote-backend`/`promote-web` gated only on a green infra **plan** — so **a
`demo-v*` tag can go green end to end while `demo` lacks the new role
assignment.** Clearing the backlog is therefore **two acts in order**: the HCP
VCS apply confirmed in the UI, *then* the tag. This corrects cloud-architect's
"`demo` applies at its next promotion". The clause also corrects the **flags**:
they are **w15's, not w17's**, they are **Terraform variables** rather than
application flags, they **already default `false`** on `demo`, and there are
**three** — the third because the Graph grant is written out of band after
`Authorization_RequestDenied` **failed an entire `dev` run**. w17 flips none, so
**clearing the promotion backlog does not clear the invitation walk**.
**Clauses 41–42** invert w16's zero-infra final-integration list (`fmt`/`validate`
on **both** roots, the four-file `infra/` diff, exactly one added workflow) and
specify the known-gaps table.

**ADR-014** carries the process half. **Clause 2** fires the **two-PR shape** for
the first time since w15 defined it, and states the trap plainly: **merged is not
applied** — A17-S2 cannot pass before HCP runs, and the gate confirms **two**
applies, this wave's and PR #118's. **Clause 3** ends a rule that has been
rediscovered three waves running by making the previous wave's gate stamp a
**standing closing line** of every `reports/audit/<w>-hitl.md`. **Clause 5 is a
correction against this seat's own w16 clause 3**, which **did not execute**:
that clause renamed a file written by the **execution engine**, and *a council
rule cannot rename an engine artefact*, so it bound nobody. The enforceable
replacement binds **readers** — the engine's generic `wave-close.md` is a
fan-out delivery report, never cited as current, and the wave record is
`docs/waves/<w>-acceptance.md` plus `reports/audit/<w>-hitl.md`. **Clause 7**
carries the order and a **second correction to this seat's own skeleton**:
constraint 4 is **withdrawn** (software-architect ruled NW-63 needs no
migration), `reviewViewModel.ts` is a **two-item file** needing two phases or one
task, **NW-66 must follow NW-63** because e2e N20 asserts the link *lands*, and
⚠ **`FactTable.tsx` still has no assigned writer** — reached independently by
three seats and **the only finding of this table that no ADR closes**.

**ADR-015 `none`** — the new right is an Azure **data-plane role** on an existing
principal, so no federated credential, GitHub secret, subject claim or Graph
right changes. **No new ADR and no supersession from this seat.** The wave's
entire CI delta is **one added workflow file**.

**Round 3 extends both footers** (same seat, same wave, no new ADR and still no
supersession): **ADR-016 w17 clauses 43–45** and **ADR-014 w17 clauses 8–9**, so
the ranges above read **36–45** and **1–9**. Three of the five clauses are
corrections against this seat's own w17 text. **ADR-016 clause 43** corrects
clause 40's mechanism — `scripts/hcp_vcs_wiring.py:104-106` wires **both**
workspaces to `main` + `infra/`, so **`demo`'s infra moves at the merge, never at
the promotion tag**, and clause 37's stated reason ("breaks the promotion path
one tag later") is wrong while its ruling is strengthened. **Clause 44** records
that a dispatch before the `raffa-dev` apply is **destructive, not merely
failed** (ADR-011 clause 26), which falsifies ADR-014 w17 clause 2's own wording.
**Clause 45** adopts ADR-022 clause 6b's `dev`-only narrowing and carries its
three delivery consequences. **ADR-014 clause 8** adds W17-A1 (h) and corrects
clause 4(g) from "two applies" to **three runs across two workspaces, not
interchangeable**; **clause 9** records that the **phase graph is unchanged** by
round 3, checked item by item, with `FactTable.tsx` still unowned.

## Post-w19 / w20 product feedback (2026-09-22) — Ask Raffa capability gaps, interview and web research

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-030 | Ask Raffa: capability gaps, drafted negotiation email, interview and web research | software-architect (+ product-owner, security-architect, client-architect on their respective seams) | A capability gap is a **gate label** (after Legal, before Capability) from a five-entry IT/EN catalog, never a planner intent; `draft` and `interview` extend the reply contract alongside a required nullable `payload`; ambiguous turns ask **one server-authored question resolved by key** before retrieval, while web research stays an isolated, consented exception behind a kill switch, workspace opt-in and daily budget; the email gap with a resolved contract runs the guarded draft workflow and never abstains; other gaps redirect with follow-ups and an in-chat feedback card that stores a `feature_request` row first and then opens a GitHub issue best-effort through the host publisher. |

ADR-030 amends ADR-024's engine and wire contract (capability-gap gate,
`draft`, `interview`, `payload`, follow-ups and `external`), records the draft
guard / `chat.drafted` / `chat.interviewed` audit consequences, and keeps web
research a narrow exception to R-AI-03 (separate role, no pack slot, explicit
consent, `WebGuard`, always unverified). ADR-016 also gains the w20 footer for
the demo invitation / guest-provisioning flip and the feedback token gate.
