# modules/postgres -- Azure Database for PostgreSQL Flexible Server with
# pgvector enabled (ADR-005, ADR-007). RLS/tenant_id isolation is a
# schema-level (SQL migration) concern owned by the data-access layer,
# not Terraform -- out of scope here.
#
# ADR-007 forbids secrets in Terraform *source*. The administrator
# password is generated with random_password so no literal secret is
# ever written to a .tf file; it still lands in remote state (HCP
# Terraform), which is expected and is not "source". The connection
# string is written to this environment's Key Vault (modules/keyvault)
# and pulled into Container Apps via managed identity (ADR-011).
locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }

  database_name = "raffa_${var.environment}"

  # Npgsql keyword format. override_special below excludes ';' so the
  # password cannot split the connection string.
  connection_string = "Host=${azurerm_postgresql_flexible_server.this.fqdn};Database=${local.database_name};Username=${var.administrator_login};Password=${random_password.administrator.result};Ssl Mode=Require"
}

resource "random_password" "administrator" {
  length           = 32
  special          = true
  min_upper        = 2
  min_lower        = 2
  min_numeric      = 2
  min_special      = 2
  override_special = "!@#%^*-_=+"
}

resource "azurerm_postgresql_flexible_server" "this" {
  name                   = "psql-raffa-${var.environment}"
  location               = var.location
  resource_group_name    = var.resource_group_name
  version                = var.postgres_version
  sku_name               = var.sku_name
  storage_mb             = var.storage_mb
  administrator_login    = var.administrator_login
  administrator_password = random_password.administrator.result

  # No delegated_subnet_id/private_dns_zone_id yet: this scaffold creates
  # the server on public access (still firewalled -- no rule opens it by
  # default). Private networking through modules/network's postgres
  # subnet is wired in a later task.
  #
  # Azure assigns an availability zone on create. The config does not pin
  # `zone` (Burstable has no HA), so a later plan would try to clear it
  # and azurerm errors: "zone can only be changed when exchanged with
  # high_availability.0.standby_availability_zone". Ignore the computed
  # zone so apply stays idempotent.

  tags = local.tags

  lifecycle {
    ignore_changes = [zone]
  }
}

resource "azurerm_postgresql_flexible_server_configuration" "pgvector" {
  name      = "azure.extensions"
  server_id = azurerm_postgresql_flexible_server.this.id
  value     = "VECTOR"
}

# 0.0.0.0-0.0.0.0 is Azure's documented "Allow Azure services" rule so
# Container Apps in this same subscription can reach the public endpoint.
# Private-endpoint wiring through modules/network is later work.
resource "azurerm_postgresql_flexible_server_firewall_rule" "allow_azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_postgresql_flexible_server_database" "app" {
  name      = local.database_name
  server_id = azurerm_postgresql_flexible_server.this.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}
