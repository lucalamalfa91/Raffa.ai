---
id: E18/F01/US02/T01
type: task
story: us-02-spa-sends-the-token
wave: w15
status: live
target_repo: raffa-web
---

# task-01-spa-sends-the-token — One async token accessor, one header helper, 37 call sites collapsed

## Coding objective

Make the SPA send the token it already asks for. Change `createApiClient`'s
identity parameter from the synchronous `GetUserId = () => string | null`
(`client.ts:48`) to an **async token accessor**, and route every request through
**one** internal helper that attaches `Authorization: Bearer`. Delete
`userIdHeaders` (`:55-58`) and its 37 inline spreads, and delete `main.tsx:70-73`'s
closure over `getAllAccounts()[0]?.username`. Acquisition is
`acquireTokenSilent({ scopes: appConfig.oidcApiScopes, account })` with an
`acquireTokenPopup` fallback on `InteractionRequiredAuthError` — and **never** a
redirect from inside a request.

There is nothing to discover here: `buildLoginRequest` (`msalConfig.ts:80-84`)
already requests `appConfig.oidcApiScopes`, and `web/src` today contains **zero**
`acquireTokenSilent|Popup|Redirect` and **zero** `Bearer`. The SPA asks for the
token and throws it away.

## Parent story AC covered

- AC-1 … AC-8 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/api/client.ts` | the async token accessor (`:48`); one `Authorization` attachment helper; `userIdHeaders` (`:55-58`) and its 37 spreads deleted; `X-Tenant-Id` continues to be sent as a selector. **Single-writer of this file in phase 2** |
| `web/src/main.tsx` | the `getAllAccounts()[0]?.username` closure (`:70-73`) is replaced by the token accessor |
| `web/src/auth/msalConfig.ts` | the acquisition helper (silent → popup); `buildLoginRequest` (`:80-84`) and the `sessionStorage` cache choice (`:36`) are **unchanged** |
| `web/src/api/client.test.ts` (or `web/tests/api/`) | the surface-enumeration vitest case below |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-05 (web half)`.

Decision row: `reports/architecture/waves/w15.md` — **NW-05**, client-architect
cell (the choke point, the acquisition path, the never-stored token, the tenant
selector) and **NW-06**, client-architect cell (the no-role-claim guard).

- **Architecture decisions in force**: **ADR-012** w15 §1–§3; **ADR-010** w14
  clause 3; **ADR-025 T15**; **ADR-026** w14 clause 3.
- **One sentence of ADR-012's w14 clause 4 is superseded** — *"Headers are
  attached per method in `client.ts`, never by a global wrapper"* — because it
  described a **mechanism, not a rule**. Clause 4's two actual rules are
  re-affirmed and must survive this edit: the SPA declares **no role** to the
  API ever (`X-Role`/`X-Workspace-Role`: still **zero** occurrences in
  `web/src`), and a `{tenantId}` route gets **no** `X-Tenant-Id`.
- **Never a redirect from inside a request.** A redirect unloads the page, and on
  `/invite/accept` that destroys the in-memory invitation token (ADR-012 w14
  clause 6; ADR-020 state 5).
- **A 401 after a successful silent acquisition is a server rejection, not a
  stale token.** Surface it; do not retry silently. A silent retry loop here
  hides exactly the misconfiguration A15-8's second half exists to catch.
- **Do not "harden" MSAL's cache.** ADR-025 T15 is about the **invitation**
  token; `msalConfig.ts:36`'s `sessionStorage` cache was chosen deliberately for
  a narrower XSS blast radius and is not a violation.
- **Do not read a `roles` claim.** A token is trivially decoded in a browser;
  `role` keeps coming from the `GET /api/workspaces` row, which
  `useValidatedContractCount.ts:65` already reads, and
  `web/src/components/shell/workspaceRole.ts:37-39`'s least-privilege parse is
  unchanged — including the deliberate split at `:41-53`, where permissions
  degrade to least privilege and the **label does not**.
- **`web.yml:201-205`'s scope literals are an equality to preserve, not an edit
  to make.** They hardcode `api://raffa-<env>-api/Raffa.Read|Write` into the
  SPA's `config.json` and the SPA already requests them; NW-05 reuses them
  **verbatim**. A mismatch fails **at token acquisition in the browser, after CI
  is green** — which is why the pre-wave sign-in check is W15-A1 point (f).
- **Do not touch**: `web/openapi/raffa-api.v1.json` and `web/src/api/generated/**`
  — the generator parses only `responses`, so headers are hand-written by
  construction and this task does not contend for the contract. Do not touch
  `web/src/routes/**`. Do not touch `.github/workflows/**`. Do not change
  `appConfig.ts`'s validation — but note that its doc comment at `:30` still
  documents the scope in the `api://<client-id>/…` form this product does not
  use, which is where the earlier confusion came from.

## Definition of done

- [ ] `cd web && npm run build` exit 0 (runs `generate:api && tsc --noEmit && vite build`)
- [ ] `cd web && npm test` exit 0 — including the surface-enumeration case
- [ ] `grep -rn "X-User-Id\|userIdHeaders" web/src/` returns **no match**
- [ ] `grep -c "Authorization" web/src/api/client.ts` shows the header attached in **one** place, not 37
- [ ] `grep -rn "acquireTokenRedirect" web/src/` returns **no match**
- [ ] `grep -rn "X-Role\|X-Workspace-Role" web/src/` still returns **no match**
- [ ] `grep -rn "\"roles\"\|idTokenClaims\[.roles.\]" web/src/` shows no role read from a token

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | **every** method of the `ApiClient` surface (`client.ts:1021`, 36 methods) attaches `Authorization` — the 37-call-site risk gets a real gate this wave rather than a deferred one | `web/src/api/client.test.ts` |
| unit | `InteractionRequiredAuthError` falls back to popup, never to redirect | `web/tests/auth/msalConfig.test.ts` |
| unit | a 401 after a successful silent acquisition surfaces rather than retrying | `web/src/api/client.test.ts` |

`web.yml` runs `npm test` (vitest) today, so these run in CI this wave.
A15-8's forged-header assertion belongs to the backend seats: **a browser cannot
forge a header it does not send.**

## Open questions blocking this task

- **OQ-w15-ca-03** — resolved: the tenant stays a header. Not blocking.

## Wave-spec entry
```yaml
- id: E18/F01/US02/T01
  prompt: reports/workitems/epic-18-api-authentication/feature-01-token-identity/us-02-spa-sends-the-token/tasks/task-01-spa-sends-the-token.md
  produces: [web-bearer-token]
  depends_on: [api-jwt-identity]
  effort: M
  layer: frontend
  status: live
```
