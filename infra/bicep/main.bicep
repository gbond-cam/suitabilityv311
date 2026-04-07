// Main Bicep file for SuitabilityWriter.v3 Azure deployment
// This file provisions all required resources for the solution

param environmentName string
param location string = resourceGroup().location
param userAssignedIdentityName string = 'suitability-identity'

// Resource Group Tag


// User-Assigned Managed Identity
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: userAssignedIdentityName
  location: location
}

// Storage Account
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: uniqueString(resourceGroup().id, environmentName, 'storage')
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
  tags: {
    'azd-service-name': 'storage'
  }
}

// Application Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${environmentName}-appinsights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
  tags: {
    'azd-service-name': 'appinsights'
  }
}

// Log Analytics Workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${environmentName}-loganalytics'
  location: location
  sku: {
    name: 'PerGB2018'
  }
  properties: {}
  tags: {
    'azd-service-name': 'loganalytics'
  }
}

// Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: '${environmentName}-kv-${uniqueString(resourceGroup().id, environmentName, 'kv')}'
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enablePurgeProtection: true
    enableSoftDelete: true
    accessPolicies: []
    enabledForDeployment: true
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: true
  }
  tags: {
    'azd-service-name': 'keyvault'
  }
}

module auditLineage 'function-app.bicep' = {
  name: 'auditLineage'
  params: {
    environmentName: environmentName
    location: location
    userAssignedIdentityResourceId: identity.id
    userAssignedIdentityPrincipalId: identity.properties.principalId

    storageAccountName: storage.name
    appInsightsInstrumentationKey: appInsights.properties.InstrumentationKey
    logAnalyticsWorkspaceResourceId: logAnalytics.id
  }
}
// Repeat for other services as needed
