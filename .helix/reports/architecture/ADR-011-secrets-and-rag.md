# ADR-011 — Key Vault layout, CI auth, RAG authorization, audit, no-training

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: security-architect (owner); delivery-manager and cloud-architect concur at council-close
- **Locked citations**: `locked-decisions.md` row "Auth/secrets" (secrets in Key Vault; no secrets in
  code, client bundles, or Terraform source); row "AI" (Foundry only via AI Gateway); row "Delivery"
  (GitHub CI/CD to `dev` and `demo`). Product spec §14.1 (secret mgmt, TLS, encrypted backups, audit of
  access and changes), §14.2 (AI privacy: no training on public/shared models, centralized logged
  gateway, log model/version/prompt/timestamp/input hash), §14.3 (export/deletion), §8.3 (authorization
  filter before retrieval). Brief §8/§10 (tenant_id, RAG must not retrieve unauthorized docs, audit of
  access and corrections, managed identity).

## Context and problem statement

Three security concerns share one root cause (secrets and the boundary between "user may see it" and
"model may see it"):

1. **Secrets** must live in Key Vault and reach compute without ever appearing in code, client bundles,
   or Terraform source. CI must authenticate to Azure to deploy without a stored secret.
2. **RAG must not leak cross-tenant content.** Ask Raffa (spec §8.3) shows the authorization filter
   **before** retrieval — intent detection and semantic retrieval are downstream of an authorization
   decision, so an unauthorized contract can never be embedded into LLM context.
3. **Audit** of access and corrections is a listed enterprise control, and **customer contract content
   must not train public/shared models.**

## Decision drivers

- **No secrets in code/bundles/Terraform** (locked) — anything repository-visible is non-secret config.
- **Managed identity** (brief §10) — compute authenticates to Key Vault and Foundry via workload
  identity, not stored keys.
- **Authorization before retrieval** (spec §8.3, Appendix C #4) — the retrieval pipeline cannot run
  before a tenant+role+object authorization check.
- **Reproducible AI logging without leaking content** (§14.2) — we log model/version/prompt/timestamp/
  input **hash**, never the raw prompt or retrieved contract text.

## Considered options

1. **Per-environment Key Vault + managed-identity access + federated OIDC for CI** — one Key Vault per
   env; apps/worker use managed identity; GitHub Actions use workload-identity federation (no stored
   secret).
2. **One shared Key Vault for both envs + service-principal secrets in Terraform state** — fewer objects
   but crosses the isolation boundary and stores a secret in IaC.
3. **Key Vault per env but CI via long-lived service-principal client secret stored in GitHub** — no
   Terraform secret but a GitHub secret still exists.

## Decision outcome

**Chosen: Option 1 — one Key Vault per environment (`kv-raffa-dev`, `kv-raffa-demo`), accessed via
Azure managed identity for the API and worker, and GitHub Actions authorized via OpenID Connect /
workload-identity federation (subject-claim scoped to the repo + environment).** Authorization in the
Ask Raffa path is enforced **before** retrieval: the chat endpoint resolves the caller's tenant +
role + object permissions, and only the resulting authorized scope is passed to the semantic/vector
retrieval, which adds a `tenant_id` filter at the database/index level. Audit records access and
corrections; AI logs capture model/version/prompt-version/timestamp/input-hash — never raw prompt or
retrieved content. Foundry calls flow only through the AI Gateway, which is configured for a
no-training model endpoint.

### Consequences

- **Good**: No secret ever transitively stored in Git (federation exchanges a short-lived OIDC token for
  a short-lived Azure token). Isolation preserved (per-env Key Vault). RAG isolation is structural
  (authz scope computed first, then retrieval filtered by tenant).
- **Good**: Audit trail satisfies §14.1 "comprehensive audit logging for access and data changes" and
  brief "audit of access and corrections" without logging unauthorized content.
- **Good**: No-training enforced at the gateway: the Foundry deployment/model selected must be a
  no-training endpoint, and the gateway is the single choke point that proves it.
- **Bad**: Two Key Vaults + federation config add Terraform surface; each env's managed-identity
  assignments must be kept in sync.
- **Neutral**: Audit log retention/query cost is a delivery-manager/cloud-architect concern at the
  cheapest SKU.

## Pros and cons of the options

### Option 1 — per-env Key Vault + managed identity + OIDC federation (chosen)
- Good: no stored secret anywhere in the delivery path; isolation intact; managed identity means no
  runtime secret materialization.
- Bad: more IaC wiring per env.

### Option 2 — shared Key Vault + SP secret in state
- Good: simplest.
- Bad: violates environment isolation and "no secrets in Terraform source"; rejected outright.

### Option 3 — per-env Key Vault + GitHub-stored SP secret
- Good: keeps KV separated.
- Bad: a long-lived client secret still exists (not in Terraform, but in GitHub), weaker than federation.

## Implications for the decomposition

- Terraform (cloud-architect ADR) creates `kv-raffa-dev` and `kv-raffa-demo`, and grants the API and
  worker managed identities `get`/`list` on their own env's vault. No access-policy cross-env.
- GitHub Actions (delivery-manager ADR) authenticate via OIDC/`azure/login` with `client-id`,
  `tenant-id`, `subscription-id` only (non-secret) and a subject claim pinned to the repo + environment
  (`repo:lucalamalfa91/raffa:environment:dev|demo`). No `AZURE_CREDENTIALS` secret.
- The API reads connection strings, Foundry endpoint/key, and signing config from Key Vault at startup,
  not from appsettings committed to Git (appsettings may hold only non-secret keys like the Vault URI and
  non-secret config).
- The chat/Ask Raffa service must implement: resolve tenant/role/object authz → build authorized
  retrieval scope → run semantic/vector retrieval with a mandatory `tenant_id` filter → assemble LLM
  context. Retrieval cannot be invoked before the authz step (enforced in code, cited by §8.3/§C.4).
- The AI Gateway (software-architect ADR) is the only component that calls Foundry; it enforces the
  no-training model and emits the reproducible log record (model, model version, prompt version,
  timestamp, input hash). Raw prompt and retrieved contract text are **never** written to logs.
- Audit of access and corrections: an `audit` domain logs who/when/what changed (create/update/correct/
  delete of contract facts) and access events, keyed by tenant; unauthorized-content data is excluded
  from any log payload.

## Assumptions

- Microsoft Foundry in the chosen region offers a no-training model endpoint; if the only available
  model trains on shared data, the AI Gateway must be configured to opt out (or the cheapest
  compliant model selected — resolved jointly with software-architect + cloud-architect in the Foundry
  model-ID ADR). Recorded in `reports/open-questions.md`.
- "Input hash" for reproducibility = a content hash (e.g. SHA-256) of the retrieved evidence/prompt, so
  we can verify a given model/version ran on a given input without storing the confidential input itself.

## Amendment (2026-09-08, epic-12 / ADR-023)

Ask Raffa keeps **two corpora**, never one blended index:

1. **Tenant RAG** — this workspace’s contracts only (authz → `tenant_id` filter
   → retrieval). Unchanged from the original decision.
2. **Market** — `IBenchmarkService` (fixture now). Not written into tenant
   pgvector and not sourced from another tenant’s PDFs.

Off-domain questions must not run RAG. See ADR-023.

## Amendment (2026-09-08, epic-13 / ADR-024)

Two corpora remain, never one blended index:

1. **Tenant RAG** — this workspace's validated contracts (authz →
   `tenant_id` filter → retrieval); embedding rows gain `page` / `section`.
   Unchanged from the original decision.
