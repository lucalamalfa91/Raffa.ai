# modules/monitor -- Log Analytics workspace with a daily ingestion cap
# (ADR-005: Pay-As-You-Go with a data cap, e.g. 1 GB/day, to prevent
# idle-log runaway cost).
locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }
}

resource "azurerm_log_analytics_workspace" "this" {
  name                = "log-raffa-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = var.daily_quota_gb

  tags = local.tags
}
