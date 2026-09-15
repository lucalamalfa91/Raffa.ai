---
wave: w16
source: inputs/next/w16-todo.md
source_sha256: d3518a64062ee1a867b0a6de433820e6dd48e4ea8c8c6b86b0bf84cd47103edc
design_sources: []
baseline: f0b3436 (helix/w16)
generated: 2026-09-14T18:15Z
baseline_refreshed: 2026-09-14T20:22Z (was ff66ee6; source_sha256 unchanged — file kept, not rewritten)
previous_wave: w15
caps: { max_tasks: 20, max_phases: 5 }
focus: "none"
---

# Wave w16 — normalized requirements

Written by `next-intake` from the raw file above. The raw file stays the
human's; this file is the process oracle for the council and the decomposer.
Items keep the source ids (`NW-07`); one item the raw file does not have gets
`W16-01`. Every claim about "today" cites a file in `../`.

**Wave theme: nothing the product knows lives only in a browser tab.** Four
identity residuals w15 overflowed, then four surfaces whose state is still
`sessionStorage`.

> **Read this before trusting any other close record.**
> `reports/execution/wave-close.md` is **stale**. It is stamped
> `2026-09-14T02:53:30Z` and lists 9 of 11 w15 tasks as *undelivered*. It was
> written **before** PR #103 (`e52f663`) merged `integration` into `main`, and
> before the follow-ups #111–#117. Every w15 area was re-verified against this
> checkout and is **delivered** — see §1 "Last wave". A later agent that reads
> `wave-close.md` and concludes w15 failed is reading a pre-merge snapshot.

## 1. Oracles in force

