# ADR 0017: BUSL-1.1 with a three-seat, 50k-LOC free tier; permissive-only dependencies

Status: accepted (2026-09-20)

## Context
The repository has been public since 2026-09-18 with no `LICENSE` file. `gh repo view` returned
`licenseInfo: null`, and no `LICENSE`, `NOTICE`, `COPYING` or third-party notice file was tracked.
Under default copyright that grants no rights to anyone, which sounds protective and is a poor
place to stay: it blocks any enterprise evaluation, and M3-004 is about to publish single-file
binaries, a GHCR image and a marketplace `action.yml`, all of which redistribute third-party
assemblies with no attribution file.

Monetisation is already assumed across the docs. ROADMAP's post-MVP backlog names a "Hosted tier:
container behind an API, AKS, per-key quotas"; ARCHITECTURE says "The container image is the free
tier; the hosted tier wraps the same image later"; M1-002's Notes record "no revenue yet,
monetisation planned". Nothing had been written down about what stops a third party from taking the
engine and selling it.

Timing favoured acting now: 0 forks, 0 stars, nothing tagged or released, and no outside
contributors whose copyright could block a relicence. That is the cheapest this decision ever gets.

Going private was considered first and rejected on cost: SonarCloud's free tier and CodeQL's free
tier are public-repo-only, and Actions minutes become metered. This repo's entire gate stack
(ADR 0009, `codeql.yml`, `mutation.yml`) rides on those tiers pre-revenue.

## Decision
`equiv` is licensed under the **Business Source License 1.1**, source-available and explicitly not
open source, with these parameters:

| Parameter | Value |
|---|---|
| Licensor | Nicholas Wentz and Ibasho Corp. |
| Licensed Work | equiv, (c) 2026 Nicholas Wentz and Ibasho Corp. |
| Change Date | 2030-09-20 |
| Change License | Apache License, Version 2.0 |

Both holders are named deliberately. If Ibasho Corp. ceases operations, Nicholas Wentz is already a
named copyright holder and Licensor, so no relicensing, assignment or successor notice is needed.

The Additional Use Grant permits production use only while **no more than three individuals use the
Licensed Work or receive, view or act upon its output**, and **no analysed codebase exceeds 50,000
lines of code**, measured per codebase rather than summed across a comparison — so a 49k-to-49k
migration is inside the grant and a 49k-to-52k migration is not. Affiliates aggregate, and splitting
use across entities, accounts or repositories to stay under the limits does not extend the grant.
Offering `equiv` as a hosted or embedded service, using it to serve third parties, reselling it, or
using it to build a competitor is excluded at any scale and cannot be bought under this licence.

Three drafting constraints came out of the BUSL covenants and shaped the text:

1. **Covenant 2 forbids an Additional Use Grant that imposes "any additional restriction on the
   right granted in this License".** The base terms already grant copy, modify, derivative works,
   redistribution and *non-production use* unconditionally. The seat and LOC caps therefore attach
   to production use only. An earlier draft time-boxed evaluation to 30 days; that would have
   clawed back the base non-production grant and breached the covenant, so it was dropped.
   Consequence, accepted knowingly: non-production evaluation is free and uncapped for everyone,
   including large organisations. That is inherent to BUSL.
2. Because of (1), the grant states explicitly that **CI and any commercial software development
   process count as production use.** Without that line, the product's main use case could be
   argued into the uncapped non-production bucket, which would give away the thing being sold.
3. **Covenant 1 requires a GPL-2.0-or-later-compatible Change License.** Apache-2.0 is compatible
   with GPL-3.0 and so satisfies it, and carries an express patent grant that MIT lacks.

The licence body below the parameter block is byte-identical to the canonical MariaDB text; only
the parameters differ. The Covenants of Licensor are obligations owed to MariaDB and are not
reproduced in `LICENSE`, following the convention of every BUSL adopter checked (HashiCorp,
CockroachDB, Sentry, MariaDB Server).

### Dependency licence policy
Every dependency must carry a licence on this allowlist, recorded in its ADR 0002 row:

