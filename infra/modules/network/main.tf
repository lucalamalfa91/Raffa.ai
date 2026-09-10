# modules/network -- VNet, subnets, and the Postgres Flexible Server
# delegation (ADR-007 module layout). Kept self-contained at this
# scaffold stage: no other module consumes an output from this one yet;
# a later task wires modules/postgres and modules/containerapps into the
# subnets created here.
locals {
  tags = {
    project = "raffa"
    env     = var.environment
  }
}

resource "azurerm_virtual_network" "this" {
  name                = "vnet-raffa-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  address_space       = var.address_space

  tags = local.tags
}

resource "azurerm_subnet" "apps" {
  name                 = "snet-raffa-${var.environment}-apps"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = ["10.0.1.0/24"]

  # azurerm_subnet does not support a `tags` argument -- tagging happens
  # at the VNet and resource level instead.
}

resource "azurerm_subnet" "postgres" {
  name                 = "snet-raffa-${var.environment}-postgres"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = ["10.0.2.0/24"]

  delegation {
    name = "postgres-flexible-server"

    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}
