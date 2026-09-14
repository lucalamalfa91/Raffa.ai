# ADR-010 — Entra ID app registrations / OIDC for web and mobile

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: security-architect (owner); client-architect and delivery-manager concur at council-close
- **Locked citations**: `locked-decisions.md` row "Auth/secrets" (OIDC, SSO-ready Entra ID); row "API"
  (API-first, web and mobile consume the backend API); row "Cloud/Environments" (two isolated envs).
  Product spec §14.1 (MFA capability, SSO-ready OIDC/SAML, Entra ID / Okta), §13.1 (API-first domains).

## Context and problem statement

Raffa must be SSO-ready on Entra ID and every client (web and mobile) consumes the backend API over
OIDC. There are two Azure environments (`dev`, `demo`), each fully isolated (data, identities, resource
groups). The same codebase and client apps must authenticate against whichever environment they target,
without shipping secrets or environment-specific config into client bundles.

The question this ADR answers: **how many Entra app registrations, and what OIDC flow does each client
use against the API?**

## Decision drivers

- **SSO-ready on Entra ID** (locked) — customers authenticate with their own Entra tenant in the future,
  so we must not hard-code a single customer's directory.
- **API-first** (locked) — both web (SPA/browser) and mobile (native) call the same ASP.NET Core API;
  the JWT audience/scopes must be uniform.
- **Two isolated environments** (locked) — `dev` and `demo` have separate identities; a token minted for
  one environment must not be accepted by the other.
- **No secrets in the client** (locked) — the public client uses PKCE/authorization-code, never a client
  secret; the API validates tokens by signature + issuer, never by shared secret.

## Considered options

1. **Two Entra app registrations per environment** — one *public client* registration (web + mobile
   share redirect/native reply URLs) and one *API* registration exposing scopes. Per env, so `dev` and
   `demo` each have their own pair. Four registrations total.
2. **A single pair shared across both environments** — one client + one API registration, issuer
   constant across `dev`/`demo`.
3. **Separate registrations for web vs mobile clients** — more granular but more registrations to manage.

## Decision outcome

**Chosen: Option 1 — per-environment pairs: one public-client registration and one API registration in
`dev`, and the same pair in `demo` (four app registrations total).** Web and mobile share the public
client registration via a browser redirect URI (web) and a native reply/custom redirect (mobile), both
using the OIDC **authorization-code + PKCE** flow (spec §14.1 "OIDC/SAML; Entra ID"). The API
registration exposes scopes (e.g. `Raffa.Read`, `Raffa.Write`) that both clients request. Each
environment's API validates `iss` + `aud` against that environment's known Entra tenant/registration, so
a `demo` token never works on `dev`.

### Consequences

- **Good**: Matches the locked "two isolated environments" rule exactly (separate identities = separate
  registrations). No client secrets anywhere (PKCE). Uniform audience/scopes for web and mobile.
- **Good**: Multi-customer SSO-ready: adding a customer's Entra tenant later means adding it to the
  appropriate environment's API trust, not changing client code.
- **Bad**: Four registrations to maintain in Terraform (IaC), each with reply-URI and scope config that
  must stay in sync.
- **Neutral**: Web and mobile differ only in *reply URI type*, not flow or scopes.

## Pros and cons of the options

### Option 1 — per-environment pairs (chosen)
- Good: clean isolation boundary; uniform scope model; no secrets; multi-tenant-ready.
- Bad: four registrations; Terraform must template them identically per env.

### Option 2 — single shared pair
- Good: fewer objects to manage.
- Bad: violates environment identity isolation; a shared issuer weakens the `dev`/`demo` data boundary.

### Option 3 — separate web vs mobile registrations
- Good: per-client consent granularity.
- Bad: more surface area for no isolation benefit in V1 (both are first-party clients of the same API).

## Implications for the decomposition

- Terraform (cloud-architect's ADR) must declare two app registrations per environment (public client +
  API) with the API exposing `Raffa.*` scopes and the client pre-authorized for them.
- The backend API must configure JWT bearer auth using that environment's Entra `issuer` and `audience`
  (metadata via OIDC discovery URL), not a single hard-coded authority — injected per environment at
  runtime from a config value that is not a secret.
- Mobile uses the native OIDC authorization-code + PKCE flow (no secret); the redirect URI for the
  native client is the platform's declared scheme (e.g. `raffa://callback`), registered on the public
  client registration.
- Tokens are validated by signature/issuer/audience/expiry only; the API does **not** store or share
  client secrets (there are none).

## Assumptions