2. **Market** — the market-intelligence feed (mock now) with its **own
   vector index** `market_embedding`: no `tenant_id`, readable by every
   tenant, written only by the ingestion job, never containing tenant
   content, never joined with tenant tables.

**Conversations** (`conversation`, `conversation_message`) are tenant tables
under the same RLS policy, keyed by tenant + user. Off-domain turns retrieve
from neither corpus. Documents are classified **before** persistence;
rejected files are never stored (audit hash only). This footer supersedes
the epic-12 amendment above. See ADR-024.

## Amendment (2026-09-10, wave w14 — no new secret, and authz-before-retrieval strengthened)

Serves **NW-58, NW-01, NW-04**. The Decision outcome above is unchanged: one Key
Vault per environment reached by managed identity, no secrets in source, bundle
or Terraform, authorization **before** retrieval, audit of access and
corrections, no training on customer content. This footer records three w14
consequences and adds no new principle.

**1. The invitation token needs no Key Vault entry — deliberately.** The intake
assumed "a signed, single-use, expiring token (secret in Key Vault, ADR-011)".
ADR-025 §C chooses a **256-bit CSPRNG token stored only as a SHA-256 hash**
instead. A signed token would buy stateless verification this design cannot use —
single use, revocation and expiry all require the invitation row to exist anyway
— while costing a vault secret, an environment variable, a per-environment
rotation story, and the property that **rotating the key invalidates every
outstanding invitation**. The `:95-97` slot for "signing config" is simply not
used: a narrowing, not a contradiction. **w14 therefore adds no Key Vault secret,
no environment variable and no Terraform change**, which is the condition
cloud-architect's zero-delta confirmation rests on (ADR-005 w14 footer).

**2. If a mail transport lands later**, its credential is a per-environment Key
Vault secret reached by **managed identity** — Option 1 above, unchanged — never
in source, Terraform or a client bundle, and plumbed by delivery-manager before
the feature ships (ADR-016). The transport is deferred by ADR-001's w14 footer;
this rule binds whichever wave lands it.

