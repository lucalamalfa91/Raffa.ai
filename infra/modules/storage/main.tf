# modules/storage -- one Storage Account (Blob + Queue) per environment
# (ADR-005). dev and demo each get their own account; never shared.
locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }
}

resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
  numeric = true
}

resource "azurerm_storage_account" "this" {
  name                     = "straffa${var.environment}${random_string.suffix.result}"
  location                 = var.location
  resource_group_name      = var.resource_group_name
  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = "LRS"

  tags = local.tags
}

resource "azurerm_storage_container" "documents" {
  name                  = "documents"
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}

# ADR-005 "Queue (simple)" row: Queue storage within this same GPv2 account
# (not a second queue product) as inbox/dead-letter for lightweight jobs.
resource "azurerm_storage_queue" "jobs" {
  name               = "jobs"
  storage_account_id = azurerm_storage_account.this.id
}
