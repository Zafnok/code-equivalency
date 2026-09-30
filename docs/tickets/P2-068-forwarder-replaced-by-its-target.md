# P2-068 A call to a one-line forwarder and a call to its target are the same call
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit (`docs/runs/2026-09-30-divergent-audit.md`) found that 11 of 51 false Divergents,
the largest EQ002 cause, share one shape. The legacy side calls a solution-local helper whose whole
body forwards its parameters to a BCL member, and the modern side calls that BCL member directly. The
two callee identities are unrelated uninterpreted functions, so the call trace differs (ADR 0018),
and sometimes the result or `threw` differs too. Minimal repro, as a sample pair:

```csharp
// legacy
static class Text { public static bool Blank(string? s) => string.IsNullOrWhiteSpace(s); }
static string Name(string? s) => Text.Blank(s) ? "none" : s!;
// modern
static string Name(string? s) => string.IsNullOrWhiteSpace(s) ? "none" : s!;
```

Today this is EQ002. It should be Equivalent. Decide, through `equiv-adr`, how a call to a forwarder
(a non-virtual method whose body is one call, passing its parameters through unchanged, and returning
that call's result) is identified with the call it forwards to. Then implement it for both lowerings.

## Spec references
ADR 0018 (the call trace is observable), ADR 0019 (modular verdicts; rejects inlining matched
callees), VERIFICATION-MODEL section 3 (call identity), P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. The `equiv-adr` outcome is merged before any code change.
2. The pair above, as `samples/forwarder-to-bcl`, is Equivalent, and the SARIF says which forwarder
   was resolved.
3. A forwarder that changes an argument, or adds a side effect, is not resolved: a sample variant
   whose helper passes `s?.Trim()` stays Divergent.

## Tests
- Integration test on `samples/forwarder-to-bcl` (both variants).
- Unit tests the decision names, in the affected frontend test projects.

## Out of scope
Inlining general callees (ADR 0019 rejects it). Forwarders to non-BCL targets unless the decision
covers them at no extra cost.

## Notes
- Found by P2-047: 11 false positives on Git Extensions (a `Strings.IsNullOrEmpty` /
  `IsNullOrWhiteSpace` wrapper replaced by `string.IsNullOrEmpty` / `IsNullOrWhiteSpace`).
