# Wave w15 "async documents, real invitations" — acceptance runbook (W15-A1, A15-1…A15-9, N3b)

Operator checklist for wave `w15` (`.helix/reports/context/waves/w15-requirements.md`;
decision record `.helix/reports/architecture/waves/w15.md`; ADR-027 async document
processing, ADR-025 §J / ADR-026 w15 footers for invitations, ADR-010 w15 footer for the
bearer token). One section per acceptance item, each naming **the URL, the header posture and
the expected status code** plus the observable pass condition — nothing here is "check that it
looks right". Same shape as [`w14-acceptance.md`](w14-acceptance.md).

| | |
|---|---|
| Owner | task E16/F04/US01/T01 (`w15-integration`), story `us-01-final-integration` |
| Oracles | `w15-requirements.md` items W15-01, NW-27, NW-61, NW-10, NW-69, NW-67, NW-68, NW-58r, NW-05, NW-06 · ADR-027 (async processing, D1–D12, C1–C11) · ADR-025 §J · ADR-026 w15 footers §1–§10 · ADR-010 w15 footer (the token) · ADR-012 w15 footers (the client) · ADR-020 w15 footers (the copy) |
| API contract | `web/openapi/raffa-api.v1.json` — **every route below is quoted from it** (`uploadDocument`, `listDocuments`, `reprocessDocument`, `getContract360`, `getPortfolio`, `inviteWorkspaceMember`, `acceptInvitation`) |
| Screens | `web/README.md` "Screens (ADR-024 V2 route map)" → `/documents`, `/ask`, `/contracts`, `/contracts/:contractId`, `/workspace/members`, `/invite/accept` |
| Automated cover | `backend/tests/Raffa.Api.Tests` (`DocumentUploadEndpointTests`, `DocumentsV2EndpointTests`, `DocumentsListCountsTests`, `ContractReadinessTests`, `WorkspaceInviteOutcomeTests`, `InvitationLifecycleEndpointTests`), `Raffa.Documents.Contracts.Tests` (`DocumentLifecycleTests` — the split gate on Postgres), `Raffa.Identity.Workspace.Tests` (`InviteProvisioningOrderingTests`, `WorkspaceInvitationServiceTests`, `WorkspaceMembershipServiceTests`), `Raffa.AiGateway.Tests.SdkAllowListTests`, `Raffa.ArchitectureTests` — all under `dotnet test Raffa.slnx` in `backend.yml` · `web` vitest (`npm test`, 915 cases) in `web.yml` · `web/e2e/v2.spec.ts` A1 (rewritten for rows), `web/e2e/invite.spec.ts` (N3b, **skipped with the passcode reason**) |

