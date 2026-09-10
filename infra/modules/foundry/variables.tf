variable "environment" {
  description = "Deployment environment. Must be \"dev\" or \"demo\"."
  type        = string

  validation {
    condition     = contains(["dev", "demo"], var.environment)
    error_message = "environment must be \"dev\" or \"demo\"."
  }
}

variable "location" {
  description = "Region of the shared AI resource group / account (create mode) and of this environment's Foundry project. North Europe (ADR-006); every deployment SKU the roots declare was verified available there (ADR-004 amendment 2026-09-09)."
  type        = string
  default     = "North Europe"
}

# ADR-011: this environment's OWN workload identity only -- the caller
# (infra/environments/{dev,demo}/main.tf) must pass its own
# module.identity.workload_principal_id, never a literal, never the other
# environment's -- same rule modules/keyvault and modules/acr follow.
variable "workload_principal_id" {
  description = "Object (principal) id of this environment's user-assigned workload identity (modules/identity's `workload_principal_id` output). Granted Cognitive Services User + Cognitive Services OpenAI User on the shared account so the AI Gateway authenticates with a managed-identity token, never a stored key (ADR-011)."
  type        = string
}

# ADR-008 amendment 2026-09-09: exactly ONE root (dev) creates the shared
# account; every other root attaches to it by name. Two `true`s would be a
# second account, which ADR-008 forbids -- scripts/foundry_connection_verify.py
# asserts a single owner across the env roots.
variable "create_shared_account" {
  description = "true in exactly one root (dev): create rg-raffa-ai and the shared aisvc-raffa Azure AI Services account. Every other root passes false."
  type        = bool
  default     = false
}

variable "attach_shared_account" {
  description = "true to look the shared account up by name (data source) and create this environment's own project, deployments and role assignments on it. Ignored when create_shared_account is true. false = nothing is read or created, so a root's plan never fails before the owning root has applied."
  type        = bool
  default     = false
}

# Two-phase rollout (commit a750746's invariant, kept structural): the
# account, project and deployments can exist and be probed while the
# Container Apps still run the fixture gateway. Nothing is ever published
# unless the account is actually created or attached.
variable "publish_endpoint" {
  description = "true exports the account endpoint and the AiGateway__Models__* map to the Container Apps (Foundry path); false keeps both empty so the apps stay on the fixture gateway."
  type        = bool
  default     = false
}

variable "ai_operator_principal_ids" {
  description = "Entra object ids of human operators granted the same two data-plane roles as the workload identity (live probes with `az account get-access-token --resource https://cognitiveservices.azure.com`, the Foundry portal playground). Object ids are not secrets. List an id in ONE root only: a second root would try to create the same role assignment again (RoleAssignmentExists)."
  type        = list(string)
  default     = []
}

variable "extra_gateway_env" {
  description = "Extra non-secret AiGateway__* env vars published to the Container Apps together with the model map (per-role knobs such as AiGateway__Models__Extract__ReasoningEffort, settled by the live probe). Published only when publish_endpoint is true."
  type        = map(string)
  default     = {}
}

# ADR-004 amendment 2026-09-09: the model ids are confirmed per environment
# and created here as deployments -- never hard-coded in the backend, which
# binds AiGateway:Models:<Role>:ModelId / :ModelVersion from the env vars
# this module publishes. Keyed by the Azure model name; the deployment is
# named "<model>-<environment>" so dev and demo never contend for one name
# on the shared account.
variable "model_deployments" {
  description = "This environment's model deployments on the shared account, keyed by the Azure OpenAI model name: model_version (exact, pinned -- version_upgrade_option is NoAutoUpgrade), sku_name (DataZoneStandard preferred, else GlobalStandard; provisioned SKUs carry a fixed cost and are rejected) and capacity in thousands of tokens per minute."
  type = map(object({
    model_version = string
    sku_name      = string
    capacity      = number
  }))

  validation {
    condition = alltrue([
      for k, d in var.model_deployments : contains(["DataZoneStandard", "GlobalStandard"], d.sku_name)
    ])
    error_message = "sku_name must be DataZoneStandard (EU data zone, preferred) or GlobalStandard; provisioned SKUs carry a fixed cost (ADR-005)."
  }

  validation {
    condition = alltrue([
      for k, d in var.model_deployments : d.capacity >= 1 && d.capacity <= 1000
    ])
    error_message = "capacity is in thousands of tokens per minute (1..1000)."
  }

  validation {
    condition = alltrue([
      for k, d in var.model_deployments : length(trimspace(d.model_version)) > 0
    ])
    error_message = "model_version must be pinned explicitly (ADR-011: the logged model version must be reproducible)."
  }
}

variable "model_roles" {
  description = "AI Gateway role -> key of model_deployments. Exactly classify, extract, embed and answer; the fifth role, ocr, is Document Intelligence prebuilt-read and is fixed inside this module (ADR-017)."
  type        = map(string)

  validation {
    condition     = toset(keys(var.model_roles)) == toset(["classify", "extract", "embed", "answer"])
    error_message = "model_roles must bind exactly classify, extract, embed and answer."
  }
}
