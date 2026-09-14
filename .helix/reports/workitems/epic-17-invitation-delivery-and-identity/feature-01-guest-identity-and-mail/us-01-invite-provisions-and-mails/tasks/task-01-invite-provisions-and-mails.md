---
id: E17/F01/US01/T01
type: task
story: us-01-invite-provisions-and-mails
wave: w15
status: live
target_repo: raffa-backend
---

# task-01-invite-provisions-and-mails — Guest provisioning, the real mailer, the absolute link and the 201's new shape

## Coding objective

Make the invite request do the two things it has never done. Add an
`IGuestProvisioner` seam with a Microsoft Graph adapter in
`Raffa.Api/Infrastructure/` that calls `POST /invitations` with
`sendInvitationMessage: false` using `DefaultAzureCredential`, and call it from
the invite handler **before** any row is written; bind the returned guest object
id into `workspace_user.ExternalSubjectId`. Add `AcsInvitationMailer`
implementing the existing `IInvitationMailer` seam (`:16-33`) and register it in
place of `NullInvitationMailer` — today `ServiceCollectionExtensions.cs:61` is
the only registration, and `TryAdd` means first registration wins, so the
replacement must be reachable **before** `AddIdentityWorkspaceModule` runs.
Compose an **absolute** `acceptUrl` from `Invitations__AcceptUrlBase`, keeping
the `#` fragment. Finally give the 201 its two new fields and give a provisioning
failure a 502 with a closed `reason` set.

Nothing talks to Entra today: there is no `Microsoft.Graph` package, no
`GraphServiceClient`, no `graph.microsoft.com` and no `User.Invite.All` anywhere
in `backend/` or `infra/`. The workload identity that will make the call is
already attached to both hosts and already published as `AZURE_CLIENT_ID`.

## Parent story AC covered

- AC-1 … AC-11 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Identity.Workspace/Application/IGuestProvisioner.cs` | **new** — takes `IInvitationMailer`'s shape with a fourth **`NotConfigured`** outcome (default off) |
| `backend/src/Raffa.Api/Infrastructure/GraphGuestProvisioner.cs` | **new** — the one and only Graph call site; `sendInvitationMessage: false`; the invited address comes from the authenticated Admin's request body only |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/NullGuestProvisioner.cs` | **new** — returns `NotConfigured`; the default registration |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/AcsInvitationMailer.cs` | **new** — the real `IInvitationMailer`; plain-text body of record per ADR-020 §4 |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/NullInvitationMailer.cs` | **retire the comment at `:28-29`** ("the 201 response body is the link's only channel") — false from this wave. The class stays as the no-transport default |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/ServiceCollectionExtensions.cs` | the mailer and provisioner registrations (`:61`). **Single-writer of this file in phase 3** |
| `backend/src/Raffa.Api/Program.cs` | register the ACS mailer and the Graph provisioner **before** `AddIdentityWorkspaceModule`; the fail-closed startup validation of the mail/base pair; config binds in the host's `GetSection(...).Bind(...)` shape, **never `IOptions<T>`** (absent from `backend/src`). **Single-writer of this file in phase 3** |
| `backend/src/Raffa.Api/WorkspaceInvitesEndpointExtensions.cs` | the handler (`:71-154`) gains the provision-then-write ordering and the 502; the 401→404→403 guards (`:80-113`) are unchanged |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceMembershipService.cs` | `InviteAsync`'s `alreadyInvited` pre-check (`:142-151`) revokes and re-issues in the same `SaveChangesAsync` instead of returning `Conflict`; the `alreadyMember` branch (`:136-140`) **keeps** its `Conflict`; the `IsUniqueViolation` catch (`:174-182`) **stays**; the `ExternalSubjectId` bind at `:118-131`; the new audit verbs |
| `backend/src/Raffa.Identity.Workspace/Infrastructure/WorkspaceInvitationService.cs` | the absolute `acceptUrl` (`:58`, `:86`); `deliveryOutcome` and `mailDelivered` computed in **one** place (`:99-103`); the 100-invitation cap |
| `backend/src/Raffa.Api/Raffa.Api.csproj` | `Microsoft.Graph`, `Azure.Identity` |
| `backend/src/Raffa.Identity.Workspace/Raffa.Identity.Workspace.csproj` | the ACS Email client package |
| `backend/tests/Raffa.AiGateway.Tests/SdkAllowListTests.cs` | **package-scoped** amendment: `Microsoft.Graph` legal in `Raffa.Api` only. **Single-writer of this file in phase 3** |
| `backend/tests/Raffa.ArchitectureTests/DependencyDirectionTests.cs` | close the real hole its `ForbiddenSdkPrefixes` list has — nothing in `"Azure."`, `"Microsoft.Azure."`, `"Microsoft.AI."` (`:78-83`) matches `Microsoft.Graph`, so a **domain module** could take a direct Graph dependency today with no test objecting |
| `backend/tests/Raffa.Identity.Workspace.Tests/InviteProvisioningOrderingTests.cs` | **new** — guest-before-row, abort, re-issue by replacement, the cap |
| `backend/tests/Raffa.Api.Tests/WorkspaceInviteOutcomeTests.cs` | **new** — the 502's closed reason set, the 201's two fields, the biconditional |
| `backend/tests/Raffa.IntegrationTests/GuestDirectoryScopeTests.cs` | **new** — S-T19 |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-67 (backend half), NW-68 (backend half)`.

Decision rows: `reports/architecture/waves/w15.md` — **NW-67** (all six seat
cells) and **NW-68** (all six seat cells).

- **Architecture decisions in force**: **ADR-025 §J.1–§J.7**; **ADR-026 §1–§6**;
  **ADR-010** w15 §2.3–§2.4; **ADR-011** w15 §1 and §4; **ADR-002** w15 footers;
  **ADR-020** w15 §4 (surface 12); **ADR-025 C9** (the fragment survives the
  transport and is now load-bearing for a second reason).
- **Design refs.** `inputs/design/prototypes/raffa-v2/screens-v2.md:147-154` (§10
  Members table and invite form) for the pane this serves. The **invitation email
  has no oracle at all** — `inputs/design/` holds screens, `markup.html`,
  `styles.css` and `app.jsx`, nothing that leaves the browser — so its form and
  copy are decided in **ADR-020 w15 §4**: plain text is the body of record; one
  column, left-aligned; **no image, no logo file, no web font, no external
  stylesheet**; any colour an inline literal of an ADR-019 token value
  (`#201e1d`, `#ec3013`, `#ae1800`), never a variable reference and never a new
  value; the accept link rendered as a **visible absolute URL** as well as an
  anchor. Its two middle lines are **screen 11 state 2 verbatim** (`ADR-020:397`)
  — the mail and the landing page must say the same thing or the invitee believes
  they clicked the wrong link. It carries the workspace name, the offered role
  and the expiry and **nothing else**; the one-time-code sentence is not
  decoration but the thing that turns Microsoft's unexpected second message into
  an expected step; and it **never claims an account was created**.
