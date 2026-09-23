variable "location" {
  description = "Azure region. Pinned to North Europe for both environments (ADR-006)."
  type        = string
  default     = "North Europe"
}

# ADR-008 amendment 2026-09-09: demo never creates the shared account (the
# dev root owns it); it attaches by name once the dev root has applied.
# While false this root reads no data source and creates no Foundry
# project, deployment or role assignment, so `terraform plan` never fails
# on an account that does not exist yet.
variable "ai_account_attached" {
  description = "true once the dev root has applied the shared aisvc-raffa account in rg-raffa-ai: attach to it and create demo's own project, deployments and role assignments."
  type        = bool
  default     = true
}

# Two-phase Foundry wiring, same as the dev root. Flipped 2026-09-21: the
# live-Foundry backend has been on demo since demo-v4 (2026-09-18), but this
# flag was never flipped with it, so demo's Container Apps still carried an
# empty AiGateway__Endpoint and no AiGateway__Models__* map -- the API and
# worker bound FixtureAiGateway (regex extraction, empty list stages) while
# dev ran the real models. true publishes the shared account endpoint and
# demo's own gpt-5.4 / gpt-5.4-nano / text-embedding-3-large deployments.
variable "ai_gateway_wired" {
  description = "Publish AiGateway__Endpoint and the AiGateway__Models__* env vars to this environment's Container Apps (Foundry path). false keeps the fixture gateway even though the account exists."
  type        = bool
  default     = true
}

# Entra object ids (not secrets) granted the two data-plane roles on the
# shared account. Must not repeat an id already listed in the dev root
# (RoleAssignmentExists on the second apply).
variable "ai_operator_principal_ids" {
  description = "Entra object ids of human operators granted the two data-plane roles on the shared AI services account. Not secrets. Never repeat an id listed in the dev root."
  type        = list(string)
  default     = []
}

variable "ai_gateway_extra_env" {
  description = "Extra non-secret AiGateway__* env vars for both Container Apps, published only when ai_gateway_wired is true."
  type        = map(string)
  default     = {}
}

# ADR-030: Ask Raffa's web research (kill switch), on in demo because demo
# is where the feature is tested. Published as Chat__WebResearch__Enabled
# through the same gated env map; a search still needs the workspace
# Admin's opt-in and the user's consent on each question.
variable "web_research_enabled" {
  description = "Publishes Chat__WebResearch__Enabled to both Container Apps (ADR-030 kill switch). false keeps the web path off even with the research role bound."
  type        = bool
  default     = true
}

# Task E16/F01/US01/T01 (NW-68, ADR-005 w15 footer §5 rule 3 / ADR-016 w15
# footer clause 14): both false at the w15 merge, to be flipped by a
# one-line PR after demo's own post-promotion acceptance. That flip is this
# one (owner's ruling 2026-09-22, ADR-016 w20 footer): demo runs the same
# product switches as dev from this apply. The ACS sender and connection
# already come from module.communication, so InvitationHostOptions'
# fail-closed validation holds; the Graph User.Invite.All grant for demo's
# workload identity is still written OUT-OF-BAND by a Global Administrator
# (docs/waves/w15-acceptance.md §0.3 step 2) -- until it is, invitations
# on demo run in the NotConfigured (link-only) shape, never a failure.
# ADR-030 D5 (Ask Raffa feedback loop): the GitHub token is a SENSITIVE
# HCP workspace variable of category "Terraform variable" (not
# "Environment variable" like ARM_*: Terraform never reads an env var of
# this name), never a .tf literal (ADR-011); empty means no
# secret and a stored-only feedback loop. Unlike invitation_mail_enabled,
# the switch is on for demo too (owner's ruling 2026-09-22): a switch with
# no token behind it is harmless, the token's presence is the real gate.
variable "github_feedback_token" {
  description = "Fine-grained GitHub PAT (Issues: write on lucalamalfa91/Raffa.ai) for Ask Raffa's feedback issues. Set as a sensitive HCP workspace variable of category Terraform (an Environment variable of this name is never read); empty = no secret (ADR-030 D5)."
  type        = string
  sensitive   = true
  default     = ""
}

variable "feedback_github_enabled" {
  description = "Publishes Feedback__GitHub__Enabled to the API app (ADR-030 D5). demo: true from this apply (owner's ruling 2026-09-22), effective only once the raffa-demo HCP workspace carries github_feedback_token -- without the secret the AND-gate in modules/containerapps keeps the publisher off and every request stored-only."
  type        = bool
  default     = true
}

variable "invitation_mail_enabled" {
  description = "Publishes Invitations__Mail__Enabled to the API app (ADR-005 w15 footer). demo: true from this apply (owner's ruling 2026-09-22, ADR-016 w20 footer) -- module.communication already provides the sender and the connection string."
  type        = bool
  default     = true
}

variable "guest_provisioning_enabled" {
  description = "Enables the count-gated Graph app-role assignment (modules/identity) and publishes Invitations__GuestProvisioning__Enabled (modules/containerapps). demo: true from this apply (owner's ruling 2026-09-22, ADR-016 w20 footer); the grant itself stays out-of-band, see guest_role_assignment_managed."
  type        = bool
  default     = true
}

# Fix 2026-09-14: false for the same reason as dev -- the Graph
# User.Invite.All grant is written OUT-OF-BAND by a Global Administrator,
# never by this apply, because the identity running the HCP apply is not a
# directory administrator (on dev it returned `Authorization_RequestDenied`
# and failed the whole run, Service Bus and ACS included). Now that
# guest_provisioning_enabled above is true, this var is the ONLY thing
# keeping modules/identity from writing the assignment: the resource is
# count-gated on both, so the flip publishes the product switch to the API
# app and creates nothing in the directory. Same rule as dev: flip to true
# only if the apply identity is ever granted the directory right, and
# import the existing assignment in the same change.
variable "guest_role_assignment_managed" {
  description = "Whether Terraform manages the Graph User.Invite.All app-role assignment (modules/identity). demo: false -- the same apply identity, the same out-of-band grant."
  type        = bool
  default     = false
}
