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

# The seed-market-intelligence workflow (dev) runs the AI ingest curation
# from the GitHub runner, authenticated as the OIDC deploy principal, which
# has no data-plane role on aisvc-raffa by default. true grants it Cognitive
# Services User + Cognitive Services OpenAI User there (dev root only).
# Do not also list that principal's object id in ai_operator_principal_ids
# (distinct() tolerates it). false = the job falls back to curation off.
variable "ci_deploy_ai_access" {
  description = "Grant the CI OIDC deploy principal Cognitive Services User + OpenAI User on the shared AI Services account so the dev market-ingest job can call Foundry (Market:Ingestion:Curation). false removes the grant."
  type        = bool
  default     = true
}

# Per-role AI Gateway knobs settled by the live probe (e.g.
# AiGateway__Models__Extract__ReasoningEffort = "low"). Non-secret;
# published only while ai_gateway_wired is true.
variable "ai_gateway_extra_env" {
  description = "Extra non-secret AiGateway__* env vars for both Container Apps, published only when ai_gateway_wired is true."
  type        = map(string)
  default     = {}
}

# Raffa.AiFlows behaviour flags, ALL ON in dev only (demo and the code defaults
# are untouched: every one of these ships Off / Shadow in the options classes).
# Published through module.foundry's extra_gateway_env, so -- like every other
# AiGateway__* knob -- they reach both Container Apps only while
# ai_gateway_wired is true. Keys are the ASP.NET Core env form of the options
# keys (":" -> "__"); backend/tests/Raffa.AiFlows.Tests/Configuration/
# DevAiFlowsEnvBindingTests parses THIS default and binds it through the real
# options classes, so a mistyped key fails CI. Flag -> purpose, the flags left
# off (Jev, provenance label) and the owner's test checklist:
# docs/ai-flows-dev-flags.md. Keep one
# `Key = "value"` per line (the test reads the block as text).
variable "ai_flows_env" {
  description = "Non-secret Raffa.AiFlows behaviour flags enabled in dev (env-var form of Documents:Extraction:*, QuoteExtraction:*, Chat:AskTurn:*, Chat:GapInvestigation:*, Chat:Council:*, Chat:Drafting:*, Chat:Interview:*, Chat:WebResearch:*). Merged into extra_gateway_env; published to both Container Apps only when ai_gateway_wired is true. Set to {} to return dev to the code defaults. Chat__WebResearch__Enabled and AiGateway__Jev__Enabled have their own variables and must not be set here."
  type        = map(string)
  default = {
    # F5 document extraction (Documents:Extraction). Span verification and
    # cross-stage rules run in Gate (owner decision for dev): a fact whose span
    # is not found, or a violated rule, routes the involved fields to review
    # (values are never edited). Flip back to Shadow if review volume explodes.
    # TokensPerMinuteLimit is per Worker process: dev chat capacity 300K TPM
    # (gpt-5.4-nano) / 5 Worker replicas (worker_max_replicas) = 60000.
    Documents__Extraction__UseContextDependencies   = "true"
    Documents__Extraction__MaxConcurrentStages      = "4"
    Documents__Extraction__TransientRetriesPerStage = "1"
    Documents__Extraction__SpanVerification         = "Gate"
    Documents__Extraction__CrossStageValidation     = "Gate"
    Documents__Extraction__DocumentFirstPrompt      = "true"
    Documents__Extraction__SecondOpinionShadow      = "true"
    Documents__Extraction__TokensPerMinuteLimit     = "60000"

    # F6 quote extraction header stage.
    QuoteExtraction__HeaderStageEnabled = "true"

    # F1 Ask turn (Chat:AskTurn).
    Chat__AskTurn__BudgetBeforeAgents         = "true"
    Chat__AskTurn__AgentPackReserveTokens     = "1500"
    Chat__AskTurn__RevalidatePlaysAfterBudget = "true"
    Chat__AskTurn__MemoizeTurnReads           = "true"
    Chat__AskTurn__AnswerOnRunner             = "true"
    Chat__AskTurn__AgentPhaseDeadline         = "00:02:00"
    Chat__AskTurn__AnswerDeadline             = "00:02:00"
    Chat__AskTurn__TurnDeadline               = "00:04:00"

    # F4 capability investigator (Chat:GapInvestigation).
    Chat__GapInvestigation__Mode         = "Triggered"
    Chat__GapInvestigation__ContextTurns = "2"

    # F2 negotiation council (Chat:Council).
    Chat__Council__MarketResearchParallelQueries = "true"
    Chat__Council__SemanticPlayRetry             = "true"
    Chat__Council__MarketNoteValues              = "true"

    # F4 drafting (Chat:Drafting).
    Chat__Drafting__ReuseCouncilPlays  = "true"
    Chat__Drafting__EnforceTone        = "true"
    Chat__Drafting__LocalizedTemplates = "true"

    # Interview stage 2: model interpretations (Chat:Interview).
    Chat__Interview__UseModelStage = "true"

    # F3 web research refinements (the kill switch itself is web_research_enabled).
    Chat__WebResearch__AllowReportedFigures         = "true"
    Chat__WebResearch__ChargeOnlyWhenSourcesFound   = "true"
    Chat__WebResearch__StripTrackingParameters      = "true"
    Chat__WebResearch__CiteSourcesOnAbstain         = "true"
    Chat__WebResearch__RedactSensitiveQueryPatterns = "true"
    Chat__WebResearch__VendorMasterAsTier1          = "true"
    Chat__WebResearch__MaxSourceTier                = "3"

    # F3-D08 source policy data: official domains of the draft vendor master
    # (backend/tests/Raffa.AiEval/golden/pending/vendor-master.v1.json) at
    # tier 1; the code's own WebVendorMaster adds its domains at tier 1 too
    # (VendorMasterAsTier1). An unlisted domain is tier 3, so MaxSourceTier 3
    # ranks without dropping anything: lower it to 2 to keep ONLY listed
    # domains. Left out on purpose because tenants host content there:
    # atlassian.net, amazonaws.com, bare google.com. No DeniedDomains: none
    # invented. Quoted keys because a domain contains dots.
    "Chat__WebResearch__DomainTiers__adobe.com"            = "1"
    "Chat__WebResearch__DomainTiers__atlassian.com"        = "1"
    "Chat__WebResearch__DomainTiers__aws.amazon.com"       = "1"
    "Chat__WebResearch__DomainTiers__azure.com"            = "1"
    "Chat__WebResearch__DomainTiers__cloud.google.com"     = "1"
    "Chat__WebResearch__DomainTiers__docusign.com"         = "1"
    "Chat__WebResearch__DomainTiers__hubspot.com"          = "1"
    "Chat__WebResearch__DomainTiers__microsoft.com"        = "1"
    "Chat__WebResearch__DomainTiers__netsuite.com"         = "1"
    "Chat__WebResearch__DomainTiers__office.com"           = "1"
    "Chat__WebResearch__DomainTiers__oracle.com"           = "1"
    "Chat__WebResearch__DomainTiers__salesforce.com"       = "1"
    "Chat__WebResearch__DomainTiers__sap.com"              = "1"
    "Chat__WebResearch__DomainTiers__servicenow.com"       = "1"
    "Chat__WebResearch__DomainTiers__slack.com"            = "1"
    "Chat__WebResearch__DomainTiers__workday.com"          = "1"
    "Chat__WebResearch__DomainTiers__workspace.google.com" = "1"
    "Chat__WebResearch__DomainTiers__zendesk.com"          = "1"
    "Chat__WebResearch__DomainTiers__zoom.com"             = "1"
    "Chat__WebResearch__DomainTiers__zoom.us"              = "1"

    # F7 market retrieval (Market:Retrieval), read by the API. Hybrid and
    # Diversify are enabled without the F7-D08 golden set (owner decision for dev).
    Market__Retrieval__Hybrid    = "true"
    Market__Retrieval__Diversify = "true"
  }

  validation {
    condition = alltrue([
      for k, v in var.ai_flows_env : !contains(["Chat__WebResearch__Enabled", "AiGateway__Jev__Enabled", "AiGateway__Jev__ApiKey", "AiGateway__QueryHash__Key"], k)
    ])
    error_message = "ai_flows_env must not carry Chat__WebResearch__Enabled (web_research_enabled), AiGateway__Jev__* (jev_enabled / jev_api_key) or any secret such as AiGateway__QueryHash__Key."
  }

  validation {
    condition     = alltrue([for k, v in var.ai_flows_env : length(split("__", k)) >= 2])
    error_message = "ai_flows_env keys must be in ASP.NET Core env-var form with \"__\" as the section separator."
  }
}

