# ADR-015 — How GitHub CI/CD authenticates to Azure dev and demo

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: delivery-manager + cloud-architect + security-architect (joint)
- **Locked citations**:
  - Auth / secrets — "OIDC, SSO-ready (Entra ID). Secrets in Key Vault. No secrets in code, client
    bundles, or Terraform source."
  - Delivery — "GitHub CI/CD releases to Azure `dev` and Azure `demo`."
  - IaC — "HCP Terraform … No secrets in Terraform source; apps read secrets at runtime from Key Vault."
  - Security (brief §10) — "managed identity for Azure resources."

## Context and problem statement

GitHub Actions (or an equivalent GitHub CI runner) must deploy Terraform-applied infrastructure and
application artifacts to two isolated Azure environments, `dev` and `demo`, without embedding secrets
in the repository, in Terraform source, or in the CI workflow files. The brief locks "managed
identity for Azure resources" and "no secrets in code … or Terraform source," but leaves *how CI
authenticates* to the council (brief §1 "how CI authenticates to Azure").

## Decision drivers

- **No persistent secrets in the repo or Terraform** — the locked constraint is absolute.
- **Two isolated environments** — `dev` and `demo` need distinct, least-privilege deployment identities.
- **Cost / simplicity** — cheapest mechanism that is still secure and doesn't require standing up
  self-hosted runners or extra VMs.
- **Claude Code reproducibility** — the trust setup must be expressible as a Terraform/one-time
  bootstrap step, not manual secrets pasted into GitHub.

## Considered options

1. **OpenID Connect (OIDC) federated credentials** — GitHub Actions exchanges its OIDC token for an
   Entra ID service principal / managed-identity-scoped credential; no stored client secret.
2. **Long-lived service-principal client secrets stored as GitHub repository secrets** — classic
   AZURE_CREDENTIALS secrets.
3. **User-assigned managed identity on a self-hosted runner** — an Azure VM runner with a managed
   identity that deploys both environments.

## Decision outcome

**Chosen: Option 1 — OIDC federated credentials** from GitHub Actions to Entra ID, one least-privilege
service principal per environment (`raffa-sp-dev`, `raffa-sp-demo`), each federated to the Raffa
repo/branch/path scope. No client secret is ever stored in GitHub; the only credential material is the
OIDC trust relationship (subject claim → service principal). This is the cheapest option, satisfies
"no secrets in code" and "managed identity" semantics (short-lived tokens issued to a known identity),
and is reproducible as Terraform output for the federation config.

### Consequences

- **Good**: no rotated GitHub secrets; per-env least privilege; short-lived tokens; no self-hosted
  runner cost; aligns with locked "no secrets in code / Terraform."
- **Bad**: requires the GitHub org to be Entra-telemetry/trust configured and the federation subject
  claims to be pinned (repo + env/branch) so a fork/PR cannot mint tokens for `demo`; setup is slightly
  more involved than pasting a secret.
- **Neutral**: the service principal is what Terraform's `azurerm` provider (via OIDC) also uses, so
  the same identity story covers both "CI runs Terraform" and "CI deploys app/container artifacts."

## Pros and cons of the options

### Option 1 — OIDC federated credentials (chosen)
- Good: zero stored secrets; least-privilege per environment; cheap; reproducible.
- Bad: needs pinned subject claims and a per-env SP; requires GitHub → Entra federation config up front.

### Option 2 — Service principal client secret in GitHub secrets
- Good: simplest to set up initially.
- Bad: a long-lived secret in GitHub violates the spirit of "no secrets in code"; needs rotation
  discipline; riskier for two envs and a long-lived `demo` approval path.

### Option 3 — Self-hosted runner with managed identity
- Good: strongest "no secret at all" posture.
- Bad: a VM/tier cost and operational burden; overkill for the cost guideline and for a `dev`/`demo`
  only footprint.

## Implications for the decomposition

- Terraform (`infra/`) must create and output the two service principals with least-privilege role
  assignments scoped to the `dev` and `demo` resource groups respectively.
- A one-time bootstrap records the GitHub OIDC federation (subject claim = `repo:lucalamalfa91/raffa:*`
  plus environment claim for `demo`); the workflow never contains a client secret.
- GitHub Actions workflow for `demo` promotion runs under the `demo` environment with the federation
  restricted to tag-triggered runs (see `ADR-promotion-dev-demo.md`).
- The AI Gateway / backend / worker authenticate to Foundry, Key Vault, and other services using the
  same managed-identity model at runtime; CI identity is only for deploy-time control-plane actions.

## Assumptions

- (open-question OQ-DM-003) GitHub OIDC federation to the customer's Entra ID tenant is permitted and
  available on the org plan; if the tenant does not allow federation, fall back to a short-lived service
  principal **certificate** in Key Vault (never a plaintext secret in GitHub). Recorded in
  `reports/open-questions.md`.
