# ADR 0030: Z3 comes from its official GitHub release nupkg through a hash-pinned local feed

Status: accepted (2026-09-23)

## Context
`Microsoft.Z3` on nuget.org stops at 4.12.2 (December 2023). Upstream is at 5.1.0 (2026-08-16)
and attaches an official `Microsoft.Z3.<ver>.nupkg` (MIT, same package id) to every GitHub
release. It stopped pushing to nuget.org over code-signing process, not abandonment
(Z3Prover/z3#10711, open, with NuGet team contact). Between 4.12.2 and 5.1.0 the release notes
carry soundness fixes in theories this encoder uses: bit-vector equalities derived from arrays
(`intblast`), the `elim-term-ite` simplifier, and QF_BV returning `unknown` in parallel mode. A
solver soundness bug is a false Equivalent. 4.12.2 also ships no Linux native (M3-001 Notes), so
CI takes `libz3.so` from a PyPI wheel. The 5.1.0 nupkg ships `libz3` for linux-x64, linux-arm64,
osx-x64, osx-arm64, win-x64 and win-arm64.

## Decision
`Microsoft.Z3` is pinned to the newest upstream release and restored from a local folder feed
(`.z3-feed/`, gitignored), filled by `tools/z3-feed/fetch.ps1`, which downloads the nupkg from the
Z3Prover/z3 GitHub release by exact URL and fails unless its SHA-256 matches the value checked in
beside it. `NuGet.config` maps `Microsoft.Z3` to that feed only (package source mapping); every
other package still comes only from nuget.org. When upstream publishes the same or a newer
version to nuget.org, the feed is deleted and this ADR is superseded.

## Why
- Same publisher, same package id, same licence: only the transport changes.
- Hash pinning gives the same integrity guarantee as a lock file; nothing floats.
- One package covers every platform, which removes the PyPI wheel step and the Linux gap.
- Tried: nuget.org (no newer version), searching for another id (only unofficial mirrors).

## Rejected
- Stay on 4.12.2: three years of solver soundness fixes missing from a verifier.
- Commit the nupkg: 65 MB, above GitHub's 50 MB warning; needs LFS.
- GitHub Packages feed: restoring even a public package needs a token, so every contributor and
  Dependabot would need credentials.
- An unofficial nuget.org mirror: third-party, single maintainer, not the Microsoft publisher.
- Building libz3 from source: slow CI, and we would own the build.

## Consequences
- Every restore (build.ps1, all workflows, Dependabot) needs the feed first. Dependabot cannot
  run the fetch script, so `Microsoft.Z3` gets a Dependabot `ignore` entry and is bumped by hand
  (watch the Z3 releases page); the ticket must prove Dependabot still updates the other packages.
- ADR 0002's `Microsoft.Z3` row changes (version, source, platforms). The licence gate
  (`tools/licence-check`) reads the same nuspec.
- The Linux native needs glibc 2.38 or newer (Ubuntu 24.04+). Debian 12, Ubuntu 22.04 and Alpine
  hosts are unsupported; container base images must be Ubuntu 24.04-based. It does not need
  `libgomp` despite the nuspec description (checked from the ELF `NEEDED` entries: libstdc++,
  libm, libgcc_s, libc).
- Ticket M3-027.
