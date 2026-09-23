# M6-001 Run the release image as a Container Apps Job, inside the free grant
Status: todo
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
