# modules/foundry -- non-secret Foundry / Document Intelligence connection
# info (ADR-004, ADR-008, ADR-017) plus the workload identity's RBAC grant
# on the shared AI services account (ADR-011: "Terraform manages the
# identity ... that let the AI Gateway authenticate to the Foundry
# account").
#
# Task E10/F02/US01/T01 (foundry-ocr-ca). Inventory before this task: grep
# across infra/ for foundry|cognitive|DocumentIntelligence|OpenAI matched
# only a forward-looking comment in modules/identity/outputs.tf -- no
# azurerm_cognitive_* resource, no AI Gateway env var, anywhere under
# infra/modules. infra/modules/containerapps/main.tf's env {} list was
# connection-strings-only (Postgres/Storage). AC-1's inventory on both
# `dev` and `demo`: still absent. This module, plus the three new env {}
# blocks added to modules/containerapps, are AC-2's Terraform.
#
# What this module is NOT: it does not create the Azure AI Foundry hub,
# projects, or the AI services account itself. ADR-008 keeps that outside
# the Terraform module surface for V1 ("Model deployment may be a
# one-time Azure-based or portal step... not part of the Terraform module
# surface initially") -- scripts/bootstrap_hcp_org.py's
# FOUNDRY_HUB_NAME / AI_SERVICES_ACCOUNT_NAME / FOUNDRY_PROJECTS already
# record that shape structurally (task E01/F02/US05/T01), and
# scripts/foundry_connection_verify.py (task E01/F02/US05/T02) proves it
# is still complete and consistent. This module is the next link in the
# same chain -- the one part Terraform *is* responsible for.
locals {
  # Single shared account, no env suffix (ADR-008: "never a second
  # account") -- MUST stay in lockstep with scripts/bootstrap_hcp_org.py's
  # AI_SERVICES_ACCOUNT_NAME. scripts/foundry_connection_verify.py asserts
  # both sides still agree (check_ai_services_account_name_matches_terraform).
  ai_services_account_name = "aisvc-contigo"

  # Per-project isolation (ADR-008: one project per environment) -- MUST
  # stay in lockstep with scripts/bootstrap_hcp_org.py's FOUNDRY_PROJECTS
  # ("project" / "document_intelligence_connection" per env).
  foundry_project_name             = "contigo-${var.environment}"
  document_intelligence_connection = "conn-docint-contigo-${var.environment}"

  # Custom-subdomain endpoint form managed-identity/Azure AD auth requires
  # (the region-shorthand endpoint only accepts an API key -- ADR-011
  # forbids a stored key). Deterministic from the fixed account name
  # above, so this is a pure string derivation, never a `data` lookup
  # against a resource that -- per the portal-step note above -- may not
  # exist yet. A `data` source here would fail every `terraform plan` for
  # this environment root, including its unrelated resources, until a
  # human finishes that one-time step; a derived local never blocks.
  ai_services_endpoint = "https://${local.ai_services_account_name}.cognitiveservices.azure.com/"
}

# Cognitive Services User: the standard data-plane "call this account with
# an Azure AD token, no key" role (ADR-011 "no model key in Terraform
# source or app code"). Gated on var.ai_services_resource_id being set:
# an account that is not live yet (the ADR-008 portal step is outstanding
# on both dev and demo as of this task) cannot be a role-assignment scope
# -- skip_service_principal_aad_check mirrors modules/keyvault's own
# workload_secrets_user grant (AAD replication lag when the principal was
# itself just created in the same apply).
resource "azurerm_role_assignment" "workload_ai_services_user" {
  count = var.ai_services_resource_id != "" ? 1 : 0

  scope                            = var.ai_services_resource_id
  role_definition_name             = "Cognitive Services User"
  principal_id                     = var.workload_principal_id
  skip_service_principal_aad_check = true
}
