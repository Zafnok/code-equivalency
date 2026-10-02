# M6-001 Run the release image as a Container Apps Job, inside the free grant
Status: in-progress
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M3-004

## Goal
Prove ADR 0032 on a real Azure subscription for about $0: the released `equiv` image runs as an
Azure Container Apps Job on the Consumption plan, compares every sample from an Azure Files share,
and writes the SARIF back to it. Everything is created by one Bicep deployment and removed by one
script. No API, queue, keys or quotas yet; the job is started by hand.

## Spec references
ADR 0032; ADR 0031 (the image must load solutions on Linux); M3-004 (GHCR image, which must include a
linux/amd64 variant: Container Apps runs nothing else, and images up to 8 GB).

## Free-grant budget
The Consumption plan's monthly free grant is per subscription: 180,000 vCPU-seconds and 360,000
GiB-seconds (Microsoft Learn, "Billing in Azure Container Apps", checked 2026-09-23). At the job
size below (1 vCPU, 2 GiB) that is 50 hours of execution a month. Nothing else in this ticket may
cost more than cents: the image comes from public GHCR (no Azure Container Registry, whose cheapest
tier is not free), app logs are off or go to Log Analytics under its free ingestion allowance, and
the storage account is Standard LRS with a few MB of samples.

## Acceptance criteria (all must hold; nothing beyond them)
1. `deploy/aca/main.bicep` creates, in one resource group: a workload-profiles Container Apps
   environment that uses only the Consumption profile (a Consumption-only environment caps a
   replica at 2 vCPU / 4 GiB; the Consumption profile allows 4 / 8 GiB), a Standard LRS storage account with an Azure Files share mounted into the
   environment, and a Manual-trigger job running `ghcr.io/<owner>/equiv:<version>` with 1 vCPU,
   2 GiB, a 30-minute replica timeout and no retries. No Container Registry, no VNet, no Dedicated
   workload profile (each of those costs money outside the grant).
2. The same deployment creates a resource-group budget of $5/month that emails the owner at 50%
   and 100% of actual spend.
3. `deploy/aca/deploy.ps1 -Subscription <id> -Location <region> -Version <tag>` deploys, uploads
   `samples/` to the share, and prints the job name. `deploy/aca/run.ps1 -Sample <name>` starts one
   execution with `equiv compare --legacy /mnt/work/samples/<name>/legacy/... --modern ...
   --out /mnt/work/out/<name>.sarif`, waits for it, and downloads the SARIF. `deploy/aca/teardown.ps1`
   deletes the resource group.
4. Running every sample through `run.ps1` gives SARIF equal, after removing timestamps and absolute
   paths, to the Linux CI parity job's output for the same version (M3-029). Notes record each
   execution's duration and the peak memory Azure reports.
5. Notes record the month's metered vCPU-seconds and GiB-seconds after the run from Cost Management
   (both far below the grant) and the actual charge for the run (expected $0.00 plus storage cents).
6. README gains a "Hosted (preview)" section: what M6-001 deploys, the free-grant arithmetic above,
   and the three scripts. It states plainly that it is a test deployment, not the hosted tier.
7. Nothing is deployed from CI. The user runs the scripts against their own subscription; ask before
   the first `deploy.ps1` run.

## Files
`deploy/aca/main.bicep`, `deploy/aca/main.bicepparam`, `deploy/aca/deploy.ps1`, `deploy/aca/run.ps1`,
`deploy/aca/teardown.ps1`, `README.md`, `docs/tickets/M6-001-container-apps-job-free-grant.md` (Notes).

## Tests
No automated tests (no `src/` change). `az bicep build deploy/aca/main.bicep` runs clean, and the
criteria 4 and 5 evidence goes in Notes.

## Size guard
Any change under `src/` or `tests/`, or a resource type not named in criteria 1 and 2, means the
ticket is being turned into the hosted tier: stop.

