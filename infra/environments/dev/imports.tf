# Adopt resources created out of band on live rg-contigo-dev (manual
# patch before the Key Vault / database / AcrPull wiring landed in
# Terraform). Without these blocks HCP apply 409s: "already exists"
# / RoleAssignmentExists. Terraform >= 1.5 import blocks are no-ops
# once the address is in state, so they can stay after the first
# successful apply.
import {
  to = module.postgres.azurerm_postgresql_flexible_server_database.app
  id = "/subscriptions/47fb604b-85fa-4eb5-916d-78c064a7a08f/resourceGroups/rg-contigo-dev/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-contigo-dev/databases/contigo_dev"
}

import {
  to = module.postgres.azurerm_postgresql_flexible_server_firewall_rule.allow_azure_services
  id = "/subscriptions/47fb604b-85fa-4eb5-916d-78c064a7a08f/resourceGroups/rg-contigo-dev/providers/Microsoft.DBforPostgreSQL/flexibleServers/psql-contigo-dev/firewallRules/AllowAzureServices"
}

import {
  to = module.acr.azurerm_role_assignment.acr_pull
  id = "/subscriptions/47fb604b-85fa-4eb5-916d-78c064a7a08f/resourceGroups/rg-contigo-dev/providers/Microsoft.ContainerRegistry/registries/acrcontigodevzrkb3n/providers/Microsoft.Authorization/roleAssignments/f622524d-01c0-442e-9254-0f49eaf41bb6"
}
