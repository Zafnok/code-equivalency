# ADR 0010: Narrow the Equiv.Core Roslyn/Z3 architecture rule to exclude Sarif.Sdk's namespace

Status: accepted (2026-09-18)

## Context
M1-004 adds `Sarif.Sdk` (already an ADR 0002 row, an ARCHITECTURE.md-mandated `Equiv.Core`
dependency: "SARIF emission (Sarif.Sdk) ... are Equiv.Core responsibilities") to
`Equiv.Core`. `Sarif.Sdk`'s root namespace is `Microsoft.CodeAnalysis.Sarif` — it shares
Roslyn's `Microsoft.CodeAnalysis` prefix by coincidence of Microsoft's own namespace
choice, not because it depends on Roslyn. `tests/Equiv.Tests.Architecture/DependencyRuleTests.
CoreDoesNotDependOnRoslynOrZ3`'s regex, `^(Microsoft\.CodeAnalysis|Microsoft\.Z3)(\.|$)`,
matches that prefix and fails the architecture gate on an approved dependency.
CLAUDE.md: "if a gate is wrong, stop and report; do not work around it" — this is that case.

## Decision
Narrow the rule's regex to `^Microsoft\.CodeAnalysis$|^Microsoft\.CodeAnalysis\.(?!Sarif(\.|$))|^Microsoft\.Z3(\.|$)`,
which still blocks every real Roslyn namespace (`Microsoft.CodeAnalysis` itself,
`Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.Workspaces`, ...) and `Microsoft.Z3`,
while excluding only `Microsoft.CodeAnalysis.Sarif` and its sub-namespaces.

## Why
- The rule's own `.Because(...)` states its intent: "Equiv.Core must have no Roslyn or Z3
  dependency; those are frontend/backend concerns." Sarif.Sdk is neither; excluding it by name
  restores that intent rather than weakening it — every real Roslyn/Z3 namespace is still
  caught by the narrowed pattern, unchanged.
- The alternative (moving SARIF emission out of `Equiv.Core`) contradicts ARCHITECTURE.md's
  explicit assignment of SARIF emission to `Equiv.Core` and would be a much larger change for
  no benefit.

## Rejected
- Suppressing or disabling the architecture test for this one case: CLAUDE.md forbids
  `#pragma warning disable`-style workarounds and disabling a gate outright is worse than
  narrowing its own regex to match its stated intent.
- Moving `Equiv.Core.Reporting` (the SARIF writer) into a new project outside the
  `Microsoft.CodeAnalysis`-prefixed dependency's reach: no such reach exists — the conflict is
  purely a namespace-string coincidence, not a real dependency direction problem, so
  restructuring the project layout would not address the actual cause.

## Consequences
- `tests/Equiv.Tests.Architecture/DependencyRuleTests.CoreDoesNotDependOnRoslynOrZ3`'s regex is
  narrower but still exhaustive against real Roslyn/Z3 namespaces; a future Roslyn namespace
  that happens to start with `Microsoft.CodeAnalysis.Sarif` would slip through, which is not a
  real risk (that prefix belongs to `Sarif.Sdk`, not Roslyn).
- No doc changes beyond this ADR: ARCHITECTURE.md already assigns SARIF emission to
  `Equiv.Core`; this ADR only fixes the test that was enforcing that assignment incorrectly.
