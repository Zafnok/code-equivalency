# P2-274 CI: GitHub's automatic NuGet dependency submission fails when it restores a Z3 project before the local feed exists
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
GitHub's dynamic "Automatic Dependency Submission (NuGet)" workflow (check `submit-nuget`) finds every project file on a
clean runner and restores each one, without running `tools/z3-feed/fetch.ps1`. `Equiv.Verify.Z3` restores
`Microsoft.Z3` from the local `.z3-feed` source (ADR 0030), which does not exist there, so restoring it, or a project that
references it (`tests/Equiv.Verify.Z3.Tests`), fails with NU1301 and the check fails. Whether it is reached first depends
on the order the workflow lists projects in: 4 of the last 14 runs failed this way, among them `main` at `4b2e0df6`
(14:59 on 2026-10-20) and P2-247's PR #558 twice, while `main` at the same commit passed at 17:40. P1-019 met the same
failure for a spike project and conditioned that project's Z3 reference on the feed; the solution's own projects have no
such condition. When done, `submit-nuget` passes on every run of a commit that the gates pass, without lowering a gate,
and the gates still restore `Microsoft.Z3` from the verified local feed.

## Spec references
ADR 0030 (Z3 from the local feed), ADR 0002, `tools/z3-feed/fetch.ps1`, `NuGet.config`, ticket P1-019 (its CI note),
`docs/QUALITY-GATES.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The dependency submission restores every project on a clean runner without NU1301 (for example: a repository-level
   dependency-submission workflow that runs `tools/z3-feed/fetch.ps1` first, or a restore that tolerates the absent
   local source for the dependency graph only), recorded in `docs/QUALITY-GATES.md`.
2. Three runs of the check on one commit pass.

## Notes
- Filed 2026-10-20 by P2-247, whose docs-only PR #558 failed `submit-nuget` twice on NU1301 for
  `tests/Equiv.Verify.Z3.Tests/Equiv.Verify.Z3.Tests.csproj`; the check is not a required one.
