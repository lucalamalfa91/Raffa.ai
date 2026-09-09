# Task E10/F02/US01/T01. Consumed by infra/environments/{dev,demo}/main.tf
# to wire modules/containerapps' three new AiGateway__* env vars (AC-2/
# AC-3) -- non-secret (an endpoint URL and two names, no key), so these
# are plain outputs, never a Key Vault secret (ADR-011).
# Empty until the ADR-008 account really exists (var.ai_services_resource_id
# set). This is not a cosmetic default: Contigo.Api binds IAiGateway to the
# Foundry client whenever AiGateway:Endpoint is non-empty, and to the fixture
# gateway otherwise. Publishing the derived endpoint string for an account that
# has not been created yet made the dev API call a host that does not exist,
# fail to get a managed-identity token, and answer HTTP 500 to every document
# upload -- the product's main action, broken by a value that only *looked*
# configured. An environment without the account now falls back to the fixture
# gateway and the whole pipeline works end to end; the moment the account is
# wired, the endpoint appears here and the same code takes the Foundry path.
output "ai_services_endpoint" {
  description = "Custom-subdomain endpoint of the shared ADR-008 AI services account (aisvc-contigo), or \"\" while var.ai_services_resource_id is empty (the account does not exist yet, so the app must not be told to call it)."
  value       = var.ai_services_resource_id != "" ? local.ai_services_endpoint : ""
}

output "foundry_project_name" {
  description = "This environment's Foundry project name (ADR-008: one project per environment, e.g. \"contigo-dev\"). Matches scripts/bootstrap_hcp_org.py FOUNDRY_PROJECTS[].project."
  value       = local.foundry_project_name
}

output "document_intelligence_connection" {
  description = "This environment's per-project Document Intelligence connection name (ADR-017 AC-4). Matches scripts/bootstrap_hcp_org.py FOUNDRY_PROJECTS[].document_intelligence_connection."
  value       = local.document_intelligence_connection
}

# Task E01/F02/US05/T02's own workload_identity_id output comment already
# anticipated this task by name ("recording the ADR-008 Foundry/Document
# Intelligence connection's RBAC grant (ADR-011)"). null while
# var.ai_services_resource_id is still empty, rather than an apply-time
# error -- see modules/foundry's own variables.tf for why that is the
# correct, non-blocking default until the ADR-008 portal step lands.
output "workload_role_assignment_id" {
  description = "Resource id of the workload identity's Cognitive Services User role assignment on the AI services account, or null while var.ai_services_resource_id is still empty (ADR-008 portal step outstanding)."
  value       = try(azurerm_role_assignment.workload_ai_services_user[0].id, null)
}
