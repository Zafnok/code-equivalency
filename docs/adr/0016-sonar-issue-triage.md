# ADR 0016: SonarQube's overall backlog becomes batched GitHub issues, filtered by a checked-in policy

Status: accepted (2026-09-20)

## Context
ADR 0009 bought a *changegate*: SonarQube Cloud grades new code on each PR. It deliberately
says nothing about the code that was already there. Measured against the live API on
2026-09-20, `Zafnok_code-equivalency` has 211 open issues — 65 in `src/`, 134 in `tests/`,
12 in `tools/`; all `CODE_SMELL`, zero bugs, zero vulnerabilities. Nothing surfaces them,
so they are neither fixed nor consciously accepted.

Two measurements shape what follows. First, roughly 150 of the 211 are `external_roslyn:*` —
this repo's own Meziantou/IDE/xUnit analyzers at their default severity. `MA0003`, `IDE0046`
and `IDE0301` appear nowhere in `.editorconfig`, so they emit below warning level,
`TreatWarningsAsErrors` never sees them, and Sonar's Roslyn import ingests them anyway. Only
~61 are genuine `csharpsquid` findings. Second, some findings are correct but wrong *for this
repo*: `S2094` fires on the three `AssemblyMarker.cs` files that exist solely for ArchUnitNET
assembly discovery, and `S2178` would fire on the non-short-circuit `&` that M1-003 chose
deliberately so each field is one branch under the 100% branch-coverage gate.

SonarQube Cloud cannot file GitHub issues. Its GitHub features for a bound project are PR
decoration, branch protection via the quality-gate check, and Code Scanning alerts — the last
being Enterprise-plan and security-issues-only, which with zero vulnerabilities would surface
nothing here. Asked directly, SonarSource's position is that automatic ticket creation is not
offered on purpose: "For some issues you won't want a ticket. For some, you'll want to group
several into one ticket & so on."

## Decision
Add `tools/sonar-triage/sonar-triage.ps1`, run weekly and on demand, which reads
`/api/issues/search`, drops findings matched by `tools/sonar-triage/policy.jsonc`, batches the
rest, and syncs each batch to one GitHub issue labelled `sonar`.

Batching is hybrid: a rule spanning three or more files becomes one rule-wide issue (one
decision, one PR); everything else groups per file; single-finding leftovers roll up into one
long-tail issue per top-level area. Over today's 211 that yields 11 rule batches and 21 file
batches, about 26 issues after roll-up.

`policy.jsonc` is the one place a deviation is argued. `verdict: accept` requires a `reason`,
and a reason that appeals to a repo-wide design choice must cite an ADR or a ticket. The
script can optionally push those reasons to SonarCloud as Won't Fix transitions, so the
dashboard and the repo agree.

Fixing an `external_roslyn` finding also pins `dotnet_diagnostic.<ID>.severity` in
`.editorconfig` in the same PR, converting a silent suggestion into a build error so the
finding cannot return. `.editorconfig`, not Sonar, stays the source of truth for Roslyn rules.

These issues are tracked in GitHub, not `docs/tickets/` — the single documented exception to
CLAUDE.md's "work is defined in `docs/tickets/`". The issue body carries goal, findings and
acceptance criteria, so it *is* the ticket.

## Why
- The batching rule is the direct answer to Sonar's own reason for not shipping this: grouping
  and selection are repo-specific judgement, and both halves are now explicit and reviewable.
- A checked-in register beats scattered `// NOSONAR` comments: the reasoning lives in one
  file, is diffed in review, and cannot drift per call site.
- Severity pinning collapses two overlapping gates into one. Today a rule can be clean in CI
  and dirty in Sonar; after a fix PR it is enforced in the build, where it belongs.
- A drained backlog is the precondition for the promotion ADR 0009 deferred — dropping
  `continue-on-error` from `sonar.yml` — which is currently unowned in ROADMAP.md.
- A PowerShell script needs no new NuGet package or dotnet tool, so ADR 0002 is unchanged.

## Rejected
- **`sonar.cs.roslyn.ignoreIssues=true`.** Clears ~150 findings instantly and makes the gate
  promotable this week, but silences real, fixable suggestions instead of fixing them. Kept as
  the escape hatch if the 88-finding `MA0003` batch turns out to be unwanted churn.
- **A SonarCloud project webhook as the trigger.** It fires once per analysis with a
  quality-gate payload and no issue list, so consuming it still means calling
  `/api/issues/search` and grouping. It is only an alternative trigger, and a worse one: it
  fires on every PR analysis, not just `main`.
- **The SonarQube MCP server.** A Docker image plus per-session config on a Windows dev box,
  to buy interactive querying that each issue body already contains (rule text, messages,
  permalinks). Revisit if fix sessions need ad-hoc Sonar queries.
- **Per-finding `// NOSONAR`.** What M1-003 did; it works, but the rationale ends up copied
  into every call site and is invisible to anyone reading the dashboard.
- **A C# tool with its own test project**, mirroring `tools/check-coverage`. Disproportionate
  for HTTP calls and `gh` shell-outs; `build.ps1` is the precedent for an untested script.
- **Auto-fixing in CI.** Considered and dropped: nothing writes code unattended. GitHub
  Copilot Autofix (free on public repos) only covers Code Scanning alerts, and Sonar AI
  CodeFix is not on the free plan, so there is no free bot for these findings regardless.

## Consequences
- GitHub Issues become a real work surface for this repo, which until now had zero issues.
  CLAUDE.md, `docs/tickets/README.md` and `docs/QUALITY-GATES.md` record the exception.
- The weekly job needs `issues: write`; it reads Sonar anonymously and needs no secret.
- `-PushResolutions` writes to a shared external service and therefore stays opt-in,
  confirmed per run, and requires a `SONAR_TOKEN` with issue-admin rights.
- Sonar issue keys are not stable across re-analysis; batches are identified by rule or path,
  never by issue key, and a fix session re-verifies every finding against `HEAD` before acting.
- If the backlog is ever drained, this tooling becomes a no-op rather than something to remove.

## Clarifications
- 2026-09-23 (GH-91, GH-92). The Context's `S2094` example is wrong. The empty
  `AssemblyMarker.cs` types were never used for ArchUnitNET assembly discovery:
  `tests/Equiv.Tests.Architecture` loads each assembly by name (`Assembly.Load`). They were
  M0-002's namespace placeholders, left behind once real types landed. Both remaining ones are
  deleted, the `S2094` accept entry is removed from `policy.jsonc`, and `MA0182`/`MA0206` are
  pinned to error. The Decision is unchanged. The `S2178` example still stands.
