# Task E01/F02/US01/T01 (ADR-007: two thin environment roots instantiate
# the shared module library; this one is "dev"). See infra/versions.tf
# and infra/provider.tf for why the terraform{}/provider{} blocks below
# are duplicated here rather than shared -- Terraform has no
# cross-directory include, so each root carries its own copy in
# lockstep.
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

# Task E01/F02/US02/T01 (instantiate the dev environment): promote
# `environment` from a bare string literal to var.environment (declared in
# variables.tf, defaulted+locked to "dev") so this root carries "dev
# variables (location, env)" per this task's file scope, instead of a
# hardcoded local.
locals {
  environment = var.environment
}

resource "azurerm_resource_group" "this" {
  name     = "rg-contigo-${local.environment}"
  location = var.location

  tags = {
    project = "contigo"
    env     = local.environment
  }
}

module "network" {
  source = "../../modules/network"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}

module "staticwebapp" {
  source = "../../modules/staticwebapp"

  environment         = local.environment
  resource_group_name = azurerm_resource_group.this.name
  # location defaults to West US 2: Microsoft.Web/staticSites is not
  # offered in North Europe; West Europe is ineligible on this tenant.
}

module "identity" {
  source = "../../modules/identity"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  web_redirect_uri    = "https://${module.staticwebapp.default_host_name}/"
}

module "postgres" {
  source = "../../modules/postgres"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # ADR-005: Burstable, smallest tier (the module already enables the
  # pgvector/VECTOR extension unconditionally) -- pinned explicitly here so
  # the dev SKU named in this task's coding objective is visible at the env
  # root rather than resting on the module's (identical) default.
  sku_name = "B_Standard_B1ms"
}

module "storage" {
  source = "../../modules/storage"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}

module "servicebus" {
  source = "../../modules/servicebus"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}

module "containerapps" {
  source = "../../modules/containerapps"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # Task E01/F02/US04/T02 (ADR-011): this root's OWN identity module
  # instance only -- never demo's -- so the API/worker Container Apps can
  # only ever present dev's workload identity.
  workload_identity_id          = module.identity.workload_identity_id
  workload_identity_client_id   = module.identity.workload_identity_client_id
  acr_login_server              = module.acr.login_server
  postgres_connection_secret_id = module.keyvault.postgres_connection_secret_versionless_id
  storage_connection_secret_id  = module.keyvault.storage_connection_secret_versionless_id
  spa_host_name                 = module.staticwebapp.default_host_name
  # Task E10/F02/US01/T01 (foundry-ocr-ca): this root's OWN module.foundry
  # instance only -- never demo's.
  ai_gateway_endpoint                         = module.foundry.ai_services_endpoint
  ai_gateway_project_name                     = module.foundry.foundry_project_name
  ai_gateway_document_intelligence_connection = module.foundry.document_intelligence_connection
  ai_gateway_model_env                        = module.foundry.model_env
}

# ADR-008 amendment 2026-09-09: this root OWNS the single shared Azure AI
# Services account (rg-contigo-ai / aisvc-contigo); demo attaches to it by
# name and creates only its own project, deployments and grants. This
# root's OWN identity module instance only -- never demo's -- so the grant
# never crosses envs (same rule module.keyvault and module.acr follow).
# ADR-004 amendment 2026-09-09: dev is deliberately cheap (gpt-5.4-nano for
# every chat role, text-embedding-3-small); demo carries the frontier
# models. Every SKU/version below was verified in northeurope for this
# subscription on 2026-09-09.
module "foundry" {
  source = "../../modules/foundry"

  environment           = local.environment
  location              = var.location
  workload_principal_id = module.identity.workload_principal_id

  create_shared_account = true
  attach_shared_account = false
  publish_endpoint      = var.ai_gateway_wired

  ai_operator_principal_ids = var.ai_operator_principal_ids
  extra_gateway_env         = var.ai_gateway_extra_env

  model_deployments = {
    "gpt-5.4-nano"           = { model_version = "2026-03-17", sku_name = "DataZoneStandard", capacity = 300 }
    "text-embedding-3-small" = { model_version = "1", sku_name = "GlobalStandard", capacity = 100 }
  }

  model_roles = {
    classify = "gpt-5.4-nano"
    extract  = "gpt-5.4-nano"
    answer   = "gpt-5.4-nano"
    embed    = "text-embedding-3-small"
  }
}

# ADR-015 SPs are out of band. display_name "contigo-sp-dev" matches more
# than one principal in this tenant; pin the GitHub Environment
# AZURE_CLIENT_ID (not a secret) so the grant hits the OIDC deploy SP.
data "azuread_service_principal" "ci_deploy" {
  client_id = "888079b1-faad-456f-9719-fcea97e2eb9f"
}

module "keyvault" {
  source = "../../modules/keyvault"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # Task E01/F02/US04/T01 (ADR-011): this root's OWN identity module
  # instance only -- never demo's -- so the grant never crosses envs.
  workload_principal_id      = module.identity.workload_principal_id
  ci_deploy_principal_id     = data.azuread_service_principal.ci_deploy.object_id
  postgres_connection_string = module.postgres.connection_string
  storage_connection_string  = module.storage.primary_connection_string
}

module "acr" {
  source = "../../modules/acr"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # This root's OWN identity only -- never demo's -- so AcrPull cannot
  # pull images from the other environment's registry.
  workload_principal_id = module.identity.workload_principal_id
}

module "monitor" {
  source = "../../modules/monitor"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # ADR-005: Pay-As-You-Go Log Analytics with a daily ingestion cap so an
  # idle dev environment cannot run up a log bill; pinned explicitly (the
  # module default is already 1) so the cap is visible at the env root.
  daily_quota_gb = 1
}
