# modules/foundry -- the shared Azure AI Services account, this
# environment's Foundry project and model deployments, and the RBAC that
# lets the AI Gateway call them with a managed-identity token (ADR-004,
# ADR-008, ADR-011, ADR-017 -- all amended 2026-09-09).
#
# Before 2026-09-09 this module only derived names and gated one role
# assignment on an account id an operator was supposed to record after a
# manual Azure Portal step (ADR-008 V1 note). Nobody ever performed that
# step, so every environment ran the fixture gateway. The amended ADR-008
# makes Terraform the owner:
#
#   * ONE root (dev, `create_shared_account = true`) creates the shared
#     resource group rg-raffa-ai and the account aisvc-raffa (kind
#     AIServices: Azure OpenAI + Document Intelligence on one endpoint,
#     account-native Foundry projects, no hub).
#   * every root creates ITS OWN project (raffa-<env>), ITS OWN model
#     deployments (named <model>-<env>) and ITS OWN role assignments; demo
#     attaches to the account by name (`attach_shared_account = true`),
#     never through terraform_remote_state and never by naming the other
#     environment's resource group (scripts/demo_isolation_scan.py).
#   * the endpoint and the AiGateway__Models__* map are published to the
#     Container Apps only when `publish_endpoint` is true AND the account
#     is created or attached -- commit a750746's invariant ("never
#     advertise a Foundry endpoint for an account that does not exist"),
#     now structural: the endpoint is read from the resource/data source,
#     never derived from a string.
#
# scripts/foundry_connection_verify.py asserts this shape (account name
# and resource group in lockstep with scripts/bootstrap_hcp_org.py, gated
# role assignments and outputs, per-env deployment names, no hub-based
# azurerm_ai_foundry, no second account).
locals {
  # Single shared account, no env suffix (ADR-008: "never a second
  # account") -- MUST stay in lockstep with scripts/bootstrap_hcp_org.py's
  # AI_SERVICES_ACCOUNT_NAME / AI_RESOURCE_GROUP_NAME.
  ai_services_account_name = "aisvc-raffa"
  ai_resource_group_name   = "rg-raffa-ai"

  # Per-project isolation (ADR-008: one project per environment) -- MUST
  # stay in lockstep with scripts/bootstrap_hcp_org.py's FOUNDRY_PROJECTS.
  # The Document Intelligence "connection" is an informational value the
  # backend sends as a request header (Document Intelligence is native to
  # the account -- there is no connection resource to create).
  foundry_project_name             = "raffa-${var.environment}"
  document_intelligence_connection = "conn-docint-raffa-${var.environment}"

  # ADR-017 amendment 2026-09-09: every PDF and image goes through
  # Document Intelligence Read. prebuilt-read is a built-in model on the
  # account: no deployment resource, but the same ModelId/ModelVersion
  # binding as the four chat/embedding roles.
  ocr_model_id      = "prebuilt-read"
  ocr_model_version = "2024-11-30"

  account_enabled = var.create_shared_account || var.attach_shared_account
  publish         = local.account_enabled && var.publish_endpoint

  # try(): the unselected [0] index errors are swallowed; "" only when no
  # account is created or attached -- exactly the state in which nothing
  # may be published (see outputs.tf).
  ai_services_account_id = try(
    azurerm_cognitive_account.this[0].id,
    data.azurerm_cognitive_account.this[0].id,
    "",
  )
  ai_services_endpoint = try(
    azurerm_cognitive_account.this[0].endpoint,
    data.azurerm_cognitive_account.this[0].endpoint,
    "",
  )

  tags_shared = {
    project = "raffa"
    env     = "shared"
  }
  tags = {
    project = "raffa"
    env     = var.environment
  }

  # Cognitive Services User covers the Document Intelligence data plane;
  # Cognitive Services OpenAI User covers Azure OpenAI inference. Both are
  # granted to the workload identity and to each listed operator.
  data_plane_roles = ["Cognitive Services User", "Cognitive Services OpenAI User"]

  operator_grants = {
    for pair in setproduct(var.ai_operator_principal_ids, local.data_plane_roles) :
    "${pair[0]}-${replace(lower(pair[1]), " ", "-")}" => {
      principal_id = pair[0]
      role         = pair[1]
    }
    if local.account_enabled
  }

  # Filtered comprehensions (not `cond ? {...} : {}`) so nothing below is
  # evaluated while the account is disabled -- a conditional between two
  # object literals with different attribute sets is an "inconsistent
  # conditional result types" error, and a filtered comprehension over an
  # empty source never reaches an invalid index.
  enabled_model_deployments = { for k, d in var.model_deployments : k => d if local.account_enabled }

  role_model = merge(
    {
      for role, key in var.model_roles : role => {
        id      = azurerm_cognitive_deployment.model[key].name
        version = azurerm_cognitive_deployment.model[key].model[0].version
      }
      if local.account_enabled
    },
    {
      for role, m in { ocr = { id = local.ocr_model_id, version = local.ocr_model_version } } : role => m
      if local.account_enabled
    },
  )

  # Empty unless published: modules/containerapps emits one env block per
  # entry, so a fixture-gateway environment never carries a model id it
  # does not call. Key shape = the backend's AiGateway:Models:<Role>:ModelId
  # / :ModelVersion configuration keys in env-var form.
  model_env = merge(
    { for role, m in local.role_model : "AiGateway__Models__${title(role)}__ModelId" => m.id if local.publish },
    { for role, m in local.role_model : "AiGateway__Models__${title(role)}__ModelVersion" => m.version if local.publish },
    { for k, v in var.extra_gateway_env : k => v if local.publish },
  )
}

