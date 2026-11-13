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
# every resource name (rg-raffa-demo, psql-raffa-demo,
# sbns-raffa-demo, ...) and the remote state backend
# (backend.tf -> HCP workspace "raffa-demo", ADR-007) are distinct
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
# from dev's "rg-raffa-dev", never a target of data-plane promotion.
resource "azurerm_resource_group" "this" {
  name     = "rg-raffa-${local.environment}"
  location = var.location

  tags = {
    project = "raffa"
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
  # Task E16/F01/US01/T01 (NW-67): true since the owner's ruling of
  # 2026-09-22 (ADR-016 w20 footer) -- the w15 clause-14 flip, done.
  guest_provisioning_enabled = var.guest_provisioning_enabled
  # Fix 2026-09-14: same as dev -- the grant is out-of-band, Terraform
  # never owns it, so demo's later flip cannot repeat the dev failure.
  guest_role_assignment_managed = var.guest_role_assignment_managed
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

# ADR-005: Service Bus Standard tier (topics for extraction events) --
# Basic is rejected because it omits topics. Sessions are deliberately NOT
# enabled on the document-processing subscription (modules/servicebus);
# the comment this replaces predates the subscription and was corrected by
# cloud-architect's ADR-005 w15 footer §6.
module "servicebus" {
  source = "../../modules/servicebus"

  environment         = local.environment
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  # Task E16/F01/US01/T01 (ADR-011 w15 footer §2a): this root's OWN
  # identity module instance only -- never dev's -- so the two
  # topic-scoped role assignments never cross envs.
  workload_principal_id = module.identity.workload_principal_id
  # Task E20/F02/US01/T01 (ADR-005 w17 §15, ADR-022 w17 clause 1): the CI
  # deploy principal already looked up above -- no new data source needed.
  ci_deploy_principal_id = data.azuread_service_principal.ci_deploy.object_id
}

# Task E16/F01/US01/T01 (NW-68, ADR-005/ADR-007 w15 footers): one set of
# ACS resources per environment, never shared with dev's.
module "communication" {
  source = "../../modules/communication"

  environment         = local.environment
  resource_group_name = azurerm_resource_group.this.name

  # Sender domain (two-phase, see modules/communication/variables.tf). All null/false = unchanged.
  custom_domain                = var.mail_custom_domain
  dns_zone_name                = var.mail_dns_zone_name
  dns_zone_resource_group_name = var.mail_dns_zone_resource_group_name
  use_custom_sender            = var.mail_use_custom_sender
}

# ADR-005: Container Apps Environment (consumption-only workload
# profile) plus the API and worker Container Apps. The worker keeps
# min_replicas = 0 (KEDA scales it from the queue); the API keeps ONE warm
# replica (api_min_replicas = 1 below) -- the only always-on line in this root.
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
  ai_gateway_model_env                        = module.foundry.model_env
  # Task E16/F01/US01/T01 (NW-27, ADR-007 w15 footer §2): this root's OWN
  # module.servicebus instance only -- never dev's.
  servicebus_namespace_name    = module.servicebus.name
  servicebus_fqdn              = module.servicebus.fqdn
  servicebus_topic_name        = module.servicebus.topic_name
  servicebus_subscription_name = module.servicebus.subscription_name
  # ADR-005 w15 footer §2: pinned explicitly (equals the module default)
  # so both ceilings are visible at the env root.
  api_max_replicas    = 3
  worker_max_replicas = 3
  # Warm API replica, DEMO ONLY (ADR-005 amendment footer, 2026-10-22). Demo is
  # the stakeholder-facing environment and, with the API at min_replicas = 0,
  # the first request after a few idle minutes waits ~23 s for a cold start
  # (measured on dev, same module and SKUs). One always-on 0.25 vCPU / 0.5 GiB replica costs about $14/month,
  # which is the ceiling ADR-027 priced for a min_replicas = 1 line; that
  # figure is accepted here for the API only. The worker stays scale-to-zero
  # (ADR-027's rejection of a worker floor stands) and `dev` is unchanged: it
  # leaves api_min_replicas unset, so it keeps the module default of 0.
  api_min_replicas = 1
  # Task E16/F01/US01/T01 (NW-68): this root's OWN module.communication and
  # module.keyvault instances only -- never dev's.
  acs_connection_secret_id = module.keyvault.acs_connection_secret_versionless_id
  acs_sender_address       = module.communication.sender_address
  invitation_mail_enabled  = var.invitation_mail_enabled
  # ADR-030 D5 (Ask Raffa feedback loop): this root's OWN module.keyvault
  # instance only -- never the other environment's.
  github_feedback_token_secret_id = module.keyvault.github_feedback_token_secret_versionless_id
  feedback_github_enabled         = var.feedback_github_enabled
  # Task E16/F01/US01/T01 (NW-05, NW-67): this root's OWN module.identity
  # instance only -- never dev's.
  azuread_authority          = module.identity.issuer
  azuread_tenant_id          = module.identity.tenant_id
  azuread_client_id          = module.identity.api_client_id
  azuread_audience           = module.identity.api_identifier_uri
  guest_provisioning_enabled = var.guest_provisioning_enabled
  platform_admin_object_ids  = var.platform_admin_object_ids
  # Renewal plan F2/F3/F3b: company mailboxes, inbound supplier mail (Resend)
  # and notifications -- this root's OWN module.keyvault instance only. Every
  # value is optional: empty keeps the feature off and the manual path works.
  outreach_env        = local.outreach_env
  outreach_secret_ids = local.outreach_secret_ids
}

# ADR-008 amendment 2026-09-09: demo NEVER creates the shared account (the
# dev root owns rg-raffa-ai / aisvc-raffa); it attaches by name inside
# modules/foundry once var.ai_account_attached is true and creates only its
# own project, deployments and grants. This root's OWN identity module
# instance only -- never dev's. ADR-004 amendment 2026-09-09: demo is
# deliberately frontier (gpt-5.4 for extraction and answers, gpt-5.4-nano
# for classification, text-embedding-3-large -- the backend forces
# dimensions = 1536 so the pgvector column width never changes). Every
# SKU/version below was verified in northeurope for this subscription on
# 2026-09-09 and applied successfully.
#
# ADR-004 amendment 2026-09-22: the GPT-5.6 swap (terra for extract/answer,
# luna for classify, amendment 2026-09-21) is rolled back. ARM rejected
# gpt-5.6-terra / gpt-5.6-luna 2026-07-09 in northeurope for BOTH SKUs the
# module allows: first "The specified SKU 'DataZoneStandard' for model
# 'gpt-5.6-terra 2026-07-09' is not supported in this region 'northeurope'"
# (2026-09-21), then the identical InvalidResourceProperties for
# 'GlobalStandard' (2026-09-22). No SKU this module accepts deploys the
# family from this account's region today, and the account (aisvc-raffa,
# rg-raffa-ai, owned by the dev root) cannot move region for demo alone.
# Do not put GPT-5.6 back here until a deployment of it has been created
# by hand (Portal or `az cognitiveservices account deployment create`)
# on aisvc-raffa in northeurope and the SKU that worked is recorded in
# ADR-004. The failed applies already destroyed gpt-5.4-demo /
# gpt-5.4-nano-demo (deployment keys changed in the same run), so this
# apply recreates them and republishes their ids to both Container Apps;
# text-embedding-3-large-demo was accepted and is untouched.
#
# ADR-004 amendment 2026-11-02: extract / answer / research move to
# gpt-6-astra 2026-09-03 (DataZoneStandard). A hand-made deployment
# (gpt-6-astra-test, 50K TPM) was accepted by ARM on aisvc-raffa in
# northeurope the same day, which is the revisit rule above. The GPT-5.6 and
# gpt-6-luna/sol families are still NOT standalone-deployable here (they only
# appear inside the model-router pool), so classify stays on gpt-5.4-nano.
# Capacity is 100 (not 200): the subscription quota for gpt-6-astra
# DataZoneStandard in northeurope is 100K TPM total (InsufficientQuota at 200,
# 2026-11-02). Delete the manual gpt-6-astra-test (50K) BEFORE apply, or the
# create needs 150 against 50 free.
module "foundry" {
  source = "../../modules/foundry"

  environment           = local.environment
  location              = var.location
  workload_principal_id = module.identity.workload_principal_id

  create_shared_account = false
  attach_shared_account = var.ai_account_attached
  publish_endpoint      = var.ai_gateway_wired

  ai_operator_principal_ids = var.ai_operator_principal_ids
  # ADR-030: the web-research kill switch, published with the rest of the
  # gated AiGateway__* map. demo is the environment the feature is tested
  # in, so it carries the switch exactly like dev.
  extra_gateway_env = merge(
    var.ai_gateway_extra_env,
    { Chat__WebResearch__Enabled = var.web_research_enabled ? "true" : "false" },
    # ADR-024 amendment 2026-11-05: the Ask orchestrator (the model chooses the
    # tools; the router is its fallback), on in demo as in dev. Flip to "false"
    # to return demo to the router without a code change.
    { Chat__AskTurn__Agentic = "true" },
  )

  model_deployments = {
    "gpt-6-astra"            = { model_version = "2026-09-03", sku_name = "DataZoneStandard", capacity = 100 }
    "gpt-5.4-nano"           = { model_version = "2026-03-17", sku_name = "DataZoneStandard", capacity = 200 }
    "text-embedding-3-large" = { model_version = "1", sku_name = "GlobalStandard", capacity = 100 }
  }

  # ADR-030: research (Responses API + web_search) on the frontier chat
  # deployment demo already declares -- no new capacity.
  model_roles = {
    classify = "gpt-5.4-nano"
    extract  = "gpt-6-astra"
    answer   = "gpt-6-astra"
    embed    = "text-embedding-3-large"
    research = "gpt-6-astra"
  }
}

# ADR-005: Key Vault Standard tier (no Premium/HSM), RBAC-authorized;
# per-env, never shared with dev's vault.
# ADR-015 SPs are out of band. display_name "raffa-sp-demo" is not
# unique in this tenant; pin the GitHub Environment AZURE_CLIENT_ID
# (not a secret) so the grant hits the OIDC deploy SP.
# Rotated 2026-09-10 with the Contigo -> Raffa rebrand: contigo-sp-demo
# (1f7f7bd7-f741-4aff-b572-368a36a07879) was replaced by raffa-sp-demo,
# so this literal and the GitHub Environment variable must be changed
# together -- a stale value here fails the plan outright ("no matching
# service principal"), not just the Key Vault grant.
data "azuread_service_principal" "ci_deploy" {
  client_id = "de126866-abe0-4956-b273-8533080069f2"
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
  # Task E16/F01/US01/T01 (NW-68, ADR-011 w15 footer §1): this root's OWN
  # module.communication instance only -- never dev's.
  acs_connection_string = module.communication.primary_connection_string
  # ADR-030 D5 (Ask Raffa feedback loop): this root's own HCP workspace
  # variable; empty keeps the secret uncreated.
  github_feedback_token = var.github_feedback_token
  # Renewal plan F2/F3/F3b: this root's own HCP workspace variables; empty
  # values create no secret.
  outreach_secrets = local.outreach_secrets
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

# ---------------------------------------------------------------------------------------------------------
# Renewal plan F2/F3/F3b (docs/architecture/renewal-in-app-mail-plan.md): company mailboxes, inbound supplier
# mail and notifications. Secret values live in this root's HCP workspace variables and land in Key Vault;
# the API app reads them by env var name.
# ---------------------------------------------------------------------------------------------------------
locals {
  outreach_secret_names = {
    "CaseMail__TokenKey"                = "casemail-token-key"
    "CaseMail__Microsoft__ClientSecret" = "casemail-ms-client-secret"
    "CaseMail__Google__ClientSecret"    = "casemail-google-client-secret"
    "CaseMail__Resend__ApiKey"          = "casemail-resend-api-key"
    "CaseMail__Resend__WebhookSecret"   = "casemail-resend-webhook-secret"
    "Notifications__VapidPrivateKey"    = "notifications-vapid-private-key"
  }

  outreach_secrets = {
    "casemail-token-key"              = var.case_mail_token_key
    "casemail-ms-client-secret"       = var.case_mail_microsoft_client_secret
    "casemail-google-client-secret"   = var.case_mail_google_client_secret
    "casemail-resend-api-key"         = var.case_mail_resend_api_key
    "casemail-resend-webhook-secret"  = var.case_mail_resend_webhook_secret
    "notifications-vapid-private-key" = var.notifications_vapid_private_key
  }

  outreach_secret_ids = {
    for env_name, secret_name in local.outreach_secret_names :
    env_name => module.keyvault.outreach_secret_versionless_ids[secret_name]
    if contains(keys(nonsensitive(module.keyvault.outreach_secret_versionless_ids)), secret_name)
  }

  outreach_env = {
    "CaseMail__InboundDomain"       = var.case_mail_inbound_domain
    "CaseMail__ApiPublicBaseUrl"    = var.case_mail_api_public_base_url
    "CaseMail__Microsoft__ClientId" = var.case_mail_microsoft_client_id
    "CaseMail__Google__ClientId"    = var.case_mail_google_client_id
    "Notifications__VapidSubject"   = var.notifications_vapid_subject
    "Notifications__VapidPublicKey" = var.notifications_vapid_public_key
  }

  # The MX record of the inbound sub-domain, only when it sits in this root's mail DNS zone.
  case_mail_inbound_in_zone = (
    var.case_mail_inbound_domain != "" && var.case_mail_inbound_mx != "" && var.mail_dns_zone_name != null
    && endswith(var.case_mail_inbound_domain, ".${coalesce(var.mail_dns_zone_name, "-")}")
  )
}

data "azurerm_dns_zone" "case_mail_inbound" {
  count = local.case_mail_inbound_in_zone ? 1 : 0

  name                = var.mail_dns_zone_name
  resource_group_name = var.mail_dns_zone_resource_group_name
}

# Resend receiving: the inbound sub-domain's MX points at Resend (the host the Resend dashboard shows for
# the receiving domain). Its verification TXT/DKIM records are added as Resend lists them.
resource "azurerm_dns_mx_record" "case_mail_inbound" {
  count = local.case_mail_inbound_in_zone ? 1 : 0

  name                = trimsuffix(var.case_mail_inbound_domain, ".${var.mail_dns_zone_name}")
  zone_name           = data.azurerm_dns_zone.case_mail_inbound[0].name
  resource_group_name = data.azurerm_dns_zone.case_mail_inbound[0].resource_group_name
  ttl                 = 3600

  record {
    preference = 10
    exchange   = var.case_mail_inbound_mx
  }
}
