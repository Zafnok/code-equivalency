#!/usr/bin/env pwsh
<#
M6-001: deploy main.bicep (Container Apps environment, Azure Files share, manual job, $5 budget) into one
resource group, upload samples/ to the share, and print the job name. Run by hand against your own
subscription; nothing in CI calls this. teardown.ps1 removes everything; run.ps1 starts executions.

Needs the Azure CLI (`az login` done). The image ghcr.io/<ImageOwner>/equiv:<Version> must exist and be
public: Container Apps pulls it anonymously, and there is no Container Registry in this deployment.
#>
param(
    [Parameter(Mandatory)] [string]$Subscription,
    [Parameter(Mandatory)] [string]$Location,
    [Parameter(Mandatory)] [string]$Version,
    [string]$ResourceGroup = 'equiv-aca',
    # The budget's 50% and 100% notifications go here. Defaults to the signed-in account when that is an email address.
    [string]$OwnerEmail,
    [string]$ImageOwner = 'zafnok'
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

# az prints the value with -o tsv; capture it and fail loudly on a non-zero exit.
function Get-Az {
    $value = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed with exit code $LASTEXITCODE" }
    return ($value | Out-String).Trim()
}

Invoke-Az account set --subscription $Subscription

if (-not $OwnerEmail) {
    $OwnerEmail = Get-Az account show --query user.name -o tsv
    if ($OwnerEmail -notmatch '^[^@\s]+@[^@\s]+$') { throw "pass -OwnerEmail: the signed-in account '$OwnerEmail' is not an email address" }
}

# A new subscription has neither provider registered; registering is idempotent.
foreach ($provider in 'Microsoft.App', 'Microsoft.Storage', 'Microsoft.Consumption') {
    Invoke-Az provider register --namespace $provider --wait --only-show-errors
}

Invoke-Az group create --name $ResourceGroup --location $Location --only-show-errors -o none

$env:EQUIV_VERSION = $Version
$env:EQUIV_OWNER_EMAIL = $OwnerEmail
$env:EQUIV_IMAGE_OWNER = $ImageOwner
$outputs = Get-Az deployment group create --resource-group $ResourceGroup --name equiv-aca `
    --parameters (Join-Path $PSScriptRoot 'main.bicepparam') --query properties.outputs -o json --only-show-errors |
    ConvertFrom-Json

$env:AZURE_STORAGE_ACCOUNT = $outputs.storageAccountName.value
$env:AZURE_STORAGE_KEY = Get-Az storage account keys list --resource-group $ResourceGroup `
    --account-name $outputs.storageAccountName.value --query '[0].value' -o tsv
$share = $outputs.shareName.value

# Upload a copy without bin/ and obj/: a restore done on this machine records Windows paths in
# project.assets.json, and the job restores every project itself on Linux.
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$samples = Join-Path $repoRoot 'samples'
$stage = Join-Path ([System.IO.Path]::GetTempPath()) "equiv-aca-samples-$([guid]::NewGuid().ToString('N'))"
try {
    Get-ChildItem -Path $samples -Recurse -File |
        Where-Object { $_.FullName.Substring($samples.Length) -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $target = Join-Path $stage $_.FullName.Substring($samples.Length).TrimStart('\', '/')
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target
        }
    Invoke-Az storage directory create --share-name $share --name samples --only-show-errors -o none
    Invoke-Az storage directory create --share-name $share --name request --only-show-errors -o none
    Invoke-Az storage file upload-batch --destination $share --destination-path samples --source $stage --only-show-errors -o none
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $stage -ErrorAction SilentlyContinue
}

Write-Host "Deployed equiv $Version to resource group '$ResourceGroup' ($Location)."
Write-Host "Job: $($outputs.jobName.value)"
Write-Host "Next: ./run.ps1 -Sample identical   (and ./teardown.ps1 when done)"
