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
