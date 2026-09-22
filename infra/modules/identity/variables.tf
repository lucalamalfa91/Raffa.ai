variable "environment" {
  description = "Deployment environment. Must be \"dev\" or \"demo\"."
  type        = string

  validation {
    condition     = contains(["dev", "demo"], var.environment)
    error_message = "environment must be \"dev\" or \"demo\"."
  }
}

variable "location" {
  description = "Azure region. Pinned to North Europe for both environments (ADR-006)."
  type        = string
  default     = "North Europe"
}

variable "resource_group_name" {
  description = "Name of the per-environment resource group this module's resources are created in."
  type        = string
}

variable "web_redirect_uri" {
  description = "SPA redirect URI for the public-client Entra registration (ADR-010 / ADR-012). The env root passes https://<staticwebapp.default_host_name> from modules/staticwebapp. Origin-only values are stored with a trailing slash (Entra requirement)."
  type        = string
}

# Task E16/F01/US01/T01 (NW-67, ADR-015 clause 4): default false so a
# missing apply-plane Graph permission degrades this feature instead of
# blocking the w15 apply. dev flipped true at w15; demo followed on
# 2026-09-22 (ADR-016 w15 footer clause 14, closed by its w20 footer). The
# grant itself is out-of-band on both roots, see guest_role_assignment_managed.
variable "guest_provisioning_enabled" {
  description = "Grants the workload identity the Microsoft Graph User.Invite.All application permission via a count-gated azuread_app_role_assignment, so it can provision an Entra B2B guest at invite time (NW-67)."
  type        = bool
  default     = false
}

# Fix 2026-09-14 (first real dev apply of w15): the app-role assignment and
# the product feature are two different decisions and must be gated
# separately. The identity running the HCP apply is NOT a directory
# administrator -- writing an app-role assignment returned
# `Authorization_RequestDenied` and took the whole apply down with it,
# including Service Bus and ACS. Set this false when a Global Administrator
# has granted User.Invite.All out-of-band (the path infra/README.md already
# called "preferably"); guest_provisioning_enabled then still publishes the
# product flag, and Terraform simply does not manage the grant.
variable "guest_role_assignment_managed" {
  description = "Whether Terraform manages the Graph User.Invite.All app-role assignment for the workload identity. false = a Global Administrator granted it out-of-band; the grant stays outside state and the apply needs no directory right."
  type        = bool
  default     = true
}