# ---------------------------------------------------------------------------
# The shared account (create mode -- dev root only)
# ---------------------------------------------------------------------------

resource "azurerm_resource_group" "ai" {
  count = var.create_shared_account ? 1 : 0

  name     = local.ai_resource_group_name
  location = var.location

  tags = local.tags_shared
}

# kind = AIServices: one account, one endpoint
# (https://aisvc-raffa.cognitiveservices.azure.com/) that serves the
# Azure OpenAI data plane (openai/...) and Document Intelligence
# (documentintelligence/...). project_management_enabled is what makes the
# account-native Foundry projects below possible; the custom subdomain is
# required both for Entra-token auth and for projects. local_auth_enabled =
# false: no key exists anywhere (ADR-011), every caller presents a token.
resource "azurerm_cognitive_account" "this" {
  count = var.create_shared_account ? 1 : 0

  name                  = local.ai_services_account_name
  location              = var.location
  resource_group_name   = azurerm_resource_group.ai[0].name
  kind                  = "AIServices"
  sku_name              = "S0"
  custom_subdomain_name = local.ai_services_account_name

  project_management_enabled    = true
  local_auth_enabled            = false
  public_network_access_enabled = true

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags_shared
}

# Attach mode (demo root): the account by name in the shared resource
# group. Gated on attach_shared_account so a root's plan never fails on an
# account the owning root has not applied yet.
data "azurerm_cognitive_account" "this" {
  count = (var.attach_shared_account && !var.create_shared_account) ? 1 : 0

  name                = local.ai_services_account_name
  resource_group_name = local.ai_resource_group_name
}

# ---------------------------------------------------------------------------
# This environment's project, deployments and grants
# ---------------------------------------------------------------------------

# ADR-008: one Foundry project per environment (account-native, no hub).
# The backend calls the account endpoint directly; the project is where
# the environment's usage, evaluations and playground live in the Foundry
# portal and is passed to the backend as AiGateway__ProjectName.
resource "azurerm_cognitive_account_project" "this" {
  count = local.account_enabled ? 1 : 0

  name                 = local.foundry_project_name
  cognitive_account_id = local.ai_services_account_id
  location             = var.location
  display_name         = local.foundry_project_name
  description          = "Raffa ${var.environment} Foundry project (ADR-008: one project per environment)."

  identity {
    type = "SystemAssigned"
  }

  tags = local.tags
}

# One deployment per model this environment binds (ADR-004 amendment
# 2026-09-09). NoAutoUpgrade: the version the audit log records (brief §8,
# ADR-011) is the version that answered; a newer model is a pull request
# in the environment root, never a silent upgrade.
resource "azurerm_cognitive_deployment" "model" {
  for_each = local.enabled_model_deployments

  # Azure serializes writes on a Cognitive Services account: a deployment
  # PUT that overlaps the project PUT fails with RequestConflict
  # ("Another operation is in progress on the resource ..."), which is
  # exactly how the first dev apply (2026-09-09 22:30) lost raffa-dev
  # while both deployments succeeded. Nothing here reads the project --
  # the dependency exists only to order the two writes.
  depends_on = [azurerm_cognitive_account_project.this]

  name                 = "${each.key}-${var.environment}"
  cognitive_account_id = local.ai_services_account_id

  model {
    format  = "OpenAI"
    name    = each.key
    version = each.value.model_version
  }

  sku {
    name     = each.value.sku_name
    capacity = each.value.capacity
  }

  version_upgrade_option = "NoAutoUpgrade"

  lifecycle {
    precondition {
      condition     = alltrue([for role, key in var.model_roles : contains(keys(var.model_deployments), key)])
      error_message = "Every model_roles value must be a key of model_deployments."
    }
  }
}

# Data-plane grants for this environment's workload identity (ADR-011: a
# managed-identity token, never a key). skip_service_principal_aad_check
# and the ignore_changes list mirror modules/keyvault's own grants (AAD
# replication lag; ARM rejects in-place role-assignment updates).
resource "azurerm_role_assignment" "workload_ai_services_user" {
  count = local.account_enabled ? 1 : 0

  scope                            = local.ai_services_account_id
  role_definition_name             = "Cognitive Services User"
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

resource "azurerm_role_assignment" "workload_ai_services_openai_user" {
  count = local.account_enabled ? 1 : 0

  scope                            = local.ai_services_account_id
  role_definition_name             = "Cognitive Services OpenAI User"
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

# Human operators (live probes, Foundry portal playground). Owner has no
# data-plane rights by itself -- Owner carries no dataActions -- so the
# same two roles are granted explicitly.
resource "azurerm_role_assignment" "operator" {
  for_each = local.operator_grants

  scope                = local.ai_services_account_id
  role_definition_name = each.value.role
  principal_id         = each.value.principal_id

  lifecycle {
    ignore_changes = [
      principal_type,
      name,
    ]
  }
}
