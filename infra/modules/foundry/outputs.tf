# Consumed by infra/environments/{dev,demo}/main.tf to wire
# modules/containerapps' AiGateway__* env vars -- all non-secret (an
# endpoint URL, names and model ids, no key -- ADR-011), so plain outputs,
# never a Key Vault secret.
#
# ai_services_endpoint and model_env are the two-phase gate. Contigo.Api
# binds IAiGateway to the Foundry client whenever AiGateway:Endpoint is
# non-empty and to the fixture gateway otherwise, so publishing an endpoint
# the account cannot back (commit a750746: HTTP 500 on every upload) or a
# model id the backend has not been taught to call must be impossible by
# construction: both are "" / {} unless the account is created or attached
# AND var.publish_endpoint is true.
output "ai_services_endpoint" {
  description = "Endpoint published to the Container Apps, or \"\" unless the account is created/attached AND var.publish_endpoint is true (never an endpoint for an account that does not exist)."
  value       = local.publish ? local.ai_services_endpoint : ""
}

output "model_env" {
  description = "AiGateway__Models__{Classify,Extract,Embed,Answer,Ocr}__{ModelId,ModelVersion} (plus extra_gateway_env) for both Container Apps; {} unless published (same gate as ai_services_endpoint)."
  value       = local.model_env
}

output "ai_services_account_endpoint" {
  description = "Endpoint of the shared account whenever it is created/attached (operator probes), independent of var.publish_endpoint; \"\" if neither."
  value       = local.ai_services_endpoint
}

output "ai_services_account_id" {
  description = "ARM id of the shared aisvc-contigo account (created or attached), or \"\"."
  value       = local.ai_services_account_id
}

output "ai_resource_group_name" {
  description = "Name of the shared AI resource group (rg-contigo-ai, tags env=shared)."
  value       = local.ai_resource_group_name
}

output "foundry_project_name" {
  description = "This environment's Foundry project name (ADR-008: one project per environment, e.g. \"contigo-dev\"). Matches scripts/bootstrap_hcp_org.py FOUNDRY_PROJECTS[].project."
  value       = local.foundry_project_name
}

output "foundry_project_id" {
  description = "ARM id of this environment's Foundry project, or null while not created."
  value       = try(azurerm_cognitive_account_project.this[0].id, null)
}

output "foundry_project_endpoints" {
  description = "Endpoints exported by this environment's Foundry project (AI Foundry API etc.), {} while not created."
  value       = try(azurerm_cognitive_account_project.this[0].endpoints, {})
}

output "document_intelligence_connection" {
  description = "This environment's per-project Document Intelligence connection name (ADR-017 AC-4) -- an informational header value the backend sends, not a resource. Matches scripts/bootstrap_hcp_org.py FOUNDRY_PROJECTS[].document_intelligence_connection."
  value       = local.document_intelligence_connection
}

output "model_deployment_names" {
  description = "Azure model name -> deployment name on the shared account for this environment (e.g. gpt-5.4-nano -> gpt-5.4-nano-dev), {} while not created."
  value       = { for k, d in azurerm_cognitive_deployment.model : k => d.name }
}

output "workload_role_assignment_id" {
  description = "Cognitive Services User assignment id for the workload identity, or null while the account is neither created nor attached."
  value       = try(azurerm_role_assignment.workload_ai_services_user[0].id, null)
}

output "workload_openai_role_assignment_id" {
  description = "Cognitive Services OpenAI User assignment id for the workload identity, or null while the account is neither created nor attached."
  value       = try(azurerm_role_assignment.workload_ai_services_openai_user[0].id, null)
}
