# modules/keyvault -- one Standard-tier Key Vault per environment
# (ADR-005). RBAC authorization (not access-policy) so runtime access is
# granted via role assignment to modules/identity's managed identity in
# a later task -- see azurerm_role_assignment.workload_secrets_user below
# (task E01/F02/US04/T01), which is that later task.
data "azurerm_client_config" "current" {}

locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }
}

resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
  numeric = true
}

resource "azurerm_key_vault" "this" {
  name                       = "kv-raffa-${var.environment}-${random_string.suffix.result}"
  location                   = var.location
  resource_group_name        = var.resource_group_name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = false
  soft_delete_retention_days = 7

  tags = local.tags
}

# Task E01/F02/US04/T01 (ADR-011): grants this environment's own API/worker
# managed identity (modules/identity's single shared "workload" identity)
# read access to secrets in this environment's own vault -- and only this
# one, since var.workload_principal_id is always wired from this same
# environment root's own `module.identity` output (see
# infra/environments/{dev,demo}/main.tf), never the other environment's.
#
# This vault has `rbac_authorization_enabled = true` above, so Azure
# ignores legacy `access_policy` blocks entirely; "Key Vault Secrets User"
# is the RBAC-role equivalent of the legacy get+list secret permissions
# ADR-011 calls for. `skip_service_principal_aad_check` guards against the
# well-known AAD replication lag when the principal (the managed identity)
# was itself just created in the same apply.
resource "azurerm_role_assignment" "workload_secrets_user" {
  scope                            = azurerm_key_vault.this.id
  role_definition_name             = "Key Vault Secrets User"
  principal_id                     = var.workload_principal_id
  skip_service_principal_aad_check = true
}

# RBAC-enabled vaults grant the creator no data-plane rights. The applying
# principal (HCP workspace ARM_* / local az login) needs Secrets Officer
# to write the connection-string secrets below.
resource "azurerm_role_assignment" "deployer_secrets_officer" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# backend.yml schema apply (ADR-021) authenticates as raffa-sp-<env>,
# not the workload identity. Same vault-only scope as workload_secrets_user.
resource "azurerm_role_assignment" "ci_secrets_user" {
  scope                            = azurerm_key_vault.this.id
  role_definition_name             = "Key Vault Secrets User"
  principal_id                     = var.ci_deploy_principal_id
  skip_service_principal_aad_check = true

  lifecycle {
    ignore_changes = [
      skip_service_principal_aad_check,
      principal_type,
      name,
    ]
  }
}

resource "azurerm_key_vault_secret" "postgres_connection" {
  name         = "postgres-connection"
  value        = var.postgres_connection_string
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [
    azurerm_role_assignment.deployer_secrets_officer,
    azurerm_role_assignment.workload_secrets_user,
  ]
}

resource "azurerm_key_vault_secret" "storage_connection" {
  name         = "storage-connection"
  value        = var.storage_connection_string
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [
    azurerm_role_assignment.deployer_secrets_officer,
    azurerm_role_assignment.workload_secrets_user,
  ]
}

# Task E16/F01/US01/T01 (w15 Terraform, ADR-011 w15 footer): the wave's one
# new secret, wired to the API app only (modules/containerapps `acs-cs`
# handle) -- same depends_on RBAC pair as postgres-connection/
# storage-connection above, for the same reason: it stops the first apply
# racing RBAC propagation on this rbac_authorization_enabled vault.
resource "azurerm_key_vault_secret" "acs_connection" {
  name         = "acs-connection"
  value        = var.acs_connection_string
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [
    azurerm_role_assignment.deployer_secrets_officer,
    azurerm_role_assignment.workload_secrets_user,
  ]
}

# ADR-030 D5 (Ask Raffa feedback loop): count-gated on the token being set,
# because an empty Key Vault secret value is rejected and an environment
# without a PAT must still apply. Same depends_on RBAC pair as every secret
# above. Consumed by modules/containerapps' `gh-feedback` handle, API app
# only.
resource "azurerm_key_vault_secret" "github_feedback_token" {
  count = var.github_feedback_token == "" ? 0 : 1

  name         = "github-feedback-token"
  value        = var.github_feedback_token
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [
    azurerm_role_assignment.deployer_secrets_officer,
    azurerm_role_assignment.workload_secrets_user,
  ]
}

# Jev classify-role pilot (dev-only trial): count-gated on the key being set,
# same reasoning as github_feedback_token above -- an environment with the
# pilot off (or not yet given a key) must still apply. Consumed by
# modules/containerapps' `jev-api-key` handle, API app only (the only app
# that ever calls DocumentAdmissionGate.EvaluateAsync -- that gate runs
# synchronously before persistence, never re-run from the Worker).
resource "azurerm_key_vault_secret" "jev_api_key" {
  count = var.jev_api_key == "" ? 0 : 1

  name         = "jev-api-key"
  value        = var.jev_api_key
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [
    azurerm_role_assignment.deployer_secrets_officer,
    azurerm_role_assignment.workload_secrets_user,
  ]
}
