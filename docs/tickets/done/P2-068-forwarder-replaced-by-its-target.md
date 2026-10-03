# P2-068 A call to a one-line forwarder and a call to its target are the same call
Status: done (PR #360)
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
- `equiv-adr` outcome: a new ADR, 0043 (PR #355), because it narrows ADR 0019's rejection of
  inlining and section 3's rule that two identities are two functions, changes verdicts, and adds a
  SARIF property.
- Decision: the rule is narrower than the ticket's "non-virtual method". A forwarder is static, its
  target is static, and its arguments are its own parameters in order. That is what the audited
  cases need. An instance forwarder or an instance target needs a receiver's null check and
  dispatch wired into both lowerings (ADR 0043, Rejected).
- Decision: four more conditions, each because the call would otherwise do more or less than its
  target: no static constructor on the declaring type, not `async`, no `[Conditional]`, no
  `System.Security` attribute on the method or its type.
- Decision: a forwarder both sides have is resolved only when the two sides forward to the same
  target. Found on `samples/partly-compiling-modern`: its legacy `Pricing.Currency` is a forwarder
  and its modern one does not bind, and resolving the legacy side alone made the unchanged
  `HasCurrency` Unknown (a rebound call, ADR 0042) where it was Equivalent under ADR 0019's named
  assumption. So the frontend finds the forwarders whose sides disagree before lowering, and no
  body resolves those.
- Decision: the bound fingerprint spells a forwarder call as its target. Otherwise congruence would
  decide a pair from a callee name the lowering no longer uses. The ticket's pair is therefore
  Equivalent by congruence, without the solver.
- Decision: the IL lowering's seam takes the side's `CallSites` where it took a set of rebound
  identities, so it can report the forwarders it resolved. A pair that keeps its IL bodies lists
  those.
- Non-BCL targets are covered at no extra cost: the target is any static method.
- Surprise: `samples/forwarder-to-bcl`'s `Text.BlankTrimmed` is `string.IsNullOrWhiteSpace(s?.Trim())`,
  as criterion 3 asks. The IL lowering reads that null-conditional argument through a stack slot of
  type `object`, with two cast maps the IOperation lowering never uses, so the two lowerings of
  that one method are not equivalent. It is recorded as a known difference in `IlLowererTests` and
  `IlLoweringParityTests`. It is an IL-lowering gap, not part of this ticket.
- Surprise: `SetSshDivergenceTests` (P2-037) is this ticket's case, standalone: Git Extensions' own
  `Strings.IsNullOrEmpty` wrapper. Its two bodies now lower to the same IR. The pair is still
  Divergent across .NET Framework 4.8 and .NET 10, but only through `Environment.SetEnvironmentVariable`,
  a runtime-changes row (EQ006). So a resolved forwarder can uncover a runtime-changed call that the
  trace difference used to hide, and not every one of the 11 audited results need become Equivalent.
- Not run: the corpus. The 11 audited results are expected to move only if Git Extensions'
  wrappers meet every condition above (static, one call, no static constructor on `Strings`).
