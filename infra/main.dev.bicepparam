using 'main.bicep'

param environmentName = 'test'
param location = 'centralindia'

// Supplied at deploy time so no tenant-specific id or secret lands in git.
// $env:SMMS_SQL_ADMIN_OBJECT_ID = az ad group show --group "SMMS SQL Admins" --query id -o tsv
param sqlAdminObjectId = readEnvironmentVariable('SMMS_SQL_ADMIN_OBJECT_ID')
param sqlAdminLogin = 'SMMS SQL Admins'
param sqlAdminPrincipalType = 'Group'
param jwtSigningKey = readEnvironmentVariable('SMMS_JWT_SIGNING_KEY')

// No private endpoint and scale to zero: the two biggest fixed costs removed.
// Trade-off is a cold start of roughly 5-15 seconds on the first request.
param enablePrivateNetworking = false
param minReplicas = 0
param maxReplicas = 2
param containerCpu = '0.5'
param containerMemory = '1Gi'

param useElasticPool = false
param databaseSkuName = 'Basic'
param databaseSkuTier = 'Basic'
param elasticPoolSkuName = 'BasicPool'
param elasticPoolTier = 'Basic'
param elasticPoolCapacity = 50
param perDatabaseMaxCapacity = 5

param imageName = 'smms-api'
param imageTag = 'latest'
param tenantBaseDomain = 'ssms.yuvaansoft.shop'
