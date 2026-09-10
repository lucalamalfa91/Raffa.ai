# modules/identity -- Entra app registrations + user-assigned managed
# identity (ADR-007 module layout). The managed identity is what
# containerapps' API/worker apps use to read Key Vault, Storage, and
# Service Bus at runtime without a stored secret (ADR-007: no secrets in
# Terraform source).
#
# Task E01/F02/US04/T01 (ADR-010) adds the second of the two
# per-environment app registrations -- a PKCE-only public client, pre-
# authorized for the API's Raffa.Read/Raffa.Write scopes -- so each
# environment carries its own isolated pair (api + public client; four
# registrations total across dev+demo). Neither application ever declares
# a `password {}` block: the public client authenticates with
# Authorization Code + PKCE (web SPA and native mobile), and the API
# validates tokens by signature/issuer/audience only (ADR-010; ADR-011 "no
# secrets in ... Terraform source").
data "azuread_client_config" "current" {}

locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }

  # ADR-012: SPA redirect is the Static Web App origin, passed in by the
  # env root from modules/staticwebapp.default_host_name. Entra requires a
  # trailing slash when the URI has no path segment (`single_page_application
  # redirect_uris`); origin-only values are normalized here so callers can
  # pass with or without `/`.
  web_redirect_uri = (
    can(regex("^https://[^/]+/?$", var.web_redirect_uri))
    ? "${trimsuffix(var.web_redirect_uri, "/")}/"
    : var.web_redirect_uri
  )
}

resource "azurerm_user_assigned_identity" "workload" {
  name                = "id-raffa-${var.environment}-workload"
  location            = var.location
  resource_group_name = var.resource_group_name

  # oidcPublicClientId is not secret (PKCE public client). web.yml reads it
  # over ARM so the deploy job does not need Microsoft Graph (ADR-015).
  tags = merge(local.tags, {
    oidcPublicClientId = azuread_application.public_client.client_id
  })
}

# Stable scope ids: azuread requires `oauth2_permission_scope.id` up front
# (it is not provider-computed), so each scope gets its own random_uuid
# resource instead of a hand-picked literal -- generated once and then
# held fixed in state across applies.
resource "random_uuid" "scope_read" {}
resource "random_uuid" "scope_write" {}

# ADR-010 option 1: one API registration per environment, exposing the two
# delegated scopes both the web SPA and the native mobile client request.
resource "azuread_application" "api" {
  display_name = "raffa-${var.environment}-api"

  # Single-tenant: dev/demo isolation is by distinct registration, not by
  # separate Entra tenants (ADR-009's tenant_id is a product/DB-row
  # concept, not an Azure AD directory boundary) -- see outputs.tf `issuer`.
  sign_in_audience = "AzureADMyOrg"

  # `api://raffa-<env>-api` rather than `api://<client_id>`: the
  # client_id is not known yet when this resource's own arguments are
  # evaluated (that would be a self-reference). Azure AD accepts any
  # tenant-unique string after `api://` for a single-tenant app, so this
  # stays fixed and human-readable across applies.
  identifier_uris = ["api://raffa-${var.environment}-api"]

  api {
    # v2 access tokens carry `aud` = this application's client_id,
    # matching ADR-010's "each environment's API validates iss + aud".
    requested_access_token_version = 2

    oauth2_permission_scope {
      id                         = random_uuid.scope_read.result
      value                      = "Raffa.Read"
      type                       = "User"
      enabled                    = true
      admin_consent_description  = "Allow the app to read the signed-in user's Raffa procurement data."
      admin_consent_display_name = "Read Raffa data"
      user_consent_description   = "Allow this app to read your Raffa procurement data."
      user_consent_display_name  = "Read your Raffa data"
    }

    oauth2_permission_scope {
      id                         = random_uuid.scope_write.result
      value                      = "Raffa.Write"
      type                       = "User"
      enabled                    = true
      admin_consent_description  = "Allow the app to create and update the signed-in user's Raffa procurement data."
      admin_consent_display_name = "Write Raffa data"
      user_consent_description   = "Allow this app to create and update your Raffa procurement data."
      user_consent_display_name  = "Write your Raffa data"
    }
  }

  # azuread_application tags are a flat list of category strings (Entra's
  # own tag model), not the key/value azurerm resource tags used
  # elsewhere in this module -- this is the closest equivalent for
  # project/env tracking on an Entra object.
  tags = ["project:raffa", "env:${var.environment}"]
}

resource "azuread_service_principal" "api" {
  client_id = azuread_application.api.client_id
}

# ADR-010 option 1: the public client shared by web and mobile -- one
# Authorization Code + PKCE registration per environment, no client
# secret. `single_page_application` carries the browser redirect (PKCE via
# fetch/CORS, ADR-012's React SPA); `public_client` carries the native
# reply used by the Expo/React Native app (ADR-013's `raffa://callback`).
resource "azuread_application" "public_client" {
  display_name     = "raffa-${var.environment}-public-client"
  sign_in_audience = "AzureADMyOrg"

  single_page_application {
    redirect_uris = [local.web_redirect_uri]
  }

  public_client {
    redirect_uris = ["raffa://callback"]
  }

  # Declares the scopes this client intends to request so they show up as
  # this app's registered API permissions; azuread_application_pre_authorized
  # below is what actually skips the admin-consent prompt for them.
  required_resource_access {
    resource_app_id = azuread_application.api.client_id

    resource_access {
      id   = azuread_application.api.oauth2_permission_scope_ids["Raffa.Read"]
      type = "Scope"
    }

    resource_access {
      id   = azuread_application.api.oauth2_permission_scope_ids["Raffa.Write"]
      type = "Scope"
    }
  }

  tags = ["project:raffa", "env:${var.environment}"]
}

resource "azuread_service_principal" "public_client" {
  client_id = azuread_application.public_client.client_id
}

# The API pre-authorizes its own public client for both scopes so the
# Authorization Code + PKCE flow never prompts for admin consent (ADR-010:
# "the public client is pre-authorized" for Raffa.Read/Raffa.Write).
resource "azuread_application_pre_authorized" "public_client" {
  application_id       = azuread_application.api.id
  authorized_client_id = azuread_application.public_client.client_id

  permission_ids = [
    azuread_application.api.oauth2_permission_scope_ids["Raffa.Read"],
    azuread_application.api.oauth2_permission_scope_ids["Raffa.Write"],
  ]
}
