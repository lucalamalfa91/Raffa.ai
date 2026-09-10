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

  template {
    min_replicas = 0
    max_replicas = 1

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

  template {
    min_replicas = 0
    max_replicas = 1

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
  }

  tags = local.tags

  lifecycle {
    ignore_changes = [template[0].container[0].image]
  }
}
