# modules/acr -- one Basic-tier Container Registry per environment
# (ADR-005: per-env isolation for any deployment-time pull identity, one
# Basic registry per env rather than one shared registry). Admin
# credentials disabled -- pulls use this environment's workload identity
# + RBAC (AcrPull) below; no admin secret is ever generated.
locals {
  tags = {
    project = "contigo"
    env     = var.environment
  }
}

resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
  numeric = true
}

resource "azurerm_container_registry" "this" {
  name                = "acrcontigo${var.environment}${random_string.suffix.result}"
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "Basic"
  admin_enabled       = false

  tags = local.tags
}

# Container Apps pull `contigo-api:<sha>` / `contigo-worker:<sha>` with
# this environment's user-assigned identity (modules/containerapps
# registry { identity = var.workload_identity_id }). Contributor on the
# RG is not enough -- ACR requires the AcrPull data-plane role.
# skip_service_principal_aad_check matches modules/keyvault (AAD lag on
# a just-created managed identity). Azure role assignments are
# immutable: after `import {}` the provider still tries to PATCH
# skip_service_principal_aad_check / principal_type and ARM returns
# "doesn't support update". Ignore those; scope + principal + role
# stay the live grant.
resource "azurerm_role_assignment" "acr_pull" {
  scope                            = azurerm_container_registry.this.id
  role_definition_name             = "AcrPull"
  principal_id                     = var.workload_principal_id
  skip_service_principal_aad_check = true

  lifecycle {
    ignore_changes = [
      skip_service_principal_aad_check,
      principal_type,
      name,
    ]
  }
}
