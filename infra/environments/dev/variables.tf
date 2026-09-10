variable "location" {
  description = "Azure region. Pinned to North Europe for both environments (ADR-006)."
  type        = string
  default     = "North Europe"
}

variable "environment" {
  description = "Deployment environment for this root (ADR-007: two thin environment roots, one per environment, never sharing state). This is the \"dev\" root and must always stay \"dev\" -- use environments/demo to instantiate \"demo\"."
  type        = string
  default     = "dev"

  validation {
    condition     = var.environment == "dev"
    error_message = "environments/dev must always set environment = \"dev\"; use environments/demo for the demo root."
  }
}

# ADR-008 amendment 2026-09-09: two-phase Foundry wiring. false = the
# shared account, this root's project and deployments exist (and can be
# probed) while the Container Apps keep the fixture gateway; true = the
# endpoint and the AiGateway__Models__* env vars are published and the apps
# take the Foundry path. Flipped by pull request after the live probe.
variable "ai_gateway_wired" {
  description = "Publish AiGateway__Endpoint and the AiGateway__Models__* env vars to this environment's Container Apps (Foundry path). false keeps the fixture gateway even though the account exists."
  type        = bool
  default     = true
}

# Entra object ids (not secrets) granted Cognitive Services User +
# Cognitive Services OpenAI User on the shared aisvc-raffa account for
# live probes and the Foundry portal playground. Owner carries no
# data-plane rights by itself. Do not repeat an id in the demo root.
variable "ai_operator_principal_ids" {
  description = "Entra object ids of human operators granted the two data-plane roles on the shared AI services account (live probes, Foundry playground). Not secrets. Never repeat an id in the demo root (RoleAssignmentExists)."
  type        = list(string)
  default     = ["ab5b6f66-1bd6-44e3-8ea0-2ceff69b62a6"]
}

# Per-role AI Gateway knobs settled by the live probe (e.g.
# AiGateway__Models__Extract__ReasoningEffort = "low"). Non-secret;
# published only while ai_gateway_wired is true.
variable "ai_gateway_extra_env" {
  description = "Extra non-secret AiGateway__* env vars for both Container Apps, published only when ai_gateway_wired is true."
  type        = map(string)
  default     = {}
}
