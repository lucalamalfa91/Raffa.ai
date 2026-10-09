# Task E01/F02/US02/T02 (dev-outputs-verify): exposes the "Key Vault URI"
# resource id/endpoint ADR-005's Concrete-services table calls for -- apps
# read secrets at runtime via this URI plus their managed identity.
output "id" {
  description = "Azure resource ID of the Key Vault."
  value       = azurerm_key_vault.this.id
}

output "vault_uri" {
  description = "URI of the Key Vault, used by apps to read secrets/keys at runtime."
  value       = azurerm_key_vault.this.vault_uri
}

output "postgres_connection_secret_versionless_id" {
  description = "Versionless Key Vault secret ID for postgres-connection, consumed by Container Apps secret { key_vault_secret_id }."
  value       = azurerm_key_vault_secret.postgres_connection.versionless_id
  sensitive   = true
}

output "storage_connection_secret_versionless_id" {
  description = "Versionless Key Vault secret ID for storage-connection, consumed by Container Apps secret { key_vault_secret_id }."
  value       = azurerm_key_vault_secret.storage_connection.versionless_id
  sensitive   = true
}

# Task E16/F01/US01/T01 (w15 Terraform, ADR-011 w15 footer): the wave's one
# new secret. Consumed by modules/containerapps' `acs-cs` handle on the
# API app only -- the worker neither issues nor sends invitations.
output "acs_connection_secret_versionless_id" {
  description = "Versionless Key Vault secret ID for acs-connection, consumed by the API Container App's secret { key_vault_secret_id }."
  value       = azurerm_key_vault_secret.acs_connection.versionless_id
  sensitive   = true
}

# ADR-030 D5: null when no token was supplied (the secret is count-gated),
# which modules/containerapps reads as "no gh-feedback secret, publisher
# disabled" -- never an empty string.
output "github_feedback_token_secret_versionless_id" {
  description = "Versionless Key Vault secret ID for github-feedback-token, consumed by the API Container App's secret { key_vault_secret_id }; null when no token was supplied."
  value       = length(azurerm_key_vault_secret.github_feedback_token) > 0 ? azurerm_key_vault_secret.github_feedback_token[0].versionless_id : null
  sensitive   = true
}

# Jev classify-role pilot: null when no key was supplied (the secret is
# count-gated), which modules/containerapps reads as "no jev-api-key secret,
# pilot cannot start even if AiGateway:Jev:Enabled is true".
output "jev_api_key_secret_versionless_id" {
  description = "Versionless Key Vault secret ID for jev-api-key, consumed by the API Container App's secret { key_vault_secret_id }; null when no key was supplied."
  value       = length(azurerm_key_vault_secret.jev_api_key) > 0 ? azurerm_key_vault_secret.jev_api_key[0].versionless_id : null
  sensitive   = true
}