**Allowed:** `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `MS-PL`, `ISC`.

**Denied:** `GPL-*`, `AGPL-*`, `SSPL`, `CeCILL*`, `EUPL`, and any licence whose terms are
non-commercial, evaluation-only, or conditioned on sponsorship for commercial use. `LGPL-*` and
`EPL-2.0` are denied for anything linked into the product, and permitted only for a standalone
executable invoked as a separate process and never redistributed.

Two standing exceptions, both outside the shipped artifact:

- `dotnet-sonarscanner` is **LGPL-3.0**. It is a CI executable invoked as a process, never linked
  and never redistributed. No `PackageReference` on a Sonar library may be taken.
- `Microsoft.AspNet.WebApi.Core` 5.3.0 is under the **Microsoft .NET Library EULA**
  (`requireLicenseAcceptance=true`), the only non-OSI licence in the repository. It is referenced by
  a `samples/` fixture, isolated from the root build by `samples/Directory.Build.props`. Those
  assemblies must never be vendored into the container image or a release artifact.

First-party data files that encode third-party knowledge — `src/Equiv.Core/RuntimeChanges/runtime-changes.json`
today, and the equivalent tables the Java frontend will need — cite vendor documentation by URL and
must contain original prose. Do not paste vendor documentation text into the repository. Facts about
behaviour are not copyrightable; the wording describing them is.

### Known landmines for M3 and later
Nothing adopted so far is copyleft. The risks are all ahead, in areas the roadmap already names:

- **Java frontend** (README: "Later: Java 11 to 25"). JavaParser is dual LGPL-3.0/Apache-2.0 — take
  Apache-2.0 explicitly and record which. Eclipse JDT Core is EPL-2.0, file-level copyleft, denied
  for linking under the policy above. **Spoon is CeCILL-C/LGPL — denied.** ANTLR4 (BSD-3) and
  tree-sitter with tree-sitter-java (MIT) are clean.
- **Solvers beyond Z3.** CVC5 (BSD-3) and Bitwuzla/Boolector (MIT) are clean.
  **Yices is GPL-3.0 and MathSAT5 is non-commercial-only — both are denied** and would be
  disqualifying for a paid product. `Boogie.ExecutionEngine`, the deferred second backend named in
  ADR 0002, is MIT and clean.
- **MSBuild redistribution, not a copyleft issue but a distribution one.**
  `Microsoft.CodeAnalysis.Workspaces.MSBuild` needs a real MSBuild at runtime to load legacy
  `.csproj` (ADR 0004). VS Build Tools is not freely redistributable inside a container image.
  M3-004 must either stay on the .NET SDK's redistributable MSBuild or use Microsoft's build-tools
  base image under its per-container EULA. This constrains the hosted tier's packaging.

## Consequences
- `LICENSE`, `LICENSES/Apache-2.0.txt`, `THIRD-PARTY-NOTICES.md` and `CONTRIBUTING.md` are added.
  `Directory.Build.props` gains `Copyright`, `Authors`, `Company`, `Product`, repository URLs and
  `PackageRequireLicenseAcceptance`.
- `IsPackable` now defaults to **false**. The four `src/` projects previously set nothing and were
  packable-by-default with no licence metadata, so a stray `dotnet pack && dotnet nuget push` would
  have published unlicensed packages. M3-004 opts the shipping project back in explicitly.
- ADR 0002 gains a **Licence** column. Combined with the existing CLAUDE.md rule that no package
  lands without an ADR 0002 row, this is the manual form of the gate until the automated one lands.
- Contributions require a signed CLA assigning copyright to the Licensor. Without an inbound grant,
  one accepted outside patch makes the codebase un-relicensable and blocks both the Apache-2.0
  conversion and any future proprietary enterprise build.
- The 50,000-LOC cap is only enforceable if it is measurable, so the CLI and SARIF output must
  report analysed line counts per codebase — reported separately, not summed, to match the grant.
  Filed as a ROADMAP follow-up against M3-003/M3-004.
- BUSL is enforceable as a contract as well as a copyright licence. That matters more than usual
  here: this codebase is largely AI-authored, and copyright in purely machine-generated material is
  thin under current US Copyright Office guidance, with human selection and direction being what
  attracts protection. The ticket and ADR trail is the evidence of that direction and should be
  kept. The corollary is that the hosted tier and any enterprise-only modules belong in a separate
  private repository, where trade-secret protection also applies, rather than behind a flag here.
- This ADR is not legal advice. The BUSL body is a standard form, but the Additional Use Grant is
  bespoke and should be reviewed by a lawyer before the first commercial licence is sold.

## Rejected
- **Proprietary "All Rights Reserved", repo stays public.** The most restrictive text available, but
  it grants nothing, offers no conversion path, and reads to an enterprise evaluator as a dead end —
  while still handing a competitor a free read of the implementation.
- **Proprietary, repo goes private.** Strongest protection and adds trade-secret cover, but forfeits
  the free SonarCloud, CodeQL and Actions tiers this gate stack depends on. Real recurring cost
  pre-revenue. Revisit if the project is funded.
- **FSL-1.1-ALv2.** Shorter and easier for a buyer to accept, but permits unlimited internal
  production use at any headcount, which is exactly the outcome the seat cap exists to prevent.
- **SSPL.** Narrower than it appears — it targets hosting specifically and says nothing about seat
  or size limits — and is treated as toxic by many procurement teams.
- **MIT / Apache-2.0.** Gives the product away.
- **A licence-allowlist CI gate in this change.** Deferred to its own ticket to keep this PR to
  licence and policy. It should land before M3-001, the first ticket to add a redistributed native
  dependency.
