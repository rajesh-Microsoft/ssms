// Container Apps replacement for the App Service plan. Consumption workload
// profile only - the Dedicated profile carries a ~INR 6,890/month management
// fee that this workload cannot justify.
@description('Azure region for all resources.')
param location string

@description('Prefix applied to every resource name.')
param namePrefix string

@description('Tags applied to every resource.')
param tags object

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

@description('Subnet delegated to Microsoft.App/environments.')
param infrastructureSubnetId string

@description('Resource id of the Log Analytics workspace that receives console logs.')
param logAnalyticsWorkspaceId string

@description('Fully qualified domain name of the SQL logical server.')
param sqlServerFqdn string

@description('SKU clause appended to CREATE DATABASE for new tenant databases.')
param newDatabaseSqlOptions string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Base domain that tenant subdomains hang off.')
param tenantBaseDomain string

@description('Symmetric key used to sign JWTs. Keep this stable across deployments or every user is logged out.')
@secure()
param jwtSigningKey string

@description('vCPU per replica. Must pair with containerMemory, e.g. 0.5 with 1Gi.')
param containerCpu string

@description('Memory per replica, e.g. 1Gi.')
param containerMemory string

@description('Minimum replicas. 0 lets dev/test scale to zero; production wants 1.')
@minValue(0)
param minReplicas int

@description('Maximum replicas.')
@minValue(1)
param maxReplicas int

var tenantConnectionTemplate = 'Server=tcp:${sqlServerFqdn},1433;Database={DbName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
var controlConnectionString = replace(tenantConnectionTemplate, '{DbName}', 'SmmsControlDb')

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: last(split(logAnalyticsWorkspaceId, '/'))
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnetId
      internal: false
    }
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: [
        {
          server: acrLoginServer
          identity: identityId
        }
      ]
      secrets: [
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrLoginServer}/${imageName}:${imageTag}'
          resources: {
            cpu: json(containerCpu)
            memory: containerMemory
          }
          env: [
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
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
              name: 'ControlPlane__NewDatabaseSqlOptions'
              value: newDatabaseSqlOptions
            }
            {
              name: 'Jwt__Key'
              secretRef: 'jwt-signing-key'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
          ]
          probes: [
            {
              // Generous budget: first boot runs EF migrations for every tenant.
              type: 'Startup'
              httpGet: {
                path: '/api/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 10
              failureThreshold: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/api/health'
                port: 8080
              }
              periodSeconds: 15
              failureThreshold: 3
            }
            {
              // TCP, not /api/health: that endpoint returns 503 when SQL is
              // unreachable, and restarting the container never fixes SQL.
              type: 'Liveness'
              tcpSocket: {
                port: 8080
              }
              periodSeconds: 30
              failureThreshold: 5
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output environmentName string = environment.name
output appName string = api.name
output appFqdn string = api.properties.configuration.ingress.fqdn
output staticIp string = environment.properties.staticIp
