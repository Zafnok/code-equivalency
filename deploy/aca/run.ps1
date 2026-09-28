#!/usr/bin/env pwsh
<#
M6-001: run one sample through the deployed job. Writes the sample's name to the share, starts one
execution (`equiv compare --legacy /mnt/work/samples/<name>/legacy/*.sln --modern .../modern/*.slnx
--out /mnt/work/out/<name>.sarif`, see the script in main.bicep), waits for it to finish, and downloads
<name>.sarif, <name>.log and <name>.exit into -OutDir. Also writes checkout.txt and exit-codes.json there,
so `.github/scripts/sarif-parity.ps1 -Left <OutDir> -Right <CI's parity-Linux artifact>` can diff it
against the Linux CI leg (M3-029) for the same version.
#>
param(
    [Parameter(Mandatory)] [string]$Sample,
    [string]$Subscription,
    [string]$ResourceGroup = 'equiv-aca',
    [string]$OutDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'equiv-aca-out'),
    [int]$TimeoutMinutes = 35
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

function Get-Az {
    $value = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed with exit code $LASTEXITCODE" }
    return ($value | Out-String).Trim()
}

if ($Subscription) { Invoke-Az account set --subscription $Subscription }

# `az containerapp job` lives in an extension.
Invoke-Az extension add --name containerapp --upgrade --only-show-errors

$job = 'equiv-compare'
$share = 'work'
$account = Get-Az storage account list --resource-group $ResourceGroup --query '[0].name' -o tsv
if (-not $account) { throw "no storage account in resource group '$ResourceGroup'; run deploy.ps1 first" }
$env:AZURE_STORAGE_ACCOUNT = $account
$env:AZURE_STORAGE_KEY = Get-Az storage account keys list --resource-group $ResourceGroup --account-name $account --query '[0].value' -o tsv

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Drop the previous run's outputs so a stale SARIF cannot pass for this one. A missing file is fine.
$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
foreach ($extension in 'sarif', 'log', 'exit') {
    az storage file delete --share-name $share --path "out/$Sample.$extension" --only-show-errors 2>&1 | Out-Null
}
$ErrorActionPreference = $previous

$request = Join-Path $OutDir 'sample'
Set-Content -Path $request -Value $Sample -NoNewline -Encoding ascii
Invoke-Az storage file upload --share-name $share --source $request --path request/sample --only-show-errors -o none

$execution = Get-Az containerapp job start --name $job --resource-group $ResourceGroup --query name -o tsv
Write-Host "Started $execution for '$Sample'."

$terminal = 'Succeeded', 'Failed', 'Stopped', 'Degraded'
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
do {
    Start-Sleep -Seconds 10
    $state = Get-Az containerapp job execution show --name $job --resource-group $ResourceGroup `
        --job-execution-name $execution --query properties -o json | ConvertFrom-Json
    Write-Host "  $($state.status)"
} while ($terminal -notcontains $state.status -and (Get-Date) -lt $deadline)
if ($terminal -notcontains $state.status) { throw "execution $execution did not finish within $TimeoutMinutes minutes (last status: $($state.status))" }

$duration = 'unknown'
if ($state.startTime -and $state.endTime) { $duration = '{0:N0} s' -f ([datetimeoffset]$state.endTime - [datetimeoffset]$state.startTime).TotalSeconds }
Write-Host "Execution $execution finished: $($state.status), duration $duration."

# The log and exit code are written even when the job fails, so fetch what exists and say what is missing.
$exitCode = $null
foreach ($extension in 'log', 'exit', 'sarif') {
    $local = Join-Path $OutDir "$Sample.$extension"
    Remove-Item -LiteralPath $local -ErrorAction SilentlyContinue
    $ErrorActionPreference = 'Continue'
    az storage file download --share-name $share --path "out/$Sample.$extension" --dest $local --only-show-errors -o none 2>&1 | Out-Null
    $downloaded = $LASTEXITCODE -eq 0
    $ErrorActionPreference = $previous
    if (-not $downloaded) { Write-Host "  no $Sample.$extension on the share"; continue }
    if ($extension -eq 'exit') { $exitCode = [int](Get-Content -LiteralPath $local -Raw).Trim() }
}

# What sarif-parity.ps1 needs beside the SARIF: the root to normalise away and each sample's exit code.
Set-Content -Path (Join-Path $OutDir 'checkout.txt') -Value '/mnt/work' -Encoding ascii
$exitFile = Join-Path $OutDir 'exit-codes.json'
$codes = [ordered]@{}
if (Test-Path -LiteralPath $exitFile) {
    (Get-Content -LiteralPath $exitFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $codes[$_.Name] = $_.Value }
}
$codes[$Sample] = $exitCode
$codes | ConvertTo-Json | Set-Content -Path $exitFile -Encoding utf8

Write-Host "equiv exit code: $exitCode. Outputs in $OutDir"
if (-not (Test-Path -LiteralPath (Join-Path $OutDir "$Sample.sarif"))) { throw "no SARIF for '$Sample'; see $Sample.log" }
