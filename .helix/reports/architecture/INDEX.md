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
