# Exposed for task E01/F02/US03/T02 (demo-isolation-check): comparing
# this output against the `dev` root's own `resource_group_name` is how
# a later automated check proves "rg-contigo-demo" is never
# "rg-contigo-dev" (ADR-016). scripts/terraform_env_roots_scan.py only
# asserts this file exists (AC-4); it does not read output values.
output "resource_group_name" {
  description = "Name of the demo resource group every module in this root deploys into."
  value       = azurerm_resource_group.this.name
}

output "location" {
  description = "Azure region this environment is deployed to."
  value       = var.location
}

output "static_web_app_name" {
  description = "Name of the demo Static Web App (swa-contigo-demo); web.yml composes this."
  value       = module.staticwebapp.name
}

output "static_web_app_hostname" {
  description = "Default hostname of the demo Static Web App (SPA origin / OIDC redirect)."
  value       = module.staticwebapp.default_host_name
}

# ADR-008 amendment 2026-09-09: the shared account demo attaches to, and
# demo's own Foundry project and model deployments.
output "ai_services_account_id" {
  description = "ARM id of the shared aisvc-contigo account once attached (ai_account_attached = true), else \"\"."
  value       = module.foundry.ai_services_account_id
}

output "ai_services_endpoint" {
  description = "Endpoint the Container Apps actually receive: \"\" until ai_gateway_wired = true."
  value       = module.foundry.ai_services_endpoint
}

output "foundry_project_id" {
  description = "ARM id of the contigo-demo Foundry project, or null while not attached."
  value       = module.foundry.foundry_project_id
}

output "ai_model_deployment_names" {
  description = "Azure model name -> deployment name on the shared account (gpt-5.4 -> gpt-5.4-demo, ...)."
  value       = module.foundry.model_deployment_names
}
