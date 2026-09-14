---
id: us-02
type: user-story
parent: feature-01
wave: w15
status: active
---

# us-02-spa-sends-the-token — The SPA acquires the token and attaches it everywhere

## Story

As a **signed-in user**, I want the browser to send the token it already asks
Entra for, on every single request, so that the API can trust me — and so that
one forgotten call site does not log me out of one screen with no explanation.

## Acceptance criteria

- [ ] AC-1 `createApiClient`'s identity parameter becomes an **async token accessor**, and **one** internal helper attaches `Authorization: Bearer` to every request. The 37 inline `userIdHeaders` spreads become one attachment point.
- [ ] AC-2 `userIdHeaders`, the `X-User-Id` header, the synchronous `GetUserId` accessor and `main.tsx`'s closure over `getAllAccounts()[0]?.username` are **deleted, not made conditional**.
- [ ] AC-3 Acquisition is `acquireTokenSilent({ scopes: appConfig.oidcApiScopes, account })` falling back to `acquireTokenPopup` on `InteractionRequiredAuthError` — **never a redirect from inside a request**, because a redirect unloads the page and on `/invite/accept` destroys the in-memory invitation token.
- [ ] AC-4 A **401 after a successful silent acquisition is a server rejection, not a stale token** — surfaced, never retried silently.
- [ ] AC-5 The SPA **never stores the access token**: read from MSAL's cache per request, never copied into React state, a module variable, `sessionStorage` or `localStorage`.
- [ ] AC-6 The SPA reads **no `roles` claim** from the token. `role` continues to come from the `GET /api/workspaces` row, and `workspaceRole.ts`'s least-privilege parse is unchanged.
- [ ] AC-7 `X-Tenant-Id` continues to be sent as a selector; the `raffa.signin.currentWorkspace` **hint survives unchanged** with its four-way resolution order, and the `v2.spec.ts:68-85` e2e seam survives unchanged.
- [ ] AC-8 A **vitest** case enumerates the `ApiClient` surface (36 methods) and asserts **every** method attaches `Authorization`.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-api-validates-the-token | the API must accept `Authorization` before the SPA stops sending the header the API reads today; otherwise the integration branch 401s on every request |

## Architecture decisions in force

- **ADR-012** (w15 §1–§3) — the choke point, the never-stored token, the no-role-claim guard, the tenant selector. This **supersedes exactly one sentence** of ADR-012's w14 clause 4 — *"Headers are attached per method in `client.ts`, never by a global wrapper"* — which described a **mechanism, not a rule**. Clause 4's two actual rules stand: the SPA declares **no role** to the API, ever; and a `{tenantId}` route gets no `X-Tenant-Id`.
- **ADR-010** (w14 clause 3) — the header must stop being *read*; this story is the half where it stops being *sent*.
- **ADR-025 T15** — about the **invitation** token. MSAL's own `sessionStorage` cache (`msalConfig.ts:36`, chosen for a narrower XSS blast radius) is **not** a violation of it; recorded so no task "hardens" the wrong thing.
- **ADR-026** (w14 clause 3) — `role` is a bare wire string, so the least-privilege parse is mandatory rather than defensive.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | One async token accessor, one header helper, 37 call sites collapsed | M | phase-2 |

## Council decisions carried into this story

**This is not a find-and-replace, and it is the one structural client change in
the wave.** `client.ts:48` declares the identity accessor **synchronous**
(`GetUserId = () => string | null`) and its header literal (`:55-58`) is spread
inline into **37 separate `await fetch(` call sites** — verified, exactly 37, one
per operation. Token acquisition is asynchronous, so 37 literals are **37 chances
to omit one**, and after NW-05 an omitted header is a **401 with no fallback**.

**The web half needs no Terraform, no CI change and no contract edit** — stated
plainly so the decomposer does not place this task behind the infrastructure
phase. `buildLoginRequest` already requests the API scopes; `oidcApiScopes` is
already declared, validated and assigned; and the generator parses **only
`responses`**, so headers are hand-written by construction and this story **does
not contend for the contract file**.

**A token is trivially decoded in a browser**, which is exactly why the
no-`roles`-claim guard is written down before the task exists rather than caught
in review.

## Open questions

- **OQ-w15-ca-03** — resolved: the tenant stays a header, so the hint and the e2e seam both survive. The rejected alternative is recorded with its cost so it is not re-proposed as a simplification.
- None blocking.