- Client stack selection (client-architect) produces a web client and a native mobile client that both
  support the OIDC authorization-code + PKCE flow (SAML is listed as future, not required in V1 — we
  adopt OIDC only, consistent with the locked row "OIDC, SSO-ready (Entra ID)").
- The exact scopes (`Raffa.Read`/`Raffa.Write`) are named here as placeholders; final scope names are
  adopted when the API surface (software-architect) is fixed, without changing the registration shape.

## Amendment (2026-09-10, wave w14 — the token carries identity only)

Serves **NW-58** forward, and binds **NW-05 / NW-08 (W15)**. The Decision outcome
above is unchanged: per-environment public-client + API registration pairs,
authorization-code + PKCE, four registrations, per-environment `iss`/`aud`
validation. **No w14 task edits any of it** — w14 ships on the ADR-022 interim
identity. This footer fixes one rule now so W15 cannot silently undo w14.

**1. The token is an identity assertion, not an authorization decision.** When
JWT bearer auth lands, the validated token establishes **who** the caller is
(`sub`/`oid`, and the email/UPN used for matching) and nothing else. **Tenant and
role are resolved from the database on every request.** A `tenant_id` claim or a
`roles` claim in a token is **never** the authorization source.

**2. This amends a shipped contract, and names the seam.**
`WorkspacePrincipalAuthorization.cs` is built on the opposite premise today:
`TenantIdClaimType = "tenant_id"` (`:35`), `TryAuthorize` reads the tenant from
that claim (`:62-67`) and the role from `ClaimTypes.Role` (`:69-70`), and
`GET /api/audit` consumes it (`AuditEndpointExtensions.cs:30-31`). Left alone,
W15 would ship a **stale-authorization window** — a removed member keeps access
until token expiry — and a **last-Admin-guard bypass**, silently undoing ADR-025
D.5a/D.5b, whose whole claim is that revocation is immediate because nothing
caches authorization. The remedy is one file: `:32-33` records that "whichever
task adds it (ADR-010) is free to change this constant, since **every caller goes
through this one place**". NW-05/NW-08 change `TryAuthorize`'s tenant/role
resolution from claim-reading to database-reading at that single seam, keeping its
fail-closed posture (`:42-44`).

**3. The interim header is ignored, not merely overridden.** `X-User-Id`
(ADR-022) is retired by NW-05. The retirement test is written in w14 and
activated in W15: with a validated token present, a request carrying token `A`
and `X-User-Id: B` acts as `A` and **never** as `B`. Precedence is not enough —
the header must stop being read at all.

**4. Scope names are no longer placeholders, and they are now a rename risk.**
The Assumptions above call `Raffa.Read`/`Raffa.Write` placeholders. They are
live and **hardcoded in CI**: `.github/workflows/web.yml:204-205` builds
`api://raffa-<env>-api/Raffa.Read|Write`. Recorded because it is the
*identity-plane* half of the `Raffa` → `Raffa` rebrand (W14-01, OQ-w14-003) and
it fails differently from a resource name: a wrong resource name fails a deploy
loudly, whereas a scope that no longer matches the app registration fails at
**token acquisition, in the browser, after CI is green**. Every w14 item is about
the signed-in identity, so the wave base must be verified with one interactive
sign-in on deployed `dev` before acceptance. Whether the scope literal changes is
delivery-manager's W14-A1; that it must be *checked at the identity plane* is
this ADR's.

## Amendment (2026-09-13, wave w15 — the token is validated here, and two claims are never authorization)

Serves **NW-05, NW-06**, and constrains **NW-67**. The Decision outcome above is
unchanged: per-environment public-client + API registration pairs,
authorization-code + PKCE, four registrations, per-environment `iss`/`aud`
validation, no client secrets. The w14 footer is unchanged and still in force.
This footer records the **validation parameters** the API now applies and closes
the one path by which wiring JWT could make the product *less* safe than the
interim it replaces. Rule ids `S15-n` are this seat's w15 lane
(`draft/next/security-architect/w15.md`).

### 1. Validation parameters (S15-1…S15-5)

**1.1 Delegated user tokens only (S15-1).** The API accepts a **v2 access token**
issued for this environment's API registration. An **id token** is not an access
token and fails audience validation. An **app-only** token (no `scp`,
`idtyp=app`) is rejected: this API defines no application role, so no app-only
token should exist, and the rejection is what keeps it that way. Both are **401**.

