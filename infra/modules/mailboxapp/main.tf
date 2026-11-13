# modules/mailboxapp -- renewal plan F2 (docs/architecture/renewal-in-app-mail-plan.md):
# the Entra app a person signs in to when they connect their company mailbox
# (Microsoft 365 / Outlook) so Raffa can send the approved negotiation emails
# from their own address. Multi-tenant (customers' tenants), delegated
# Mail.Send + User.Read only -- Raffa never reads a mailbox. The redirect is
# the API's OAuth callback; the client secret goes to Key Vault through the
# environment root (outreach secrets), never to an output in clear.

data "azuread_application_published_app_ids" "well_known" {}

data "azuread_service_principal" "msgraph" {
  client_id = data.azuread_application_published_app_ids.well_known.result["MicrosoftGraph"]
}

resource "azuread_application" "mailbox" {
  display_name     = "raffa-${var.environment}-mailbox"
  sign_in_audience = "AzureADMultipleOrgs"

  web {
    redirect_uris = ["${trimsuffix(var.api_public_base_url, "/")}/api/mailbox/callback/microsoft"]
  }

  required_resource_access {
    resource_app_id = data.azuread_application_published_app_ids.well_known.result["MicrosoftGraph"]

    dynamic "resource_access" {
      for_each = toset(["Mail.Send", "User.Read", "offline_access", "openid", "email", "profile"])

      content {
        id   = data.azuread_service_principal.msgraph.oauth2_permission_scope_ids[resource_access.value]
        type = "Scope"
      }
    }
  }
}

resource "azuread_service_principal" "mailbox" {
  client_id = azuread_application.mailbox.client_id
}

resource "azuread_application_password" "mailbox" {
  application_id = azuread_application.mailbox.id
  display_name   = "raffa-${var.environment}-api"
}
