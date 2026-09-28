#!/usr/bin/env pwsh
<#
M6-001: delete the resource group deploy.ps1 created, and with it the environment, job, storage account
and budget. Asks for confirmation unless -Confirm:$false is passed.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$Subscription,
    [string]$ResourceGroup = 'equiv-aca'
)

$ErrorActionPreference = 'Stop'

if ($Subscription) {
    az account set --subscription $Subscription
    if ($LASTEXITCODE -ne 0) { throw "az account set failed with exit code $LASTEXITCODE" }
}

if ($PSCmdlet.ShouldProcess("resource group '$ResourceGroup'", 'delete with everything in it')) {
    az group delete --name $ResourceGroup --yes
    if ($LASTEXITCODE -ne 0) { throw "az group delete failed with exit code $LASTEXITCODE" }
    Write-Host "Deleted resource group '$ResourceGroup'."
}
