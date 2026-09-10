# Task E01/F02/US02/T02 (dev-outputs-verify): task T01 (E01/F02/US02/T01)
# scaffolded this module without an outputs.tf, so the dev/demo env roots
# had no `module.postgres.*` attribute to re-export -- see the note this
# task removed from environments/dev/outputs.tf. Exposes exactly the
# "Postgres FQDN" resource id/endpoint ADR-005's Concrete-services table
# calls for.
output "id" {
  description = "Azure resource ID of the PostgreSQL Flexible Server."
  value       = azurerm_postgresql_flexible_server.this.id
}

output "name" {
  description = "Name of the PostgreSQL Flexible Server."
  value       = azurerm_postgresql_flexible_server.this.name
}

output "fqdn" {
  description = "Fully qualified domain name of the PostgreSQL Flexible Server (connection endpoint)."
  value       = azurerm_postgresql_flexible_server.this.fqdn
}

output "database_name" {
  description = "Application database name (raffa_<env>), created on this server."
  value       = azurerm_postgresql_flexible_server_database.app.name
}

output "connection_string" {
  description = "Npgsql connection string for the application database. Written to this environment's Key Vault; never re-exported from the env root (ADR-007/ADR-011)."
  value       = local.connection_string
  sensitive   = true
}
