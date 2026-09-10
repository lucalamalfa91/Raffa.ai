# modules/staticwebapp -- Azure Static Web Apps Free tier (ADR-012).
# Named stably so .github/workflows/web.yml can compose
# `swa-raffa-<env>` the same way backend.yml composes Container App
# names -- no GitHub Environment variable required.
#
# Microsoft.Web/staticSites is not offered in North Europe. West Europe
# is locationineligible on this tenant. The region only hosts managed
# Functions / staging slots; static assets are a global CDN. This module
# therefore does NOT inherit the env-root North Europe pin (ADR-006).
locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }
}

resource "azurerm_static_web_app" "this" {
  name                = "swa-raffa-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku_tier            = "Free"
  sku_size            = "Free"

  # CLI/OIDC deploy from GitHub Actions (web.yml), not a VCS-connected
  # SWA. Preview environments would be extra Free-tier slots we do not use.
  preview_environments_enabled = false

  tags = local.tags

  lifecycle {
    ignore_changes = [repository_branch, repository_url]
  }
}
