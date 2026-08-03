using 'main.bicep'

param environmentName = 'prod'
param location = 'centralindia'

// Supplied at deploy time so no tenant-specific id or secret lands in git.
// $env:SMMS_SQL_ADMIN_OBJECT_ID = az ad group show --group "SMMS SQL Admins" --query id -o tsv
param sqlAdminObjectId = readEnvironmentVariable('SMMS_SQL_ADMIN_OBJECT_ID')
param sqlAdminLogin = 'SMMS SQL Admins'
param sqlAdminPrincipalType = 'Group'
param jwtSigningKey = readEnvironmentVariable('SMMS_JWT_SIGNING_KEY')

// Production pays for a private endpoint and keeps one replica warm.
param enablePrivateNetworking = true
param minReplicas = 1
param maxReplicas = 5
param containerCpu = '0.5'
param containerMemory = '1Gi'

// Individual Basic databases until ~15 tenants, then flip useElasticPool.
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