**1.2 `ValidateAudience = true`, and the value is `api_client_id` (S15-2).**
`infra/modules/identity/main.tf:75` sets `requested_access_token_version = 2`, so
`aud` is the API application's **client id** — `outputs.tf:13` says so verbatim:
"the default `aud` claim on v2 access tokens issued for this environment's API".
`api_identifier_uri` (`outputs.tf:18`) is the **alternate** resource identifier a
client *requests*, not the audience a v2 token *carries*. A task that wires the
`api://` URI as the audience sees **every** token rejected, and the tempting
repair is `ValidateAudience = false`. It is forbidden. A second accepted audience
is a new decision, not a fix. (Cloud-architect publishes **both** `AzureAd__ClientId`
and `AzureAd__Audience` deliberately — ADR-005 w15 footer §4 — which removes an
entire class of "token acquired, then 401"; this clause names which one the
validator compares.)

**1.3 `ValidateIssuer = true`, pinned to this environment's concrete tenant issuer
(S15-3).** `identity/outputs.tf:25-28` already emits
`https://login.microsoftonline.com/<tid>/v2.0`. **`common`, `organizations` and
`consumers` are never accepted**, and the API never derives the issuer from the
token. Defence in depth is mandatory here rather than optional: the SPA's
runtime-config validator (`scripts/write_web_runtime_config.py:80-83`) checks the
`https://login.microsoftonline.com/` **prefix only**, so `…/common` passes it
today. Extending that validator to reject the three multi-tenant segments is a
cheap and welcome addition, and it is delivery-manager's file.

**1.4 The rest, fail closed (S15-4).** `ValidateIssuerSigningKey` and
`RequireSignedTokens` true; keys from the environment's OIDC discovery document
(JWKS, auto-refreshed) — never a pinned key and never a shared secret, which the
Implications above already require ("the API does **not** store or share client
secrets"). `ValidateLifetime` true with `ClockSkew` **≤ 2 minutes**: the 5-minute
library default is a five-minute extension of stale authorization for free. A
token whose `ver` is not `2.0` is rejected.

**1.5 A missing scope denies; a present scope grants nothing (S15-5).**
`Raffa.Read` / `Raffa.Write` (`identity/main.tf:79`, `:90`) are **client
capability**: they say the browser was allowed to ask, never that the caller may
touch a given tenant. Absent required scope → **403**. Present scope → the request
proceeds to the membership check, which is the only grant. Recorded because "the
token has `Raffa.Write`" is the most natural wrong reading of a scope.

### 2. Which claim is the identity (S15-6…S15-9)

**2.1 `oid` is the identity; `email` is a binding aid, never the standing key
(S15-6).** Membership matches on `workspace_user.ExternalSubjectId = oid`. An
email address is mutable and re-assignable inside a directory, so matching a
*standing* membership on email lets a re-assigned alias silently inherit someone
else's grant. Email keeps exactly two jobs: the **first** bind of a subject to an
invited address (ADR-025 Rule D.3c step 2, `LinkSignInAsync`, unchanged) and the
invitation email match (Rule D.3b). After the bind, `oid` is the key.
`WorkspaceRoleResolver.cs:129` already matches `ExternalSubjectId`, so the swap
needs no query change — exactly what ADR-025 Rule F.2c promised.

**2.2 `tid` is the Entra directory, never the Raffa workspace tenant (S15-7).**
A v2 token carries `tid`, the directory GUID. It is **not**
`WorkspacePrincipalAuthorization.TenantIdClaimType` (`:35`, `"tenant_id"`), and
mapping one onto the other to "make `GET /api/audit` work" would ship exactly the
stale-authorization window clause 2 of the w14 footer and ADR-025 §I forbid. Both
environments use the **same** directory (`identity/main.tf:63`, `:118`), so `tid`
is identical for `dev` and `demo` and for every customer workspace — it can never
select a tenant. **No Raffa code reads `tid` for any purpose.**

**2.3 The `#EXT#` UPN is never parsed into an email for an authorization decision
(S15-8).** A B2B guest's UPN in the resource directory is mangled
(`luca_gmail.com#EXT#@<tenant>.onmicrosoft.com`). ADR-025 Rule D.3b matches the
signed-in identity against the invited address by case-insensitive equality
(`WorkspaceInvitationService.cs:187`, `:197`); against a mangled UPN that
comparison **fails**, and A15-4 dies at its last step with the invitee signed in
and locked out. The de-mangling "fix" (replace the final `_` before the domain
with `@`) is ambiguous for addresses containing `_` and must never be a grant
basis. Resolution order: **the `oid` bound at invite time by NW-67 (ADR-025 §J.3)
→ the `email` claim → refuse (403) and tell the Admin to re-invite.** Never the
UPN.

**2.4 The `email` optional claim must be requested, in Terraform (S15-9).**
Verified: there is **no `optional_claims` block anywhere** in
`infra/modules/identity/main.tf`. 2.3's second branch exists only if the access
token carries `email`, which needs
`optional_claims { access_token { name = "email" } }` on
`azuread_application.api`. Cloud-architect owns the file; this seat owns the
requirement. The design does not *depend* on it — `oid` is bound at invite time —
so its absence degrades to a 403 and a re-invite, never to a silent grant.

**2.5 Nothing about a token's shape is proven until one interactive sign-in on
deployed `dev` (S15-10).** Clause 4 of the w14 footer already requires this for
the identity plane, and it fails **after CI is green, in the browser**. The NW-05
task's acceptance names the claims it actually observed (`ver`, `aud`, `iss`,
`oid`, `scp`, `email`) in the runbook. This is the honest form of every Entra
claim statement above.

