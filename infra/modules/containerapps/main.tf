# modules/containerapps -- Container Apps Environment + the API and
# worker Container Apps (ADR-007 module layout; ADR-005: Consumption
# profile, min replicas = 0, 0.25 vCPU / 0.5 GiB default per replica).
# Self-contained at this scaffold stage: it does not yet take
# modules/monitor's Log Analytics workspace id as an input -- a later
# task wires that.
locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }
}

resource "azurerm_container_app_environment" "this" {
  name                = "cae-raffa-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name

  tags = local.tags
}

resource "azurerm_container_app" "api" {
  name                         = "ca-raffa-${var.environment}-api"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"

  # Task E01/F02/US04/T02 (ADR-011): this environment's OWN workload
  # identity only (var.workload_identity_id, wired from this same root's
  # module.identity -- see infra/environments/{dev,demo}/main.tf) -- never
  # a literal, never the other environment's. This is what lets the API
  # actually exercise modules/keyvault's "Key Vault Secrets User" grant on
  # this environment's own vault at runtime, with no stored secret.
  identity {
    type         = "UserAssigned"
    identity_ids = [var.workload_identity_id]
  }

  registry {
    server   = var.acr_login_server
    identity = var.workload_identity_id
  }

  secret {
    name                = "pg-cs"
    key_vault_secret_id = var.postgres_connection_secret_id
    identity            = var.workload_identity_id
  }

  secret {
    name                = "st-cs"
    key_vault_secret_id = var.storage_connection_secret_id
    identity            = var.workload_identity_id
  }

  # Task E16/F01/US01/T01 (NW-68, ADR-011 w15 footer §1): the wave's one
  # new secret, API app only -- the worker neither issues nor sends
  # invitations and must not receive it "for symmetry".
  secret {
    name                = "acs-cs"
    key_vault_secret_id = var.acs_connection_secret_id
    identity            = var.workload_identity_id
  }

  template {
    min_replicas = 0
    # ADR-005 w15 footer §2: default 3, up from 1 -- not cosmetic. A15-1
    # uploads three files in flight and one 0.25-vCPU replica serialising
    # fifteen blob writes is the likeliest way that target fails once the
    # pipeline itself is fixed.
    max_replicas = var.api_max_replicas

    container {
      name   = "api"
      image  = var.container_image
      cpu    = var.cpu
      memory = var.memory

      env {
        name        = "ConnectionStrings__IdentityWorkspace"
        secret_name = "pg-cs"
      }

      env {
        name        = "ConnectionStrings__DocumentsContracts"
        secret_name = "pg-cs"
      }

      env {
        name        = "ConnectionStrings__Audit"
        secret_name = "pg-cs"
      }

      # Task E03/F03/US01/T02 (renewal-action): Raffa.Renewals's first DbContext
      # (RenewalActionService/RenewalAction) -- Raffa.Api.Program throws at startup
      # without this, the same fail-fast shape as the other ConnectionStrings__* above.
      env {
        name        = "ConnectionStrings__Renewals"
        secret_name = "pg-cs"
      }

      # Task E09/F02/US01/T01 (schema-connstrings, ADR-021): Raffa.Api.Program already
      # reads ConnectionStrings:Savings (task E04/F02/US02/T01, savings-opportunity) and
      # ConnectionStrings:Quotes (task E05/F01/US01/T01, quote-extraction) and throws at
      # startup without them -- this module never carried the two env vars, so the live
      # API app has been missing them since those tasks landed. Same "pg-cs" secret as
      # the other ConnectionStrings__* above; Savings and Quotes are separate
      # DbContexts/schemas on the same Postgres server (ADR-003).
      env {
        name        = "ConnectionStrings__Savings"
        secret_name = "pg-cs"
      }

      env {
        name        = "ConnectionStrings__Quotes"
        secret_name = "pg-cs"
      }

      # Task E13/F05/US01/T02 (conversations-api): Raffa.Chat's first DbContext
      # (ConversationService/ChatDbContext, ADR-024 "Conversations (D5)") -- Raffa.Api.Program
      # now reads ConnectionStrings:Chat and throws at startup without it, the same fail-fast
      # shape as every other ConnectionStrings__* above. Same "pg-cs" secret -- Chat is a separate
      # schema on the same shared Postgres server (ADR-003), not a separate database. The worker
      # does not call AddChatModule, so it gets no matching env block below.
      env {
        name        = "ConnectionStrings__Chat"
        secret_name = "pg-cs"
      }

      # Task E13/F06/US01/T01 wired AddSuppliersProductsModule into the API, and
      # Raffa.Api/Program.cs fail-fasts on ConnectionStrings:Suppliers exactly like the
      # blocks above -- without this env var the container never starts. Found by task
      # E13/F11/US01/T01 while writing the V2 acceptance runbook. Same "pg-cs" secret:
      # Suppliers is a separate schema on the same shared Postgres server (ADR-003).
      env {
        name        = "ConnectionStrings__Suppliers"
        secret_name = "pg-cs"
      }

      # Task E13/F02/US01/T02 (market-index): the shared, tenant-agnostic market_record /
      # market_embedding index the Worker's `ingest-market` job fills and Ask reads
      # (ADR-024, R-MKT-03). Unlike the blocks above this one is optional in the host --
      # absent, the API falls back to the in-memory mock projection -- but a deployed
      # environment must have it, or the seeded index is written and never read.
      env {
        name        = "ConnectionStrings__Market"
        secret_name = "pg-cs"
      }

      env {
        name        = "ConnectionStrings__Storage"
        secret_name = "st-cs"
      }

      # Task E10/F02/US01/T01 (foundry-ocr-ca): non-secret (see variables.tf),
      # so plain `value`, never a Key Vault `secret_name` like the
      # ConnectionStrings__* blocks above.
      env {
        name  = "AZURE_CLIENT_ID"
        value = var.workload_identity_client_id
      }

      env {
        name  = "AiGateway__Endpoint"
        value = var.ai_gateway_endpoint
      }

      env {
        name  = "AiGateway__ProjectName"
        value = var.ai_gateway_project_name
      }

      env {
        name  = "AiGateway__DocumentIntelligenceConnection"
        value = var.ai_gateway_document_intelligence_connection
      }

      # ADR-004 amendment 2026-09-09: per-role model ids/versions from
      # modules/foundry (one block per entry; nothing while the environment
      # is not wired to the account -- see var.ai_gateway_model_env).
      dynamic "env" {
        for_each = var.ai_gateway_model_env

        content {
          name  = env.key
          value = env.value
        }
      }

      # Task E16/F01/US01/T01 (NW-27 publish side, ADR-005 w15 footer §5
      # rule 1): optional, never `?? throw` -- mirrors Raffa.Worker/
      # Program.cs:44's own precedent for a dependency that must degrade,
      # never crash. Module outputs, never a root literal.
      env {
        name  = "ServiceBus__FullyQualifiedNamespace"
        value = var.servicebus_fqdn
      }

      env {
        name  = "ServiceBus__TopicName"
        value = var.servicebus_topic_name
      }

      # The API reads the same subscription's dead-letter subqueue on Reprocess
      # (stranded Uploaded recovery). Default in ExtractionQueueOptions is
      # document-processing; injecting the module output keeps API and Worker
      # aligned if the subscription name ever changes.
      env {
        name  = "ServiceBus__SubscriptionName"
        value = var.servicebus_subscription_name
      }

      # Task E16/F01/US01/T01 (NW-68, ADR-005 w15 footer §5 rule 3):
      # Enabled is a PRODUCT switch, not a provisioning gate -- a working
      # connection string behind Enabled = false is harmless, so resources,
      # secret and all four keys land in one apply per environment. dev
      # "true", demo "false" (infra/environments/{dev,demo}/variables.tf).
      env {
        name  = "Invitations__Mail__Enabled"
        value = tostring(var.invitation_mail_enabled)
      }

      env {
        name  = "Invitations__Mail__SenderAddress"
        value = var.acs_sender_address
      }

      env {
        name        = "Invitations__Mail__ConnectionString"
        secret_name = "acs-cs"
      }

      # Composed here from var.spa_host_name -- "https://<host>", no
      # trailing slash (AC-6) -- never a typed literal in an environment
      # root, and never derived from a request header.
      env {
        name  = "Invitations__AcceptUrlBase"
        value = "https://${var.spa_host_name}"
      }

      env {
        name  = "Invitations__GuestProvisioning__Enabled"
        value = tostring(var.guest_provisioning_enabled)
      }

      env {
        name  = "Invitations__GuestProvisioning__TenantId"
        value = var.azuread_tenant_id
      }

      # Task E16/F01/US01/T01 (NW-05, ADR-016 w15 footer clause 15):
      # fail-closed, never crash-closed -- absent, the authenticated routes
      # answer 401 and the API still boots; no header fallback is
      # re-added, not even temporarily. ClientId and Audience are BOTH
      # published on purpose: at requested_access_token_version = 2 the
      # `aud` claim IS the client id, while the SPA requests scopes
      # against the identifier URI (ADR-010 w15 footer S15-2) -- wiring
      # api_identifier_uri where the client id belongs applies cleanly,
      # deploys cleanly, and then 401s every request in the browser.
      env {
        name  = "AzureAd__Authority"
        value = var.azuread_authority
      }

      env {
        name  = "AzureAd__TenantId"
        value = var.azuread_tenant_id
      }

      env {
        name  = "AzureAd__ClientId"
        value = var.azuread_client_id
      }

      env {
        name  = "AzureAd__Audience"
        value = var.azuread_audience
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = 8080

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }

    cors {
      allowed_origins           = ["https://${var.spa_host_name}"]
      allowed_methods           = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"]
      allowed_headers           = ["*"]
      allow_credentials_enabled = false
    }
  }

  tags = local.tags

  # CI (`backend.yml`) owns the image tag after the first `az acr build`.
  # Do not let a later HCP apply revert API/worker to the MCR placeholder.
  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}

resource "azurerm_container_app" "worker" {
  name                         = "ca-raffa-${var.environment}-worker"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"

  # Task E01/F02/US04/T02 (ADR-011): same identity as the "api" app above
  # -- this environment's own workload identity only -- so the worker can
  # also reach only this environment's own Key Vault, never the other's.
  identity {
    type         = "UserAssigned"
    identity_ids = [var.workload_identity_id]
  }

  registry {
    server   = var.acr_login_server
    identity = var.workload_identity_id
  }

  secret {
    name                = "pg-cs"
    key_vault_secret_id = var.postgres_connection_secret_id
    identity            = var.workload_identity_id
  }

  # Task E16/F01/US01/T01 (NW-27, ADR-005 w15 footer §5 / ADR-027 D12/C2):
  # the worker's own missing key. var.storage_connection_secret_id already
  # reaches this module (see the API's identical handle above) -- no new
  # module variable, no environment-root change.
  secret {
    name                = "st-cs"
    key_vault_secret_id = var.storage_connection_secret_id
    identity            = var.workload_identity_id
  }

  template {
    min_replicas = 0
    # ADR-005 w15 footer §2: default 3, up from 1. min_replicas stays 0 --
    # raising that floor to 1 is rejected outright (a new ~$14/env/month
    # fixed line the cost lock forbids); scale-out on consumption is paid
    # only while used.
    max_replicas = var.worker_max_replicas

    container {
      name   = "worker"
      image  = var.container_image
      cpu    = var.cpu
      memory = var.memory

      env {
        name        = "ConnectionStrings__DocumentsContracts"
        secret_name = "pg-cs"
      }

      env {
        name        = "ConnectionStrings__Audit"
        secret_name = "pg-cs"
      }

      # Task E03/F03/US01/T02 (renewal-action): Raffa.Renewals's first DbContext --
      # Raffa.Worker.Program throws at startup without this, same as the api app above.
      env {
        name        = "ConnectionStrings__Renewals"
        secret_name = "pg-cs"
      }

      # Task E09/F02/US01/T01 (schema-connstrings, ADR-021): this task's scope is "API,
      # and worker if it already mounts Renewals" -- this Container App does (above), so
      # it gets the same two env vars as the api app for consistency, even though
      # Raffa.Worker.Program does not read ConnectionStrings:Savings/Quotes yet
      # (WorkerServiceCollectionExtensions.AddWorkerHost only takes
      # DocumentsContracts/Audit/Renewals today). A later worker task wires these in when
      # the worker registers Raffa.Savings/Raffa.Quotes.
      env {
        name        = "ConnectionStrings__Savings"
        secret_name = "pg-cs"
      }

      env {
        name        = "ConnectionStrings__Quotes"
        secret_name = "pg-cs"
      }

      # Task E16/F01/US01/T01 (NW-27, ADR-005 w15 footer §5 rule 2): the
      # boundary that MUST stay fail-fast -- a worker that silently cannot
      # read blobs marks every document Failed, a config gap wearing the
      # costume of a product defect across a whole environment. Mirrors
      # Raffa.Api/Program.cs:66-69's own fail-fast shape.
      env {
        name        = "ConnectionStrings__Storage"
        secret_name = "st-cs"
      }

      # Task E16/F01/US01/T01 (NW-27, ADR-005 w15 footer §5 rule 1):
      # optional -- absent degrades to today's shipped (synchronous)
      # behaviour. MaxAutoLockRenewalMinutes is coupled to
      # lock_duration = "PT5M" on the document-processing subscription
      # (modules/servicebus) via ServiceBusProcessorOptions.MaxAutoLockRenewalDuration.
      env {
        name  = "ServiceBus__FullyQualifiedNamespace"
        value = var.servicebus_fqdn
      }

      env {
        name  = "ServiceBus__TopicName"
        value = var.servicebus_topic_name
      }

      env {
        name  = "ServiceBus__SubscriptionName"
        value = var.servicebus_subscription_name
      }

      env {
        name  = "ServiceBus__MaxAutoLockRenewalMinutes"
        value = "30"
      }

      # Fix 2026-09-14, after the first real twenty-file batch on dev: one document takes
      # ~100 s through the pipeline (classify, staged extraction, embeddings -- model
      # calls, so the replica is waiting on the network most of that time). Two
      # messages in flight per replica left the batch visibly frozen for minutes; four
      # doubles throughput at no fixed cost (the replica only exists while the queue is
      # non-empty). Bounded above by max_replicas x this value; a Foundry 429 surfaces as
      # ExtractionTransientException -> abandon -> redelivery, never a lost document.
      env {
        name  = "ServiceBus__MaxConcurrentCalls"
        value = "4"
      }

      # Task E10/F02/US01/T01 (foundry-ocr-ca): the worker runs the hybrid
      # OCR pre-pass (ADR-017) and needs the same non-secret AI Gateway
      # connection info as the api app above.
      env {
        name  = "AZURE_CLIENT_ID"
        value = var.workload_identity_client_id
      }

      env {
        name  = "AiGateway__Endpoint"
        value = var.ai_gateway_endpoint
      }

      env {
        name  = "AiGateway__ProjectName"
        value = var.ai_gateway_project_name
      }

      env {
        name  = "AiGateway__DocumentIntelligenceConnection"
        value = var.ai_gateway_document_intelligence_connection
      }

      # Same per-role model map as the api app above (ADR-004 amendment
      # 2026-09-09): the worker runs the same AI Gateway.
      dynamic "env" {
        for_each = var.ai_gateway_model_env

        content {
          name  = env.key
          value = env.value
        }
      }
    }

    # Task E16/F01/US01/T01 (NW-27, ADR-005 w15 footer §2, OQ-w15-cl-01):
    # identity-based auth on the scale rule itself -- `identity_id`,
    # proved against the pinned azurerm ~> 4.0 provider schema
    # (`terraform providers schema -json`) rather than copied from a
    # council document. Zero secrets: the first of the preference order's
    # three options. min_replicas stays 0 (unchanged) -- KEDA raises the
    # worker off zero as the queue fills. messageCount is the scale
    # trigger's own threshold, independent of the subscription's
    # max_delivery_count. 1 (not 4): a leftover handful of extraction
    # messages must still wake a replica -- at 4, 1-3 stuck Uploaded
    # documents never left "Processing in the background".
    custom_scale_rule {
      name             = "servicebus-document-processing"
      custom_rule_type = "azure-servicebus"
      identity_id      = var.workload_identity_id

      metadata = {
        namespace        = var.servicebus_namespace_name
        topicName        = var.servicebus_topic_name
        subscriptionName = var.servicebus_subscription_name
        messageCount     = "1"
      }
    }
  }

  tags = local.tags

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}