# ADR-030: Ask Raffa's web research (kill switch). Published as
# Chat__WebResearch__Enabled through the same gated env map; even when
# true, a search needs the workspace Admin's opt-in and the user's consent
# on each question. Bound to the research role above.
variable "web_research_enabled" {
  description = "Publishes Chat__WebResearch__Enabled to both Container Apps (ADR-030 kill switch). false keeps the web path off even with the research role bound."
  type        = bool
  default     = true
}

# Task E16/F01/US01/T01 (NW-68, ADR-005 w15 footer §5 rule 3 / ADR-016 w15
# footer clause 14): mirrors ai_gateway_wired's own per-environment
# lifecycle. dev flips both true from this apply.
# ADR-030 D5 (Ask Raffa feedback loop): the GitHub token is a SENSITIVE
# HCP workspace variable of category "Terraform variable" (not
# "Environment variable" like ARM_*: Terraform never reads an env var of
# this name), never a .tf literal (ADR-011); empty means no
# secret and a stored-only feedback loop. The switch mirrors
# invitation_mail_enabled's per-environment lifecycle.
variable "github_feedback_token" {
  description = "Fine-grained GitHub PAT (Issues: write on lucalamalfa91/Raffa.ai) for Ask Raffa's feedback issues. Set as a sensitive HCP workspace variable of category Terraform (an Environment variable of this name is never read); empty = no secret (ADR-030 D5)."
  type        = string
  sensitive   = true
  default     = ""
}