## Out of scope
Queue triggers, blob inputs, an HTTP API, authentication, API keys, quotas, CI deployment (OIDC),
Dedicated workload profiles, `equiv mcp` over HTTP, multi-region.

## Notes
- Decision (`equiv-decide`): a manual execution takes no parameters unless the job's container is
  re-declared in the start call, and the CLI's override semantics for `--env-vars`/`--command` on
  `az containerapp job start` are not documented precisely enough to rely on offline. So the sample name
  travels through the share: `run.ps1` uploads it to `/mnt/work/request/sample.txt`, and the job's fixed
  command (a bash script inline in `main.bicep`) reads it. The command is still exactly the ticket's
  `equiv compare --legacy /mnt/work/samples/<name>/legacy/*.sln --modern .../modern/*.slnx --out
  /mnt/work/out/<name>.sarif`, preceded by a `dotnet restore` of each sample project (parity-run.ps1 does
  the same before compare; restore output is Linux-specific, so `deploy.ps1` uploads `samples/` without
  `bin/` and `obj/`). The script also tees its output to `out/<name>.log` and writes `out/<name>.exit`,
  because logs are off (no Log Analytics workspace: it is not a resource type criteria 1 and 2 name).
- Decision (`equiv-decide`): `main.bicepparam` reads `EQUIV_VERSION`, `EQUIV_OWNER_EMAIL` and
  `EQUIV_IMAGE_OWNER` from the environment, which `deploy.ps1` sets, because `az deployment group create`
  cannot combine a `.bicepparam` file with inline `--parameters`. `deploy.ps1` gains optional `-OwnerEmail`
  (default: the signed-in account when it is an email address), `-ResourceGroup` (default `equiv-aca`) and
  `-ImageOwner` (default `zafnok`) beyond the three parameters criterion 3 names.
- Decision (`equiv-decide`): the Azure Files mount uses `mountOptions: uid=1654,gid=1654,file_mode=0777,dir_mode=0777,nobrl`.
  M3-004's image runs as the base image's `app` user (uid/gid 1654), and an SMB mount is otherwise owned by
  root, which would fail the `obj/` writes MSBuildWorkspace needs. `HOME=/tmp` is set on the container so
  NuGet and the .NET CLI have a writable home.
- Verified without Azure (2026-09-27): `bicep build deploy/aca/main.bicep` and `bicep build-params
  deploy/aca/main.bicepparam` are clean (Bicep CLI 0.47.16 from the `Azure.Bicep.CommandLine.win-x64` NuGet
  package, unpacked in a scratch directory because this box has no `az`); the embedded bash passes `bash -n`;
  the three scripts parse. Not verified: the deployment itself, the script inside the real image (Docker
  Desktop was not running), and the CLI flags of `az containerapp job start|execution show` and
  `az storage file upload-batch|download`, all written from documentation.