- (open-question OQ-DM-004) The subject-claim pinning for `demo` (environment + tag) is sufficient and
  the org does not allow unrestricted fork-triggered OIDC — assumed; if it does, security-architect must
  gate deployment to branch/`pull_request: false` before this is accepted.
- Foundry account shape (one vs two) is cloud-architect's ADR; this ADR only fixes the CI→Azure control
  plane identity and does not pre-decide the Foundry runtime identity.

## Amendment (2026-09-13, wave w15)

Item **NW-67** (Raffa provisions the invitee's Entra B2B guest at invite time).
Seat: delivery-manager, with the text supplied by cloud-architect and the
permission ruled by security-architect — the same joint authorship the body
records. This is this ADR's **first** amendment. Everything above is unchanged
and `Status: accepted` stands. OIDC federation, the two per-environment service
principals and the subject-claim pinning are untouched; **no GitHub secret, no
new federated credential and no new environment is added by this wave.**

**1. The runtime workload identity gains a *directory* permission — the first
of its kind.** The Implications section's last bullet (`:82-83`) already says
the API, the worker and the AI Gateway authenticate to Azure services with the
per-environment managed identity, "CI identity is only for deploy-time
control-plane actions". NW-67 extends that sentence to a **directory** API for
the first time: `id-raffa-<env>-workload`, already attached to both container
apps and already published as `AZURE_CLIENT_ID`, receives the Microsoft Graph
**application** permission `User.Invite.All` (security-architect's ruling —
an app-role assignment is a fixed, named, Terraform-visible grant, whereas the
Guest Inviter directory role is a Microsoft-owned bundle that can widen without
our Terraform changing). `DefaultAzureCredential` against
`https://graph.microsoft.com/.default`; **no secret, no app registration, no
client credential**, so ADR-011 gains nothing for NW-67.

**2. The change that is genuinely this ADR's: the *apply* plane.**
`infra/modules/identity/main.tf:41-42` records that "web.yml reads it over ARM
so the deploy job does not need Microsoft Graph (ADR-015)". That stays true for
the **deploy** job and stops being true for the **apply** identity: creating an
`azuread_app_role_assignment` is itself a directory write, so the identity that
runs the HCP apply must hold Graph `AppRoleAssignment.ReadWrite.All` +
`Application.Read.All`, or the **Privileged Role Administrator** / **Cloud
Application Administrator** role. It does not hold them today. That grant is a
**one-time operator act, out of band, before the first apply**, verified at the
ADR-014 w15 gate — never discovered from a red HCP run. It is the **only
change to any deploy-plane identity's rights in this wave**, which is why it is
recorded here rather than folded into an ADR-016 ordering clause.

**3. For a managed identity the assignment *is* the consent.** There is no
separate "Grant admin consent" click to schedule — cloud-architect's correction,
accepted as stated, and it replaces this seat's lane preference for an
out-of-Terraform grant. Keeping the assignment in Terraform state is better than
an untracked portal act, provided clause 4 holds.

**4. The grant must not be able to error the shared run.** Both environment
roots are a single state each, and w15's one infrastructure PR carries Service
Bus wiring (NW-27) and ACS Email (NW-68) alongside this. If the app-role
assignment errored, three features would fail on one missing directory right.
The control is cloud-architect's `count`-gated resource behind
`var.guest_provisioning_enabled` (default **`false`**): with the permission
absent the flag stays false, `count = 0`, **the whole w15 apply still succeeds**,
and NW-67 lands on a later one-line flip. A missing directory right therefore
**degrades NW-67** instead of blocking the wave. Fallback if the operator
declines to widen the apply identity permanently: a Global Administrator
performs the single assignment out of band, the resource stays at `count = 0`,
and the deviation is recorded with an `import` path.

**5. CI gains nothing, stated as a rule so it is not "helpfully" added.** No
Graph permission on `raffa-sp-dev` / `raffa-sp-demo`, no new subject claim, no
new repository or environment secret, and no workflow mentions Graph or
`User.Invite` (zero occurrences across all ten workflow files, surveyed
2026-09-13). Security-architect records the same conclusion as `none — ADR-015`
from their side: the runtime permission is not a CI credential.

**Not decided here**: the Graph call's placement and failure contract
(software-architect, ADR-026), the permission's blast radius and the `oid` bind
(security-architect, ADR-025 §J), and the Terraform resources themselves
(cloud-architect, ADR-005 / ADR-007).

## Amendment (2026-09-14, wave w15 — re-entry round: the apply identity's grant is re-priced, and one of clause 2's three options cannot perform it)

Item **NW-67**. Seat: delivery-manager, ruling adopted from security-architect
(`ADR-011` w15 §7–§10, their second w15 footer). Everything above is unchanged —
the body and the first w15 footer's clauses 1–5 — and `Status: accepted` stands.
**Nothing is superseded.** This footer adds no resource, no federated credential
and no workflow. It changes **which of two options already on this page is the
default**, and it **strikes a third that cannot work.** Clause numbering
continues at **6**.

**6. Clause 2 named three options and one of them cannot perform this
assignment.** `:126-128` offers Graph `AppRoleAssignment.ReadWrite.All` +
`Application.Read.All`, **or** Privileged Role Administrator, **or** Cloud
Application Administrator. Security-architect checked all three against the
permission actually being granted (`ADR-011` §8): **Cloud Application
Administrator — and Application Administrator — may consent to delegated and
application permissions *excluding Microsoft Graph application permissions*, and
`User.Invite.All` is precisely one of those.** That option is **struck**.

It is not a near-miss. It is the narrowest-sounding entry on a list this seat
wrote, so it is the one a least-privilege-minded operator reaches for **first**,
and it fails **at the apply** — producing exactly the red HCP run clause 2's own
closing words exist to prevent ("verified at the ADR-014 w15 gate — never
discovered from a red HCP run"). The two remaining options both work and **both
are tenant-wide escalation**: Privileged Role Administrator assigns any
directory role, Global Administrator included. A list whose narrow option is
broken and whose working options are unbounded routes an operator to a red run
and then, under time pressure, to the broadest grant on the page. That is this
seat's error, in this seat's file, and it is corrected here rather than
explained.

**7. The default inverts: clause 4's fallback becomes the preferred shape, and
the standing grant becomes the fallback.** Clause 4 recorded the out-of-band
assignment as a degradation ("*Fallback if the operator declines to widen the
apply identity permanently*", `:148-151`). Security-architect rules it the
preferred shape (`ADR-011` §9), and the pricing that decides it is the half this
seat never did: **`AppRoleAssignment.ReadWrite.All` is not a narrow right — it
grants any application permission of any API, Graph's own
`Directory.ReadWrite.All` and `RoleManagement.ReadWrite.Directory` included, to
any service principal, including itself.** Held by an *automation* identity
whose trigger is a merge to `infra/`— this seat's own two-merge structure
(ADR-014 w15 clause 5) — it means **whoever can merge Terraform can mint
arbitrary directory privilege in the customer's tenant**, inside a plan that
reads as one assignment. The need is one assignment, of one permission, once.

- **Default** — a Global Administrator performs the single `User.Invite.All`
  app-role assignment **out of band, once**, before the first apply.
  `var.guest_provisioning_enabled` stays `false`, the resource stays at
  `count = 0`, and the later `import` leaves the apply identity needing only
  **read** (`Application.Read.All`) — a read right cannot grant anything.
  **Revocation stays a human act**, not something a merge can perform.
- **Fallback** — the standing grant, under security-architect's four conditions:
  never Privileged Role Administrator; exactly one assignment, and never one
  whose principal is the apply identity itself; re-reviewed at **every**
  `infra/` wave; recorded in the ADR-016 runbook with date and grantor.

Clause 3 is untouched: for a managed identity the assignment **is** the consent,
and there is still no "Grant admin consent" click to schedule. Clause 4's
`count` gate is untouched and is what makes **either** shape safe — with the
permission absent the flag stays `false` and **the whole w15 apply still
succeeds**, so NW-67 degrades instead of blocking Service Bus and mail.

**8. The gate check gets a named expected outcome per shape, so it proves the
answer either way.** Clause 2 asks the operator to verify the apply identity's
rights *before* the first apply; it never said what "verified" looks like, and an
unstated expectation is how a check becomes a shrug. Security-architect's
`ADR-011` §10 supplies the shape and this seat adopts it as the wording of that
gate step:

| Shape in force | What the `raffa-dev` plan must show | If it shows otherwise |
|---|---|---|
| **Default** (out-of-band grant) | `azuread_app_role_assignment` at `count = 0` — **no directory write in the plan at all** | a plan proposing the assignment means the flag was set without the grant — stop |
| **Fallback** (standing grant) | exactly **one** `azuread_app_role_assignment` to create, principal `id-raffa-<env>-workload` | more than one, or a principal that is the apply identity itself, is clause 7's escalation condition — stop |

A plan that **errors** on a missing directory right is the failure this check
prevents, not its result. This discharges the ask this seat carried into the
table — *the apply identity's Graph rights are verified at the gate rather than
discovered from a red run* — and it is read at the operator-prerequisite list of
ADR-014 w15 clause 7, whose wording does not change.

**Not decided here** (unchanged): the Graph call's placement and failure
contract (ADR-026), the permission's blast radius and the `oid` bind (ADR-025
§J), and the Terraform resources themselves (ADR-005 / ADR-007).