- **Product**: `inputs/requirements.md` §5.2 (R-CONV-01 AC-1 "another user of the
  same workspace cannot list or read the conversation"; R-CONV-03 "with ADR-010
  in force, user = API token subject… must be replaced by the token claim in the
  same task that lands the API JWT" — `:252-256`); `inputs/product-spec.md` §12.2
  (negotiation outcome capture, `:562`), §7 (`:214` "trackable SavingsOpportunity
  with status, owner and realized outcome"), `:925` (`POST /api/negotiations/outcomes`);
  `inputs/percorso-pilota-v1.md` (pilot path unchanged this wave).
- **Locked**: `reports/context/locked-decisions.md` — Auth/secrets ("OIDC,
  SSO-ready (Entra ID). Secrets in Key Vault."), API ("API-first. Web and mobile
  consume the backend API."), Backend ("Modular monolith + background worker").
  Cited, not re-opened.
- **ADRs touched by this wave**: ADR-002 (module boundaries — NW-13, NW-21,
  W16-01), ADR-003 + ADR-021 (new tables/columns and the checked-in migration
  script — NW-13, NW-21), ADR-009 (RLS on every tenant table — NW-11, NW-13,
  NW-21), ADR-010 (token subject — NW-07, NW-08), ADR-011 (audit integrity —
  NW-32; audit read — NW-08), ADR-012 (**§1 w14 footer "a client store never
  stands in for a missing GET"**, and **§3 "one task owns the contract file per
  phase"** — NW-08, NW-11, NW-12, NW-13, NW-21, NW-31), ADR-016 (the reprocess
  workflow, w15 clause 21 — NW-31), ADR-022 (the interim posture's named
  retirement — NW-31, NW-32), ADR-024 (`OQ-askv2-005` closes with NW-07),
  ADR-025 (membership is the authorization source — NW-08), ADR-026 (API
  contract shape — NW-08, NW-11, NW-12, NW-13), ADR-027 (`:198` "reprocess
  collapses into re-enqueue" — NW-31; the R0 queue it superseded — W16-01).
- **Design**: the raw file names **no** `inputs/design/**` path, so
  `design_sources: []`. Two anchors are nevertheless load-bearing and are cited
  per item because they *define the surface*, not because the raw file asks:
  `inputs/design/prototypes/raffa-v2/screens-v2.md:106-108` (the four **named**
  negotiation steps) and `.../app.jsx:109,166` (`racts` is **one shared fact**
  across Renewals, Contract 360 and Savings).
- **Last wave**: `reports/execution/wave-close.md` is **stale** (see the banner
  above). Re-verified against `ff66ee6`: NW-27/NW-61 async processing
  **delivered** (`../backend/src/Raffa.Messaging/ServiceBusExtractionConsumerHostedService.cs:33,46,98-100`);
  `Rejected` **delivered** (`../backend/src/Raffa.Documents.Contracts/Domain/DocumentProcessingStatus.cs:24`);
  NW-67 Graph guest **delivered** (`../backend/src/Raffa.Api/Infrastructure/GraphGuestProvisioner.cs:67`);
  NW-68 ACS mail **delivered** (`../backend/src/Raffa.Api/Infrastructure/AcsInvitationMailer.cs:38`,
  registered `Program.cs:60` ahead of the `TryAdd` null mailer);
  NW-05 JWT **delivered** (`../backend/src/Raffa.Api/Program.cs:120-163`,
  `Infrastructure/CallerContext.cs:116-120`); NW-06 role-from-membership
  **delivered** (`../backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs:73-87`);
  NW-69 **delivered** (`../web/src/routes/workspace/members/InvitePane.tsx:53-56`).
  No `salvage/*` tag needs recovering. Known gap carried forward from
  `../docs/waves/w15-acceptance.md:300`: `Invitations__Mail__Enabled` and
  `guest_provisioning_enabled` are **`false` on `demo`** — a reviewer must not
  read that as NW-67/NW-68 undelivered, and **no w16 task may flip either flag**.
- **Carry-over**: `reports/workitems/BACKLOG.md` "Queued for the next wave" —
  four tasks with `status: queued`, `wave: w15`, and **no entry in
  `reports/plan/slices/w15.yaml`**: `E18/F02/US01/T01` (NW-07),
  `E18/F02/US02/T01` (NW-08), `E18/F03/US01/T01` (NW-31), `E18/F03/US02/T01`
  (NW-32). Per raw §0.4 they are the **head** of this wave and are promoted to
  `live`; their bodies are not rewritten, only their *status today* is re-audited
  below — and **three of the four moved**.
- **Wave base**: `helix/w16` == `origin/main` == **`f0b3436`**, `0 0` ahead/behind.
  Unlike w14 (OQ-w14-003) and w15 (OQ-w15-001), **this wave needs no
  "merge `origin/main` first" item** and no W16-01-style base row.
  **Baseline refreshed this run** from `ff66ee6` → `f0b3436`: two merges landed
  after the first pass — `676bdb5` (PR #119, documents list = work queue order)
  and `f0b3436` (PR #118, Worker throughput). `git diff --name-only
  ff66ee6..HEAD` is **exactly four files** —
  `backend/src/Raffa.Documents.Contracts/Application/DocumentQueryService.cs`,
  `backend/tests/Raffa.Api.Tests/DocumentsListCountsTests.cs`,
  `infra/environments/dev/main.tf`, `infra/modules/containerapps/main.tf` —
  and **none is cited as evidence by any item below**, so the §2 audit stands
  unchanged. Re-verified on `f0b3436`: `documentStore.ts` still absent and
  `9c4975e` still an ancestor (NW-10 CLOSED-ON-MAIN); the `IQueueConsumer` trio
  still registered in `Raffa.Worker` (W16-01); the ten `UnattributedActor`
  declarations still present, the three extra `unattributed` grep hits being
  **doc comments only** (`DocumentsEndpointExtensions.cs:73`,
  `DocumentDeleteService.cs:61`, `LoggingAiGateway.cs:42`) — which is precisely
  the stale prose NW-31 folds in and why NW-32's acceptance reads "outside
  comments the council allows"; the four carry-over task files still
  `wave: w15`, `status: queued`, and still **absent from
  `reports/plan/slices/w15.yaml`** (which carries only `E18/F01/US01/T01` and
  `E18/F01/US02/T01`); epics `01…18` used, so **`epic-19` is still the next
  free number**.

## 2. Items

| ID | Title | Kind | Priority | Area | Status today | Seats | ADR touchpoints | Design refs | Acceptance |
|---|---|---|---|---|---|---|---|---|---|
| NW-07 | Conversation `user_id` is the token subject | carry-over | should | backend / auth | **PARTIAL** — seam closed on main; the old-row question is open | product-owner, software-architect, security-architect | ADR-010, ADR-024 (`OQ-askv2-005`), ADR-009 | none | A16-1 *(reworded — see item)* |
| NW-08 | `GET /api/audit` works for a real Admin | carry-over | should | backend / auth / web | **OPEN** — a real Admin gets **403**, not 401 | security-architect, software-architect, client-architect | ADR-025, ADR-010, ADR-011, ADR-026, ADR-012 §3 | none | A16-2 |
| NW-31 | Retire the dual role headers | carry-over | should | backend / ci / contract | **PARTIAL** — resolver done; 3 surfaces open | software-architect, security-architect, delivery-manager, client-architect, cloud-architect | ADR-022, ADR-016 (w15 cl. 21), ADR-027, ADR-012 §3 | none | A16-3 |
| NW-32 | Every write names its actor | carry-over | should | backend / audit | **OPEN** — reframed: falsified audit, **not** an auth bypass | software-architect, security-architect | ADR-011, ADR-022 | none | A16-4 *(reworded — see item)* |
| NW-11 | Renewal actions have an HTTP read-back | change | should | backend / web | **OPEN** | software-architect, client-architect | ADR-012 §1/§3, ADR-026, ADR-009 | `screens-v2.md:123-130`, `app.jsx:109,166` | A16-5 |
| NW-12 | Quote GET + negotiation-outcome list | change | should | backend / web | **OPEN** — store is **write-only** | software-architect, client-architect, product-owner | ADR-012 §1/§3, ADR-026 | `screens-v2.md:139-145` | A16-6 |
| NW-13 | Contract 360 step ticks are server state | change | should | backend / web | **OPEN** | software-architect, client-architect, product-owner | ADR-002, ADR-003, ADR-021, ADR-012 §1/§3, ADR-026 | `screens-v2.md:106-108` | A16-7 |
| NW-21 | Quote outcome updates Savings | change | should | backend / web | **PARTIAL** — propagation exists but is unreachable | software-architect, product-owner, client-architect | ADR-002, ADR-003, ADR-021, ADR-001, ADR-012 §3 | `screens-v2.md:132-137` | A16-8 |
| W16-01 | The R0 in-process queue is still registered in the Worker | bug | could | backend | **OPEN** — intake-originated | none | ADR-027, ADR-002 | none | W16-A1 |
| NW-10 | Rail Documents badge reads the server | carry-over | — | web | **CLOSED-ON-MAIN** (`9c4975e`) | none | none | none | — (out) |

---

### NW-07 — Conversation `user_id` is the token subject

- **Source**: NW-07, `inputs/next/w16-todo.md` §1 (`:53-67`); queued task
  `E18/F02/US01/T01`.
- **Raw**: "Conversations still have a private `TryResolveUserId` that prefers
  `NameIdentifier`/`sub` only if authenticated, otherwise the raw header, with a
  **normalization mismatch** (`CallerIdentity` lower-cases; this helper does not)
  that silently splits one user into two."
- **Today (evidence)** — **the raw file's premise is out of date; the code moved
  in w15.** `TryResolveUserId` **does not exist** anywhere under `../backend/src`.
  Its only trace is a dangling `<see cref="TryResolveUserId"/>` in a stale doc
  comment at `../backend/src/Raffa.Api/ConversationsEndpointExtensions.cs:24`,
  whose paragraph `:23-37` still asserts "falls back to the required `X-User-Id`
  header" and "a missing header with no authenticated principal is a 400" —
  **both false of the code beneath it**. All five conversation/chat handlers now
  resolve through `ICallerContext` and key on `caller.Identity!`:
  `ConversationsEndpointExtensions.cs:100→111` (list), `:142→153` (**create**),
  `:188→199` (get), `:237→248` (post message), and
  `ChatEndpointExtensions.cs:56→67`. Absent identity is **401**
  (`Infrastructure/CallerContext.cs:117-120`), never 400. The claimed
  normalization mismatch **does not exist**: `TokenCallerIdentity.Resolve()`
  returns `user.GetObjectId()` verbatim (`Infrastructure/CallerIdentity.cs:80-81`)
  and `:60-68` records that not lower-casing is *deliberate* — `oid` is an
  opaque, case-sensitive Entra object id. `X-User-Id` is read **nowhere** in
  `../backend/src`.
- **Gap** — two real residuals survive, neither of them the one the raw file
  describes:
  1. **No migration or backfill exists for rows written under the old key.**
     `../backend/src/Raffa.Chat/Migrations/` holds only `Initial` and
     `AddTenantRowLevelSecurity`; the only `migrationBuilder.Sql` calls are RLS
     policy DDL. Conversations created before w15 are keyed by the MSAL
     *username* (an email); a returning caller is now keyed by `oid`, so those
     rows are **silently orphaned** — an empty rail, and `ConversationService.GetAsync`
     returns 404. The raw file's own third `must` ("decide the fate of rows
     written under the old key and record it") is **unaddressed**.
  2. **A residual normalization divergence on the same column.**
     `../backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceDirectoryService.cs:104,144`
     compares a **lower-cased** identity against `ExternalSubjectId`, while
     `../backend/src/Raffa.Api/Infrastructure/CallerContext.cs:143` compares the
     same column **case-sensitively**. Benign while every `oid` is a canonical
     lowercase GUID; a real split the moment a non-lowercase subject is stored.
- **Seats**: product-owner (the raw file's acceptance is unsatisfiable as written
  and the orphaned-row decision is user-visible data), software-architect (the
  backfill/retire decision and the migration, if any), security-architect (the
  identity column's comparison rule must be one rule).
- **ADR touchpoints**: amend ADR-024 (retire `OQ-askv2-005` — this item closes
  it); ADR-010 (`oid` is the subject — already decided in w15, cited not amended);
  ADR-009 (the RLS identity GUC is unaffected).
- **Design refs**: none.
- **Acceptance**: **A16-1 as written cannot fail and cannot pass.** The raw file
  asks to "sign in as the same user with different UPN casing"; the key is `oid`,
  which is invariant under UPN casing, so the check is vacuous. Reworded, and the
  council should ratify: *a conversation created after w15 is readable by the
  same `oid` and by no other member of the workspace (R-CONV-01 AC-1); a
  conversation created **before** w15 is either still readable by its owner or is
  provably and intentionally retired, and which of the two is recorded.* See
  OQ-w16-001.
- **Proposed epic**: **epic-18 F02 US01** (exists; promote `E18/F02/US01/T01`
  from `queued` to `live`).
- **Task sketch**: delete the stale doc paragraph at
  `ConversationsEndpointExtensions.cs:23-37`; one rule for comparing
  `ExternalSubjectId`; a recorded decision (and migration, if the decision is
  "migrate") for pre-w15 `conversation.user_id` rows.

### NW-08 — `GET /api/audit` works for a real Admin

- **Source**: NW-08, `inputs/next/w16-todo.md` §1 (`:69-79`); queued task
  `E18/F02/US02/T01`.
- **Raw**: "Route exists but the guard still wants a custom `tenant_id` claim +
  `ClaimTypes.Role` (forbidden as an authorization source by ADR-010's w14
  footer). Path is **absent** from `web/openapi/raffa-api.v1.json`."
- **Today (evidence)** — confirmed, and **sharper than the raw file states**.
  The route is live: `../backend/src/Raffa.Api/AuditEndpointExtensions.cs:18`
  (`MapGet("/api/audit", …)`), mapped at `../backend/src/Raffa.Api/Program.cs:381`.
  Its guard is `WorkspacePrincipalAuthorization.TryAuthorize`
  (`AuditEndpointExtensions.cs:30-36`), which reads a `tenant_id` claim
  (`../backend/src/Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs:62-67`)
  and `ClaimTypes.Role` (`:69-74`) — **no membership lookup anywhere in the
  file**. Because `../backend/src/Raffa.Api/Program.cs:128` sets
  `MapInboundClaims = false`, an Entra token keeps `tid` / `oid` / `roles` and
  mints **neither** claim, and **no `IClaimsTransformation` is registered**. So a
  valid Admin token authenticates and then fails at `:63` → **403**. The only
  principal that ever satisfies this guard is test-only
  (`../backend/tests/Raffa.IntegrationTests/TestPrincipalStartupFilter.cs:27-42`,
  whose own comment `:11-12` says it is "never registered by production
  `Raffa.Api.Program`"). `/api/audit` is **absent** from the 33 paths in
  `../web/openapi/raffa-api.v1.json` — its only mention is prose in `info.description`
  at `:6` listing routes "deliberately NOT added here"; `../web/src/api/generated/schema.ts`
  has **zero** `audit` matches. **No web audit surface exists**: `../web/src/routes/`
  holds only `ask, contracts, documents, invite, quotes, renewals, savings, signin,
  workspace`, and `../web/src/App.tsx:222-236` registers no audit route.
- **Gap**: the guard must move to membership; the path and a typed client must be
  published. The membership seam already exists and is already used by sibling
  endpoints — `WorkspaceRoleResolver.IsAdminAsync`
  (`../backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs:91-93`) and
  `ICallerContext.ResolveTenantAsync` (`Infrastructure/CallerContext.cs:108-161`,
  401 at `:116-120`, 404-on-non-member at `:138-152`), used together at
  `../backend/src/Raffa.Api/DocumentsEndpointExtensions.cs:492-499` then `:506-509`.
  **One semantic change the raw file does not name**: today the route derives the
  tenant *from the claim* and **deliberately forbids** a `?tenantId=` query
  (`AuditEndpointExtensions.cs:25-29`); `ICallerContext` derives it from an
  `X-Tenant-Id` header verified against membership. That is a **contract change
  for this route**, not a drop-in. See OQ-w16-002.
- **Seats**: security-architect (membership as the only authorization source; the
  404 / 403 / 401 ladder), software-architect (the route's tenant-derivation
  contract change), client-architect (publish the path and regenerate the typed
  client — ADR-012 §3 constrains *when*).
- **ADR touchpoints**: amend ADR-025 (the audit read joins the membership-guarded
  set) and ADR-026 (the contract gains `/api/audit`); ADR-010 / ADR-011 cited.
- **Design refs**: none — and **no new screen this wave** (OQ-w16-003).
- **Acceptance**: A16-2 — a signed-in Admin reads the tenant's audit on `dev`; a
  Procurement member gets 403; a non-member gets 404; no token gets 401; OpenAPI
  lists `/api/audit`. *(The raw file's "Admin opens audit **or calls** `GET
  /api/audit`" is what makes an API-only close legitimate.)*
- **Proposed epic**: **epic-18 F02 US02** (exists; promote `E18/F02/US02/T01`).

### NW-31 — Dual role headers (`X-Role` / `X-Workspace-Role`) retired

- **Source**: NW-31, `inputs/next/w16-todo.md` §1 (`:81-94`); queued task
  `E18/F03/US01/T01`. **This is the one wave allowed to open
  `.github/workflows/reprocess-tenant-documents.yml`** (ADR-016 w15 clause 21;
  `OQ-w15-dm-04` `resolved`; `../docs/waves/w15-acceptance.md:299`).
- **Raw**: "delete the capabilities reader and the contract parameter; give the
  reprocess workflow a working authentication (or convert it to a worker-side
  enqueue — NW-27 made that possible) **in this wave**."
- **Today (evidence)**:
  - **Closed**: `WorkspaceRoleResolver` reads no headers —
    `../backend/src/Raffa.Api/Infrastructure/WorkspaceRoleResolver.cs:54-88` uses
    only `callerIdentity.Resolve()` (`:62`) and the membership join (`:73-83`);
    pinned by `../backend/tests/Raffa.Api.Tests/WorkspaceRoleResolverTests.cs:38,45,46`,
    which sets both headers and asserts they confer nothing.
  - **Open 1 — the live reader**: `../backend/src/Raffa.Api/CapabilitiesEndpointExtensions.cs:54`
    (`private const string RoleHeaderName = "X-Role"`), consumed at `:77-80`
    (`CallerIsAdmin`) and gating the catalog at `:64-67`. Live in the host
    (`Program.cs:433`). Its doc comment `:41-48` still claims "No host
    authentication is wired yet" — stale since NW-05.
  - **Open 2 — the contract**: `../web/openapi/raffa-api.v1.json:5239` declares the
    `X-Role` header parameter (`#/paths/~1api~1capabilities/get/parameters/0`),
    named again in the description at `:5236`; `:1227` carries a stale
    `X-Workspace-Role` sentence on `deleteDocument`'s description (description
    string only — the declared parameters are `X-Tenant-Id` and `id`);
    `reprocessDocument` `:1310` inherits it by reference.
  - **Open 3 — the workflow**: `.github/workflows/reprocess-tenant-documents.yml`
    sends `X-Tenant-Id`, `X-User-Id`, `X-Role: Admin`, `X-Workspace-Role: Admin`
    at `:203-206` (GET) and `:254-257` (POST). It authenticates to **Azure only**
    (`:124-129`, `./.github/actions/azure-login`) for FQDN, env var and Key Vault
    reads; a grep for `Authorization|Bearer|access token` over the file returns
    **zero** matches. After NW-05 it now fails **401 at the very first API step**
    (`GET /api/documents` → `DocumentsEndpointExtensions.cs:129` →
    `CallerContext.cs:116-120`), and `:208-211` turns that into `exit 1` before
    any write — so its own 403 diagnostic at `:274` is **unreachable dead code**.
  - **Already clean**: `../web/src` sends **neither** header
    (`../web/src/api/client.ts:76` sends only `Authorization: Bearer`).
- **Gap**: delete the capabilities reader and the contract parameter; repair or
  replace the workflow's authentication; sweep the stale prose. **Scope the raw
  file understates** — five test files pin the headers
  (`CapabilitiesEndpointTests.cs:18,70,85,100`;
  `DocumentAdminActionsAuthorizationTests.cs:24,100,101,185,190`;
  `DocumentsV2EndpointTests.cs:26,155,170,215,313,464,487`;
  `WorkspaceInviteAuthorizationTests.cs:29,173,180,181,197,204,205`;
  `WorkspaceRoleResolverTests.cs:38,45,46`) and the docs carry it at
  `../backend/README.md:213,237,384,390,1160,2690-2693`,
  `../docs/ask-v2-acceptance.md:71,357,600`.
  **Folded into this item** (same theme — retiring the interim posture's stale
  record): the dead `private const string TenantHeaderName = "X-Tenant-Id"` with
  zero usages at `../backend/src/Raffa.Api/DocumentsEndpointExtensions.cs:80`, and
  the ~dozen doc comments still asserting the retired posture
  (`DocumentsEndpointExtensions.cs:69-76`, `ConversationsEndpointExtensions.cs:26`,
  `Program.cs:366,427`, `WorkspaceInvitesEndpointExtensions.cs:36,38`,
  `WorkspaceMembersEndpointExtensions.cs:28,30`). These are load-bearing: they are
  exactly what makes a grep-based audit re-report a false verdict.
- **Seats**: software-architect (the capabilities endpoint and its contract),
  security-architect (the role source of truth, and — per `OQ-w15-dm-04` — a
  token-bearing deploy identity is an identity-plane change this seat owns),
  delivery-manager (owns the CI workflow and the one-wave window on it, ADR-016),
  client-architect (the contract file and the regenerated client, ADR-012 §3),
  cloud-architect (**only if** the repair is a token-bearing identity needing
  Terraform; not needed if the council takes ADR-027's re-enqueue path — see
  OQ-w16-004).
- **ADR touchpoints**: amend ADR-022 (its named retirement items are discharged),
  ADR-016 (the workflow's disposition), ADR-027 (if the re-enqueue path is taken);
  ADR-012 §3 governs contract-file ownership.
- **Design refs**: none.
- **Acceptance**: A16-3 — no `X-Role` / `X-Workspace-Role` in OpenAPI,
  capabilities, or the workflow; Admin catalog rows come from membership; the
  workflow authenticates (or is replaced by an enqueue that needs no API auth)
  and completes a reprocess on `dev`.
- **Proposed epic**: **epic-18 F03 US01** (exists; promote `E18/F03/US01/T01`).

### NW-32 — Every write names its actor

- **Source**: NW-32, `inputs/next/w16-todo.md` §1 (`:96-107`); queued task
  `E18/F03/US02/T01`.
- **Raw**: "nine sites that **do not take an actor from the request**, so they
  survived NW-05. Collapse the three absent-identity behaviours (401 on
  `ICallerIdentity` consumers, 400 on conversations, silent `"unattributed"`
  audit rows) to a **single 401**."
- **Today (evidence)** — the constant survived exactly as claimed; **the security
  framing did not.** Under `../backend/src`: **10 declarations** of
  `private const string UnattributedActor = "unattributed"` and **14 runtime write
  sites across 9 service types** —
  `Raffa.Api/AskCopilotService.cs:103`/`:995`;
  `Raffa.Documents.Contracts/Application/ContractCorrectionService.cs:118`/`:306,332,346,360`;
  `Raffa.Documents.Contracts/Application/DocumentUploadService.cs:59`/`:117,160`;
  `Raffa.Quotes/Application/Outcome/NegotiationOutcomeService.cs:88`/`:190`;
  `Raffa.Quotes/Application/QuoteUploadService.cs:38`/`:135`;
  `Raffa.Quotes/Application/Normalization/SkuMappingService.cs:90`/`:205`;
  `Raffa.Chat/Application/RagAnswerService.cs:67`/`:140`;
  `Raffa.Renewals/Application/RenewalActionService.cs:57`/`:138`;
  `Raffa.Savings/Application/SavingsOpportunityService.cs:95`/`:165,316`.
  **One declaration is already dead** — `DocumentsEndpointExtensions.cs:81` has
  zero references (the `ResolveActor` helper it fed was deleted in w15). None of
  the nine methods takes an actor parameter (`RenewalActionService.SetActionAsync`'s
  `owner` at `:72-78` is domain data, not the actor).
- **Gap — reframed, and the council must see the reframing**:
  - **(a) 401 on `ICallerIdentity` consumers — CONFIRMED** (`CallerContext.cs:116-120`;
    seven direct consumers all 401).
  - **(b) "400 on conversations" — REFUTED.** No such branch exists; absent
    identity is 401 (see NW-07). Only the *comment* at
    `ConversationsEndpointExtensions.cs:35-36` still claims 400.
  - **(c) "silent `unattributed` audit rows" — REFRAMED.** Since PR #117
    (`949004e`, "every tenant-scoped route needs a validated identity") **every**
    endpoint reaching these nine services sits behind `ICallerContext` and returns
    401 first — verified gate-by-gate (`ContractsEndpointExtensions.cs:365`→`:387`,
    `NegotiationsEndpointExtensions.cs:73`→`:84`, `QuotesEndpointExtensions.cs:67`→`:118`
    and `:331`→`:345`, `RenewalsEndpointExtensions.cs:365`→`:380`,
    `SavingsEndpointExtensions.cs:107`→`:121`, `DocumentsEndpointExtensions.cs:183`→`:272`,
    `ConversationsEndpointExtensions.cs:237`→`:301`). So `"unattributed"` is no
    longer written on an *identity-absent* branch — it is an **unconditional
    hardcode on every call, signed or not**. **The defect is a falsified audit
    trail, not an authentication bypass**: an authenticated caller's writes are
    attributed to nobody. That is a live **ADR-011** violation and it is unchanged
    by w15 — but it is *not* the data-loss/security-bypass class, so the raw
    file's `should` stands rather than being promoted to `must`.
  - **Counter-evidence that the pattern is already solved**: three document paths
    thread the real actor today — `DocumentsEndpointExtensions.cs:147` (validate),
    `:512` (reprocess), `:576` (delete) all pass `caller.Identity!`. The upload
    path (`:272`) does not. The fix is a known shape, applied nine more times.
  - **A green test pins the defect**:
    `../backend/tests/Raffa.Chat.Tests/RagAnswerServiceTests.cs:70` asserts
    `Assert.Equal("unattributed", entry.Actor)`. It must be rewritten, not
    deleted silently (OQ-w16-007).
- **Seats**: software-architect (nine service signatures change across five
  modules — a cross-module API edit), security-architect (ADR-011 audit integrity;
  the actor is an identity concern and two of the nine have no HTTP caller at all).
- **ADR touchpoints**: amend ADR-011 (every audit row names a validated actor) and
  ADR-022 (its second named retirement item is discharged).
- **Design refs**: none.
- **Acceptance**: **A16-4 as written is already satisfied** — an unsigned POST is
  401 with nothing persisted, today, before this item runs. Reworded: *a **signed**
  POST on each of the nine paths writes an audit row naming the caller's resolved
  subject; `grep -r unattributed ../backend/src` returns nothing outside comments
  the council allows; no test asserts the literal.*
- **Proposed epic**: **epic-18 F03 US02** (exists; promote `E18/F03/US02/T01`).

### NW-11 — Renewal actions have an HTTP read-back

- **Source**: NW-11, `inputs/next/w16-todo.md` §2 (`:123-133`).
- **Raw**: "`POST /api/renewals/{id}/action` persists; `RenewalActionService.GetActionAsync`
  exists but is **unrouted**… Renewals list, Contract 360 and Savings all read that store."
- **Today (evidence)**: the write is real and RLS-scoped — route
  `../backend/src/Raffa.Api/RenewalsEndpointExtensions.cs:97`, handler `:353`,
  service `../backend/src/Raffa.Renewals/Application/RenewalActionService.cs:107-128`,
  table `renewal_action` (`Infrastructure/Configurations/RenewalActionConfiguration.cs:11`,
  unique `(tenant_id, contract_id)` `:41`), RLS policy
  `Migrations/20260904223135_AddTenantRowLevelSecurity.cs:42,52-54`.
  `GetActionAsync` exists at `RenewalActionService.cs:158` and its own comment
  `:150-157` says "no HTTP route calls this yet" — **zero** production callers.
  Only three renewal routes exist (`:95`, `:96`, `:97`), none a GET of the action;
  `GET /api/renewals` does not merge it (its `action` field at `:239` is the
  computed `RecommendedAction`, not the persisted row). The mirror is
  `../web/src/routes/renewals/renewalActionStore.ts`, key
  **`raffa.renewals.actions`** (`:28`), `sessionStorage` (`:63,70,82,96`).
  All three surfaces confirmed: Renewals (`../web/src/routes/renewals/index.tsx:50,99-101,104,145`),
  Contract 360 (`../web/src/routes/contracts/contract360/index.tsx:75,88,256,264`),
  Savings (`../web/src/routes/savings/index.tsx:95,97` →
  `savingsViewModel.ts:216-226`). OpenAPI has three renewal paths
  (`../web/openapi/raffa-api.v1.json:2922,3163,3318`), no action GET — and the
  spec text itself concedes it at `:3321`.
- **Gap**: route `GetActionAsync` (list and/or by id) so a second browser sees the
  action; retire the session store as the record.
- **Seats**: software-architect (the route and its contract shape),
  client-architect (three surfaces stop reading a store and read a GET;
  ADR-012 §1 is the governing rule).
- **ADR touchpoints**: amend ADR-012 (its w14 §1 disposition table gains this
  store) and ADR-026 (the contract gains the route); ADR-009 cited — the table is
  already RLS-protected, so no policy work.
- **Design refs**: `inputs/design/prototypes/raffa-v2/screens-v2.md:123-130`
  (Renewals; `:130` "Status shared with the Contract 360 tracker (`racts`)") and
  `.../app.jsx:109,166` — the prototype models `racts` as **one shared fact**
  across all three surfaces, which is exactly the server row this item exposes.
- **Acceptance**: A16-5 — post an action → reload / second browser → the same
  action on Renewals, Contract 360 **and** Savings.
- **Proposed epic**: **epic-19** F01.

### NW-12 — Quote GET + negotiation-outcome list

- **Source**: NW-12, `inputs/next/w16-todo.md` §2 (`:135-145`).
- **Raw**: "no `GET /api/quotes`, no `GET /api/quotes/{id}`… Product shape of a
  history-inside-Quote-check / Ask-citable list is **NW-57 (W18)** — this item is
  the **read-back**, not that UX."
- **Today (evidence)**: `../backend/src/Raffa.Api/QuotesEndpointExtensions.cs:34-42`
  maps exactly three routes — `POST /api/quotes` (`:36`),
  `GET /api/quotes/{id}/assessment` (`:38`),
  `POST /api/quotes/{id}/assessment/recalculate` (`:40`). No quote GET.
  `../backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs:34` maps **only**
  `POST /api/negotiations/outcomes` — no GET for outcomes either. OpenAPI agrees:
  `../web/openapi/raffa-api.v1.json:3432` (post), `:3587` (get assessment),
  `:3861` (post recalculate), `:4261` (post outcomes). **The data is already in
  Postgres**: `Quote` (`../backend/src/Raffa.Quotes/Domain/Quote.cs:55`) and
  `NegotiationOutcome` (`.../Domain/NegotiationOutcome.cs:47`, table
  `negotiation_outcome`, written at `Application/Outcome/NegotiationOutcomeService.cs:178-179`),
  both RLS-enabled (`Migrations/20260905123141_AddTenantRowLevelSecurity.cs:42-44`,
  `20260905170108_AddNegotiationOutcome.cs:28`). There is **no** `QuoteQueryService`
  or repository analogous to `PortfolioQueryService` / `DocumentQueryService`.
- **Gap — worse than the raw file states**: the store
  `../web/src/routes/quotes/quoteOutcomeStore.ts` (key
  **`raffa.quotes.negotiationOutcomes`** `:31`, `sessionStorage` `:50,60`) is
  **write-only in production**. `../web/src/routes/quotes/index.tsx:290` calls
  `rememberNegotiationOutcome`, but `loadNegotiationOutcomes` and
  `sumRealizedSavings` have **zero callers** in `../web/src` — referenced only by
  `../web/tests/routes/quotes/quoteOutcomeStore.test.ts`. So today **no surface
  renders a recorded outcome at all**, not even in the same tab. The item is a
  pure read path: a GET, plus the first surface that shows it.
- **Seats**: software-architect (the GET(s) and the missing query service),
  client-architect (the store's retirement and the read-back),
  product-owner (the store being write-only means "what does the read-back
  *render*" is an open product question, and the boundary against NW-57's
  history UX in W18 needs holding).
- **ADR touchpoints**: amend ADR-012 (§1 disposition table) and ADR-026 (contract);
  **not** ADR-001 — this adds no capability beyond reading what is already stored.
- **Design refs**: `inputs/design/prototypes/raffa-v2/screens-v2.md:139-145` —
  Quote check shows lines and market band; "Target and negotiation levers are one
  step further", which is the boundary that keeps the *history UX* in NW-57 (W18).
- **Acceptance**: A16-6 — upload a quote, record an outcome, reload / second
  browser → both visible.
- **Proposed epic**: **epic-19** F02.

### NW-13 — Contract 360 negotiation-step ticks are server state

- **Source**: NW-13, `inputs/next/w16-todo.md` §2 (`:147-154`).
- **Raw**: "`negotiationStepsStore.ts` stores four booleans per contract under
  `sessionStorage["raffa.contract360.steps.<id>"]`; no endpoint records them."
- **Today (evidence)**: confirmed —
  `../web/src/routes/contracts/contract360/negotiationStepsStore.ts:10`
  (`STEPS_KEY_PREFIX = "raffa.contract360.steps."`, key built `:13-15`), `:11`
  (`NEGOTIATION_STEP_COUNT = 4`), shaped to four at `:18,24,31`, persisted as a
  JSON boolean array `:32`. Exactly one importer —
  `../web/src/routes/contracts/contract360/index.tsx:20`, reading `:76,89,267`,
  writing `:274`, clearing `:265`; presentation-only consumer
  `AnswersBand.tsx:10-11,104-112`. **Nothing server-side exists**: no
  `negotiation step` / `StepTick` / checklist match anywhere in
  `../backend/src/**/*.cs`; the only `negotiation` route is
  `NegotiationsEndpointExtensions.cs:34` (outcome capture); no entity, table or
  migration. The store's own comment `:6-7` concedes it.
- **Gap — with a precision the raw file misses**: the store holds a **positional,
  unnamed `boolean[4]`** — it stores **no step names**. The labels live in
  `../web/src/routes/contracts/contract360/contract360ViewModel.ts:211-218`, and
  **two of the four are parameterized** (supplier name, cancellation deadline).
  A server design keyed by array **index** would be brittle. The design oracle
  supplies the stable keys: the four steps are **named and ordered** in
  `screens-v2.md:106-108`.
  **Module-boundary constraint (documented in code, not speculation)**:
  `Raffa.Renewals`'s ADR-002 allow-list is `[SharedKernel, Benchmark]`
  (`../backend/src/Raffa.Renewals/Application/RenewalActionService.cs:23`), so it
  cannot reference `Raffa.Documents.Contracts`. The owning module must be chosen
  explicitly (OQ-w16-006).
- **Seats**: software-architect (a new table + migration + the module-ownership
  call), client-architect (store retirement, optimistic-tick discipline),
  product-owner (are the four named steps the canonical set, and are ticks
  per-contract or per-renewal-cycle).
- **ADR touchpoints**: amend ADR-002 (which module owns the ticks), ADR-003 +
  ADR-021 (a new table and the checked-in migration script — note the
  `backend.yml` script arrays at `:277-285`/`:309-317` list **modules**, and every
  candidate module is already listed, so **the CI arrays do not move**),
  ADR-012 (§1 table), ADR-026 (contract).
- **Design refs**: `inputs/design/prototypes/raffa-v2/screens-v2.md:106-108` —
  the four named steps: *Notify · Request revised pricing · Counter with the
  market benchmark · Sign or send non-renewal notice*.
- **Acceptance**: A16-7 — tick a step → reload / second browser → still ticked.
- **Proposed epic**: **epic-19** F03.
- **Task sketch**: follow the existing contract child-resource shape —
  `GET /api/contracts/{id}/corrections`
  (`../backend/src/Raffa.Api/ContractsEndpointExtensions.cs:66`, handler `:431-470`:
  `ResolveTenantAsync` first `:442-446`, GUID→400 `:451-454`, null→404 `:459-462`).

### NW-21 — Quote outcome updates Savings

- **Source**: NW-21, `inputs/next/w16-todo.md` §2 (`:156-166`).
- **Raw**: "`PropagateAsync` runs **only** when the body carries
  `savingsOpportunityId`; the web never sends one… so a recorded outcome never
  moves the Savings KPI."
- **Today (evidence)**: confirmed, with the guard at the **call site** rather than
  inside the method — `../backend/src/Raffa.Api/NegotiationsEndpointExtensions.cs:97`
  (`if (outcome.SavingsOpportunityId is { } savingsOpportunityId)`) → `:99-100`;
  the method itself
  (`../backend/src/Raffa.Api/NegotiationOutcomePropagationService.cs:111-114`)
  takes a **non-nullable** id and always does the work once called. The field
  comes from the body
  (`../backend/src/Raffa.Quotes/Application/Outcome/NegotiationOutcomeCaptureRequest.cs:55`
  → entity at `NegotiationOutcomeService.cs:173`). The web never sends it: the one
  call site `../web/src/routes/quotes/index.tsx:274-282` builds six fields and
  omits it; the type allows it at `../web/src/api/client.ts:877` but nothing
  populates it. So the 201 always reports `savingsPropagated: null`
  (`NegotiationsEndpointExtensions.cs:95-96,119-120`).
- **Gap — two distinct defects, and the second is not in the raw file**:
  1. **No derivable link exists.** `SavingsOpportunity` carries only `SupplierId`
     and `ContractId`
     (`../backend/src/Raffa.Savings/Domain/SavingsOpportunity.cs:42,44`; its comment
     `:25-27` says a `QuoteId` column "is that future task's own migration to
     add"). `NegotiationOutcome.cs:127-131` states outright that "there is no
     derivable link this module could compute on its own between a `Quote` and a
     tracked opportunity". `QuoteLine` has no savings reference, and
     `Raffa.Savings`'s ADR-002 allow-list is `[SharedKernel, Benchmark]`
     (`SavingsOpportunity.cs:19-20`), so the resolution must live in `Raffa.Api`
     — where `NegotiationOutcomePropagationService` already sits. **This needs a
     new column + migration**, not just a client change.
  2. **Even once propagated, the KPI number does not move.** Propagation sets
     `Status = Realized` and inserts a `RealizedSavings` row
     (`../backend/src/Raffa.Savings/Application/SavingsOpportunityService.cs:292,300-308`),
     and the KPI read is uncached
     (`.../Application/SavingsKpiQueryService.cs:32-38`) so the opportunity does
     move buckets. **But the Realized bucket sums `EstimatedSavingsLow/High`**
     (`.../Application/SavingsKpiCalculator.cs:103-104`), never the realized
     amount — a gap documented in that file at `:46-54`. So A16-8's "Savings KPI /
     row moves" is satisfied only as a *status* move, not an *amount* move. See
     OQ-w16-005.
- **Seats**: software-architect (the new link column, its migration, and the
  cross-module composition in `Raffa.Api`), product-owner (what "the KPI moves"
  must mean — status vs amount — and whether an outcome may be *resolved* to an
  opportunity rather than *naming* one), client-architect (send the resolved id
  and stop relying on the client store).
- **ADR touchpoints**: amend ADR-003 + ADR-021 (a new column and its migration;
  again **no `backend.yml` array movement** — `Raffa.Savings` and `Raffa.Quotes`
  are both already listed at `:281-282`), ADR-002 (composition point), and
  **possibly ADR-001** if product-owner rules the KPI must report realized amounts
  (that is a capability change, not a read-back).
- **Design refs**: `inputs/design/prototypes/raffa-v2/screens-v2.md:132-137` —
  the opportunities table column is **"Estimate"**, and `app.jsx:109` derives the
  row **status** from `racts`. The oracle supports a *status* move; it does not
  show a realized amount replacing the estimate.
- **Acceptance**: A16-8 — record an outcome tied to a savings opportunity →
  the opportunity's status / row moves; a second browser agrees. *(Whether the KPI
  **number** must also change is OQ-w16-005.)*
- **Proposed epic**: **epic-19** F04.

### W16-01 — The R0 in-process queue is still registered in the Worker

- **Source**: **intake-originated.** Not in the raw file; found while verifying
  that w15's NW-27 landed. Recorded rather than silently dropped.
- **Today (evidence)**: `../backend/src/Raffa.Worker/WorkerServiceCollectionExtensions.cs:70-72`
  **unconditionally registers** the R0 placeholder trio
  (`Queue/IQueueConsumer.cs`, `Queue/InMemoryQueueConsumer.cs`,
  `Queue/QueueConsumerHostedService.cs`) **alongside** the real ADR-027 consumer.
  It is a second live `IHostedService` in every Worker process, including deployed
  environments. Nothing publishes to it, so it is inert — but
  `Queue/IQueueConsumer.cs:13-18` still describes itself as an "R0 placeholder"
  and says "swapping in an `Azure.Messaging.ServiceBus`-backed implementation once
  a producer exists is a later task", which is now false and actively misleading.
  (Distinct from the *intended* `InMemoryExtractionQueue` fallback at
  `../backend/src/Raffa.Messaging/MessagingServiceCollectionExtensions.cs:37-41,61-65`,
  which is a documented CI/local transport and must **stay**.)
- **Gap**: delete the dead trio and its registration. Contained; no contract, no
  migration, no infra.
- **Seats**: **none** — a contained backend cleanup. The decomposer writes the task.
- **ADR touchpoints**: none — ADR-027 already superseded this queue in w15; this
  is the deletion it did not do.
- **Design refs**: none.
- **Acceptance**: **W16-A1** — `Raffa.Worker` boots exactly one hosted service for
  document extraction; `grep -r IQueueConsumer ../backend/src` returns nothing.
- **Proposed epic**: **epic-19** F05 (decomposer may instead fold it into any task
  already opening `Raffa.Worker`).
- **Priority note**: `could`, and **first to cut** if the cap binds — it is
  intake-originated and must never displace a queued item (see §5).

### NW-10 — Rail Documents badge reads the server — **CLOSED-ON-MAIN**

- **Source**: NW-10, `inputs/next/w16-todo.md` §0.5 and §2 (`:117-120`), which
  requires the intake to *confirm with evidence and keep it out if closed*.
- **Verdict**: **CLOSED-ON-MAIN.** Commit **`9c4975e`** (2026-09-14,
  "E16/F03/US01/T01: documents truthful surfaces — server counts, the refusal row,
  the stopped poll, the rail badge, three gated screens"), an ancestor of `HEAD`
  and present on `origin/main`. It deleted `web/src/routes/documents/documentStore.ts`
  (86 lines) and its test, and added `../web/src/components/shell/useDocumentCounts.ts`.
  The badge now reads the server: `useDocumentCounts.ts:29-32` calls
  `apiClient.listDocuments(workspace.id, { pageSize: 1 })` → `GET /api/documents`
  (`../web/src/api/client.ts:1877,1884`), and
  `../web/src/components/shell/RailNav.tsx:72` computes the badge from
  `documentCounts.all` / `.needsReview`, passed as a prop from
  `AppShell.tsx:44,55`. **No** `sessionStorage` / `localStorage` read sits behind
  it; no file in `../web/src` imports the deleted module. Pinned by
  `../web/tests/components/shell/RailNav.test.tsx:143-173`.
- **Out of the wave. Not reopened.** One documentation residual is recorded but
  generates **no task**: `../web/README.md:1166` still lists `documentStore.ts`
  and claims "RailNav.tsx's badge still reads it" — stale prose describing a
  deleted file (the same README is correct at `:235` and `:541-542`). Any task
  that opens `web/README.md` should sweep it; none is created for it.

## 3. Seat roster for this wave

| Seat | Involved | Items | Why (one line) |
|---|---|---|---|
| product-owner | **yes** | NW-07, NW-12, NW-13, NW-21 | Two acceptances are provably wrong as written (A16-1, A16-4→NW-32 corrected without a seat), one store is write-only so "what does the read-back render" is unanswered, and "the Savings KPI moves" has two incompatible readings. |
| software-architect | **yes** | NW-07, NW-08, NW-31, NW-32, NW-11, NW-12, NW-13, NW-21 | Every item is an endpoint, a table, a module boundary or a cross-module service-signature change. |
| cloud-architect | **yes (conditional)** | NW-31 | Only if the reprocess workflow is repaired with a token-bearing identity needing Terraform; **not** if the council takes ADR-027's re-enqueue path (OQ-w16-004). |
| security-architect | **yes** | NW-07, NW-08, NW-31, NW-32 | The identity column's comparison rule, membership-as-the-only-authorization-source for the audit read, the role-header retirement, and ADR-011 audit integrity. |
| client-architect | **yes** | NW-08, NW-31, NW-11, NW-12, NW-13, NW-21 | Three `sessionStorage` stores retire under ADR-012 §1, and **six items touch `web/openapi/raffa-api.v1.json`** under ADR-012 §3's one-writer-per-phase rule. |
| ux-ui-designer | **no** | — | No new screen, state or copy: the audit read closes through the API (raw §6 A16-2 "or calls `GET /api/audit`"), and ADR-012 §1 + ADR-018 `:117` already fix the error state every retiring store inherits. Revisit only if product-owner rules an audit screen in scope (OQ-w16-003). |
| delivery-manager | **yes** | NW-31 | Owns `.github/workflows/reprocess-tenant-documents.yml` and this is the **one wave** allowed to open it (ADR-016 w15 clause 21). |

## 4. Proposed epics

**No new epic is needed for theme A.** The four head items are already decomposed
under **epic-18** with real task files; raw §0.4 forbids rewriting their bodies.
Only their `status:` moves `queued → live`.

| Epic | Slug | Theme | Items | Extends |
|---|---|---|---|---|
| epic-18 *(exists)* | api-authentication | A · identity residuals — **promote F02/F03 to `live`** | NW-07 (`E18/F02/US01/T01`), NW-08 (`E18/F02/US02/T01`), NW-31 (`E18/F03/US01/T01`), NW-32 (`E18/F03/US02/T01`) | epic-13 F05, epic-01 F05, epic-14 F02 |
| **epic-19** *(new — next free; `Glob reports/workitems/epic-*` → 01…18 used)* | server-side-state | B · no session as source of truth | NW-11 (F01), NW-12 (F02), NW-13 (F03), NW-21 (F04), W16-01 (F05) | epic-03 F03 (renewals), epic-04 F03 (savings), epic-05 F03 (quotes), epic-07 F02 (Contract 360), epic-08 (web surfaces), epic-16 (the Worker, for W16-01) |

## 5. Selection for this wave (cap 20 tasks / 5 phases)

Raw §0 is binding and this section applies it literally: every queued wave runs in
order; nothing moves to a later wave than the one it is already queued for; a
`must` is never queued beyond the next wave nor demoted without a written reason;
anything that does not fit becomes the **head** of W17, never its tail.

- **In wave** (priority order — carry-over head first, per raw §0.4):
  1. **NW-07** — conversation key (`should`, head) — `E18/F02/US01/T01`
  2. **NW-08** — audit read for a real Admin (`should`, head) — `E18/F02/US02/T01`
  3. **NW-31** — role headers retired + the workflow (`should`, head) — `E18/F03/US01/T01`
  4. **NW-32** — every write names its actor (`should`, head) — `E18/F03/US02/T01`
  5. **NW-11** — renewal action read-back (`should`)
  6. **NW-12** — quote + outcome read-back (`should`)
  7. **NW-13** — negotiation-step ticks persisted (`should`)
  8. **NW-21** — outcome → savings link (`should`)
  9. **W16-01** — delete the dead R0 worker queue (`could`, intake-originated)
- **Queued** (decomposed, not in this wave): **none.** Every item of the raw
  file's W16 queue fits. Estimate ≈ 14–16 tasks against a cap of 20 (four head
  items are one task each and already written; the four theme-B items are a
  backend read/write plus a web read-back each; W16-01 is one; plus a final
  integration/acceptance task). **No item is demoted and none is deferred.**
- **Out**:
  - **CLOSED-ON-MAIN**: **NW-10** (`9c4975e` — §2 row). Also closed and **not
    reopened**, per raw §3: **NW-05, NW-06, NW-27, NW-61, NW-67, NW-68, NW-69,
    NW-58r** — all re-verified delivered on `ff66ee6` (§1 "Last wave"), against a
    stale `wave-close.md` that says otherwise.
  - **W17** (unchanged): NW-20, NW-22, NW-23, NW-25, NW-26, NW-62, NW-63, NW-64,
    NW-65, NW-66, **NW-71**. `NW-71` is parked on `helix/w17-input` and raw §0.3
    forbids ingesting it — **not ingested, not given a task, its wave unchanged**.
  - **W18** (unchanged): NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59,
    NW-60.
  - **DEFERRED**: NW-52 (paid market API — ADR-001 §1.2 and BACKLOG "never a paid
    external market API for the first `demo`"), NW-53 (mobile beyond the scaffold
    — ADR-013 non-gating), NW-54 (extra roles in nav).
- **Everything in "In wave" is closable in this product**: every item is a route,
  a column, a deletion or a client read-back on surfaces that already exist. No
  paid market API, nothing beyond the mobile scaffold, nothing in ADR-001 §1.2
  non-goals. **No Azure or Terraform delta is required** unless the council picks
  the token-bearing repair for NW-31 (OQ-w16-004); the ADR-021 `backend.yml`
  script arrays (`:277-285`, `:309-317`) list **modules**, and every module this
  wave touches is already listed, so **new migrations move no CI YAML**.

### Full remaining schedule (restated so nothing is dropped between runs)

Every wave below is executed, in order, and is the next run after the one before
it. W17/W18 are `w15-requirements.md` §5 verbatim, plus NW-71 which the raw file
adds to W17.

| Wave | Queue (head first) | Change vs `w15-requirements.md` §5 |
|---|---|---|
| **W16 (this run)** | NW-07, NW-08, NW-31, NW-32, then NW-11, NW-12, NW-13, NW-21, then W16-01 | NW-10 **leaves** — closed on main by w15 (`9c4975e`), per raw §0.5. W16-01 added by this intake as a `could`. |
| **W17** | NW-20, NW-22, NW-23, NW-25, NW-26, NW-62, NW-63, NW-64, NW-65, NW-66, **NW-71** | **+ NW-71** (auto-accept ≥ 90 %, grouped with the review-screen set NW-63/64/65/66), per raw §0.3 / §4. Otherwise unchanged. |
| **W18** | NW-30, NW-40, NW-41, NW-50, NW-55, NW-56, NW-57, NW-59, NW-60 | unchanged |

**Nothing overflows out of W16**, so W17's queue keeps its recorded head.

### Order constraints

1. **Theme A before theme B — and this is a single-writer constraint, not a
   preference.** NW-32 edits `RenewalActionService`, `NegotiationOutcomeService`,
   `QuoteUploadService`, `SkuMappingService` and `SavingsOpportunityService` —
   the **same five services** NW-11, NW-12 and NW-21 modify. If a theme-A and a
   theme-B task share a phase, `check_single_writer.py` will reject the slice.
2. **`web/openapi/raffa-api.v1.json` — one task owns it per phase** (ADR-012 §3:
   `schema.ts` is regenerated wholesale, so two tasks editing the contract in one
   phase produce a conflicting artefact, not a mergeable diff). **Six items touch
   it** — NW-08 (add `/api/audit`), NW-31 (delete the `X-Role` parameter `:5239`
   and the stale `X-Workspace-Role` description `:1227`), NW-11, NW-12, NW-13,
   NW-21 (new routes). Six contract edits cannot each own a phase inside a 5-phase
   cap. **Recommended resolution: two contract tasks, not six** — one theme-A
   contract task (NW-08 + NW-31, same phase) and one theme-B contract task
   (NW-11 + NW-12 + NW-13 + NW-21, a later phase). This is the tightest scheduling
   constraint of the wave and the decomposer must resolve it explicitly.
3. **NW-31 owns `.github/workflows/reprocess-tenant-documents.yml`** — no other
   task may open it (ADR-016 w15 clause 21). It is also the only CI-YAML file this
   wave is expected to touch.
4. **NW-07 and NW-32 no longer collide the way the raw file predicts.** Raw §5
   says they "share the conversations 400→401 seam"; that seam is already 401
   (`CallerContext.cs:117-120`), so there is nothing to reconcile. They still touch
   adjacent code — `AskCopilotService.WriteAuditAsync` is reached from
   `ConversationsEndpointExtensions.cs:301` — so **one writer owns
   `AskCopilotService.cs`**.
5. **NW-12 with or before NW-21** — both touch the negotiation-outcome POST path
   (`NegotiationsEndpointExtensions.cs`), and NW-21 adds the field NW-12's
   read-back will render.
6. **W16-01 last, and first to cut.** It is intake-originated and `could`; if the
   cap binds it is dropped to the head of W17's `could` tier and **must not
   displace** any item already queued for W17.

## 6. Superseded work items

| Existing item | Superseded by | Why |
|---|---|---|
| none | — | The raw file contains no "cancels / replaces" statement about a work item. Its §0.5 NW-10 note is a **do-not-reopen record**, not a cancel, and §3's list is a wave assignment, not a supersession. **No status banner is written this wave and no `status:` line is changed to `superseded`.** What *does* change is four `status: queued` → `live` lines on the epic-18 F02/F03 task files (raw §0.4), which is a promotion, not a supersession. One **oracle assumption** is superseded rather than a work item: `inputs/requirements.md` R-CONV-03 (`:252-256`) is satisfied only once NW-07 lands, and `OQ-askv2-005` retires with it (OQ-w16-001). |

## 7. Open questions and assumptions in force

Also appended to `reports/open-questions.md`. None gates a task.

- **OQ-w16-001** — NW-07: **A16-1 is unsatisfiable as written** (the key is `oid`,
  invariant under UPN casing), and the raw file's third `must` — the fate of
  conversation rows written under the pre-w15 email key — is unaddressed; no
  migration exists (`../backend/src/Raffa.Chat/Migrations/` has only `Initial` and
  the RLS policy). **Assumption in force**: the orphaned rows are **left in place
  and not backfilled** — mapping an email to an `oid` needs a directory lookup the
  product has no batch path for — and the decision is *recorded* rather than
  executed; the acceptance becomes "post-w15 conversations are readable by the
  same `oid` and by no other member (R-CONV-01 AC-1)". `OQ-askv2-005` retires with
  this item. product-owner + software-architect.
- **OQ-w16-002** — NW-08: moving the guard to `ICallerContext` changes how the
  route derives its tenant (a `tenant_id` **claim** today, an `X-Tenant-Id`
  **header verified against membership** after), and the route currently
  *deliberately forbids* a `?tenantId=` query (`AuditEndpointExtensions.cs:25-29`).
  **Assumption in force**: `/api/audit` adopts the same shape as every other
  tenant-scoped route (`DocumentsEndpointExtensions.cs:492-509`) — `X-Tenant-Id`
  + membership, 401 / 404 / 403 in that order — and the "no `?tenantId=`" rule is
  preserved as "the header is a selector, never an authorization input"
  (ADR-022's retirement wording). security-architect + software-architect.
- **OQ-w16-003** — NW-08: is a **web audit surface** in scope? No audit route,
  page or fetch exists in `../web/src`. **Assumption in force**: **no** — raw §6
  A16-2 reads "Admin opens audit (**or calls** `GET /api/audit`)", so the item
  closes through the API and a typed client; ADR-020's screen inventory is
  unchanged and **ux-ui-designer is not seated**. product-owner may overrule, in
  which case ux-ui-designer joins for one screen.
- **OQ-w16-004** — NW-31: repair the reprocess workflow with a **token-bearing
  identity** (identity plane — a registration/federated credential, Terraform,
  ADR-010/ADR-015) or convert it to **ADR-027's worker-side re-enqueue**?
  **Assumption in force**: the **re-enqueue path**, because ADR-027 `:198` already
  rules that "reprocess collapses into re-enqueue" on the same publisher, and it
  needs **no new identity, no secret and no Terraform** — keeping the wave's cloud
  delta at zero and cloud-architect on `PASS`. If the council prefers the token,
  cloud-architect's seat becomes active and an infrastructure phase is required
  before the workflow task. security-architect + delivery-manager + cloud-architect.
- **OQ-w16-005** — NW-21: does "the Savings KPI moves" mean the opportunity's
  **status** becomes `Realized` (already implemented once propagation fires), or
  must the **realized amount** replace the estimate in the KPI? The Realized
  bucket sums `EstimatedSavingsLow/High`
  (`../backend/src/Raffa.Savings/Application/SavingsKpiCalculator.cs:103-104`,
  gap documented at `:46-54`), and the design oracle's column is **"Estimate"**
  (`screens-v2.md:135`). **Assumption in force**: W16 delivers the **link and the
  status move only**; changing the KPI to report realized amounts is a separate
  product decision and is **not** in this wave's text — if product-owner rules
  otherwise it becomes an ADR-001 capability change and the head of W17.
  product-owner.
- **OQ-w16-006** — NW-13: which module owns the step ticks, and what is the row
  keyed by? `Raffa.Renewals` cannot reference `Raffa.Documents.Contracts`
  (ADR-002 allow-list `[SharedKernel, Benchmark]`,
  `RenewalActionService.cs:23`), and the client store is **index-positional** with
  two of four labels parameterized (`contract360ViewModel.ts:211-218`).
  **Assumption in force**: the ticks are a **contract child resource** owned by
  `Raffa.Documents.Contracts`, following `GET /api/contracts/{id}/corrections`
  (`ContractsEndpointExtensions.cs:66,431-470`), and keyed by the **four named
  steps** of `screens-v2.md:106-108` — never by array index.
  software-architect + product-owner.
- **OQ-w16-007** — NW-32: `../backend/tests/Raffa.Chat.Tests/RagAnswerServiceTests.cs:70`
  asserts `Assert.Equal("unattributed", entry.Actor)` — **a green test pinning the
  defect** — and two of the fourteen write sites have no HTTP caller at all
  (`RagAnswerService.cs:140`, `SavingsOpportunityService.cs:165` `CreateAsync`).
  **Assumption in force**: the test is **rewritten** to assert the resolved actor
  (never deleted silently), and the two caller-less sites take the actor as a
  **required parameter** rather than keeping a default, so the constant can be
  deleted outright. software-architect + security-architect.
- **OQ-w16-008** — **process, not product**: `reports/plan/gates/w15.hitl-ok`
  **does not exist** (the gates directory holds `e01…e13`, `readiness-gaps` and
  `w14.hitl-ok` only), while `scripts/check_slice_prereqs.py:332` requires
  `<previous>.hitl-ok` for the `hitl_previous` check. With `previous: w15`,
  `run.ps1 -Slice w16` will **fail its prerequisites** before fan-out.
  **Assumption in force**: the operator stamps it at the w16 HITL gate —
  `python scripts/check_slice_prereqs.py --record-hitl w15` — which is accurate,
  since w15 is confirmed terminated and merged to `main` (PR #103 + #111–#117).
  No task is created for this. Operator + delivery-manager.
