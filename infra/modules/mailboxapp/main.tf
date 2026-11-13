# modules/mailboxapp -- renewal plan F2 (docs/architecture/renewal-in-app-mail-plan.md):
# the Entra app a person signs in to when they connect their company mailbox
# (Microsoft 365 / Outlook) so Raffa can send the approved negotiation emails
# from their own address, read the supplier's replies in the conversations it
# started (Mail.Read, queried one conversation id at a time -- never the rest of
# the mailbox) and post notifications to a Teams channel the person chooses
# (Team.ReadBasic.All, Channel.ReadBasic.All, ChannelMessage.Send). Multi-tenant
# (customers' tenants), delegated permissions only, none needing an admin. The redirect is
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
      for_each = toset([
        "Mail.Send", "Mail.Read", "User.Read", "offline_access", "openid", "email", "profile",
        "Team.ReadBasic.All", "Channel.ReadBasic.All", "ChannelMessage.Send",
      ])

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
