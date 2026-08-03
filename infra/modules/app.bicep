@description('Azure region for all resources.')
param location string

@description('Prefix applied to every resource name.')
param namePrefix string

@description('Tags applied to every resource.')
param tags object

@description('App Service plan sku. Slots require Standard or better.')
param appServicePlanSku string

@description('Resource id of the user-assigned identity the app runs as.')
param identityId string

@description('Client id of the user-assigned identity, for DefaultAzureCredential.')
param identityClientId string

@description('Container registry login server.')
param acrLoginServer string

@description('Container image repository name.')
param imageName string

@description('Container image tag.')
param imageTag string

@description('Subnet delegated to App Service for VNet integration.')
param appSubnetId string

@description('Fully qualified domain name of the SQL logical server.')
param sqlServerFqdn string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Key Vault name holding the JWT signing key.')
param keyVaultName string

@description('Base domain that tenant subdomains hang off.')
param tenantBaseDomain string

var tenantConnectionTemplate = 'Server=tcp:${sqlServerFqdn},1433;Database={DbName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var controlConnectionString = replace(tenantConnectionTemplate, '{DbName}', 'SmmsControlDb')

var baseAppSettings = [
  {
    // The container listens on 8080; App Service must forward to it.
    name: 'WEBSITES_PORT'
    value: '8080'
  }
  {
    // Tells DefaultAzureCredential which user-assigned identity to use.
    name: 'AZURE_CLIENT_ID'
    value: identityClientId
  }
  {
    name: 'ControlPlane__ConnectionString'
    value: controlConnectionString
  }
  {
    name: 'ControlPlane__TenantConnectionTemplate'
    value: tenantConnectionTemplate
  }
  {
    name: 'ControlPlane__TenantBaseDomain'
    value: tenantBaseDomain
  }
  {
    name: 'Jwt__Key'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=jwt-signing-key)'
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsightsConnectionString
  }
]

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${namePrefix}-plan'
  location: location
  tags: tags
  sku: {
    name: appServicePlanSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: '${namePrefix}-api'
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: appSubnetId
    siteConfig: {
      linuxFxVersion: 'DOCKER|${acrLoginServer}/${imageName}:${imageTag}'
      acrUseManagedIdentityCreds: true
      acrUserManagedIdentityID: identityClientId
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/api/health'
      // Route outbound traffic through the VNet so SQL resolves privately.
      vnetRouteAllEnabled: true
      appSettings: baseAppSettings
    }
  }
}

resource stagingSlot 'Microsoft.Web/sites/slots@2023-12-01' = {
  parent: site
  name: 'staging'
  location: location
  tags: tags
  kind: 'app,linux,container'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: appSubnetId
    siteConfig: {
      linuxFxVersion: 'DOCKER|${acrLoginServer}/${imageName}:${imageTag}'
      acrUseManagedIdentityCreds: true
      acrUserManagedIdentityID: identityClientId
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/api/health'
      vnetRouteAllEnabled: true
      appSettings: baseAppSettings
    }
  }
}

output siteName string = site.name
output defaultHostName string = site.properties.defaultHostName
output stagingHostName string = stagingSlot.properties.defaultHostName
