variable "location" {
  description = "Azure region. Pinned to North Europe for both environments (ADR-006)."
  type        = string
  default     = "North Europe"
}

variable "environment" {
  description = "Deployment environment for this root (ADR-007: two thin environment roots, one per environment, never sharing state). This is the \"dev\" root and must always stay \"dev\" -- use environments/demo to instantiate \"demo\"."
  type        = string
  default     = "dev"

  validation {
    condition     = var.environment == "dev"
    error_message = "environments/dev must always set environment = \"dev\"; use environments/demo for the demo root."
  }
}

# ADR-008 amendment 2026-09-09: two-phase Foundry wiring. false = the
# shared account, this root's project and deployments exist (and can be
# probed) while the Container Apps keep the fixture gateway; true = the
# endpoint and the AiGateway__Models__* env vars are published and the apps
# take the Foundry path. Flipped by pull request after the live probe.
variable "ai_gateway_wired" {
  description = "Publish AiGateway__Endpoint and the AiGateway__Models__* env vars to this environment's Container Apps (Foundry path). false keeps the fixture gateway even though the account exists."
  type        = bool
  default     = true
}

# Entra object ids (not secrets) granted Cognitive Services User +
# Cognitive Services OpenAI User on the shared aisvc-raffa account for
# live probes and the Foundry portal playground. Owner carries no
# data-plane rights by itself. Do not repeat an id in the demo root.
variable "ai_operator_principal_ids" {
  description = "Entra object ids of human operators granted the two data-plane roles on the shared AI services account (live probes, Foundry playground). Not secrets. Never repeat an id in the demo root (RoleAssignmentExists)."
  type        = list(string)
  default     = ["ab5b6f66-1bd6-44e3-8ea0-2ceff69b62a6"]
}

# Per-role AI Gateway knobs settled by the live probe (e.g.
# AiGateway__Models__Extract__ReasoningEffort = "low"). Non-secret;
# published only while ai_gateway_wired is true.
variable "ai_gateway_extra_env" {
  description = "Extra non-secret AiGateway__* env vars for both Container Apps, published only when ai_gateway_wired is true."
  type        = map(string)
  default     = {}
}

# Task E16/F01/US01/T01 (NW-68, ADR-005 w15 footer §5 rule 3 / ADR-016 w15
# footer clause 14): mirrors ai_gateway_wired's own per-environment
# lifecycle. dev flips both true from this apply.
variable "invitation_mail_enabled" {
  description = "Publishes Invitations__Mail__Enabled to the API app (ADR-005 w15 footer). dev: true from this apply."
  type        = bool
  default     = true
}

variable "guest_provisioning_enabled" {
  description = "Enables the count-gated Graph app-role assignment (modules/identity) and publishes Invitations__GuestProvisioning__Enabled (modules/containerapps). dev: true from this apply, contingent on the apply identity's Graph rights (ADR-015 w15 footer)."
  type        = bool
  default     = true
}

# Fix 2026-09-14, first real dev apply: false because the Graph
# User.Invite.All grant on id-raffa-dev-workload is written OUT-OF-BAND by
# a Global Administrator, not by this apply. The HCP apply identity is not
# a directory administrator -- it returned `Authorization_RequestDenied`
# and took Service Bus and ACS down with it. This var only decides who
# writes the grant; guest_provisioning_enabled above still publishes
# Invitations__GuestProvisioning__Enabled to the API app, so the product
# feature is on. Flip to true only if the apply identity is ever granted
# AppRoleAssignment.ReadWrite.All + Application.Read.All, and then import
# the existing assignment in the same change (never let Terraform create a
# second one).
variable "guest_role_assignment_managed" {
  description = "Whether Terraform manages the Graph User.Invite.All app-role assignment (modules/identity). dev: false -- a Global Administrator holds that grant out-of-band."
  type        = bool
  default     = false
}
