# M0-009 Licensing: BUSL-1.1, third-party notices, dependency licence policy
Status: done (PR pending)
Effort: M
Model: Opus, medium effort. The licence text is legal boilerplate that must be reproduced byte-exactly; the Additional Use Grant is bespoke. Do not paraphrase either.
Depends on: none

## Goal
The repository has been public since 2026-09-18 with no `LICENSE` file, while the docs already
assume a paid hosted tier. Add the licence and the surrounding policy before M3-004 starts
publishing binaries, a container image and a marketplace action that redistribute third-party
assemblies with no attribution file. Adopt BUSL-1.1 with a three-seat, 50,000-LOC-per-codebase
free tier; record the choice and a dependency licence allowlist in ADR 0017; add the
third-party notices the shipped artifacts will need. No production code changes.

## Spec references
docs/adr/0017-licensing-and-ip.md; docs/adr/0002-dependencies.md; canonical BUSL-1.1 text at
<https://mariadb.com/bsl11/>

## Acceptance criteria (all must hold; nothing beyond them)
1. `LICENSE` is BUSL-1.1. Everything from the line `Notice` onward is byte-identical to the
   canonical text; only the parameter block above it differs. Verify by diffing that region
   against a known-good copy, not by reading it.
2. The parameters are: Licensor `Nicholas Wentz and Ibasho Corp.`; Licensed Work `equiv`,
   `(c) 2026 Nicholas Wentz and Ibasho Corp.`; Change Date `2030-09-20`; Change License
   `Apache License, Version 2.0`.
3. The Additional Use Grant conditions **production use only**, and places no condition on
   non-production use. BUSL covenant 2 forbids an Additional Use Grant that imposes any
   additional restriction on the base grant, and the base grant already covers non-production
   use unconditionally. A time-boxed evaluation clause is therefore not permitted.
4. The grant caps production use at three individuals who use `equiv` or receive, view or act
   upon its output, and at 50,000 lines per analysed codebase measured per codebase rather than
   summed. It states that CI is production use, aggregates affiliates, forbids splitting use to
   stay under the caps, and excludes hosting, third-party services, resale and competing use at
   any scale.
5. `LICENSES/Apache-2.0.txt` holds the verbatim Change Licence text.
6. `THIRD-PARTY-NOTICES.md` lists every dependency with version, licence and project URL, split
   into redistributed, build-and-test-only, and samples-only sections, and reproduces the MIT
   text once. Licences are read from the restored `.nuspec` or bundled licence file on disk, not
   recalled from memory.
7. `Directory.Build.props` sets `Product`, `Authors`, `Company`, `Copyright`,
   `PackageLicenseFile`, `PackageRequireLicenseAcceptance`, `PackageProjectUrl`, `RepositoryUrl`,
   `RepositoryType`, and defaults `IsPackable` to `false`.
8. `docs/adr/0017-licensing-and-ip.md` records the decision, the covenant constraints, the
   dependency allowlist and denylist, the two standing exceptions, and the M3+ landmine list.
9. `docs/adr/0002-dependencies.md` gains a `Licence` column populated for every row.
10. `CONTRIBUTING.md` states that the repo is source-available and not open source, that
    unsolicited PRs are not accepted, and that contributions require a signed CLA.
11. `README.md` gains a `## Licence` section that summarises the free tier and defers to
    `LICENSE` as controlling.
12. `./build.ps1` is green and its output is otherwise unchanged: no file added here is
    compiled, covered or analysed.

## Files
- `LICENSE`, `LICENSES/Apache-2.0.txt`, `THIRD-PARTY-NOTICES.md`, `CONTRIBUTING.md`
- `Directory.Build.props`
- `docs/adr/0017-licensing-and-ip.md`, `docs/adr/0002-dependencies.md`
- `README.md`, `docs/ROADMAP.md`
- `docs/tickets/M0-009-licensing-and-ip.md`, `M0-010-dependency-licence-gate.md`,
  `M3-006-analysed-loc-reporting.md`, `M3-004-packaging.md`

## Tests
None. No code changes, so nothing here is coverable. The check is `./build.ps1` staying green
and the byte-for-byte licence diff in criterion 1.

## Size guard
Twelve files. More than that and the scope has drifted into the deferred gate ticket.

## Out of scope
- The dependency licence CI gate and generated notices: M0-010.
- Analysed-LOC reporting: M3-006.
- Per-release Change Date stamping and embedding notices in artifacts: M3-004.
- Any CLA bot, signing flow, or commercial-licence purchase path.
- Per-file `SPDX-License-Identifier` headers. `LICENSE` plus the `Copyright` assembly property
  is sufficient; headers on every file are churn under `dotnet format` for no added protection.

## Notes
Decision: the free-tier caps live in the Additional Use Grant rather than in a separate EULA,
so there is one controlling document.

Decision: the Covenants of Licensor are not reproduced in `LICENSE`. They are obligations owed
to MariaDB by the Licensor, and every BUSL adopter checked (HashiCorp, CockroachDB, Sentry,
MariaDB Server) omits them.

Decision: Apache-2.0 over MPL-2.0 as the Change License. Covenant 1 requires GPL-2.0-or-later
compatibility; Apache-2.0 is GPL-3.0-compatible and adds an express patent grant.

Dependency audit result, read from disk on 2026-09-20: no copyleft in anything redistributed.
All six shipped packages are MIT. `dotnet-sonarscanner` is LGPL-3.0 but is a separate CI
process; `Microsoft.AspNet.WebApi.Core` 5.3.0 is under the Microsoft .NET Library EULA but is
samples-only. `Microsoft.Z3` was not restored locally and its licence is recorded as unverified
pending M3-001.

This ticket is not legal advice, and the Additional Use Grant should be reviewed by a lawyer
before the first commercial licence is sold.
