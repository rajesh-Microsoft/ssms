// Azure SQL logical server for the DB-per-tenant model. Only the control-plane
// database is declared here; tenant databases are created at runtime by
// TenantProvisioningService, which must pass the SKU clause emitted as
// newDatabaseSqlOptions - a bare CREATE DATABASE lands on General Purpose.
@description('Azure region for all resources.')
param location string

@description('Globally unique SQL logical server name.')
param sqlServerName string

@description('Tags applied to every resource.')
param tags object

@description('Entra ID group/user object id that becomes the SQL admin.')
param sqlAdminObjectId string

@description('Display name of the Entra admin principal.')
param sqlAdminLogin string

@description('Entra principal type of the SQL admin.')
@allowed(['User', 'Group', 'Application'])
param sqlAdminPrincipalType string

@description('Reach SQL over a private endpoint instead of the public endpoint.')
param enablePrivateNetworking bool

@description('Place databases in an elastic pool. Cheaper past ~15 tenants.')
param useElasticPool bool

@description('Sku for standalone databases when not pooled.')
param databaseSkuName string

@description('Tier for standalone databases when not pooled.')
param databaseSkuTier string

@description('Elastic pool sku name, e.g. BasicPool or StandardPool.')
param elasticPoolSkuName string

@description('Elastic pool tier, e.g. Basic or Standard.')
param elasticPoolTier string

@description('Elastic pool capacity in eDTU (DTU model) or vCores (vCore model).')
param elasticPoolCapacity int

@description('Max eDTU/vCores any single tenant database may consume.')
param perDatabaseMaxCapacity int

@description('Subnet that hosts the private endpoint. Ignored when public.')
param privateEndpointSubnetId string

@description('Private DNS zone for privatelink SQL records. Ignored when public.')
param sqlPrivateDnsZoneId string

var elasticPoolName = '${sqlServerName}-pool'
var elasticPoolId = resourceId('Microsoft.Sql/servers/elasticPools', sqlServerName, elasticPoolName)

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: enablePrivateNetworking ? 'Disabled' : 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      principalType: sqlAdminPrincipalType
      tenantId: subscription().tenantId
      // No SQL logins at all - kills the sa/password class of problem.
      azureADOnlyAuthentication: true
    }
  }
}

// Container Apps Consumption has no stable outbound IP, so the public path can
// only be narrowed to "Azure services". Entra-only auth above is what actually
// guards the data.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!enablePrivateNetworking) {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource elasticPool 'Microsoft.Sql/servers/elasticPools@2023-08-01-preview' = if (useElasticPool) {
  parent: sqlServer
  name: elasticPoolName
  location: location
  tags: tags
  sku: {
    name: elasticPoolSkuName
    tier: elasticPoolTier
    capacity: elasticPoolCapacity
  }
  properties: {
    perDatabaseSettings: {
      minCapacity: 0
      maxCapacity: perDatabaseMaxCapacity
    }
  }
}

resource controlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'SmmsControlDb'
  location: location
  tags: tags
  sku: useElasticPool
    ? null
    : {
        name: databaseSkuName
        tier: databaseSkuTier
      }
  properties: {
    elasticPoolId: useElasticPool ? elasticPoolId : null
  }
  dependsOn: useElasticPool ? [elasticPool] : []
}

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = if (enablePrivateNetworking) {
  name: '${sqlServerName}-pe'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${sqlServerName}-conn'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource sqlDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = if (enablePrivateNetworking) {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'sql'
        properties: {
          privateDnsZoneId: sqlPrivateDnsZoneId
        }
      }
    ]
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerId string = sqlServer.id
output elasticPoolName string = useElasticPool ? elasticPoolName : ''
// Appended to CREATE DATABASE by the tenant provisioner so new tenant databases
// never silently default to a General Purpose SKU.
output newDatabaseSqlOptions string = useElasticPool
  ? '(SERVICE_OBJECTIVE = ELASTIC_POOL(name = [${elasticPoolName}]))'
  : '(EDITION = \'${databaseSkuTier}\', SERVICE_OBJECTIVE = \'${databaseSkuName}\')'
