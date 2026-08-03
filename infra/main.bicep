// SMMS target infrastructure: Container Apps + Azure SQL (DB-per-tenant).
// The same template serves dev/test and production; the cost and exposure
// differences are parameters, not separate files. Deploy at resource group scope.
targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short environment name, e.g. prod or test.')
@allowed(['prod', 'test'])
param environmentName string = 'prod'

@description('Prefix applied to every resource name.')
param namePrefix string = 'smms-${environmentName}'

@description('Suffix that makes globally unique names unique.')
param uniqueSuffix string = uniqueString(resourceGroup().id)

@description('Entra ID group/user object id that becomes the SQL admin.')
@minLength(36)
param sqlAdminObjectId string

@description('Display name of the Entra admin principal.')
param sqlAdminLogin string

@description('Entra principal type of the SQL admin.')
@allowed(['User', 'Group', 'Application'])
param sqlAdminPrincipalType string = 'Group'

@description('Reach SQL over a private endpoint. Costs ~INR 610/month; leave off for dev/test.')
param enablePrivateNetworking bool = false

@description('Place databases in an elastic pool. Cheaper than individual databases past ~15 tenants.')
param useElasticPool bool = false

@description('Sku for standalone databases when not pooled.')
param databaseSkuName string = 'Basic'

@description('Tier for standalone databases when not pooled.')
param databaseSkuTier string = 'Basic'

@description('Elastic pool sku name.')
param elasticPoolSkuName string = 'BasicPool'

@description('Elastic pool tier.')
param elasticPoolTier string = 'Basic'

@description('Elastic pool capacity in eDTU.')
param elasticPoolCapacity int = 50

@description('Max eDTU any single tenant database may consume.')
param perDatabaseMaxCapacity int = 5

@description('vCPU per replica.')
param containerCpu string = '0.5'

@description('Memory per replica.')
param containerMemory string = '1Gi'

@description('Minimum replicas. 0 scales to zero between requests; production wants 1.')
@minValue(0)
param minReplicas int = 0

@description('Maximum replicas.')
@minValue(1)
param maxReplicas int = 3

@description('Symmetric key used to sign JWTs. Keep stable across deployments or every user is logged out.')
@secure()
@minLength(32)
param jwtSigningKey string

@description('Container image repository name.')
param imageName string = 'smms-api'

@description('Container image tag to run.')
param imageTag string = 'latest'

@description('Base domain that tenant subdomains hang off.')
param tenantBaseDomain string = 'ssms.yuvaansoft.shop'

var tags = {
  application: 'SMMS'
  environment: environmentName
  managedBy: 'bicep'
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id'
  location: location
  tags: tags
}

module network 'modules/network.bicep' = {
  name: 'network'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    enablePrivateNetworking: enablePrivateNetworking
  }
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
  }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    location: location
    registryName: 'smms${environmentName}acr${uniqueSuffix}'
    tags: tags
    pullPrincipalId: identity.properties.principalId
  }
}

module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    keyVaultName: 'smms-${environmentName}-kv-${take(uniqueSuffix, 8)}' // vault names cap at 24 chars
    tags: tags
    readerPrincipalId: identity.properties.principalId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    sqlServerName: 'smms-${environmentName}-sql-${uniqueSuffix}'
    tags: tags
    sqlAdminObjectId: sqlAdminObjectId
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPrincipalType: sqlAdminPrincipalType
    enablePrivateNetworking: enablePrivateNetworking
    useElasticPool: useElasticPool
    databaseSkuName: databaseSkuName
    databaseSkuTier: databaseSkuTier
    elasticPoolSkuName: elasticPoolSkuName
    elasticPoolTier: elasticPoolTier
    elasticPoolCapacity: elasticPoolCapacity
    perDatabaseMaxCapacity: perDatabaseMaxCapacity
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    sqlPrivateDnsZoneId: network.outputs.sqlPrivateDnsZoneId
  }
}

module app 'modules/containerapp.bicep' = {
  name: 'app'
  params: {
    location: location
    namePrefix: namePrefix
    tags: tags
    identityId: identity.id
    identityClientId: identity.properties.clientId
    acrLoginServer: registry.outputs.loginServer
    imageName: imageName
    imageTag: imageTag
    infrastructureSubnetId: network.outputs.infrastructureSubnetId
    logAnalyticsWorkspaceId: monitoring.outputs.workspaceId
    sqlServerFqdn: sql.outputs.sqlServerFqdn
    newDatabaseSqlOptions: sql.outputs.newDatabaseSqlOptions
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    tenantBaseDomain: tenantBaseDomain
    jwtSigningKey: jwtSigningKey
    containerCpu: containerCpu
    containerMemory: containerMemory
    minReplicas: minReplicas
    maxReplicas: maxReplicas
  }
}

output acrLoginServer string = registry.outputs.loginServer
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output elasticPoolName string = sql.outputs.elasticPoolName
output newDatabaseSqlOptions string = sql.outputs.newDatabaseSqlOptions
output keyVaultName string = keyvault.outputs.vaultName
output identityName string = identity.name
output identityPrincipalId string = identity.properties.principalId
output containerEnvironmentName string = app.outputs.environmentName
output appName string = app.outputs.appName
output appFqdn string = app.outputs.appFqdn
output environmentStaticIp string = app.outputs.staticIp