variable "feedback_github_enabled" {
  description = "Publishes Feedback__GitHub__Enabled to the API app (ADR-030 D5). dev: true from this apply, effective only once the HCP workspace carries the token."
  type        = bool
  default     = true
}

# Jev (TypeSafe AI) classify-role pilot -- dev-only trial (AiGatewayJevOptions's
# own doc comment). Same "SENSITIVE HCP workspace variable of category
# Terraform variable, never a .tf literal" rule as github_feedback_token
# above: an "Environment variable" of this name is never read by Terraform.
# Empty (the default) means no secret; with jev_enabled = true and no key,
# the classify call fails gracefully per request rather than crashing either
# host -- see ServiceCollectionExtensions.AddAiGatewayModule.
variable "jev_api_key" {
  description = "OpenRouter API key for the Jev classify-role pilot (dev only). Set as a sensitive HCP workspace variable of category Terraform; empty = every classify call fails gracefully even if jev_enabled is true."
  type        = string
  sensitive   = true
  default     = ""
}

# F3-T11 / F3-D10: key of the keyed (HMAC-SHA256) web-query hash in audit
# rows. SENSITIVE HCP workspace variable of category "Terraform variable",
# never a .tf literal (ADR-011). Terraform writes it to Key Vault as the
# secret `query-hash-key`, which the API app reads as AiGateway__QueryHash__Key.
# Empty (the default) = no secret = the unkeyed SHA-256 plus one log warning.
variable "query_hash_key" {
  description = "Secret key for the keyed web-query hash in audit rows (AiGateway:QueryHash:Key). Set as a sensitive HCP workspace variable of category Terraform (e.g. `openssl rand -base64 48`); empty = unkeyed SHA-256."
  type        = string
  sensitive   = true
  default     = ""
}

variable "jev_enabled" {
  description = "Publishes AiGateway__Jev__Enabled = \"true\"|\"false\" to both Container Apps via extra_gateway_env (module.foundry). Flips the classify-role pilot on for this environment; effective only once jev_api_key also carries a real key."
  type        = bool
  default     = false
}

variable "invitation_mail_enabled" {
  description = "Publishes Invitations__Mail__Enabled to the API app (ADR-005 w15 footer). dev: true from this apply."
  type        = bool
  default     = true
}

variable "guest_provisioning_enabled" {
  description = "Enables the count-gated Graph app-role assignment (modules/identity) and publishes Invitations__GuestProvisioning__Enabled (modules/containerapps). dev: true from this apply, contingent on the apply identity's Graph rights (ADR-015 w15 footer)."
  type        = bool
  default     = true
}

# Fix 2026-09-14, first real dev apply: false because the Graph
# User.Invite.All grant on id-raffa-dev-workload is written OUT-OF-BAND by
# a Global Administrator, not by this apply. The HCP apply identity is not
# a directory administrator -- it returned `Authorization_RequestDenied`
# and took Service Bus and ACS down with it. This var only decides who
# writes the grant; guest_provisioning_enabled above still publishes
# Invitations__GuestProvisioning__Enabled to the API app, so the product
# feature is on. Flip to true only if the apply identity is ever granted
# AppRoleAssignment.ReadWrite.All + Application.Read.All, and then import
# the existing assignment in the same change (never let Terraform create a
# second one).
variable "guest_role_assignment_managed" {
  description = "Whether Terraform manages the Graph User.Invite.All app-role assignment (modules/identity). dev: false -- a Global Administrator holds that grant out-of-band."
  type        = bool
  default     = false
}