- **`User.Invite.All` as a Graph application permission, not the Guest Inviter
  directory role.** Both are narrow, so least privilege is decided by
  **auditability**: an app-role assignment is a fixed, named grant visible in the
  consented-permission surface and in Terraform state, while a directory role is
  a Microsoft-owned bundle that can widen **without our Terraform changing**.
  `User.ReadWrite.All` is rejected outright (read/update/**delete** of every user
  object, to create one guest).
- **`SdkAllowListTests` must be amended package-scoped.** Widening its single
  `AllowedProjectName` (`:25`, `:40-43`) — the one-word edit a task will reach for
  — would make **`Azure.AI.*` legal in `Raffa.Api`**, silently un-guarding the
  Foundry boundary. And `DependencyDirectionTests` **cannot** do this job for a
  host: it builds its path as `src/{moduleName}/` from the fixed ADR-002
  **domain-module** list (`:111`, list `:60-73`), and the adapter lives in a host.
  Narrow it to what it can do; do not delete it.
- **Exactly two externally visible outcomes.** `provisioned` covers *both*
  "created" and "already present"; `failed` comes from the closed reason set. A
  workspace Admin is not a directory admin, and A15-5's no-op must be
  indistinguishable in shape from a fresh provision.
- **Fail closed at startup, but do not crash on an unbound key.** ADR-026 §D6
  forbids a `?? throw` on the binding; security-architect's refusal is a
  **validated startup check on the composed pair** — "mail enabled with no usable
  base" is the one combination that must never run. "Mail enabled, no accept-url
  base" as a *configuration* shape is handled by `TrySendAsync` returning `false`,
  and the pane shows the designed "could not be sent" state with the copyable
  link.
- **`mailDelivered: true` means *accepted for delivery*, not *delivered*.** It
  stays `TrySendAsync`'s return — never a receipt, never inferred, never
  optimistically `true`. A managed domain sends from `…azurecomm.net`, which
  corporate filters treat harshly, so the `false` branch is **likely rather than
  rare** and must stay fully functional.
- **Do not touch the published OpenAPI document, the generated client, or the
  hand-written API wrapper under `web/`.** The invite contract is declared and
  consumed together by `E17/F02/US01/T01` in phase 4, so the server returns the
  two new fields for one phase before they are published. That is deliberate, so
  that exactly one task per phase regenerates the client (ADR-012 w15 §10).
- **Do not touch the documents or contracts endpoint files** — `E16/F02/US03/T01`
  owns them this phase. Do not build a directory-deletion or directory-block
  path. Do not widen the accept match to an email comparison. Do not add a Key
  Vault secret beyond `acs-connection`, which the Terraform task already created.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/tests/Raffa.Identity.Workspace.Tests` exit 0 — guest-before-row ordering; abort leaves **no** invitation row, no mail, no token; `NotConfigured` behaves link-only; replace-on-live-invitation is one transaction; `alreadyMember` still 409; the unique-violation backstop still 409; the cap at 100
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exit 0 — the 502 carries a reason from the closed set; the 201 carries `identityProvisioned` and `deliveryOutcome`; the biconditional holds across all three outcomes, so `mail_failed` with `mailDelivered: true` is not expressible; a fresh provision and a no-op are byte-identical in shape
- [ ] `dotnet test backend/tests/Raffa.AiGateway.Tests` exit 0 — `Microsoft.Graph` legal in `Raffa.Api`, illegal elsewhere; `Azure.AI.*` **still illegal** in `Raffa.Api`
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests` exit 0
- [ ] `dotnet test backend/Raffa.slnx` exit 0
- [ ] `grep -rn "inviteRedeemUrl" backend/src/` returns **no match**
- [ ] A read of every new log statement confirms no recipient address, rendered body, accept URL, token or token hash reaches the application log
- [ ] `grep -n "only channel" backend/src/Raffa.Identity.Workspace/Infrastructure/NullInvitationMailer.cs` returns **no match**

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | guest first, row second; a failure aborts with nothing persisted | `backend/tests/Raffa.Identity.Workspace.Tests/` |
| unit | `ExternalSubjectId` carries the Graph object id, so accept never depends on the `#EXT#` UPN | `backend/tests/Raffa.Identity.Workspace.Tests/` |
| unit | re-issue by replacement is one transaction; `alreadyMember` and the race backstop are unchanged | `backend/tests/Raffa.Identity.Workspace.Tests/` |
| API | the 502's closed reason set; the 201's two fields; the `deliveryOutcome` ⇔ `mailDelivered` biconditional over all three outcomes | `backend/tests/Raffa.Api.Tests/` |
| API | **S-T19** — a guest provisioned against a `dev` tenant, asserted against a `demo` tenant: zero workspaces, 404 for any tenant id named. *Directory presence is not a grant* is **proved, not assumed** | `backend/tests/Raffa.IntegrationTests/` |
| architecture | `Microsoft.Graph` is confined to `Raffa.Api`; `Azure.AI.*` is still confined to `Raffa.AiGateway` | `backend/tests/Raffa.AiGateway.Tests/SdkAllowListTests.cs` |

Provisioning itself is **proved by one real invite on deployed `dev`, never
asserted from Terraform state** — a directory policy can still refuse an
otherwise-granted call, which is why the reason set is a closed enumeration.
That walk is `E16/F04/US01/T01`'s.

## Open questions blocking this task

- **OQ-w15-005**, **OQ-w15-007**, **OQ-w15-sec-01**, **OQ-w15-sec-02**, **OQ-w15-sec-03** — all resolved or assumed at the table; see the story. None blocking.
- **OQ-w15-ca-02** — the string a freshly provisioned guest presents is provider-defined; the decision removes the guess by binding to the validated token subject and to the `oid`. Residual: if NW-05 slips inside the wave, **A15-4 must be walked against a real B2B guest** before the wave is called done — a numbered step in `docs/waves/w15-acceptance.md`, not a note. Not blocking.

## Wave-spec entry
```yaml
- id: E17/F01/US01/T01
  prompt: reports/workitems/epic-17-invitation-delivery-and-identity/feature-01-guest-identity-and-mail/us-01-invite-provisions-and-mails/tasks/task-01-invite-provisions-and-mails.md
  produces: [invitation-identity-and-mail]
  depends_on: [api-jwt-identity]
  effort: L
  layer: backend
  status: live
```
