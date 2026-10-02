// M6-001 / ADR 0032: the released equiv image as a manually started Container Apps Job, inside
// the Consumption plan's monthly free grant. One resource group, one deployment, one teardown.
// Nothing here costs money outside the grant beyond storage cents: no Container Registry (the
// image comes from public GHCR), no VNet, no Dedicated workload profile, no Log Analytics.
targetScope = 'resourceGroup'

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('GHCR namespace that publishes equiv (lower case). The image is ghcr.io/<imageOwner>/equiv:<version>.')
param imageOwner string = 'zafnok'

@description('Release version to run, without the leading v (release.yml tags the image with it).')
param version string

@description('Email address the budget notifies at 50% and 100% of actual spend.')
param ownerEmail string

@description('First day of the budget period. Budgets must start on the first of a month; the default is the current month.')
param budgetStartDate string = '${utcNow('yyyy-MM')}-01T00:00:00Z'

// The 1 vCPU / 2 GiB the ticket sizes the free-grant arithmetic on: 50 hours of execution a month.
var jobCpu = '1'
var jobMemory = '2Gi'
// The Azure Files share appears here inside the container; the job reads samples from
// /mnt/work/samples and writes SARIF to /mnt/work/out.
var mountPath = '/mnt/work'
var shareName = 'work'
var workloadProfileName = 'Consumption'

// M3-004's image runs as the base image's `app` user (uid/gid 1654). SMB has no per-file owner, so
// the mount options present every file as owned by that user and writable. nobrl turns off byte-range
// locks, which MSBuild's obj/ writes do not need and Azure Files does not always honour.
var mountOptions = 'uid=1654,gid=1654,file_mode=0777,dir_mode=0777,nobrl'

// The job's whole command, one bash script. run.ps1 puts the sample's name in /mnt/work/request/sample.txt
// before starting an execution (a manual execution takes no parameters without re-declaring the
// container, and the share is already mounted). The exit code goes to <sample>.exit: 0, 1 (Divergent)
// and 4 (a skipped project) still write SARIF and are not failures, as in parity-run.ps1.
// replace() strips the CRs a Windows checkout would add to the literal below, which bash would choke on.
var script = replace('''
set -u
sample=$(tr -d '\r\n' < /mnt/work/request/sample.txt)
root=/mnt/work/samples/$sample
out=/mnt/work/out
mkdir -p "$out"
exec > >(tee "$out/$sample.log") 2>&1
echo "sample=$sample start=$(date -u +%FT%TZ)"
for project in "$root"/legacy/*.csproj "$root"/modern/*.csproj; do
  dotnet restore "$project" || exit $?
done
dotnet /app/Equiv.Cli.dll compare --legacy "$root"/legacy/*.sln --modern "$root"/modern/*.slnx --out "$out/$sample.sarif"
code=$?
echo "$code" > "$out/$sample.exit"
# Azure samples memory once a minute, which misses most short executions; the cgroup's own
# high-water mark is exact (v2 memory.peak, else v1 max_usage_in_bytes).
peak=$(cat /sys/fs/cgroup/memory.peak 2>/dev/null || cat /sys/fs/cgroup/memory/memory.max_usage_in_bytes 2>/dev/null || echo unknown)
echo "exit=$code end=$(date -u +%FT%TZ) peak_memory_bytes=$peak"
sleep 1 # let the tee above flush the last lines
case $code in 0|1|4) exit 0 ;; *) exit "$code" ;; esac
''', '\r', '')

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'equivaca${uniqueString(resourceGroup().id)}'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  // No role uses it yet; declared so the account is ready for identity-based access (Sonar S6378).
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    // Microsoft-managed keys, plus the free second layer of infrastructure encryption (Sonar S6388).
    encryption: {
      keySource: 'Microsoft.Storage'
      requireInfrastructureEncryption: true
      services: {
        blob: { enabled: true, keyType: 'Account' }
        file: { enabled: true, keyType: 'Account' }
      }
    }
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    // The environment mounts the share with the account key, so shared-key access has to stay on.
    allowSharedKeyAccess: true
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource share 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: shareName
  properties: {
    // GiB. A few MB of samples plus SARIF and logs.
    shareQuota: 1
    enabledProtocols: 'SMB'
  }
}

// Workload-profiles environment with only the Consumption profile: a Consumption-only environment
// caps a replica at 2 vCPU / 4 GiB, this one allows 4 / 8 GiB, and it is still billed as Consumption.
// No appLogsConfiguration: logs stay off (a Log Analytics workspace is a resource type this ticket
// does not deploy); the job tees its own output to the share instead.
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'equiv-env'
  location: location
  properties: {
    workloadProfiles: [
      {
        name: workloadProfileName
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

resource environmentStorage 'Microsoft.App/managedEnvironments/storages@2024-03-01' = {
  parent: environment
  name: 'work'
  properties: {
    azureFile: {
      accountName: storage.name
      accountKey: storage.listKeys().keys[0].value
      shareName: share.name
      accessMode: 'ReadWrite'
    }
  }
}

resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: 'equiv-compare'
  location: location
  // The image is public and the share mounts with the environment's storage key, so nothing uses
  // this yet; it is what the queue trigger and blob inputs after M6-001 will authenticate with (Sonar S6378).
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: workloadProfileName
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'equiv'
          image: 'ghcr.io/${imageOwner}/equiv:${version}'
          command: [ '/bin/bash' ]
          args: [ '-c', script ]
          resources: {
            cpu: json(jobCpu)
            memory: jobMemory
          }
          env: [
            // The image's HOME may not exist for uid 1654; NuGet and the .NET CLI write under it.
            { name: 'HOME', value: '/tmp' }
            { name: 'DOTNET_NOLOGO', value: '1' }
            { name: 'DOTNET_CLI_TELEMETRY_OPTOUT', value: '1' }
          ]
          volumeMounts: [
            {
              volumeName: 'work'
              mountPath: mountPath
            }
          ]
        }
      ]
      volumes: [
        {
          name: 'work'
          storageType: 'AzureFile'
          storageName: environmentStorage.name
          mountOptions: mountOptions
        }
      ]
    }
  }
}

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'equiv-aca-monthly'
  properties: {
    category: 'Cost'
    amount: 5
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actual50: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [ ownerEmail ]
      }
      actual100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [ ownerEmail ]
      }
    }
  }
}

output jobName string = job.name
output storageAccountName string = storage.name
output shareName string = share.name
