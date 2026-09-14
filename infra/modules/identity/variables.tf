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
# blocking the w15 apply. dev flips true from this apply; demo stays false
# until its own post-promotion acceptance (ADR-016 w15 footer clause 14).
variable "guest_provisioning_enabled" {
  description = "Grants the workload identity the Microsoft Graph User.Invite.All application permission via a count-gated azuread_app_role_assignment, so it can provision an Entra B2B guest at invite time (NW-67)."
  type        = bool
  default     = false
}