Run this against **`dev`** — that is what the wave's Definition of Done asks for. Promotion to
`demo` is a separate operator act *after* this walk (see [Promotion sequence](#promotion-sequence)).

---

## 0. Before you start

### 0.1 What the wave is, in one paragraph

An upload returns the moment its bytes are stored (`201`, `processingStatus: "Uploaded"`); a
Worker claims the job from a Service Bus topic and does the parsing, the content gate and the
extraction, so a batch of fifteen files is fifteen rows in two seconds and every row reaches a
terminal status with the browser closed. Every number a screen shows is the server's own
`counts`; a contract mid-pipeline says "still being prepared" instead of rendering empty. An
invitation provisions an Entra B2B guest **before** any row is written, mails an absolute
accept link through Azure Communication Services, and the invitee signs in with a one-time
code from Microsoft and lands inside the workspace with **no second click** — the accept binds
to the guest's `oid`, never to a mangled `#EXT#` UPN. The API validates a bearer token on every
request (NW-05); `X-User-Id` is dead.

### 0.2 An honest note on how the wave was built

The Helix fan-out for w15 marked all eleven tasks delivered. Two of them had **no
implementation on their branches** — `E16/F02/US02/T01` (the queue transport: only DI stubs)
and `E17/F01/US01/T01` (guest provisioning + mail: only NuGet packages and test fixes) — and
four never ran (`E16/F02/US03/T01`, `E16/F03/US01/T01`, `E17/F02/US01/T01`, `E16/F04/US01/T01`).
All six were implemented by hand on branch `w15/manual-remaining` on 2026-09-14 (commits
`aa4ffb3`, `2370995`, `9c4975e`, `de11e34`, `acde613` and the integration commit carrying this
runbook). Nothing in this document relies on the fan-out's own delivery claims.

**A second, later hand-built round, same day.** The first real twenty-file batch on deployed
`dev` (walked against the six tasks above) showed a UX failure no council lane had modelled:
twenty truthful rows (`Uploaded`/`Processing`) that still read as stalled for the ~30 s cold
start and ~100 s/document the batch actually measured. Two more tasks —
`E16/F03/US02/T01` (backend: priority by claim, ADR-027 w15 footer C12) and `E16/F03/US02/T02`
(web: the perceived-instant row reading and the progress panel, ADR-020 w15 footer §10–12) —
were built by hand on branch `feat/upload-perceived-instant`, also 2026-09-14, outside any
council round and with no new ADR. **A15-9** below is their acceptance step.

### 0.3 The operator sequence

Run these **in order**. Every step is an explicit act — none is a side effect of a push.

Steps 1 and 2 are the two subscription/directory prerequisites. Both were found the hard way: the first real `dev` apply, on 2026-09-14, failed on both at once and took Service Bus and ACS down with it. Both are one-time acts per subscription/tenant, and neither belongs in Terraform.

| # | What | How |
|---|---|---|
| 1 | **Register the `Microsoft.Communication` resource provider** (once per subscription) | `az provider register --namespace Microsoft.Communication --wait`. It is not in the `azurerm` provider's default registration set, so without it `modules/communication` fails the apply with `MissingSubscriptionRegistration` (409) on both the Communication Service and the Email Service. **Done on subscription `47fb604b-85fa-4eb5-916d-78c064a7a08f` on 2026-09-14** — `az provider show -n Microsoft.Communication --query registrationState -o tsv` must print `Registered` before step 3 |
| 2 | **Grant the directory permission out-of-band** (once per tenant, by a Global Administrator) | The workload identity needs the Microsoft Graph **application** permission `User.Invite.All`. For a managed identity that app-role assignment **is** the admin consent — there is no portal click — but the identity running the HCP apply is not a directory administrator and gets `Authorization_RequestDenied`. So a Global Administrator writes it directly: `az rest --method post --url "https://graph.microsoft.com/v1.0/servicePrincipals/264242da-e217-4aae-8cd7-48f3f32f4f0c/appRoleAssignedTo" --headers "Content-Type=application/json" --body '{"principalId":"<workload-identity-principalId>","resourceId":"264242da-e217-4aae-8cd7-48f3f32f4f0c","appRoleId":"09850681-111b-4a89-9bed-3f2cae46d706"}'`. On `dev` the principal id is `e3baf9d5-d1e8-4520-aafa-642b6bd29c91` (`az identity show -g rg-raffa-dev -n id-raffa-dev-workload --query principalId -o tsv`); `264242da-…` is this tenant's Microsoft Graph service principal and `09850681-…` is `User.Invite.All`. Idempotent, outside Terraform state, nothing to import. `var.guest_role_assignment_managed = false` (both roots) is what keeps Terraform from trying to own it. **Skipping this step is legitimate** — the apply still succeeds and A15-4/A15-5/A15-7 then run in the `NotConfigured` (link-only) shape: record that, do not fake it |
| 3 | Merge the wave PR to `main` | `dev` deploys on push to `main` (`backend.yml`, `web.yml`); the image tag is the merged sha — W15-A1 point (a) |
| 4 | **Apply the infrastructure** (HCP Terraform `raffa-dev`, auto-apply OFF) | `infra.yml` queues the run on merge; a human confirms it in HCP. It must reach `CURRENT` **before** the walk: it carries the Service Bus topic `extraction-events` + subscription `document-processing`, the Worker's scale rule, the two topic-scoped role assignments, the ACS Email managed domain and its `acs-cs` Key Vault secret, and the `Invitations__*`/`ServiceBus__*` env keys on both container apps. **Nothing in the walk works before this apply** — an API deployed without `ServiceBus__FullyQualifiedNamespace` falls back to an in-process channel and logs that fact at startup, and the Worker then consumes nothing |
| 5 | **Check the two product flags on `dev`** | `invitation_mail_enabled` and `guest_provisioning_enabled` are both `true` in `infra/environments/dev/variables.tf` already, so step 4 publishes `Invitations__Mail__Enabled` and `Invitations__GuestProvisioning__Enabled` to the API app. Confirm them on the running revision rather than assuming; `demo` keeps both `false` this wave |
| 6 | One interactive sign-in on deployed `dev` | W15-A1 point (f) — before anything else, because NW-05 fails closed: without a valid token every tenant-scoped route is `401` |
| 7 | Walk W15-A1, then A15-1 … A15-8, N3b | this document |

### 0.4 The values every command below needs

| Value | Where it comes from |
|---|---|
| `API` | `https://ca-raffa-dev-api.gentleisland-ef9de18d.northeurope.azurecontainerapps.io` (`az containerapp show -n ca-raffa-dev-api -g rg-raffa-dev --query properties.configuration.ingress.fqdn`) |
| `WEB` | `https://ambitious-hill-01dfa571e.6.azurestaticapps.net` |
| `TOKEN` | the access token the SPA acquires (`acquireTokenSilent`, scopes `api://raffa-dev-api/Raffa.Read Raffa.Write`); read it from the browser's Network tab on any `/api/*` call. Every `curl` below sends `Authorization: Bearer $TOKEN` |
| `TENANT` | your workspace id (`GET $API/api/workspaces` → `workspaces[].id`) |
| an **external** mailbox | one you can read that is **not** in the company directory (a `gmail.com` address works) — A15-4 needs a second one for `demo` later |

```bash
curl -s -H "Authorization: Bearer $TOKEN" $API/api/workspaces        # 200, your workspace(s)
curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" $API/api/documents | jq .counts
```

---

## W15-A1 — the wave base, six points (ADR-014 w15)

Record each with its value; the wave is not closed until all six are written down.

| Point | Check | Pass |
|---|---|---|
| (a) | `cat .git/refs/remotes/origin/main` on the clone that opened the PR — the **loose ref**, never `packed-refs` (stale for every branch ref this wave touched) | the sha equals the base the PR was rebased/merged onto: `__________` |
| (b) | `git diff --stat origin/main..HEAD -- backend web infra .github docs scripts` on `main` after the merge | **empty** — nothing of w14's README/e2e work reverted |
| (c) | `docs/waves/w14-acceptance.md` and `web/e2e/invite.spec.ts` resolve on `main` | both present, `invite.spec.ts` skips with the passcode reason (`E17/F03/US01/T01`) |
| (d) | `backend.yml` "build + test" at the base commit | green (the Postgres suites run there — they need Docker, which the authoring machine did not have) |
| (e) | one `dev` deploy after the merge | `backend.yml` + `web.yml` green; the Worker revision exists (see [Post-deploy assertions](#post-deploy-revision-state-assertion-per-environment)) |
| (f) | one interactive sign-in on `WEB` | the token carries `ver: 2.0`, `aud` = the API client id, `iss` = the tenant authority, `oid`, `scp: Raffa.Read Raffa.Write`, and — for A15-4's second branch — `email`; `GET /api/workspaces` answers `200` with it |

---

## A15-1 — fifteen PDFs, fifteen rows in two seconds, every row terminal with the browser closed (NW-27, NW-61, NW-10)

1. `WEB/documents`. Drop **15 PDFs** at once (real contracts, the two sample MSAs, anything
   `%PDF-`). Start a stopwatch on the drop.
2. **Within 2 s**: 15 rows are on screen and each already carries a server id — reload the
   page: the 15 rows are still there, each reading **Uploaded** (no bar) or, once a Worker
   has claimed it, **Processing** with a real stage string underneath (wave w15 round 3,
   built by hand 2026-09-14: a row never reads **Queued…** any more — click it open for the
   six-stage checklist instead, see **A15-9** below); the chips read **Needs your attention ·
   15** / **All documents · 15**, and the rail badge reads **15 docs**. Open the same
   workspace in a second browser (or a private window): the same 15 rows.
3. `POST /api/documents` timing: the Network tab shows each upload answering `201` in well
   under 2 s (p95 < 2 s on 15 uploads) with `processingStatus: "Uploaded"` and
   `contractId: null`. **No** `422` anywhere.
4. **Close the browser.** Wait 3–5 minutes. Reopen `WEB/documents`: every row is terminal —
   **Completed**, **Needs review**, **Failed**, or **Not added** (under the third chip) — and
   none is still **Processing**. If any is, wait once more; a row that never leaves
   `Uploaded` is [the DLQ case](#dead-letter-queue).
5. `curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" "$API/api/documents?pageSize=1" | jq .counts` — the five numbers match the chips and the badge exactly (`all` = the "All documents" number; `needsReview` = the "N to review" badge when > 0; `rejected` = the "Not added" chip).

Pass: steps 2–5 all hold. Record the p95 of step 3.

## A15-2 — restart API and Worker mid-batch: nothing lost, every row terminal (NW-27)

Walked with the Worker **scaled the way `demo` will run it** (its scale rule: 0 replicas at
rest, up on queue depth).

1. Drop 10 PDFs on `WEB/documents`. As soon as the first row reads **Processing** with a
   stage string, restart both apps:
   `az containerapp revision restart -n ca-raffa-dev-api -g rg-raffa-dev --revision $(az containerapp revision list -n ca-raffa-dev-api -g rg-raffa-dev --query "[0].name" -o tsv)` and the same for `ca-raffa-dev-worker`.
2. Wait for both to be healthy, then wait 5 minutes.
3. `GET $API/api/documents?pageSize=100`: all 10 rows present, every `processingStatus`
   terminal, `counts.processing == 0`. A row interrupted mid-run was re-delivered (PeekLock
   abandon on the lost lock) and claimed again; a job that reached its third attempt reads
   **Failed** with **Retry upload** — acceptable, it is a row, not a hole.

Pass: 10 rows, 0 processing, nothing missing.

## A15-3 — Ask, Portfolio and Contract 360 while a document is processing: "still processing", never an empty contract or an invented fact (NW-61)

Do this on a **fresh workspace** (create one at `WEB` → "Create your workspace"), then drop one
large PDF and, while its row still reads **Processing**:

| Screen | Expect |
|---|---|
| `WEB/ask` | "Ask needs at least one validated contract." + **"Your document is still processing or waiting for review. Ask only answers from facts that passed validation — so it never guesses."** + **Go to Documents**. Leave the tab open: when the document completes and its contract validates, Ask switches on **without a reload** |
| `WEB/contracts` | "Nothing to triage yet" + **"Your documents are still being processed. The portfolio lights up from validated contracts."** + **Go to Documents** (never "Upload one to start." — you already have) |
| `WEB/contracts/<id>` (take the id from `GET /api/documents` → `contractId` once it is set) | **"This contract is still being prepared."** + "Raffa.ai is still extracting the facts. It will open here once they pass validation." — **never** a header with empty clauses/spend/dates. `curl … $API/api/contracts/<id> \| jq .readiness` → `{ "state": "processing", "stage": "<a stage string>", "documentCount": 1, "completedDocumentCount": 0 }` |
| the same three, after you delete every document | Ask: "Upload a contract first…"; Portfolio: the shipped "Upload one to start." sentence; the 360 route: `404` |
| a contract whose only document **Failed** | Ask: **"Raffa.ai could not finish processing your documents…"** (never "still processing"); the 360: **"This contract has no validated facts yet."** with `readiness.state == "unavailable"` |
| the poll budget | leave `WEB/documents` open on a row that never moves (an intentionally stranded one, or wait out a real one): after **five minutes without a change** the list shows **"Nothing has changed for five minutes, so this page stopped checking for updates."** + **Check again**; no row is re-labelled; the Network tab stops polling until you click |

Pass: every cell holds; `readiness` is never inferred — the 360 renders the block from the field.

## A15-4 — invite an external address the tenant has never seen (NW-67, NW-68)

1. `WEB/workspace/members` as the workspace Admin. Invite your external mailbox as
   **Procurement**. The pane reads **"Invitation sent to <address>."** and beneath it **"They
   will get a one-time code from Microsoft the first time they sign in."**; no link block.
   The 201 in the Network tab: `deliveryOutcome: "sent"`, `mailDelivered: true`,
   `identityProvisioned: true`, `acceptUrl` starting with `WEB/invite/accept#`.
2. The mail arrives **within 1 minute** from `…@<managed-domain>.azurecomm.net` (check
   spam): subject "You've been invited to <workspace> on Raffa.ai", plain text, the link
   visible as an absolute URL, the one-time-code line, the expiry — and **no** count, no
   member list, no supplier name.
3. Open the link in a private window. State 2: "Join <workspace>" · "You have been invited
   as Procurement." · **Continue with Microsoft Entra ID**. Click it once: Microsoft asks for
   the address, mails a one-time passcode, you enter it — and the popup closes and the page
   **continues into the accept on its own**: no "Join" click, no Azure portal, straight into
   `WEB/` inside the workspace as Procurement (the rail shows the workspace name; `Workspace &
   members` is hidden — Procurement).
4. `WEB/workspace/members` (as Admin): the row is **Active · Procurement**.
5. `curl … $API/api/workspaces` **with the guest's token** (from the private window): exactly
   one workspace; `GET $API/api/workspaces/<other-tenant>/members` with it → `404`.

Pass: 1–5 hold with **one** click on the accept page. If the `oid` bound at invite time does
not match the guest's token (it must — Raffa issued the Graph call), the accept answers `403`
and state 6 renders: that is a defect, not a re-invite case; capture the audit row
`workspace.invitation.rejected` and stop.

## A15-5 — invite an address already in the tenant: no duplicate guest, same shape (NW-67)

1. Invite the **same** external address into a **second** workspace (create one), or revoke
   and re-invite it into the first.
2. The 201 is byte-identical in shape to A15-4's: `identityProvisioned: true`,
   `deliveryOutcome: "sent"`; the pane says nothing about the account already existing.
3. Entra admin center → Users: **one** guest object for that address, not two.
4. The invitee signs in to the second workspace through the mail with the same passcode flow
   and lands inside it; `GET /api/workspaces` with their token now lists both.

Pass: one directory object, two memberships, indistinguishable panes.

## A15-6 — the mail transport failing: the designed copy and a working link; `mailDelivered: false` (NW-68, NW-69)

1. Break the transport on purpose: in HCP set `acs_sender_address` to an address the managed
   domain does not own (or temporarily revoke the `acs-cs` secret), apply, restart the API.
2. Invite a fresh external address. The pane reads **"Invitation created, but the email could
   not be sent."** with the **copyable link** and its expiry; the 201: `deliveryOutcome:
   "mail_failed"`, `mailDelivered: false`, `identityProvisioned: true`. **No resend action of
   any kind** renders on the pane — the raw requirement sketched one; ADR-020 w15 §3.4 decided
   against it (the server holds only the token's hash, so a "resend" would be a re-issue that
   kills the link the Admin is looking at): the link block is the remedy, and the roster row's
   **Send a new invitation** stays the only re-issue path.
3. Copy the link, open it in a private window: the accept flow of A15-4 works end to end.
4. Restore the sender, apply, restart; invite once more → `sent`.

Pass: 2–3 hold; the audit trail has `workspace.invitation.mail_failed` then `.mail_sent`.

## A15-7 — the directory permission missing: a named error, an audit row, no invitation claiming "sent" (NW-67, NW-69)

1. Remove the permission on purpose. Since 2026-09-14 the grant is **out-of-band**, not a
   Terraform resource (`var.guest_role_assignment_managed = false`), so revoke it directly and
   leave both the Terraform flag and `Invitations__GuestProvisioning__Enabled` **true** — that
   is the whole point of the test: the product believes it may provision guests, the directory
   disagrees. Find the assignment and delete it:
   `az rest --method get --url "https://graph.microsoft.com/v1.0/servicePrincipals(appId='b689f28a-875e-498d-922b-fcd52a263e54')/appRoleAssignments" --query "value[?appRoleId=='09850681-111b-4a89-9bed-3f2cae46d706'].id" -o tsv`
   then
   `az rest --method delete --url "https://graph.microsoft.com/v1.0/servicePrincipals(appId='b689f28a-875e-498d-922b-fcd52a263e54')/appRoleAssignments/<id>"`.
   Entra caches the token's roles, so restart the API revision (`az containerapp revision
   restart`) or wait for the cached app token to expire before step 2, otherwise the old
   permission is still in force and the invite succeeds.
2. Invite a fresh external address. The pane shows **"Raffa.ai is not allowed to add guests to
   your company directory yet. A tenant administrator has to approve that permission."** with
   **"No invitation was created."** beneath it; the response is `502 { "failureReason":
   "consent_missing" }`; **no** link renders; the roster gains **no** row.
3. `GET $API/api/workspaces/$TENANT/members`: the address is not there.
   Audit: one `workspace.guest.provisioning_failed` row with `reason=consent_missing` and the
   Graph `request-id`; **no** `workspace.invitation.issued` row for that address.
4. Restore the grant with the §0.3 step 2 command, restart the API revision, and invite the
   same address again → `201`, `sent` — the slot was never taken.

Pass: a 502 leaves nothing behind and the retry succeeds without a revoke.

## A15-8 — both halves of the token gate, before the `demo-v*` tag (NW-05, NW-06)

| Request | Expect |
|---|---|
| `curl -s -o /dev/null -w '%{http_code}' -H "X-User-Id: someone@acme.example" -H "X-Tenant-Id: $TENANT" $API/api/documents` — **no** bearer token, forged identity | `401` |
| `curl -s -o /dev/null -w '%{http_code}' -H "X-User-Id: someone@acme.example" $API/api/workspaces/$TENANT/members` | `401` |
| `curl -s -H "Authorization: Bearer $TOKEN" $API/api/workspaces/$TENANT/members` | `200`, and your own row carries the role the membership row says (Admin for the creator) |
| `curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" $API/api/workspaces/<a tenant you are not in>/members` | `404`, never `403` |

Pass: all four.

## A15-9 — open a queued document: the stage bar, and it finishes before its neighbours (NW-27, NW-61)

Built by hand 2026-09-14 (`E16/F03/US02/T01`/`T02`), after A15-1's own batch showed the failure
this step exists to close.

1. `WEB/documents`. Drop **6–8 PDFs** at once. Within 2 s every row reads **Uploaded**, no
   bar, "Processing in the background" — confirm none reads "Queued…" or "Processing" yet
   (that would mean a Worker somehow claimed a job in under 2 s, unlikely but not wrong if it
   happens).
2. Click the **last** row's filename (the one that dropped last, so it is last in the FIFO).
   The Progress panel opens (`?progress=<id>`): the six-stage checklist, every stage **todo**
   or the headline **"Queued, starting shortly"**, and the line "Raffa.ai is giving this
   document priority over the rest of the queue."
3. `curl -s -o /dev/null -w '%{http_code}' -X POST -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Id: $TENANT" "$API/api/documents/<id>/prioritise"` — **204**, repeatable (idempotent; a
   second call while still queued is still `204`, no second audit row — not directly
   observable from `curl`, see the DoD's audit-row assertion in the task instead).
4. **Watch this row against its neighbours.** It reaches a terminal status (**Completed**,
   **Needs review**, or **Failed**) at or before the rows that were dropped before it and were
   **not** opened — the queue-jump, observed. The panel itself never redirects: when the
   status turns, the panel offers a link (**Review now** / **Open the contract** / **Open
   Quote check**) instead of navigating on its own; click it.
5. Go back to `WEB/documents` (**← Documents**). The row now reads its terminal state, exactly
   like every row that was never prioritised — nothing about a prioritised document's own
   terminal row looks different from an unprioritised one.

Pass: steps 1–5 all hold. Record how many rows, if any, the prioritised document finished
ahead of.

## N3b — the invitation e2e, walked by hand (NW-58r)

`web/e2e/invite.spec.ts` is **skipped in CI with the passcode reason** (a one-time passcode
cannot be automated; no Playwright runner exists in CI — NW-50, W18). The manual walk **is**
A15-4 above against the operator's external mailbox; record the date, the address and the
workspace here: `__________`. `Raffa.Api.Tests.AuthenticationSeamAbsenceTests` (S-T23) proves
no test-only authentication seam ships in the image.

---

## Post-deploy revision-state assertion, per environment

| Check | Command | Pass |
|---|---|---|
| the Worker revision exists and is healthy **when given work** | `az containerapp revision list -n ca-raffa-dev-worker -g rg-raffa-dev -o table` during A15-1 | one active revision with ≥ 1 replica while jobs are queued. **Zero replicas at rest is a PASS** — a check that fails on a healthy idle environment gets waived, and the waiver is what the next silent Worker death hides behind |
| the subscription exists | `az servicebus topic subscription show --namespace-name <ns> -g rg-raffa-dev --topic-name extraction-events --name document-processing` | `200`, `status: Active` |
| the scale rule exists | `az containerapp show -n ca-raffa-dev-worker -g rg-raffa-dev --query properties.template.scale` | a rule on the topic/subscription, `minReplicas: 0` |
| both topic-scoped role assignments exist | `az role assignment list --scope <topic resource id> -o table` | `Azure Service Bus Data Sender` on the API identity, `… Data Receiver` on the Worker identity |
| the transport is selected | `az containerapp logs show -n ca-raffa-dev-api -g rg-raffa-dev --tail 200 --format text \| grep -i "extraction queue"` | the startup line names **Service Bus**, never the in-process channel |

## Dead-letter queue

Empty **before** the walk (`az servicebus topic subscription show … --query countDetails`).
Afterwards read it with **two** meanings and route it, never drain it — nothing sweeps it and
ADR-009 forbids a cross-tenant sweep:

- `DeadLetterReason: job-not-found` / a message whose job row does not exist ⇒ an upload's
  commit failed after publish; the document is not in the list; nothing to recover.
- **anything else** ⇒ a defect: capture the message body (a pointer: tenant id, document id,
  job id — no content) and the Worker log line for that job id; the row is `Uploaded` forever
  and the Documents list has stopped polling on it (the five-minute notice). Re-enqueue is an
  admin act inside the tenant (`POST /api/documents/{id}/reprocess` as an Admin of that tenant).

## Promotion sequence

1. `demo-v4` is cut on **current `main` before w15 starts** (OQ-w15-dm-03 — if the operator
   decides against it, record that here and the wave closes either way: `__________`).
2. w15 promotes as **`demo-v5`**.
3. The `demo` flag-flip PR (`invitation_mail_enabled` / `guest_provisioning_enabled` in
   `infra/environments/demo/variables.tf`) lands **before** the tag; `promote-backend` only
   after that HCP apply is `CURRENT`.
4. The `demo` invitation walk (A15-4) uses a **second** external address.

### `demo`'s three outstanding data-plane steps, which belong to w14

In order: the w14+w15 schema apply (`backend.yml` "Verify schema applied"),
`seed-demo-fixture.yml`, `backfill-workspace-membership.yml`; then `docs/waves/w14-acceptance.md`
N1–N9 + W14-A2.

## Rollback rehearsal (NW-05)

The one change whose failure mode is "nobody can use `dev`". Rehearse **once**: open a revert PR
of the wave merge to `main` → `dev` redeploys the previous image (`backend.yml`/`web.yml` on
push); the new env vars stay and are harmless — unread by that image — so **no apply and no
HCP wait**. Confirm `GET /api/workspaces` answers on the reverted build, then close the revert
PR without merging.

## Known gaps

| Gap | Status |
|---|---|
| `reprocess-tenant-documents.yml` is out of service from this wave: it sends `X-User-Id`, gets `401` on its first read and exits 1 before any write — no partial reprocess, no unattributed audit rows | **NW-31 owns it in W16** |
| A15-4 / A15-5 / A15-7 are `dev` acceptance only: `Invitations__Mail__Enabled` and `guest_provisioning_enabled` are `false` on `demo` this wave | a reviewer must not read that as NW-67/NW-68 undelivered, and **no task may switch either flag on for `demo`** to make them pass |
| An address in one of the directory's **own verified domains** cannot be provisioned as a guest: Graph refuses `POST /invitations` for it, which surfaces as `502 provisioning_failed` ("Your company directory would not add <address>…") and no invitation. Inviting a colleague who already has a company account is therefore not possible while `Invitations__GuestProvisioning__Enabled` is true; with it `false` the invite is link-only and their normal sign-in accepts through the `email` claim | record as a W16 item: a directory lookup (`User.ReadBasic.All`) before the invite, or a "member of this directory" branch |
| The Postgres-backed suites (`DocumentLifecycleTests`, `InviteProvisioningOrderingTests`, `InvitationLifecycleEndpointTests`, …) run in CI only — the authoring machine's Docker engine does not start | W15-A1 point (d) is the proof |
| `terraform fmt -check` / `terraform validate` were not run on the authoring machine (no Terraform binary); `infra/**` is untouched by the hand-built branch | `infra.yml` runs both on the PR |
| `npm run test:e2e` locally reports **19 skipped**: the specs refuse a `localhost` base by design (`RAFFA_E2E_BASE_URL` must be the real `dev`/`demo` origin) | run it against `dev` after the deploy: `RAFFA_E2E_BASE_URL=$WEB … npm run test:e2e` — `v2.spec.ts` A1 now asserts the **Not added · 2** chip and the two refused rows, not the retired card |
| `POST /api/documents/{id}/prioritise` (A15-9) only ever reorders work **inside the calling tenant**, and only while the document's classification job is still `Queued`/unclaimed — opening an already-`Processing` (claimed) or terminal document is a harmless no-op, never an error, and never moves anything. There is no cross-tenant fairness story and none is claimed: a tenant cannot make another tenant's documents wait, and a document already being worked on cannot be pulled ahead of itself | by design (ADR-027 w15 footer C12, ADR-009); not a gap to close, recorded so a reviewer does not read the 204-always contract as "always does something" |
