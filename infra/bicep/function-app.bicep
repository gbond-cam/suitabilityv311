// Bicep module for deploying a single Azure Function App with required roles and diagnostics
param environmentName string
param location string

var functionAppName = '${environmentName}-auditlineage'

resource functionApp 'Microsoft.Web/sites@2022-09-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: '' // Add App Service Plan if needed
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: reference(appInsightsResourceId, '2020-02-02').InstrumentationKey
        }
      ]
    }
  }
  tags: {
    'azd-service-name': 'auditlineage'
  }
}

// Diagnostic Settings
resource diag 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${functionAppName}-diagnostics'
  scope: functionApp
  properties: {
    workspaceId: logAnalyticsWorkspaceResourceId
    logs: [
      {
        category: 'FunctionAppLogs'
        enabled: true
      }
    ]
  }
}

// Role Assignments for Managed Identity
  name: guid(functionApp.id, 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
  scope: storageAccountResourceId
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
  name: guid(functionApp.id, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: storageAccountResourceId
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
  name: guid(functionApp.id, '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
  scope: storageAccountResourceId
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
  name: guid(functionApp.id, '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
  scope: storageAccountResourceId
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
  name: guid(functionApp.id, '3913510d-42f4-4e42-8a64-420c390055eb')
  scope: storageAccountResourceId
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '3913510d-42f4-4e42-8a64-420c390055eb')
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}
