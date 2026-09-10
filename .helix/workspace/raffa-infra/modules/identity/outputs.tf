# ------------------------------------------------------------------
# Outputs: identity module
#
# Exposes the non-secret fields that GitHub Actions workflows and
# the composite azure-login action need. No client secret or
# certificate is ever output (ADR-015 AC-2, AC-3).
# ------------------------------------------------------------------

output "client_id" {
  description = "Application (client) ID of the deployment service principal."
  value       = azuread_application.deploy.client_id
}

output "tenant_id" {
  description = "Entra ID (Azure AD) tenant ID."
  value       = data.azuread_client_config.current.tenant_id
}

output "subscription_id" {
  description = "Azure subscription ID."
  value       = data.azurerm_subscription.current.subscription_id
}

output "service_principal_object_id" {
  description = "Object ID of the service principal (for downstream role assignments)."
  value       = azuread_service_principal.deploy.object_id
}

output "federation_subject" {
  description = "OIDC federation subject claim configured for this environment."
  value       = azuread_application_federated_identity_credential.github_oidc.subject
}