**3. Authz-before-retrieval gets stronger, not weaker.** The `:98-100` chain
(resolve tenant/role/object authz → build authorized retrieval scope → retrieve
with a mandatory `tenant_id` filter) is unchanged in shape, but the **first** step
changes substance: the tenant used for retrieval becomes a *verified membership
row* rather than a client-asserted `X-Tenant-Id`. A removed member's Ask calls for
that tenant therefore retrieve nothing on the **next request**, with no cache and
no token to invalidate. "Removed from the workspace but Ask still answers about
its contracts" is exactly the failure this ADR exists to prevent, and it now has
its own test (ADR-025 T7d).

**4. Nine new audit actions, no schema change.** `workspace.created`,
`workspace.membership.granted|removed|backfilled`,
`workspace.invitation.issued|accepted|rejected|revoked`, `workspace.list.truncated`
(ADR-025 §G). `AuditEvent` is already append-only and DB-enforced, tenant-scoped,
and `ResourceId` is deliberately a plain string. **Never written to an audit row
or any log sink**: the invitation token, its hash, any raw `Authorization` or
`X-User-Id` header dump, any contract or business datum. `Detail` may carry the
invited email and the offered role — membership facts inside that tenant, visible
to its members anyway — and nothing else. Because every one of these endpoints
requires a presented identity, the `"unattributed"` actor literal cannot occur on
any of them.

## Amendment (2026-09-13, wave w15 — one new secret, one new queue, and what still never reaches a log)

Serves **NW-68, NW-67, NW-27, NW-61**. The Decision outcome above is unchanged:
one Key Vault per environment reached by **managed identity**, no secrets in
source, bundle or Terraform, authorization **before** retrieval, audit of access
and corrections, no training on customer content. All four prior footers stand;
this one adds no new principle and applies the locked posture to three new
surfaces — a mail transport, a message broker, and a directory API.

### 1. The secret ledger for this wave, stated exhaustively

| Surface | Secret | Ruling |
|---|---|---|
| ACS Email (NW-68) | **`acs-connection` → handle `acs-cs`**, API app only | **the one new Key Vault entry in w15.** Reached by the workload identity's existing `Key Vault Secrets User` grant — **ADR-011 gains a secret, not a permission** |
| Graph guest provisioning (NW-67) | **none** | managed identity + one `azuread_app_role_assignment`. **No app registration, no client credential, no vault entry** (ADR-025 §J.1a) |
| Service Bus (NW-27) | **none** | managed identity + **topic-scoped** RBAC (§2) |
| API JWT (NW-05) | **none** | four **non-secret** env vars (authority, tenant id, client id, audience) |
| Invitation token (w14) | **none** | unchanged — clause 1 of the w14 footer, a 256-bit CSPRNG token stored only as a SHA-256 hash |

**1a — this is the concrete instance of the w14 footer's clause 2** ("if a mail
transport lands later, its credential is a per-environment Key Vault secret
reached by managed identity — never in source, Terraform or a client bundle").
Whichever credential form ships, three properties bind: it exists **only** in this
environment's Key Vault; it never appears in source, a client bundle, Terraform
source, a workflow file or a container image layer; and it is never logged, never
echoed in an error, never written to an audit row.

**1b — the `TokenCredential` migration is recorded, not re-argued.** This seat
proposed authenticating to ACS with the workload identity, which removes the only
secret w15 adds. Cloud-architect ruled (ADR-005 w15 footer §3) that the
connection-string form **stands for w15** — the Key Vault path is proven twice in
that module, satisfies the locked row verbatim, and swapping mid-wave would add a
role assignment on an unprobed resource type plus an untested SDK auth path to a
wave already carrying three applies. **This seat accepts that ruling.** It was a
`PROPOSE`, never an `OBJECT`: either form satisfies the lock, and 1a binds the
form that ships. When the migration lands it is **an amendment to this ADR and one
role assignment**, not a redesign.

### 2. Service Bus: identity, not a shared access key — and a contradiction on disk

**2a — the rule.** Managed identity + Azure RBAC:
**`Azure Service Bus Data Sender`** on the identity that publishes,
**`Azure Service Bus Data Receiver`** on the identity that consumes, both
**scoped to the topic**, and **no `Manage` right on either**. The
fully-qualified namespace is **non-secret configuration**. `RootManageSharedAccessKey`
plus a Key Vault secret is the obvious shortcut and it is rejected: it hands any
holder send **and** receive on **every** entity in the namespace. **ADR-011 gains
no Service Bus secret.**

