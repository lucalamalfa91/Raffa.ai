# Same out-of-band adoption as environments/dev/imports.tf, for the
# live rg-raffa-demo objects. IDs are env-specific; do not copy
# the other root's values.
import {
  to = module.postgres.azurerm_postgresql_flexible_server_database.app
  id = "/subscriptions/47fb604b-85fa-4eb5-916d-78c064a7a08f/resourceGroups/rg-raffa-demo/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-raffa-demo/databases/raffa_demo"
}

import {
  to = module.postgres.azurerm_postgresql_flexible_server_firewall_rule.allow_azure_services
  id = "/subscriptions/47fb604b-85fa-4eb5-916d-78c064a7a08f/resourceGroups/rg-raffa-demo/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-raffa-demo/firewallRules/AllowAzureServices"
}

import {
  to = module.acr.azurerm_role_assignment.acr_pull
  id = "/subscriptions/47fb604b-85fa-4eb5-916d-78c064a7a08f/resourceGroups/rg-raffa-demo/providers/Microsoft.ContainerRegistry/registries/acrraffademo01ixvw/providers/Microsoft.Authorization/roleAssignments/4a370e39-144b-4cd1-a07d-ee8f57611678"
}
