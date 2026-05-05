targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment, used to generate a unique resource token.')
param environmentName string

@minLength(1)
@description('Primary location for all resources.')
@allowed([
  'australiaeast'
  'australiasoutheast'
  'brazilsouth'
  'canadacentral'
  'centralindia'
  'centralus'
  'eastasia'
  'eastus'
  'eastus2'
  'francecentral'
  'germanywestcentral'
  'japaneast'
  'koreacentral'
  'northeurope'
  'southcentralus'
  'southeastasia'
  'swedencentral'
  'uksouth'
  'ukwest'
  'westeurope'
  'westus2'
  'westus3'
])
@metadata({
  azd: {
    type: 'location'
  }
})
param location string

param resourceGroupName string = ''
param storageAccountName string = ''
param appServicePlanName string = ''
param applicationInsightsName string = ''
param logAnalyticsName string = ''
param functionAppName string = ''
param userAssignedIdentityName string = ''

@description('Id of the deploying user, for local storage access during development.')
param principalId string = deployer().objectId

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = { 'azd-env-name': environmentName }
var serviceName = 'mcp-demo'
var resolvedFunctionAppName = !empty(functionAppName) ? functionAppName : '${abbrs.webSitesFunctions}${serviceName}-${resourceToken}'
var deploymentStorageContainerName = 'app-package-${take(resolvedFunctionAppName, 32)}-${take(toLower(uniqueString(resolvedFunctionAppName, resourceToken)), 7)}'

resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: !empty(resourceGroupName) ? resourceGroupName : '${abbrs.resourcesResourceGroups}${environmentName}'
  location: location
  tags: tags
}

module userAssignedIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.4.1' = {
  name: 'userAssignedIdentity'
  scope: rg
  params: {
    location: location
    tags: tags
    name: !empty(userAssignedIdentityName) ? userAssignedIdentityName : '${abbrs.managedIdentityUserAssignedIdentities}${serviceName}-${resourceToken}'
  }
}

module appServicePlan 'br/public:avm/res/web/serverfarm:0.1.1' = {
  name: 'appserviceplan'
  scope: rg
  params: {
    name: !empty(appServicePlanName) ? appServicePlanName : '${abbrs.webServerFarms}${resourceToken}'
    sku: {
      name: 'FC1'
      tier: 'FlexConsumption'
    }
    reserved: true
    location: location
    tags: tags
  }
}

module storage 'br/public:avm/res/storage/storage-account:0.8.3' = {
  name: 'storage'
  scope: rg
  params: {
    name: !empty(storageAccountName) ? storageAccountName : '${abbrs.storageStorageAccounts}${resourceToken}'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    dnsEndpointType: 'Standard'
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
    blobServices: {
      containers: [
        { name: deploymentStorageContainerName }
      ]
    }
    minimumTlsVersion: 'TLS1_2'
    location: location
    tags: tags
    skuName: 'Standard_LRS'
  }
}

module logAnalytics 'br/public:avm/res/operational-insights/workspace:0.11.1' = {
  name: 'loganalytics'
  scope: rg
  params: {
    name: !empty(logAnalyticsName) ? logAnalyticsName : '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    location: location
    tags: tags
    dataRetention: 30
  }
}

module monitoring 'br/public:avm/res/insights/component:0.6.0' = {
  name: 'appinsights'
  scope: rg
  params: {
    name: !empty(applicationInsightsName) ? applicationInsightsName : '${abbrs.insightsComponents}${resourceToken}'
    location: location
    tags: tags
    workspaceResourceId: logAnalytics.outputs.resourceId
    disableLocalAuth: true
  }
}

module functionApp './app/api.bicep' = {
  name: 'functionapp'
  scope: rg
  params: {
    name: resolvedFunctionAppName
    serviceName: serviceName
    location: location
    tags: tags
    applicationInsightsName: monitoring.outputs.name
    appServicePlanId: appServicePlan.outputs.resourceId
    runtimeName: 'dotnet-isolated'
    runtimeVersion: '10.0'
    storageAccountName: storage.outputs.name
    deploymentStorageContainerName: deploymentStorageContainerName
    enableBlob: true
    enableQueue: true
    identityId: userAssignedIdentity.outputs.resourceId
    identityClientId: userAssignedIdentity.outputs.clientId
    appSettings: {}
  }
}

module rbac './app/rbac.bicep' = {
  name: 'rbac'
  scope: rg
  params: {
    storageAccountName: storage.outputs.name
    appInsightsName: monitoring.outputs.name
    managedIdentityPrincipalId: userAssignedIdentity.outputs.principalId
    userIdentityPrincipalId: principalId
    enableBlob: true
    enableQueue: true
    allowUserIdentityPrincipal: true
  }
}

output APPLICATIONINSIGHTS_CONNECTION_STRING string = monitoring.outputs.connectionString
output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_FUNCTION_NAME string = functionApp.outputs.SERVICE_API_NAME
output SERVICE_MCP_DEFAULT_HOSTNAME string = functionApp.outputs.SERVICE_MCP_DEFAULT_HOSTNAME