**2b — the contradiction, named so the decomposer does not resolve it by coin
toss.** Two ADRs written at this same table give opposite instructions for the
same environment variable. **ADR-027 §D12 (`:395`, and `:347`) specifies "the
connection as a Key Vault secret → container-app secret handle → env var, on both
hosts, exactly as `pg-cs` and `st-cs` already do".** **ADR-005's w15 footer
specifies two topic-scoped role assignments and states that NW-27 adds no Key
Vault secret at all.** A decomposer reads files, not reasoning, and would ship
whichever it opened first. **This ADR owns the secret-versus-identity question and
rules: 2a governs — identity and RBAC, no Service Bus secret.** ADR-027 §D12's own
framing ("cloud-architect owns; stated here as a contract") makes this a
narrowing of a hand-off, not a contradiction of a decision; its remaining
requirements (one subscription, dead-lettering, `max_delivery_count` strictly above
the app's `MaxAttempts`) are untouched and correct. Software-architect is asked to
note the reconciliation; no ADR body is edited.

**2c — the KEDA scaler fallback is bounded, and S15-45 is what makes it
acceptable.** Cloud-architect's OQ-w15-cl-01 records a fallback if a
`custom_scale_rule` cannot authenticate its queue-length probe with the workload
identity: a `listen`-only namespace authorization rule → Key Vault
`servicebus-listen-connection` → handle `sb-listen-cs`, referenced **only** by
`custom_scale_rule.authentication`. **Accepted, with three conditions.** (i) It is
`listen = true, send = false, manage = false`; a `manage` or namespace-wide **send**
rule is rejected outright. (ii) It **never appears in an `env {}` block** and is
never read by application code, so the application data plane stays secret-free and
the key cannot reach a process-environment dump. (iii) Namespace-wide `listen`
grants read on **every** entity including the **dead-letter queue**, which retains
message bodies for operator inspection — so the fallback is acceptable **only
because ADR-009's w15 footer §5 keeps the message to ids alone**. A leaked listen
key would disclose identifiers, not contract content. That is a rule this footer
depends on, not a coincidence. When the identity form becomes expressible the rule
and the secret are removed.

**2d — splitting the workload identity (OQ-w15-cl-02) is deferred, with the
residual named.** One identity holding both Sender and Receiver means a compromised
API host can also **drain** the queue. Accepted for this wave because both hosts
run at the same trust level, in the same environment, against the same database,
and because the roles are **topic-scoped rather than namespace-scoped** — so the
blast radius is one topic, not the namespace. Splitting re-keys four role
assignments per environment across `keyvault`, `acr`, `foundry` and `servicebus`,
in a wave already carrying three applies. Recorded as a named option for a later
wave, not silently dropped.

### 3. Authz before retrieval — the sentence w15 supersedes, and the one it does not

**3a — what changes.** The ADR-024 footer above says: *"Documents are classified
**before** persistence; rejected files are never stored (audit hash only)."*
NW-27 splits the admission gate — format and size stay in the request, **content
classification moves to the Worker** (ADR-001 w15 footer clause 2, ADR-024 w15
footer, ADR-027 §D6). So for the length of one pipeline run the bytes of a file
that will be refused **are** in tenant-prefixed blob storage. That ordering half is
superseded. **On rejection the blob is deleted** (ADR-027 §D6 step 3), so the
`:143` sentence remains true of the **resting state**, which is what it was written
to protect.

**3b — what does not change, and is the whole point of the section.** A refused
file's **content never enters the tenant corpus**: a `Rejected` document produces
**no embedding rows**, in either corpus, ever. The two-corpora rule at `:130-138`
is untouched — tenant RAG holds this workspace's **validated** contracts, the
market index never holds tenant content, and neither ever holds a refused file.
"Classified before persistence" was a *mechanism*; "a non-contract's content never
becomes retrievable" is the *rule*, and the rule is preserved exactly. What
persists after a refusal is a status, a file name, an audit row and a reason —
never the bytes and never a vector.

**3c — NW-61 strengthens `:98-100` rather than touching it.** The "still
processing" signal on Portfolio, Contract 360 and Ask is a **count or status about
tenant data**, so it is produced by a tenant-scoped read under that tenant's own
claim — never a cross-tenant aggregate (ADR-025 Rule F.5: *"A number is data"*).
Ask's off-state is decided **after** the caller's tenant is a verified membership
fact, and a document that has not passed validation contributes neither an answer
nor a fact. Today that gate is **inferred client-side** (`askViewModel.ts:250-261`);
replacing an inference with a server fact strengthens this ADR, and **no client
heuristic may re-introduce it**. Software-architect's finding that
`AskCopilotService.cs:294` already renders an **unfiltered** count into
user-facing copy is, in this ADR's terms, a live defect in the same family: a
statement about validated contracts that was not derived from the validated set.

### 4. Five new audit actions, no schema change — and the log/audit split

`workspace.guest.provisioned`, `workspace.guest.provisioning_failed`,
`workspace.invitation.cap_reached`, `workspace.invitation.mail_sent`,
`workspace.invitation.mail_failed` (ADR-025 §J.7). `AuditEvent.Action` is a
free-form string (`Raffa.Audit/Domain/AuditEvent.cs:29`) and `AuditEvent` is
already append-only, DB-enforced and tenant-scoped, so these are **values, not a
migration** — the same pattern as the nine verbs in the w14 footer's clause 4.

**4a — never written to an audit row or any log sink**, extending clause 4's list:
the Graph `inviteRedeemUrl`, any raw Graph or ACS response body, any
`Authorization` header (ours or Graph's), the access token the workload identity
obtained, the rendered mail body or subject, the accept URL, the invitation token
or its hash, and any contract or business datum — unchanged.

**4b — the audit row and the application log are different sinks, and the invited
address belongs to only one.** The **audit row** keeps the invited email: clause 4
already admits it as a membership fact **inside that tenant**, the table is
tenant-scoped and RLS-protected, and an audit trail that cannot name who was
invited is not an audit trail. The **application log** does not carry it: it is a
**cross-tenant** sink read by operators, so it carries the invitation id, the ACS
or Graph operation id, and a named outcome. This is how this seat answers
cloud-architect's ask that "no recipient address reaches a log or an audit row" —
**agreed for the log, refused for the audit row, and the line between them is
tenant scoping.** `NullInvitationMailer.cs:25-29` already models the discipline:
the `acceptUrl` parameter is in the signature and is deliberately never
interpolated into the log statement.

### 5. Unchanged and re-affirmed

No training on customer content; TLS in transit; one Key Vault per environment;
managed identity everywhere, including for the new Graph call and the new queue;
no secret in a client bundle. **w15 adds exactly one Key Vault secret
(`acs-connection`) and no new Key Vault permission.**

## Amendment (2026-09-13, wave w15 — re-entry round: the apply plane's directory rights)

Seat: security-architect (owner). This ADR's **second** w15 footer. Everything
above — the body, the three earlier amendments and the first w15 footer's
sections 1–5 — is unchanged, `Status: accepted` stands, and **nothing is
superseded**. Clauses are numbered **6–13**, continuing that footer.

### 6. The finding, and it is against this seat's own ruling

At the first w15 round this seat recorded **`none — ADR-015`**, with the reason
*"the Graph permission sits on the runtime workload identity, not a deploy SP, so
CI gains no credential."* Both halves of that sentence are true and the conclusion
drawn from it was wrong. It reasoned about the **deploy** identity — `raffa-sp-dev`
/ `raffa-sp-demo`, GitHub OIDC → ARM — and never about the **apply** identity that
runs the HCP Terraform plan. Creating an `azuread_app_role_assignment` is itself a
**directory write**, so the new privilege does not land where this seat looked.
Delivery-manager wrote the footer this seat said was unnecessary
(`ADR-015:121-132`), and clause 2 there is right that it is needed. What no seat
priced is **what that grant costs** — and pricing a standing directory privilege is
this lane's job and nobody else's.

Verified on disk, 2026-09-13: `infra/` contains **zero** `azuread_app_role_assignment`
resources; `modules/identity/main.tf` declares two applications (`:57`, `:116`), two
service principals (`:107`, `:148`) and one pre-authorization (`:155`), and its only
Graph mention is the negative at `:42`.

### 7. `AppRoleAssignment.ReadWrite.All` is the directory's escalation primitive, not a narrow right

It permits its holder to grant **any** application permission of **any** API —
including Microsoft Graph's own `Directory.ReadWrite.All`, `Application.ReadWrite.All`
and `RoleManagement.ReadWrite.Directory` — to **any** service principal, itself
included. It is a documented path to Global-Administrator-equivalent control of the
directory.

The holder here would be an **automation** identity driven by whatever lands in
`infra/**` on `main`. Delivery-manager's ADR-014 w15 clause makes the path concrete:
a wave that touches `infra/` merges an infrastructure-only PR **first**, and that
merge *is* the apply trigger. So a standing grant means whoever can merge Terraform
can mint arbitrary directory privilege **in the customer's own tenant**, inside a
plan that reads as one app-role assignment. Set that against what the product
actually needs: **one** assignment, of **one** permission, **once**.

### 8. One of the three offered options cannot perform this assignment — and it is the one an operator reaches for first

`ADR-015:126-128` offers `AppRoleAssignment.ReadWrite.All` + `Application.Read.All`,
**or** Privileged Role Administrator, **or** Cloud Application Administrator.

- **Cloud Application Administrator** (and Application Administrator) may consent to
  delegated and application permissions **excluding Microsoft Graph application
  permissions** — and `User.Invite.All` is precisely a Microsoft Graph application
  permission. That exclusion exists for the reason clause 7 gives. **This option
  cannot do it.**
- **Privileged Role Administrator** can, and is itself a full escalation path: it
  assigns any directory role, Global Administrator included. It is broader than the
  task by an entire role-assignment plane.

So the two options that work are both tenant-wide escalation, and the one that sounds
narrowest fails. An operator reading the list picks the narrow-sounding role, gets a
**red apply at the gate**, and escalates under time pressure — the "discovered from a
red run" outcome delivery-manager explicitly asked this seat to prevent. Clause 10
turns it into a check that proves the answer either way instead of an assumption.

### 9. Ruling — `ADR-015` clause 4's fallback becomes the **default**; the standing grant becomes the fallback

`ADR-015:148-151` already carries the mechanism — *"a Global Administrator performs
the single assignment out of band, the resource stays at `count = 0`, and the
deviation is recorded with an `import` path"* — and records it as a degradation. This
seat rules it the **preferred shape**. No ADR body changes and delivery-manager
re-works nothing: both options are already on their page; this clause decides which
one is first.

(a) A one-time act by a human who already holds the privilege beats a standing grant
to an automation identity that needs it once.
(b) When `guest_provisioning_enabled` later flips to `true`, the existing assignment
is bound into state by the `import` path clause 4 already names; thereafter the plan
is clean and the apply identity needs only **read** (`Application.Read.All` /
`Directory.Read.All`) to keep it so. **A read right cannot grant anything.**
(c) **Revocation stays a human act.** Withdrawing a directory grant must not be
executable by whoever can merge `infra/**`; under this shape it is not.
(d) It costs the wave nothing. `var.guest_provisioning_enabled` defaults **`false`**
and the resource is `count`-gated (`ADR-015` clause 4), so the w15 apply succeeds
either way and NW-67 lands on the one-line flip.

**If the operator prefers the standing grant, it is accepted under four conditions.**
(i) `AppRoleAssignment.ReadWrite.All` + `Application.Read.All` **only** — never
Privileged Role Administrator. (ii) `infra/**` contains **exactly one**
`azuread_app_role_assignment`, for `User.Invite.All`, on `id-raffa-<env>-workload`;
a second one, or any assignment whose principal is the **apply identity itself**, is
a defect that fails review. (iii) The grant is re-reviewed at every wave that touches
`infra/**` — granted-and-forgotten is how a temporary right becomes permanent. (iv)
It is recorded in the ADR-016 runbook with its date and its grantor, so it can be
revoked by hand by someone who did not issue it.

### 10. The gate check, written so it passes or fails on a fact

Before the w15 infrastructure PR merges (the ADR-014 w15 gate), the operator
**records which shape is in force and proves it**:

- out-of-band shape → the assignment exists on `id-raffa-<env>-workload` for
  `User.Invite.All`, and the plan shows **no** `azuread_app_role_assignment` to
  create;
- standing-grant shape → the apply identity's Graph app roles are enumerated and are
  **exactly** the two named in clause 9(i).

**A plan that errors on a missing directory right is the failure this check exists to
prevent, not its outcome.** This discharges delivery-manager's ask to this seat
verbatim — *"that the apply identity's Graph rights be verified at the gate rather
than discovered from a red run."*

### 11. Co-signed, and one premise corrected — the API registration is updated in place, never replaced

`ADR-005` clause 11 requires an in-place update (`~`), never a replacement (`-/+`),
on `azuread_application.api` for the `email` optional-claims block. This seat
**co-signs it as an authorization requirement**, not only an availability one: that
application's client id **is** the audience `ADR-010`'s w15 §1.2 pins
(`ValidateAudience = true`, `api_client_id` the only accepted value). A replacement
mints a new client id, so every token in flight fails audience validation at once —
and §1.2 already names the repair a reviewer reaches for under pressure,
`ValidateAudience = false`. **A destroy/recreate of `azuread_application.api` is a
security event**, and the plan is read for it before the apply. It would also take
`azuread_service_principal.api` (`:107`) and the pre-authorization (`:155`) with it,
and — under clause 9's standing-grant shape — any app-role assignment hung off that
principal.

**The premise is narrower than clause 11 states, at no cost to its conclusion.** The
Terraform addition is this seat's own requirement (`ADR-010` w15 §2.4, S15-9) and is
**accepted unchanged**. But §2.4 says in terms that *"the design does not depend on
it — `oid` is bound at invite time — so its absence degrades to a 403 and a re-invite,
never to a silent grant."* A15-4 dies at its last step only if the `ADR-025` §J.3
`oid` bind is **also** missed. The difference is recorded because the stronger reading
is the dangerous one: a task that believes `email` is load-bearing for accept will
implement accept as an **email match**, which `ADR-010` w15 §1.2 forbids — an email
address is mutable and re-assignable inside a directory, so a standing membership
matched on it is inherited by whoever next holds the alias. **`oid` is the key;
`email` is the second branch of §2.3's ordering and nothing else.**

### 12. Two peer clauses bound, neither objected to

**12a — `ADR-027` §C6's delivery split is evaluated inside the message's own tenant
scope.** §C6 splits on `DeliveryCount`: row present ⇒ complete, absent and `< 2` ⇒
abandon, absent and `≥ 2` ⇒ dead-letter `job-not-found`. Accepted, with the rule it
needs: **"present" and "absent" mean present or absent *in the tenant the message
names*.** The job-row match stays the first statement after `BeginScope` (ADR-009's
w15 footer, S15-43) and is **never** widened into a cross-tenant lookup to decide
which branch applies — a `job-not-found` that consulted every tenant would be exactly
the read primitive that rule denies. A message naming tenant B for tenant A's job is
`job-not-found`, not a probe. With `max_delivery_count` at **8** (`ADR-005` clause 9)
a forged message buys up to eight scope entries instead of five: still bounded, still
ids-only, still no read, and bounded in time by `P1D`. **No objection to either
number.**

**12b — `ADR-005` clause 12's topic-scoped narrowing is accepted and is strictly
stronger.** §2c's conditions (i) and (ii) are unchanged and still bind. Condition
(iii) improves: a topic-scoped `listen` rule reaches that topic's subscriptions and
their dead-letter queues only, so the residual shrinks to the single DLQ ADR-009's
w15 §5 already bounds to ids. If the topic-scoped form proves inexpressible for a
KEDA `custom_scale_rule`, §2c's namespace rule **stands exactly as accepted** — the
acceptance was never conditional on the narrowing. Either way the sentence that
matters is unchanged: **a leaked listen key discloses identifiers, not contract
content**, and that is true only because the message carries ids.

### 13. Unchanged

w15 still adds **exactly one Key Vault secret** (`acs-connection`) and **no new Key
Vault permission**. Nothing in clauses 6–12 adds a secret, a vault entry, a client
credential or a CI credential: the runtime grant stays secret-free managed identity,
and the apply-plane question is a **directory right**, not a stored credential.

## Amendment (2026-09-14, wave w16 — who may read the audit trail, and an audit row that cannot name its actor is not written)

Seat: security-architect (owner). Serves **NW-08** (the read) and **NW-32** (the
write). Everything above is unchanged and in force — the body, the four earlier
amendments and both w15 footers, `Status: accepted`, nothing superseded. Clauses
are numbered **14–19**, continuing the second w15 footer. This is the wave in which
the audit trail's two ends are both repaired: today a real Admin **cannot read it**,
and every authenticated write **lies about who made it**.

### 14. S16-4 — who may read a tenant's audit trail: a live `Admin` membership in that tenant, and nobody else

`GET /api/audit` today reads a `tenant_id` claim
(`Raffa.Identity.Workspace/Domain/WorkspacePrincipalAuthorization.cs:62-67`) and
`ClaimTypes.Role` (`:69-74`), while `Program.cs:128` sets `MapInboundClaims = false`
and registers no `IClaimsTransformation` — so a valid Entra token mints neither and
a real Admin lands on **403**. **It fails closed, which is correct. The danger is
the repair, not the defect** (ADR-009 w15 §6, ADR-010 w15 §2.2/§3).

The route adopts the ladder every other tenant-scoped route already uses
(`DocumentsEndpointExtensions.cs:492-509`), **verbatim and with no bespoke
version**:

| Situation | Status |
|---|---|
| no validated token | **401** |
| missing or non-GUID `X-Tenant-Id` | **400** |
| well-formed tenant, **no live membership** | **404** — never 403 (ADR-025 Rule B1: a 403 is a tenant-existence oracle) |
| member, role is not `Admin` | **403** |
| live `Admin` membership | **200**, that tenant's rows only |

**14a — the property the old guard defended survives, and is strengthened.** The
route deliberately forbids a `?tenantId=` query (`AuditEndpointExtensions.cs:25-29`)
to prevent a cross-tenant read. Under `ICallerContext` the tenant is still never
trusted: it is a **candidate verified against the token subject's membership before
the scope opens** (`CallerContext.cs:135-152`). Enforcement moves from *the absence
of a parameter* to *a membership fact*, which is strictly stronger. Recorded
explicitly so no reviewer reads "claim → header" as "authorization → client input":
**`X-Tenant-Id` is an authorized selector, never an assertion** (ADR-022 w15
clause 2).

**14b — membership is the gate, RLS is the backstop, and the read happens inside
the verified scope.** `audit_event` is already `ENABLE` + `FORCE ROW LEVEL
SECURITY` + `tenant_isolation` (`Raffa.Audit/Migrations/Scripts/audit.sql:54-56`).
That is what makes a mistake in the gate survivable; it is **not** a reason to
soften the gate. ADR-009 w16 clause 3a carries the ordering.

**14c — the response body is the most sensitive read in the product.** Product-owner
ruled **no web surface** this wave (OQ-w16-003). If that ever reverses, the screen
is not "just another screen": it needs its own ADR-020 row and a re-review from this
seat covering pagination bounds and the rule that it discloses **no actor identifier
beyond what the member list already shows**. Recorded now so the reversal cannot
land as a routine addition.

### 15. S16-8 — an audit row that cannot name its actor is not written

Ten declarations of `private const string UnattributedActor = "unattributed"` and
fourteen runtime write sites across nine service types. Since PR #117 every endpoint
reaching them sits behind `ICallerContext` and **401s first**, so the placeholder is
no longer an identity-absent branch: it is an **unconditional hardcode on every
call, signed or not**. **The defect is a falsified audit trail, not an
authentication bypass** — an authenticated caller's writes are attributed to nobody
— and it is a live violation of this ADR's audit posture.

The actor becomes a **required parameter with no default value** on all nine service
methods, so the placeholder cannot return by omission. `AuditEvent.Actor` stays
`required string` (`AuditEvent.cs:27`, `maxLength 200`) — **the column was never the
problem; the placeholder exists precisely because null is impossible.** No ambient
accessor: an ambient actor lets the two caller-less sites compile and silently write
nothing, which is this defect with a new name.

### 16. S16-9 — the reserved non-human principal, and the two properties that make it a fact rather than a lie

Two of the fourteen sites have no HTTP caller at all (`RagAnswerService.cs:140`,
`SavingsOpportunityService.cs:165`). Where a write genuinely originates inside the
product, the actor is a **reserved, documented principal string** in the form
`system:<component>` — the convention **already live** in this codebase at
`NegotiationOutcomePropagationService.cs:97`
(`"system:negotiation-outcome-propagation"`), so nothing is invented. Two required
properties:

- **16a — it can never collide with a subject.** The string carries a character no
  token subject can produce (`:`), while a subject is an Entra object GUID. The two
  namespaces are provably disjoint, and **the reserved prefix is never accepted as a
  resolved token subject** — a token presenting one is rejected, not honoured.
- **16b — it is greppable**, which is what makes A16-4's "`grep` returns nothing"
  reachable without a vocabulary invented for the occasion.

**A reserved actor is a fact; `"unattributed"` is a lie** — it means "we did not
know", written by a system that did. W17's operator console inherits this ruling.

**16c — a stale record this clause creates, closed in the same task.** The doc
comment at `NegotiationOutcomePropagationService.cs:94-96` calls that string "the
same interim actor placeholder as every other automated write in this host (**ADR-010
is not wired in yet**)". ADR-010 **was** wired in w15, and after this clause the
value is not interim — it is the permanent, correct actor for a non-human write. The
comment is corrected inside NW-32's own file, so it adds no task and no writer.
Left standing it would be the same class of trap NW-31 spends this wave deleting:
a comment that tells the next implementer the opposite of the rule.

### 17. S16-10 — the green test pinning the defect is rewritten, never deleted

`backend/tests/Raffa.Chat.Tests/RagAnswerServiceTests.cs:70` asserts
`Assert.Equal("unattributed", entry.Actor)`. Deleting it removes the only evidence
the behaviour changed. It is rewritten to assert the resolved actor. Paired
acceptance (S-T28/S-T29): a **signed** POST on each of the nine paths writes a row
whose `Actor` is the caller's resolved subject; the two caller-less sites write the
reserved principal; `grep -r unattributed backend/src` returns nothing outside
comments the council allows.

### 18. The append-only consequence — this wave stops the bleeding and does not clean history

`audit.sql:76-88` installs a trigger rejecting **UPDATE and DELETE** on
`audit_event`. Therefore the `"unattributed"` rows already written are
**permanent and uncorrectable**. That is correct for an audit trail and is not a
defect to work around. Recorded so that nobody proposes a "tidy the history"
backfill: **the append-only trigger is never dropped** — not for a backfill, not
inside a migration, not temporarily. A trail that can be rewritten to look correct
is worth less than one with an honest gap, and the gap is bounded: it ends the day
NW-32 lands.

### 19. Unchanged

w16 adds **no Key Vault secret, no vault entry, no new permission, no client
credential and no CI credential** — OQ-w16-004's token option was refused on the
identity plane (ADR-022 w16 clause 4) and no other item touches a secret. The
no-training posture, the RAG authorization-before-retrieval rule and the
never-logged list are untouched; clause 14c adds one item to what a future surface
may not disclose, and nothing is removed from it.
