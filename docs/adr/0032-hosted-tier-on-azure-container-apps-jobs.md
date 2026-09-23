# ADR 0032: The hosted tier runs on Azure Container Apps Jobs, not AKS

Status: accepted (2026-09-23)

## Context
ADR 0006 parks the hosted tier and the ROADMAP backlog names AKS for it. The workload is a
batch job: take two solutions, load, verify, write one SARIF log, exit. Nothing serves traffic
between runs. The 2026-09-23 audit asked whether AKS is still the right target.

## Decision
The hosted tier runs the same OCI image as the free tier as Azure Container Apps Jobs: one job
execution per comparison, event-triggered from a queue, scaling to zero between runs. Inputs and
the SARIF output move through blob storage. Nothing in the engine may assume a particular host;
the image stays a plain OCI image that runs unchanged on AKS or any Kubernetes.

## Why
- Jobs are the native shape of the workload: per-execution timeouts and retries, queue-driven
  scaling (KEDA built in), per-second billing, no cluster to patch or upgrade.
- It is Kubernetes underneath, so moving to AKS later is a redeploy of the same image, not a port.
- A small team gets no value from owning node pools, ingress and upgrades.

## Rejected
- AKS: right when we need node-level control (Windows nodes, GPUs, very large nodes); none needed
  yet, and it costs cluster operations from day one.
- Azure Functions / serverless code: runs are long and memory-heavy (Roslyn + Z3), and the
  container is already the unit we ship.
- Azure Batch: fits batch compute, but is VM-pool oriented and adds a second deployment shape.

## Consequences
- Container Apps is Linux-only, so ADR 0031's parity requirement is a hard prerequisite for
  hosting. If M3-028 forces ADR 0031's Windows-worker fallback, that worker cannot run here; it
  would need a Windows host outside Container Apps (AKS Windows node pool or a Windows VM pool)
  and a new ADR.
- The consumption profile caps a container at 4 vCPU / 8 GiB; large solutions need a dedicated
  workload profile. The first corpus run (M4-007) should record peak memory per pair.
- The hosted tier is still post-MVP and unticketed; the ROADMAP backlog line changes from AKS to
  Container Apps Jobs. An MCP endpoint for hosted use is ADR 0033's HTTP follow-up.
