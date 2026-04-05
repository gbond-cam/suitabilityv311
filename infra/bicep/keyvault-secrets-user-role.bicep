@description('Name of the Key Vault')
param keyVaultName string

@description('Object ID of the managed identity')
param principalObjectId string

@description('Role definition ID for Key Vault Secrets User')
param roleDefinitionId string = '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User

resource kv 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, principalObjectId, roleDefinitionId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      roleDefinitionId
    )
    principalId: principalObjectId
    principalType: 'ServicePrincipal'
  }
}
