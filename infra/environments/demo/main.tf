# Task E01/F02/US01/T01 (ADR-007: two thin environment roots instantiate
# the shared module library; this one is "demo"). See infra/versions.tf
# and infra/provider.tf for why the terraform{}/provider{} blocks below
# are duplicated here rather than shared -- Terraform has no
# cross-directory include, so each root carries its own copy in
# lockstep.
#
# Task E01/F02/US03/T01 (us-03-demo-environment): this root is the demo
# instantiation of that library. Every module below is called with its
# ADR-005 default SKU (see the per-module comment above each block) --
# no override is needed here because those defaults already satisfy
# ADR-005 for both environments. Isolation from `dev` (ADR-016: dev and
# demo must never share a Postgres/Storage/Service Bus, and promotion
# moves code only, never data) is structural, not configured:
# `locals.environment = "demo"` feeds every module's naming/tagging, so
# every resource name (rg-contigo-demo, psql-contigo-demo,
# sbns-contigo-demo, ...) and the remote state backend
# (backend.tf -> HCP workspace "contigo-demo", ADR-007) are distinct
# from the "dev" root's by construction -- there is no shared store id
# to assert against. See scripts/terraform_env_roots_scan.py for the
# automated structural proof and task E01/F02/US03/T02 for the
# dedicated cross-environment isolation check.
terraform {
  required_version = ">= 1.8.0, < 2.0.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

provider "azurerm" {
  features {}
}

provider "azuread" {}

locals {
  environment = "demo"
}

# ADR-005: one resource group per environment (never shared); ADR-016:
# this is demo's own isolated resource group -- structurally distinct
# from dev's "rg-contigo-dev", never a target of data-plane promotion.
resource "azurerm_resource_group" "this" {
  name     = "rg-contigo-${local.environment}"
  location = var.location

  tags = {
    project = "contigo"
    env     = local.environment
  }
}

# ADR-007: VNet + the Postgres Flexible Server subnet delegation this
# environment's postgres module will attach to.
module "network" {
  source = "../../modules/network"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}

# ADR-012: Static Web Apps Free tier. Location is West US 2 inside the
# module (not this root's North Europe pin) -- see modules/staticwebapp.
module "staticwebapp" {
  source = "../../modules/staticwebapp"

  environment         = local.environment
  resource_group_name = azurerm_resource_group.this.name
}

# ADR-007: Entra app registration + user-assigned managed identity so
# the API/worker Container Apps read Key Vault/Storage/Service Bus at
# runtime without a stored secret.
module "identity" {
  source = "../../modules/identity"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  web_redirect_uri    = "https://${module.staticwebapp.default_host_name}/"
}

# ADR-005: PostgreSQL Flexible Server, Burstable "B_Standard_B1ms" (module
# default); ADR-003: pgvector extension enabled for embeddings/search.
module "postgres" {
  source = "../../modules/postgres"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}

# ADR-005: Storage Account, General Purpose v2, Blob (LRS) hot -- blob
# for contract documents, queue for the lightweight inbox/dead-letter
# path, never shared with dev's account.
module "storage" {
  source = "../../modules/storage"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}

# ADR-005: Service Bus Standard tier (topics + sessions for extraction
# events) -- Basic is rejected because it omits topics.
module "servicebus" {
  source = "../../modules/servicebus"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}

# ADR-005: Container Apps Environment (consumption-only workload
# profile) plus the API and worker Container Apps, both min_replicas =
# 0 so idle demo costs nothing.
module "containerapps" {
  source = "../../modules/containerapps"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # Task E01/F02/US04/T02 (ADR-011): this root's OWN identity module
  # instance only -- never dev's -- so the API/worker Container Apps can
  # only ever present demo's workload identity.
  workload_identity_id          = module.identity.workload_identity_id
  workload_identity_client_id   = module.identity.workload_identity_client_id
  acr_login_server              = module.acr.login_server
  postgres_connection_secret_id = module.keyvault.postgres_connection_secret_versionless_id
  storage_connection_secret_id  = module.keyvault.storage_connection_secret_versionless_id
  spa_host_name                 = module.staticwebapp.default_host_name
  # Task E10/F02/US01/T01 (foundry-ocr-ca): this root's OWN module.foundry
  # instance only -- never dev's.
  ai_gateway_endpoint                         = module.foundry.ai_services_endpoint
  ai_gateway_project_name                     = module.foundry.foundry_project_name
  ai_gateway_document_intelligence_connection = module.foundry.document_intelligence_connection
}

# Task E10/F02/US01/T01 (foundry-ocr-ca, ADR-008/ADR-011/ADR-017): this
# root's OWN identity module instance only -- never dev's -- so the grant
# never crosses envs (same rule module.keyvault and module.acr already
# follow). var.foundry_ai_services_resource_id is empty until an operator
# completes the ADR-008 Azure Portal step and sets this root's HCP
# Terraform workspace variable of the same name; see modules/foundry's own
# variables.tf for why an empty value is a safe, non-blocking default.
module "foundry" {
  source = "../../modules/foundry"

  environment             = local.environment
  workload_principal_id   = module.identity.workload_principal_id
  ai_services_resource_id = var.foundry_ai_services_resource_id
}

# ADR-005: Key Vault Standard tier (no Premium/HSM), RBAC-authorized;
# per-env, never shared with dev's vault.
# ADR-015 SPs are out of band. display_name "contigo-sp-demo" is not
# unique in this tenant; pin the GitHub Environment AZURE_CLIENT_ID
# (not a secret) so the grant hits the OIDC deploy SP.
data "azuread_service_principal" "ci_deploy" {
  client_id = "1f7f7bd7-f741-4aff-b572-368a36a07879"
}

module "keyvault" {
  source = "../../modules/keyvault"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # Task E01/F02/US04/T01 (ADR-011): this root's OWN identity module
  # instance only -- never dev's -- so the grant never crosses envs.
  workload_principal_id      = module.identity.workload_principal_id
  ci_deploy_principal_id     = data.azuread_service_principal.ci_deploy.object_id
  postgres_connection_string = module.postgres.connection_string
  storage_connection_string  = module.storage.primary_connection_string
}

# ADR-005: Container Registry Basic tier, one per environment (isolation
# for the deployment-time pull identity, not a data store).
module "acr" {
  source = "../../modules/acr"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # This root's OWN identity only -- never dev's -- so AcrPull cannot
  # pull images from the other environment's registry.
  workload_principal_id = module.identity.workload_principal_id
}

# ADR-005: Log Analytics workspace, Pay-As-You-Go with a daily
# ingestion cap (module default) so idle logging cannot run away.
module "monitor" {
  source = "../../modules/monitor"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}
