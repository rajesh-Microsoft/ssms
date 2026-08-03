// Network foundation. The VNet is always created (it is free) and hosts the
// Container Apps environment. The private endpoint plumbing for SQL is opt-in
// so dev/test can skip its ~INR 610/month cost.
@description('Azure region for all resources.')
param location string

@description('Prefix applied to every resource name.')
param namePrefix string

@description('Tags applied to every resource.')
param tags object

@description('Create the private DNS zone used to resolve SQL privately.')
param enablePrivateNetworking bool

var vnetName = '${namePrefix}-vnet'
var sqlDnsZoneName = 'privatelink${environment().suffixes.sqlServerHostname}'

resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: ['10.20.0.0/16']
    }
    subnets: [
      {
        // Container Apps needs a delegated subnet of its own. /23 satisfies
        // both Consumption-only and workload-profile environments.
        name: 'snet-infra'
        properties: {
          addressPrefix: '10.20.0.0/23'
          delegations: [
            {
              name: 'containerapps'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'snet-pe'
        properties: {
          addressPrefix: '10.20.2.0/24'
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

resource sqlDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = if (enablePrivateNetworking) {
  name: sqlDnsZoneName
  location: 'global'
  tags: tags
}

resource sqlDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = if (enablePrivateNetworking) {
  parent: sqlDnsZone
  name: 'link-${vnetName}'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

output vnetId string = vnet.id
output infrastructureSubnetId string = vnet.properties.subnets[0].id
output privateEndpointSubnetId string = vnet.properties.subnets[1].id
// resourceId() is a pure string function, so this stays safe to evaluate even
// when the zone is not deployed.
output sqlPrivateDnsZoneId string = enablePrivateNetworking
  ? resourceId('Microsoft.Network/privateDnsZones', sqlDnsZoneName)
  : ''
