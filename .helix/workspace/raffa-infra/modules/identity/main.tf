# ------------------------------------------------------------------
# Module: identity
# Per-environment service principal with OIDC federated credentials
# for GitHub Actions -> Azure authentication.
#
# References:
#   ADR-015 — OIDC federation, per-env SP, no stored secret
#   ADR-007 — Terraform layout (modules + two env roots)
#   OQ-DM-003 — federation permitted (assumption in force)
#   OQ-DM-004 — subject-claim pinning sufficient (assumption in force)
# ------------------------------------------------------------------

# ---------------------------------------------------------------
# Variables
# ---------------------------------------------------------------

variable "environment" {
  description = "Target environment name (dev or demo)."
  type        = string

  validation {
    condition     = contains(["dev", "demo"], var.environment)
    error_message = "environment must be \"dev\" or \"demo\"."
  }
}

variable "resource_group_id" {
  description = "ID of the Azure resource group to scope the role assignment."
  type        = string
}

variable "github_repository" {
  description = "GitHub repository in owner/repo format for OIDC subject claim."
  type        = string
  default     = "lucalamalfa91/raffa"
}

variable "tags" {
  description = "Tags applied to Entra AD resources."
  type        = map(string)
  default     = {}
}

# ---------------------------------------------------------------
# Data sources
# ---------------------------------------------------------------

data "azurerm_subscription" "current" {}

data "azuread_client_config" "current" {}

# ---------------------------------------------------------------
# Entra AD Application
# ---------------------------------------------------------------

resource "azuread_application" "deploy" {
  display_name = "raffa-sp-${var.environment}"
  owners       = [data.azuread_client_config.current.object_id]

  tags = ["raffa", var.environment, "oidc-federation"]
}

# ---------------------------------------------------------------
# Service Principal (from the application above)
# ---------------------------------------------------------------

resource "azuread_service_principal" "deploy" {
  client_id = azuread_application.deploy.client_id
  owners    = [data.azuread_client_config.current.object_id]

  tags = ["raffa", var.environment, "oidc-federation"]
}

# ---------------------------------------------------------------
# OIDC Federated Identity Credential
#
# dev  -> subject pinned to refs/heads/main (auto-deploy on push)
# demo -> subject pinned to the "demo" GitHub Environment
#         (tag-triggered, requires environment approval — ADR-016)
# ---------------------------------------------------------------

resource "azuread_application_federated_identity_credential" "github_oidc" {
  application_id = azuread_application.deploy.id
  display_name   = "github-actions-${var.environment}"
  description    = "GitHub Actions OIDC federation for ${var.environment} (ADR-015)"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://token.actions.githubusercontent.com"

  subject = (
    var.environment == "demo"
    ? "repo:${var.github_repository}:environment:demo"
    : "repo:${var.github_repository}:ref:refs/heads/main"
  )
}

# ---------------------------------------------------------------
# Role Assignment — Contributor scoped to the env resource group
#
# Least-privilege: the SP can only modify resources inside its own
# environment's resource group (ADR-015 AC-1).
# ---------------------------------------------------------------

resource "azurerm_role_assignment" "deploy_contributor" {
  scope                = var.resource_group_id
  role_definition_name = "Contributor"
  principal_id         = azuread_service_principal.deploy.object_id
}
