# Wave w14 "workspace is real" — acceptance runbook (N1–N9, W14-A1, W14-A2)

Operator checklist for wave `wave-next-w14` (`.helix/reports/context/waves/w14-requirements.md`
§5; decision record `.helix/reports/architecture/waves/w14.md`, §"What the final-integration
task must run"). One section per acceptance item, each naming **the URL, the header posture
and the expected status code** plus the observable pass condition — nothing here is "check
that it looks right". Same shape as [`../ask-v2-acceptance.md`](../ask-v2-acceptance.md).

| | |
|---|---|
| Owner | task E14/F06/US01/T01 (`w14-integration`), story `us-01-final-integration` |
| Oracles | `w14-requirements.md` items W14-01, NW-01, NW-02, NW-03, NW-04, NW-09, NW-14, NW-24, NW-58 · ADR-025 (membership & invitations) · ADR-026 (workspace API & invitation schema) · ADR-014 / ADR-016 w14 footers (wave base, promotion) |
| API contract | `web/openapi/raffa-api.v1.json` — **every route below is quoted from it**; the one gap is named in [Known gaps](#known-gaps-that-shape-acceptance-today) |
| Screens | `web/README.md` "Screens (ADR-024 V2 route map)" → `/signin`, `/invite/accept`, `/workspace/members`; "Invitation accept"; "Workspace & members" |
| Automated cover | `backend/tests/Raffa.Api.Tests` (`WorkspaceDirectoryEndpointTests`, `WorkspaceInviteAuthorizationTests`, `InvitationLifecycleEndpointTests`, `MembershipRemovalEndpointTests`, `RemovedMemberRetrievalTests`), `Raffa.Identity.Workspace.Tests` (`WorkspaceDirectoryServiceTests`, `WorkspaceRosterTests`), `Raffa.IntegrationTests` — all under `dotnet test Raffa.slnx` in `backend.yml` · `web/e2e/day1.spec.ts` step "Invite a Procurement user" (N3, the reload-surviving roster) · `web/e2e/invite.spec.ts` (N3b, **skipped with a named reason** until a second Entra account exists) |

Run this against **`dev`** — that is what the wave's Definition of Done asks for. Promotion to
`demo` is a separate operator act *after* this walk (ADR-016 w14 footer): four separate
approvals, `seed-demo-fixture.yml` on `demo` only after `promote-backend` has applied the
schema its guard requires, and **no `demo-v*` tag is cut by the wave itself**. The standing
promotion check `scripts/check_demo_swa_config.py` (it asserts on the brand substring
`raffa-<env>-api`, repo-root `scripts/`) is unchanged by w14 and still gates every promotion.

---

## 0. Before you start

### 0.1 The operator sequence

Run these **in order**. Every step is an explicit, dispatchable act — none is a side effect of
a push.

| # | What | How |
|---|---|---|
| 1 | Deploy + apply the schema | merge to `main` (auto `dev`): `.github/workflows/backend.yml` must reach its **"Verify schema applied (ADR-021)"** step green — that run is W14-A1 point (d) below |
| 2 | **Backfill a membership for every workspace that already existed on `dev`** — *before* anyone signs in on the new build | Actions → **backfill-workspace-membership** → `target_environment: dev`, one dispatch per (workspace id, admin email) pair. `GET /api/workspaces` lists **live memberships only** (ADR-025 Rule F.1d); a pre-w14 workspace has none, so without this step its creator signs in and is offered "Create your workspace" — the promotion blocker the delivery-manager row of `w14.md` NW-58 records |
| 3 | Set `DEMO_ADMIN_EMAIL` **before the next `demo` seed** | repository/environment variable read by `seed-demo-fixture.yml`; the ADR-022 fixture tenant is seeded by SQL and can never receive a membership from the create path, so the seed now writes its Admin membership onto that address (`demo-fixture-seed.sql`, task E14/F05/US01/T01) — `demo` only, but decide it now |
| 4 | Walk W14-A1, then N1 … N9, W14-A2 | this document |

### 0.2 The values every command below needs

```bash
# 1. API origin — the same value web.yml bakes into the SPA's config.json.
ENV=dev
RG="rg-raffa-${ENV}"
API=$(az resource show --resource-group "$RG" --name "ca-raffa-${ENV}-api" \
        --resource-type Microsoft.App/containerApps \
        --query "properties.configuration.ingress.fqdn" -o tsv)
API="https://${API}"

# 2. SPA origin — where you click.
WEB=$(az resource show --resource-group "$RG" --name "swa-raffa-${ENV}" \
        --resource-type Microsoft.Web/staticSites \
        --query "properties.defaultHostname" -o tsv)
WEB="https://${WEB}"

# 3. Identity — the address you sign in with. The SPA sends the MSAL account
#    username as X-User-Id on every call (ADR-022 posture; ADR-025 §A2: trusted for
#    one thing only — which membership rows to look up).
ME=<your UPN>
INVITEE=<the second account's UPN>      # N3, N3b, N9
```

**Header posture for every w14 route** (`/api/workspaces…`, `/api/invites…`): identity is
`X-User-Id`; the tenant is the **route** value (`/api/workspaces/{tenantId}/…`) or, for
`GET /api/workspaces`, "whatever this identity belongs to" — **`X-Tenant-Id` is never read**
by any of them (ADR-025 §F, ADR-009 w14 footer clause 7). The two `/api/invites` routes carry
the token in **`X-Invitation-Token`**, never a path or query string (Rule C9). The document
routes N9 touches still take `X-Tenant-Id` (unchanged). Every `curl` below sets exactly the
headers the route reads; the `TENANT` used from N3 on is the workspace id N1 creates.

### 0.3 Direct database access (only where the UI cannot show it)

N1 needs to observe *rows*, and N3b needs to observe an *absence* (no membership after
removal, the invitation revoked), which no screen can prove. Same connection the workflows use:

```bash
KV=$(az keyvault list --resource-group "$RG" --query "[0].name" -o tsv)
eval "$(az keyvault secret show --vault-name "$KV" --name postgres-connection \
        --query value -o tsv | python scripts/pg_connection_string_env.py)"

# RLS stays on. Set the same session claim the app sets (ADR-009); never disable a policy.
psql -Atqc "SET app.tenant_id = '$TENANT'; SELECT count(*) FROM workspace_membership;"
```

---

## W14-A1 — the five-point green-base proof (record it here)

> Recorded at HITL before `reports/plan/gates/w14.hitl-ok` was created; the last two points
> are the ones that matter (`w14.md` row W14-01, ADR-014 w14 footer). This section is the
> record; fill in the right-hand column.

| Point | What | Proven by | Result |
|---|---|---|---|
| (a) | `rg -n "Contigo\." backend/src web/src` returns nothing | run on the wave base at `main` `6e14b39` (this task's own PR): **nothing** | ☐ re-run on the promoted sha: |
| (b) | `dotnet build` + `dotnet test Raffa.slnx`, `npm ci && npm run build && npm test` green | `backend / build + test` and `web / build` green on `main` for PR #93 and PR #95 (the two merges that closed the wave), and on this task's own PR. Locally on the operator machine the Postgres Testcontainers suites are **CI-only** (Docker Desktop does not start there) — `dotnet test` locally reports them as fixture failures, which is not a green and is not claimed as one | ☐ |
| (c) | every `raffa` literal in `.github/`, `infra/`, `scripts/`, `backend/scripts/` reviewed **line by line** against live Azure / HCP / **Entra** — the checklist is the W14-01 row of `w14.md` (resource-group, container-app, SWA, identity, image, database, SP, Key Vault names; the duplicated ADR-021 script array in `backend.yml`; `check_demo_swa_config.py`'s brand substring) | operator, at HITL | ☐ |
| (d) | **one throwaway `dev` deploy is green and reaches `backend.yml`'s "Verify schema applied (ADR-021)" step** | the `main` push for PR #93 / #95 triggered `backend.yml`; open that run → job `deploy (dev)` → that step | ☐ run id: |
| (e) | **one interactive sign-in on deployed `dev` returns a token carrying the expected scope** — identity-plane, so it fails in the browser *after* CI is green (ADR-010 w14 footer) | operator: `$WEB/signin` → **Continue with Microsoft Entra ID** → after landing, DevTools → Application → Session Storage → the MSAL `accesstoken` entry for `api://raffa-dev-api` → decode the JWT payload: `aud` = `api://raffa-dev-api`, `scp` contains `Raffa.Read Raffa.Write` (`web.yml:204-205`) | ☐ |

A wrong resource name fails a deploy loudly at `az … show`; a renamed **scope** that does
not match the app registration fails at token acquisition, in the browser, after CI is green
— and every w14 item is about the signed-in identity, which is why (e) exists.

---

## N1 — creating a workspace records its creator as Admin

> Create a workspace on `dev`; `curl GET /api/workspaces` with the same `X-User-Id` → rows in
> `workspace`, `workspace_user`, `workspace_membership` (Admin); the list contains it with
> `role: "Admin"`. (NW-02, ADR-025 §2.3, ADR-026 §D1; task E14/F02/US01/T01)

**Click path.** `$WEB/signin` → sign in as `$ME` → on the **Create your workspace** form
(rendered when this identity holds no membership yet — there is no separate "no workspaces"
screen) fill **Company**, pick **Industry** and **Country** → **Create workspace**.

**Pass when:** the shell mounts with the workspace name in the rail
(`.shell-rail-workspace-name`), and **Workspace & members** in the rail footer lists `$ME` as
`Active` · Workspace Admin with the **You** marker.

**Same thing over the API** — `POST /api/workspaces` is the only route in this wave that
takes a body and no tenant:

```bash
# 201 { id, name, createdAt, role } — role is "Admin"; 401 without X-User-Id.
curl -sS -o /tmp/ws.json -w '%{http_code}\n' -X POST "$API/api/workspaces" \
  -H "X-User-Id: $ME" -H 'Content-Type: application/json' \
  -d '{"name":"Acceptance Co","industry":"Other","country":"CH"}'
TENANT=$(jq -r .id /tmp/ws.json)

# 200 { workspaces: [ { id, name, createdAt, role, contractCount, country, currency } ] }
curl -sS "$API/api/workspaces" -H "X-User-Id: $ME" \
  | jq -r '.workspaces[] | [.id, .name, .role, .contractCount, (.country // "-"), (.currency // "-")] | @tsv'
# => contains $TENANT with role "Admin"

# 401: no identity at all.
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/workspaces"        # => 401
```

**The rows — the part only SQL can prove** (`WorkspaceProvisioningService` writes all three in
one transaction; before w14 the creator's membership was never recorded at all):

```bash
psql -Atqc "SET app.tenant_id = '$TENANT';
  SELECT (SELECT count(*) FROM workspace),
         (SELECT count(*) FROM workspace_user),
         (SELECT count(*) FROM workspace_membership m JOIN workspace_role r ON r.id = m.workspace_role_id
           WHERE r.name = 'Admin');"        # => 1|1|1
```

A **405** on `GET /api/workspaces` (the picker's *"Workspaces unavailable · Request failed with
HTTP 405"*) means the **pre-w14 API is still deployed** — before this wave only
`POST /api/workspaces` existed on that path. That is step 1 of §0.1 not having landed, not a
product defect; check the `deploy (dev)` job of the latest `backend.yml` run.

**Automated:** `Raffa.Api.Tests.WorkspaceDirectoryEndpointTests` (`T1a_identity_gets_exactly_its_own_tenant_and_nothing_from_another`, `A_caller_with_no_membership_gets_200_and_an_empty_array`, `Missing_identity_returns_401`, `Blank_identity_header_returns_401`), `Raffa.Identity.Workspace.Tests.WorkspaceProvisioningServiceTests`.

---

## N2 — a second browser finds the same workspace; nothing is created twice

> Sign in from a **second browser** with cleared storage → the same workspace is listed;
> **no second create**. (NW-01, ADR-026 §D1 "discovery is by identity, never by browser")

**Click path.** A private window (or another browser) → `$WEB/signin` → sign in as `$ME`.

**Pass when:** the browser lands **directly in the shell** for the N1 workspace (one
membership ⇒ no picker, no create form), or on **Choose a workspace** listing it when `$ME`
holds several. The create form must **not** appear — `sessionStorage` is empty in this
browser, and the list comes from `GET /api/workspaces`, not from any per-browser store (the old
`localStorage` array is gone).

```bash
# The identical answer, from anywhere — no cookie, no storage, only the identity:
curl -sS "$API/api/workspaces" -H "X-User-Id: $ME" | jq '.workspaces | length'   # => same count as N1
```

**Automated:** `WorkspaceDirectoryServiceTests` (identity-keyed discovery; a person holding two
roles in one tenant appears once, at the highest), `web/e2e/day1.spec.ts` step
*"Workspace: resolve from the server — pick, create, or auto-enter (N2)"*.

---

## N3 — an invitation survives a reload

> Invite a Procurement address, reload `/workspace/members` → the roster still lists the
> invitee as `Invited`. (NW-04, ADR-026 §D3; tasks E14/F04/US01/T01, E15/F02/US01/T01)

**Click path.** `$WEB/workspace/members` as `$ME` → **Invite a colleague** → **Work email**
`$INVITEE`, **Procurement** → **Send invitation**. Then **reload the page**.

**Pass when:**

1. Right after submit the pane reads **"Invitation ready for `$INVITEE`."** with a copyable
   single-use link (field labelled *Invitation link*, a **Copy link** button, and an expiry
   line). The word **"sent" appears nowhere** — `mailDelivered` is `false` by construction in
   w14 (no mail transport, ADR-026 §D6), and the client never infers delivery from a 201.
2. After the reload the roster is re-read from the server (a skeleton, then the table) and
   still lists `$INVITEE` · Procurement · **Invited** — nothing about it lived in this
   browser. The pane's link is gone (it was one response, not a stored fact).
3. An address outside `$ME`'s domain shows a **warning** under the field and still submits;
   a malformed address is the only thing that blocks.

```bash
# 201 { id, email, role, expiresAt, acceptUrl, mailDelivered }
#   acceptUrl is site-relative ("/invite/accept#<token>") and mailDelivered is false.
# 401 no identity → 404 not a member of the *route* tenant → 403 member but not Admin.
curl -sS -o /tmp/invite.json -w '%{http_code}\n' -X POST "$API/api/workspaces/$TENANT/invites" \
  -H "X-User-Id: $ME" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$INVITEE\",\"role\":\"Procurement\"}"
jq '{id, role, expiresAt, mailDelivered, acceptUrl}' /tmp/invite.json
TOKEN=$(jq -r .acceptUrl /tmp/invite.json | sed 's#^/invite/accept\##')
INVITATION_ID=$(jq -r .id /tmp/invite.json)

# The roster: live memberships ∪ live invitations, never a scan of workspace_user.
# status is "Active" or "Invited"; membershipId / invitationId are the action ids.
curl -sS "$API/api/workspaces/$TENANT/members" -H "X-User-Id: $ME" \
  | jq -r '.members[] | [.email, .role, .status, (.membershipId // "-"), (.invitationId // "-")] | @tsv'
# => $INVITEE  Procurement  Invited  -  <invitationId>

# 409: the same address at the same role again.
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$API/api/workspaces/$TENANT/invites" \
  -H "X-User-Id: $ME" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$INVITEE\",\"role\":\"Procurement\"}"                # => 409
```

**"Invite grants nothing"** — the part only SQL can prove (ADR-025 Rule D.1: invite writes
`workspace_user` + `workspace_invitation`, **never** a membership):

```bash
psql -Atqc "SET app.tenant_id = '$TENANT';
  SELECT count(*) FROM workspace_membership m JOIN workspace_user u ON u.id = m.workspace_user_id
   WHERE lower(u.email) = lower('$INVITEE');"                          # => 0
```

**Automated:** `web/e2e/day1.spec.ts` → step *"Invite a Procurement user (AC-1 step 2)"* now
performs `page.reload()` between the click and its assertions — that reload is the whole of
N3. Read that step's verdict in the Playwright report: the file as a whole is the V1 walk and
is still red at its last step ("Home", `web/README.md` → "Known regression", owned by
`E13/F09/US01/T01`'s `v2.spec.ts` replacement, not by this wave). Backend:
`WorkspaceInviteAuthorizationTests` (401/404/403/201, an Admin may invite another Admin),
`InvitationLifecycleEndpointTests`, `WorkspaceRosterTests`.

---

## N3b — accept in a second browser, then be removed

> Copy the accept link from the invite result (**`mailDelivered` is `false` in w14 — there is
> no inbox step**), open it in a second browser, sign in as the invitee, then have the Admin
> remove the member → the invitee lands in **that** workspace; after removal their next load
> loses access; the stale link is 404; a **new** invitation is needed to re-join.
> (NW-58 N3b-1…N3b-8, ADR-025 Rules C4/C5/C9/C10/D.3/D.5, ADR-026 §D4/§D5; tasks
> E15/F01/US01/T01, E14/F03/US02/T01, E15/F02/US01/T01)

**Click path.**

1. As `$ME` on `$WEB/workspace/members`: invite `$INVITEE` (N3) → **Copy link**.
2. In a **second browser** (cleared storage, **not** signed in): open the copied link. The
   accept screen renders **outside** the app shell — heading **Join Acceptance Co** (the
   workspace name and the offered role, **and nothing else**: never the invited address, never
   a contract count) with the CTA **Continue with Microsoft Entra ID**.
3. Click it → the sign-in opens in a **popup** (`loginPopup`, ADR-012 w14 footer clause 6 —
   the page is never unloaded, so the token held in memory survives) → sign in as `$INVITEE`
   → the same CTA now reads **Join Acceptance Co** → click it.
4. Back as `$ME`: reload `/workspace/members` → `$INVITEE` is now **Active** · Procurement →
   **Remove** on that row → the inline confirmation (never a dialog) → **Yes, remove**.
5. In the second browser: reload.

**Pass when:**

- Step 3 lands the invitee **in Acceptance Co** (rail shows its name; `/ask`), **not on a
  create form** — membership is proven by the next `GET /api/workspaces`, the accept body is
  only a hint.
- The address bar never shows the token: the fragment is cleared before first paint; a
  reload of the accept page before joining shows **"Open your invitation link again"** — a
  normal outcome, not an error (the token lives in memory for one mount).
- Signed in as a **different** account than the invited one, the accept screen shows **"This
  invitation was sent to a different address."** — and does not say which.
- Step 4: `$ME`'s own row keeps a **disabled Remove with a hint** while `$ME` is the last
  Admin ("Invite another Workspace Admin first") — never a click that 409s.
- Step 5: the invitee's next load resolves an **empty** list → **Create your workspace** — no
  new endpoint, only the ordinary sign-in revalidation; `/workspace/members` of that tenant
  is not reachable. Opening the **same link again** shows **"This invitation is no longer
  valid."** Re-joining needs a **new** invitation from `$ME`.

```bash
# Pre-accept read: 200 { workspaceName, role, expiresAt } — nothing else, ever.
# 404 unknown/revoked/accepted/malformed (one answer for every "not yours"), 410 expired.
curl -sS "$API/api/invites" -H "X-Invitation-Token: $TOKEN" | jq .

# Accept as the invitee: 200 { workspaceId, workspaceName, role }.
# 401 no identity · 403 the signed-in address ≠ the invited one (reason never echoes it,
# no membership written) · 409 a second accept by the same identity · 410 expired.
curl -sS -w '%{http_code}\n' -X POST "$API/api/invites/accept" \
  -H "X-Invitation-Token: $TOKEN" -H "X-User-Id: $INVITEE" | jq .
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$API/api/invites/accept" \
  -H "X-Invitation-Token: $TOKEN" -H "X-User-Id: $INVITEE"                  # => 409
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$API/api/invites/accept" \
  -H "X-Invitation-Token: $TOKEN" -H "X-User-Id: someone.else@raffa.test"   # => 403

# The invitee now holds a live membership: their own list has the tenant, the roster says Active.
curl -sS "$API/api/workspaces" -H "X-User-Id: $INVITEE" | jq -r '.workspaces[] | [.id, .role] | @tsv'
MEMBERSHIP_ID=$(curl -sS "$API/api/workspaces/$TENANT/members" -H "X-User-Id: $ME" \
  | jq -r --arg e "$INVITEE" '.members[] | select(.email | ascii_downcase == ($e | ascii_downcase)) | .membershipId')

# A Procurement member cannot remove anyone (403); a non-member gets 404, never 403.
curl -sS -o /dev/null -w '%{http_code}\n' -X DELETE "$API/api/workspaces/$TENANT/members/$MEMBERSHIP_ID" \
  -H "X-User-Id: $INVITEE"                                                  # => 403

# Remove as the Admin: 204. Deletes the membership only — never workspace_user — and revokes
# that email's live invitations in the same transaction. 409 if the target is the last Admin.
curl -sS -o /dev/null -w '%{http_code}\n' -X DELETE "$API/api/workspaces/$TENANT/members/$MEMBERSHIP_ID" \
  -H "X-User-Id: $ME"                                                       # => 204

# Immediate — nothing caches authorization:
curl -sS "$API/api/workspaces" -H "X-User-Id: $INVITEE" | jq '.workspaces | map(select(.id == "'$TENANT'")) | length'  # => 0
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/workspaces/$TENANT/members" -H "X-User-Id: $INVITEE"   # => 404
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/invites" -H "X-Invitation-Token: $TOKEN"              # => 404
```

**The absence — the part only SQL can prove** (the user row survives for audit continuity;
the membership is gone; the invitation is revoked, not deleted):

```bash
psql -Atqc "SET app.tenant_id = '$TENANT';
  SELECT (SELECT count(*) FROM workspace_user WHERE lower(email) = lower('$INVITEE')),
         (SELECT count(*) FROM workspace_membership m JOIN workspace_user u ON u.id = m.workspace_user_id
           WHERE lower(u.email) = lower('$INVITEE')),
         (SELECT count(*) FROM workspace_invitation
           WHERE lower(email) = lower('$INVITEE') AND revoked_at IS NULL);"   # => 1|0|0
```

The token, its hash and the raw identity headers are **never** written to `audit_event` or a
log (ADR-025 §G, ADR-011 w14 footer) — check the `workspace.invitation.*` /
`workspace.membership.*` rows (`issued`, `accepted`, `rejected`, `revoked`; `granted`,
`removed`) carry ids and actions only:

```bash
psql -Atqc "SET app.tenant_id = '$TENANT';
  SELECT action, left(detail, 120) FROM audit_event
   WHERE action LIKE 'workspace.invitation.%' OR action LIKE 'workspace.membership.%'
   ORDER BY occurred_at;"
```

**Automated:** `web/e2e/invite.spec.ts` — the two-account path above, **skipped with the named
reason "requires a second Entra account on the pilot tenant"** until
`RAFFA_E2E_SECOND_ENTRA_EMAIL` / `_PASSWORD` exist (an operator prerequisite recorded in
`reports/audit/w14-hitl.md`; the second account need **not** share the Admin's email domain —
after w14 the cross-domain check is a warning, not a block). Backend:
`InvitationLifecycleEndpointTests` (T1–T13), `MembershipRemovalEndpointTests` (T7a/T7c/T8, the
last-Admin 409, 404-never-403), `RemovedMemberRetrievalTests` (T7d — a removed member's Ask
call scoped to that tenant retrieves nothing from its corpus).

---

## N4 — one membership, no picker

> Close the tab and reopen with one membership → the shell mounts on `/ask`, no picker.
> (NW-03, ADR-020 w14 footer screen 1; task E14/F03/US02/T01)

**Click path.** As `$ME` holding exactly one membership: close the tab → open `$WEB/`.

**Pass when:** the browser ends on `$WEB/ask` inside the shell, with the workspace name in the
rail, having shown **neither** the picker **nor** the create form **nor a flash of the sign-in
screen** while it resolved (the resolving state must not read as a logout). Clear
`sessionStorage` and repeat: identical — the hint is a convenience, the list is the truth
(`hint ∉ list ⇒ discard the hint`). With two memberships the same open lands on **Choose a
workspace** instead.

**Automated:** `web/e2e/day1.spec.ts` → step *"N4 — reload with the session hint cleared, MSAL
account intact → no picker"*.

---

## N5 — a crafted `X-Tenant-Id` changes nothing; another tenant's roster is 404

> `curl GET /api/workspaces` with a crafted `X-Tenant-Id` for another tenant; `curl GET
> /api/workspaces/{otherTenant}/members` → the header changes nothing; the members call is
> **404**. (NW-01/NW-04, ADR-025 §F/§D.4, ADR-009 w14 footer clause 7)

**Not a browser check** — the SPA never lets you type a header. `OTHER` is any other
workspace's id (a colleague's, or a second one you create and then leave).

```bash
OTHER=<another tenant id>

# The header is not read: the list is identical with and without it.
diff <(curl -sS "$API/api/workspaces" -H "X-User-Id: $ME" | jq -S .) \
     <(curl -sS "$API/api/workspaces" -H "X-User-Id: $ME" -H "X-Tenant-Id: $OTHER" | jq -S .) && echo same

# Verify, then scope, then read: not a member of the *route* tenant ⇒ 404 —
# never 403 (a tenant-existence oracle), never an empty 200 (also an oracle).
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/workspaces/$OTHER/members" -H "X-User-Id: $ME"   # => 404
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/workspaces/$OTHER/members" \
  -H "X-User-Id: $ME" -H "X-Tenant-Id: $OTHER"                                                    # => 404 (header still ignored)
curl -sS -o /dev/null -w '%{http_code}\n' "$API/api/workspaces/$OTHER/members"                     # => 401
```

**Automated:** `WorkspaceDirectoryEndpointTests.T2b_a_crafted_tenant_header_changes_nothing`
and `T1a_identity_gets_exactly_its_own_tenant_and_nothing_from_another`,
`MembershipRemovalEndpointTests.Non_member_gets_404_never_403`,
`Raffa.Identity.Workspace.Tests.WorkspaceRlsCrossTenantIsolationTests` (RLS under the
unprivileged app role).

---

## N8 — the validated-contract count is the same number everywhere

> Upload ≥ 1 document, sign out, sign in → the picker count is non-zero and the rail's
> secondary badge shows the same number; `/documents` lists the same files. (NW-09, ADR-026
> §D2 "one definition, two callers"; task E14/F03/US01/T01)

**Click path.** `$WEB/documents` → upload a real contract PDF → let it reach **Completed**
(review and **Mark as validated** if it stops at **Needs review** — the count is of
**validated** contracts, i.e. a linked document that reached `Completed`; an upload parked
at needs-review does **not** count). Sign out, sign in.

**Pass when:** the picker row (or, with one membership, the rail) reads **"1 validated
contract"** (singular; `No validated contracts yet` at zero), the rail's **From your
contracts** items are no longer greyed and Ask is on, and `/documents` lists the same file
— three surfaces, one number.

```bash
# The count on the list row is PortfolioQueryService.CountValidatedContractsAsync joined in
# the host — the same rule GET /api/savings/kpis's contractsAnalyzedCount uses.
curl -sS "$API/api/workspaces" -H "X-User-Id: $ME" | jq -r '.workspaces[] | [.name, .contractCount] | @tsv'
curl -sS "$API/api/savings/kpis" -H "X-Tenant-Id: $TENANT" | jq .contractsAnalyzedCount    # => the same number
curl -sS "$API/api/documents?pageSize=100" -H "X-Tenant-Id: $TENANT" \
  | jq '[.items[] | select(.processingStatus == "Completed")] | length'                   # => the same number
```

**Automated:** `Raffa.Documents.Contracts.Tests.ValidatedContractCountTests` (a real SQL
`COUNT`, equal to `ContractsAnalyzedCount`, distinct per contract, RLS-scoped),
`WorkspaceDirectoryEndpointTests.ContractCount_reflects_real_validated_contract_data`.

---

## N9 — Admin actions are gated on the membership row, not on a header

> As the creator: Delete a document, Retry a failed upload; then as a Procurement member →
> 204 and 200; the Procurement member gets 403 and sees no Delete affordance. (NW-14,
> ADR-025 §E "a client-declared role is never an authorization source", ADR-022 w14 footer
> clause 1; task E14/F02/US02/T01)

**Click path.** As `$ME` (Admin) on `$WEB/documents`: **Delete** a document; **Retry** a
failed one. Then, in the second browser as `$INVITEE` (re-invited and accepted at
**Procurement**, N3b): the same list shows **no Delete affordance**.

**Pass when:** 204 / 200 for the Admin; the Procurement member's direct API call is **403**;
and — the point of NW-14 — an `X-Role: Admin` / `X-Workspace-Role: Admin` header sent by a
Procurement member **grants nothing**, while `X-Role: Procurement` sent by the Admin **revokes
nothing**. The membership row decides in both directions (ADR-025 Rule E2); the only
remaining reader of `X-Role` is the tenant-agnostic `GET /api/capabilities`, which hides a
menu entry and authorizes nothing.

```bash
DOC=<a document id in $TENANT>

curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$API/api/documents/$DOC/reprocess" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $ME"                                          # => 200
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$API/api/documents/$DOC/reprocess" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $INVITEE"                                     # => 403
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$API/api/documents/$DOC/reprocess" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $INVITEE" -H "X-Role: Admin" -H "X-Workspace-Role: Admin"   # => 403 (header ignored)
curl -sS -o /dev/null -w '%{http_code}\n' -X DELETE "$API/api/documents/$DOC" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $INVITEE"                                     # => 403
curl -sS -o /dev/null -w '%{http_code}\n' -X DELETE "$API/api/documents/$DOC" \
  -H "X-Tenant-Id: $TENANT" -H "X-User-Id: $ME" -H "X-Role: Procurement"                 # => 204 (header ignored)
```

**Automated:** `Raffa.Api.Tests.DocumentAdminActionsAuthorizationTests`,
`DocumentsV2EndpointTests` (membership-seeded 403/204), `Raffa.IntegrationTests.R1DocumentsV2EndToEndTests`
(the same over real Postgres + RLS, membership seeded under the tenant scope).

---

## W14-A2 — a workspace has an industry and a country; the pick row says so

> Create a workspace with Industry and Country → the picker row reads "N validated contracts
> · {currency} · {country name}" and survives a reload and a second browser. (NW-24, ADR-003
> w14 footer, ADR-020 w14 footer screen 1; task E14/F01/US01/T02)

**Click path.** `$WEB/signin` → **Create your workspace** — **Company** (the field is `name`,
the label is *Company*), **Industry** (five options ending in **Other**), **Country**
(**Switzerland · Italy · Germany · Austria**, and nothing else — which is why no currency
control exists: currency is **derived** from the country and the derivation is total over
that list) → the line **"Amounts are shown in CHF."** appears under Country before you submit
→ **Create workspace**.

**Pass when:** with two or more memberships the picker row reads **"No validated contracts
yet · CHF · Switzerland"** — a segment list joined by ` · ` that **drops** a missing segment
(never a gap, a dash or a placeholder), with the count in its zero / singular / plural forms —
and the row carries the **server role** (`Admin` here; a Procurement member's row says so,
never a frozen "Workspace Admin"). The same row renders after a reload and in a second browser.
The third segment is the **business country name**, never a cloud-region slug.

```bash
# 201 — industry/country are closed lists, currency is derived and stored, never typed;
# 400 through the ordinary Result<T> failure path for a 3-letter country or a bad code —
# never a raw Postgres length error.
curl -sS -X POST "$API/api/workspaces" -H "X-User-Id: $ME" -H 'Content-Type: application/json' \
  -d '{"name":"Profile Co","industry":"Manufacturing","country":"IT"}' | jq .
curl -sS -o /dev/null -w '%{http_code}\n' -X POST "$API/api/workspaces" -H "X-User-Id: $ME" \
  -H 'Content-Type: application/json' -d '{"name":"Bad Co","industry":"Other","country":"ITA"}'   # => 400

curl -sS "$API/api/workspaces" -H "X-User-Id: $ME" \
  | jq -r '.workspaces[] | [.name, .contractCount, (.currency // "-"), (.country // "-")] | @tsv'
# => Profile Co  0  EUR  IT      (the SPA renders the country *name*; a pre-w14 row shows "-" and drops the segment)
```

A workspace currency is a display default only — it **never** overrides a contract's own
extracted currency (ADR-003 w14 footer clause 4).

**Automated:** `WorkspaceDirectoryEndpointTests` (`Create_with_industry_and_country_derives_and_stores_currency`,
`An_unsupported_country_is_400_not_a_raw_postgres_error`,
`Absent_profile_fields_round_trip_as_absent_never_an_invented_default`),
`web/tests/routes/signin/*` (the picker's segment list, its degraded / zero / singular forms,
the server role tag).

---

## Running the automated walk

```bash
cd web
npm ci
npx playwright install --with-deps chromium      # one-time

RAFFA_E2E_BASE_URL="$WEB" \
RAFFA_E2E_ENTRA_EMAIL=<the Admin test account UPN> \
RAFFA_E2E_ENTRA_PASSWORD=<that account's password> \
  npx playwright test day1.spec.ts invite.spec.ts

# N3b needs the second account; until it exists invite.spec.ts reports itself as skipped
# with the reason "requires a second Entra account on the pilot tenant".
RAFFA_E2E_SECOND_ENTRA_EMAIL=<the invitee test account UPN> \
RAFFA_E2E_SECOND_ENTRA_PASSWORD=<its password> \
  ... npx playwright test invite.spec.ts

npx playwright show-report                       # trace / video / screenshot on failure
```

With none of the variables set both files report every case as **skipped** with its reason
and exit `0` — never a silent pass, never an attempted sign-in. With them set, read
`day1.spec.ts`'s **step 2** ("Invite a Procurement user") in the report for N3: the walk
continues into the V1 steps and is red at its **last** step by design (see Known gaps), so
its exit code is not the N3 verdict — the step is. No CI workflow runs Playwright
(`web.yml`; NW-50 is queued to W18), which is why N9 and the browser halves of N1–N4 are in
this document at all.

---

## Known gaps that shape acceptance today

Recorded as of `main` @ `6e14b39` (PR #93 + PR #95 merged, the two merges that closed the
wave). None of them is fixed by this document; each one changes what a row can honestly prove.

| # | Gap | Effect on acceptance |
|---|---|---|
| 1 | **No mail transport in w14** (OQ-w14-002; ADR-005 w14 footer "decided, not applied"): `NullInvitationMailer`, so every `POST …/invites` answers `mailDelivered: false`. | N3b's first clause is the **copyable accept link**, and the UI must never claim a mail was sent. "Invitation sent to …" on `dev` today is a defect. |
| 2 | **A second Entra account on the pilot tenant does not exist yet** (operator prerequisite, `reports/audit/w14-hitl.md`). | `invite.spec.ts` is authored and `test.skip`ped with that named reason; N3b is walked by hand with any second account — it need not share the Admin's domain. |
| 3 | **`web/e2e/day1.spec.ts` is the V1 walk and is red at its last step against the V2 shell** (`E13/F09/US01/T01`, `web/README.md` "Known regression"; `v2.spec.ts` is the replacement, not a fix to that file). This task changed one line of it — the `page.reload()` N3 asks for — and nothing else, per its own text. | "`npx playwright test day1.spec.ts` exits 0" is not literally achievable; N3's verdict is that file's **step 2** in the report. |
| 4 | **`web/openapi/raffa-api.v1.json` declares no `X-User-Id` parameter on `createWorkspace`, `inviteWorkspaceMember`, `revokeInvitation` or `removeMember`**, although each answers **401** without it (`backend/README.md` rows; `listWorkspaces`, `getWorkspaceMembers` and `acceptInvitation` do declare it). | The `curl` commands above send it regardless. Owned by the task that owns that file, not by this runbook. |
| 5 | **A pre-w14 API answers `405` on `GET /api/workspaces`** (only `POST` existed on that path). | The picker's *"Workspaces unavailable · HTTP 405"* is the signature of §0.1 step 1 not having deployed yet — check `backend.yml`'s `deploy (dev)` job before reading it as a product bug. |
| 6 | **Pre-existing `dev` workspaces hold no membership row** until `backfill-workspace-membership.yml` has run for them (§0.1 step 2). | Their creators are offered "Create your workspace" — by design (ADR-025 Rule F.1d), not a discovery bug. |
| 7 | **Docker Desktop does not start on the operator's machine**, so the Postgres Testcontainers suites (`Raffa.Api.Tests` workspace/invitation fixtures, `Raffa.IntegrationTests`, `ValidatedContractCountTests`) are **CI-only**. | W14-A1 point (b) cites the `backend / build + test` job on `main`, never a local run; a local `dotnet test` reports those fixtures as failed to construct, which is not a red either. |
