output "resource_group_name" {
  description = "Name of the dev resource group every module in this root deploys into."
  value       = azurerm_resource_group.this.name
}

output "resource_group_id" {
  description = "Full Azure resource ID of the dev resource group."
  value       = azurerm_resource_group.this.id
}

output "location" {
  description = "Azure region this environment is deployed to."
  value       = var.location
}

output "environment" {
  description = "Deployment environment this root instantiates (always \"dev\")."
  value       = local.environment
}

output "tags" {
  description = "project/env tags applied to every dev resource (AC-2); every module below tags its own resources with this same project=contigo, env=dev pair (see infra/modules/*/main.tf locals.tags)."
  value       = azurerm_resource_group.this.tags
}

# Task E01/F02/US02/T02 (dev-outputs-verify). Task T01 (E01/F02/US02/T01)
# left this file with only the four outputs above, because none of the
# nine modules under infra/modules/ declared an `output` block yet -- see
# git history on this file. This task closes that gap by adding
# infra/modules/{postgres,containerapps,storage,servicebus,keyvault,acr,
# monitor}/outputs.tf (network and identity are not named in ADR-005's
# per-service resource id/endpoint list and are left alone) and re-exports
# them here so `terraform output` on this root surfaces every resource id
#/endpoint the parent story's AC-1 requires evidence for.
output "postgres_id" {
  description = "Azure resource ID of the dev PostgreSQL Flexible Server."
  value       = module.postgres.id
}

output "postgres_fqdn" {
  description = "Fully qualified domain name of the dev PostgreSQL Flexible Server."
  value       = module.postgres.fqdn
}

output "container_app_environment_id" {
  description = "Azure resource ID of the dev Container Apps Environment."
  value       = module.containerapps.container_app_environment_id
}

output "api_fqdn" {
  description = "Ingress FQDN of the dev API Container App (external HTTPS endpoint)."
  value       = module.containerapps.api_fqdn
}

output "worker_id" {
  description = "Azure resource ID of the dev worker Container App (no ingress -- not externally reachable)."
  value       = module.containerapps.worker_id
}

output "storage_account_name" {
  description = "Name of the dev Storage Account."
  value       = module.storage.name
}

output "storage_primary_blob_endpoint" {
  description = "Primary Blob service endpoint of the dev Storage Account (contract documents)."
  value       = module.storage.primary_blob_endpoint
}

output "storage_primary_queue_endpoint" {
  description = "Primary Queue service endpoint of the dev Storage Account (lightweight inbox/dead-letter)."
  value       = module.storage.primary_queue_endpoint
}

output "servicebus_namespace_fqdn" {
  description = "Fully qualified domain name of the dev Service Bus namespace."
  value       = module.servicebus.fqdn
}

output "key_vault_uri" {
  description = "URI of the dev Key Vault, used by apps to read secrets/keys at runtime."
  value       = module.keyvault.vault_uri
}

output "acr_login_server" {
  description = "Login server URL of the dev Container Registry (docker login / az acr login target)."
  value       = module.acr.login_server
}

output "log_analytics_workspace_id" {
  description = "Workspace (Customer) ID of the dev Log Analytics workspace, used by diagnostic settings/agents."
  value       = module.monitor.workspace_id
}

output "static_web_app_name" {
  description = "Name of the dev Static Web App (swa-contigo-dev); web.yml composes this."
  value       = module.staticwebapp.name
}

output "static_web_app_hostname" {
  description = "Default hostname of the dev Static Web App (SPA origin / OIDC redirect)."
  value       = module.staticwebapp.default_host_name
}

# ADR-008 amendment 2026-09-09: the shared AI services account this root
# owns, this environment's Foundry project and its model deployments.
output "ai_resource_group_name" {
  description = "Shared AI resource group owned by this root (rg-contigo-ai, tags env=shared)."
  value       = module.foundry.ai_resource_group_name
}

output "ai_services_account_id" {
  description = "ARM id of the shared aisvc-contigo Azure AI Services account."
  value       = module.foundry.ai_services_account_id
}

output "ai_services_account_endpoint" {
  description = "Endpoint of the shared account (live-probe target), independent of ai_gateway_wired."
  value       = module.foundry.ai_services_account_endpoint
}

output "ai_services_endpoint" {
  description = "Endpoint the Container Apps actually receive: \"\" until ai_gateway_wired = true."
  value       = module.foundry.ai_services_endpoint
}

output "foundry_project_id" {
  description = "ARM id of the contigo-dev Foundry project."
  value       = module.foundry.foundry_project_id
}

output "ai_model_deployment_names" {
  description = "Azure model name -> deployment name on the shared account (gpt-5.4-nano -> gpt-5.4-nano-dev, ...)."
  value       = module.foundry.model_deployment_names
}