### 3. NW-06 — the claims branch is deleted, not enabled (S15-13)

**This is the finding that makes NW-06 urgent rather than tidy.**
`WorkspaceRoleResolver.ResolveAsync(httpContext, tenantId, …)` has two sources: a
claims branch (`:60-65`) and the tenant-scoped membership read (`:70`, `:104-137`).
The claims branch returns `claimRole` **without ever referencing its `tenantId`
parameter** — only `:70` is scoped. Today that is harmless, because this host has
no authenticated principal at all (verified: `Grep` for
`AddAuthentication|AddJwtBearer|UseAuthentication|JwtBearer|Microsoft.Identity.Web`
over `backend/src` returns **two hits, both doc comments**). **The moment NW-05
wires JWT, one Entra app role assigned once in the directory resolves to that role
in every workspace the caller can name, bypassing `workspace_membership`
entirely.** `IsAdminAsync` (`:74-76`) gates `DELETE /api/documents/{id}` and
`POST …/reprocess`, so the failure is cross-tenant **destructive** access, not
merely visibility.

So NW-06 is a **deletion**: `ResolveAsync` collapses to
`ResolveMembershipRoleAsync`; the two header constants (`:48-49`) and
`TryResolveHeaderRole` (`:89-102`, zero call sites) go in the same edit;
`WorkspaceRoleClaimResolver` keeps only its non-authorization caller
(`GET /api/capabilities`) until NW-31. Deleting costs six lines; reviewing and
tenant-scoping a claims path costs a design, a Terraform app role, an assignment
story and a permanent second authorization source.

**3.1 The deletion takes the doc comment with it.** The instruction to keep the
branch is in the **source**, not in an ADR: `WorkspaceRoleResolver.cs:20-22`
describes the claims branch as *"the ADR-010 end state, and the only source that
survives past this wave."* An implementer opening the file to wire NW-05 reads a
comment telling them to enable the very branch that is the escalation. If the
comment survives, the next wave restores the branch from its own documentation.

**3.2 §E / §I reconciliation, recorded so a reviewer reading one section does not
conclude the opposite.** ADR-025 §E (`:395`) is about a **client-declared role**
(headers). ADR-025 **§I** (`:658-677`) is the section written specifically to bind
this wave and it governs: *"the token carries identity only … a `tenant_id` or
`roles` claim is **never** the authorization source."* Where the two touch one
mechanism, **§I governs**. ADR-025's body is not rewritten.

### 4. Rejection contract (extends ADR-025 §B; no existing row changes)

| Situation | Status |
|---|---|
| No `Authorization` header, or a token failing 1.1–1.4 | **401** |
| Valid token, required scope absent (1.5) | **403** |
| Valid token, caller is not a member of the selected tenant | **404** — ADR-025 Rule B1, unchanged (a 403 there is a tenant-existence oracle) |
| Valid token, member, wrong role | **403** |
| Forged `X-User-Id` / `X-Tenant-Id` with **no** valid token | **401** (acceptance A15-8) |
| Valid token `A` **plus** `X-User-Id: B` | acts as `A`; the header is **not read** (ADR-025 Rule A3, test T14) |

**4.1 Revocation stays immediate; nothing caches authorization (S15-12).** The
token caches **identity** for its lifetime and nothing else. **No membership
cache, no role cache, no session cookie, no claims transformation that
materializes a role.** ADR-025 Rule D.5b holds *because* role and tenant are read
from the database per request. A removed member's token still authenticates and
gets 404 everywhere — which is the correct answer, not a gap.

**4.2 No authentication kill-switch, in any environment.** This seat co-signs
cloud-architect's ruling (ADR-005 w15 footer §5) from the security side: a
configuration key that turns authentication off is a security control in the hands
of whoever can run an apply, and it is exactly the "temporary" fallback that is
never removed. Rollback is an image revert, not a flag. **NW-05 fails closed**: a
bounded 401 window on `dev` is the correct failure, and no header fallback is
re-added to shorten it.