- Unblocked (2026-09-28): the owner approved tagging and deploying. `v0.1.0` was tagged at 6ec8e87 (the last
  main commit with green CI) and `release.yml` published `ghcr.io/zafnok/equiv:0.1.0`, built on
  `ubuntu-latest` (linux/amd64). The package came out public (it inherits the public repository's visibility),
  so an anonymous registry pull of the manifest worked with no manual step. Deployed to `eastus` in
  subscription "Azure subscription 1" with `deploy.ps1 -Version 0.1.0`; `what-if` beforehand listed 7
  resources, all of criteria 1 and 2's types.
- Toolchain: Windows PowerShell 5.1 turns a native command's redirected stderr into error records, which
  `$ErrorActionPreference = 'Stop'` makes fatal mid-command; it killed `az storage file upload-batch` halfway
  on the first deploy. The `az` helpers now set `Continue` locally and decide on the exit code alone.
- Toolchain: `az storage file upload --path request/sample` treats an extensionless last segment as a
  directory and PUTs `request/sample/sample` (404 ParentNotFound). The request file is now `request/sample.txt`.
- Toolchain: Azure Monitor's `UsageBytes` metric for the job is sampled once a minute, so it missed 11 of
  the first 12 ~40 s executions (it reported only `business-layer`, max 366 MiB, a working-set figure that
  includes page cache). The job script now prints the container cgroup's own high-water mark
  (`/sys/fs/cgroup/memory.peak`), which is exact, and all 12 samples were run again.
- Criterion 4 evidence (2026-09-28, second pass, cgroup peak memory; every execution `Succeeded`, and the
  `equiv` exit code equals CI's Linux leg for each sample):

  | Sample | Execution | Duration | equiv exit | Peak memory |
  |---|---|---|---|---|
  | added-branch | equiv-compare-kcvxgij | 37 s | 1 | 289 MiB |
  | added-removed | equiv-compare-t0ziozx | 41 s | 0 | 293 MiB |
  | api-drift | equiv-compare-81uuh5f | 41 s | 1 | 295 MiB |
  | business-layer | equiv-compare-7fzbeyo | 40 s | 1 | 305 MiB |
  | callee-changed | equiv-compare-dpn2np9 | 43 s | 1 | 290 MiB |
  | identical | equiv-compare-ex3f399 | 38 s | 0 | 292 MiB |
  | loop-bound-change | equiv-compare-8cve46g | 37 s | 1 | 290 MiB |
  | loop-fusion | equiv-compare-cg4mw87 | 39 s | 0 | 338 MiB |
  | loop-to-linq | equiv-compare-6k3nk4o | 42 s | 0 | 316 MiB |
  | removed-null-check | equiv-compare-pn78hon | 38 s | 1 | 292 MiB |
  | renamed-locals | equiv-compare-0rk5wu4 | 37 s | 0 | 294 MiB |
  | webapi-basic | equiv-compare-nad3w3l | 47 s | 0 | 339 MiB |

  `.github/scripts/sarif-parity.ps1 -Left <run.ps1 out> -Right <parity-Linux artifact of CI run 36367659165,
  main at 6ec8e87>` prints "All 12 samples match" for both passes. That script is the M3-029 parity diff:
  it compares results and leaves out physical locations, URIs and timestamps. Durations are the execution's
  start to end, most of it `dotnet restore` of the two sample projects; `equiv` itself finishes in about
  7 s. Peak memory stays under 350 MiB against the 2 GiB replica, so the samples leave a lot of headroom
  (real solutions are the M4-007 question).
- Criterion 5 evidence (2026-10-02). Usage, from the executions' own start and end times: 24 executions
  totalling 953 s (473 s first pass, 480 s second), so about 953 vCPU-seconds and 1,906 GiB-seconds at
  1 vCPU / 2 GiB, 0.53% of the monthly grant on each meter. Charge: $0.00. The Cost Management query API
  (`Microsoft.CostManagement/query`, Usage, 2026-09-01 to 2026-10-02, daily, grouped by resource group and
  meter) returns zero rows for the whole subscription, and `Microsoft.Consumption/usageDetails` for
  2026-09-27 to 2026-09-30 returns none. The same was true on every daily check from 2026-09-28 to
  2026-10-02. Deviation: the ticket expected Cost Management to show the metered vCPU-seconds and
  GiB-seconds; it shows nothing, so the usage figures above are the execution times, not Azure's meters.
  The subscription is a Free Trial offer (`quotaId FreeTrial_2014-09-01`, `spendingLimit On`): its usage
  is drawn from the trial credit and nothing can be billed to a card, so the actual charge is $0.00
  either way. The ~1 hour of a Standard LRS account with a few MB is far below a cent. Someone running
  M6-001 on a pay-as-you-go subscription should see the Container Apps meters in Cost analysis instead.
- Teardown (2026-09-28): `teardown.ps1 -Confirm:$false` deleted `equiv-aca` (environment, job, storage
  account and budget) in about 12 minutes; `az group exists --name equiv-aca` then printed `false`.
  Cost Management keeps the usage after deletion, so criterion 5's figures are read from it later.
