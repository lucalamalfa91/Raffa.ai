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

# Task E16/F01/US01/T01 (NW-68, ADR-005 w15 footer §5 rule 3 / ADR-016 w15
# footer clause 14): both false at this merge -- flipped by a one-line PR
# after demo's own post-promotion acceptance (OQ-w15-dm-01: demo's infra
# moves at this merge even though its code does not).
variable "invitation_mail_enabled" {
  description = "Publishes Invitations__Mail__Enabled to the API app (ADR-005 w15 footer). demo: false until its own acceptance."
  type        = bool
  default     = false
}

variable "guest_provisioning_enabled" {
  description = "Enables the count-gated Graph app-role assignment (modules/identity) and publishes Invitations__GuestProvisioning__Enabled (modules/containerapps). demo: false until its own acceptance."
  type        = bool
  default     = false
}

# Fix 2026-09-14: false for the same reason as dev -- the Graph
# User.Invite.All grant is written OUT-OF-BAND by a Global Administrator,
# never by this apply, because the identity running the HCP apply is not a
# directory administrator (on dev it returned `Authorization_RequestDenied`
# and failed the whole run, Service Bus and ACS included). demo also keeps
# guest_provisioning_enabled false this wave, so the resource is gated
# twice over; this var is what stops demo's own post-promotion flip from
# reproducing the dev failure.
variable "guest_role_assignment_managed" {
  description = "Whether Terraform manages the Graph User.Invite.All app-role assignment (modules/identity). demo: false -- the same apply identity, the same out-of-band grant."
  type        = bool
  default     = false
}
