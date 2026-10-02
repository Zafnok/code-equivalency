using 'main.bicep'

// deploy.ps1 sets these environment variables (az cannot mix a .bicepparam file with inline
// --parameters), then deploys this file.
param version = readEnvironmentVariable('EQUIV_VERSION')
param ownerEmail = readEnvironmentVariable('EQUIV_OWNER_EMAIL')
param imageOwner = readEnvironmentVariable('EQUIV_IMAGE_OWNER', 'zafnok')
