# P2-079 Soundness: the IL lowering shares an opaque that names a lambda without its body, so two different lambdas prove Equivalent
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: P1-016, P1-017

## Goal
`--il-fallback` reports a false Equivalent. `IlFragment.Of` fingerprints an opaque ILAst instruction
by its text, with each member spelled as its declaring type and documentation ID. A lambda, a local
function and a closure class are compiler-generated members whose names are ordinals
(`<set_X>b__5_0`), and their bodies are elsewhere. So two sides whose lambdas differ get the same
fingerprint, ADR 0024 shares the fragment as one call, and the pair proves. The IOperation
fingerprint (`FragmentFingerprinter`) serialises each lambda's bound body and refuses a fragment
that refers to a local function declared outside it, or that is runtime-sensitive (M3-015's rule).
The IL fingerprint does neither: its runtime check covers only the calls in the fragment itself,
never the lambda's body. `IlLowerer.Function` has the same gap for a named method group: it lowers
`ldftn` to a constant from the method's call identity and ignores `RuntimeChanged`.

Repro (written for this ticket, two .NET Framework 4.8 projects that differ only in the lambda):

```csharp
public sealed class Holder
{
    private int _x;
    public Lazy<int> Value { get; private set; }
    public int X
    {
        get => _x;
        set { _x = value; Value = new Lazy<int>(() => _x + 1); }   // modern: _x + 2
    }
    public void Subscribe(Action<Func<int>> register)
    {
        register(() => _x + 1);                                    // modern: _x + 2
    }
}
```

Without `--il-fallback`, `set_X` and `Subscribe` are Unknown(opaque: `DelegateCreation`). With it, both
are `EQ001`, `proofMethod: bounded`, `lowering: il`. When the modern side targets net10.0 the same
pair is Unknown(abstraction), but only because the delegate constructor's identity is spelled
`(object,nint)` there and `(object,System.IntPtr)` on .NET Framework. Both sides still get one
fingerprint. That is an accident, not a guard.

P1-018's hand check (`docs/runs/2026-10-01-il-fallback-verdicts.md`) found that 20 of the 21 pairs
the fallback moved to Equivalent on Git Extensions rest on this. 19 hold a lambda or local function
the proof never read, and in two of those the code inside it differs between the sides. One passes
`System.Char::IsLetter`, a `runtime-changes.json` row, as a method group. No behavioural difference
was found in any of the 21 by reading the source, so the verdicts are unproved, not shown wrong.

## Spec references
ADR 0039 (the IL lowering follows the IOperation lowering's refusals; "lambdas and local functions
stay opaque"), ADR 0024 decision 2 (shared opaque fragments), M3-015 (runtime-sensitive fragments),
ADR 0019 (a verdict lists the callees it assumed), `src/Equiv.Frontend.CSharp/Lowering/Il/IlFragment.cs`,
`src/Equiv.Frontend.CSharp/Lowering/Il/IlLowerer.Heap.cs` (`Function`), `src/Equiv.Frontend.CSharp/Lowering/Il/IlKeys.cs`
(`IsCompilerGenerated`), `src/Equiv.Frontend.CSharp/Fingerprinting/FragmentFingerprinter.cs`,
`docs/tickets/IL-COVERAGE.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The repro above is a test in `tests/Equiv.Frontend.CSharp.Tests` (the two fragments get no shared
   fingerprint) and a sample or integration test (`compare --il-fallback` on the same-runtime pair
   does not report `EQ001` for `set_X` or `Subscribe`). Both fail before the fix.
2. `IlFragment.Of` returns no fingerprint for a fragment that references a compiler-generated method
   or type (`LdFtn[lambda]`, `NewObj[closure class]`, a call to a local function or another generated
   method, and a state-machine type). Decide with `equiv-decide` whether to refuse outright or to
   hash the referenced bodies as well. If bodies are hashed, the hash covers everything reachable
   through generated members (an `async` lambda's state machine, a local function called from a
   lambda), and M3-015's rule applies to every call in them. Log the choice as a `Decision:` line.
3. `ldftn` and `ldvirtftn` of a member that `runtime-changes.json` names are not lowered to a plain
   constant. They are treated as the IOperation lowering treats a runtime-sensitive delegate
   creation. A test passes `char.IsLetter` as a method group.
4. `IL-COVERAGE.md`'s rows for `LdFtn`, `LdFtn[lambda]`, `LdVirtFtn`, `NewObj[closure class]` and the
   generated-call keys say what criteria 2 and 3 decided.
5. M0-012's IL mode (P1-017) generates pairs that differ only inside a lambda and only inside a local
   function, and the gate fails on the code before the fix. If its generator cannot produce them,
   `## Notes` says why and names the test that covers the case instead.
6. A `--il-fallback` `full` run of `gitextensions-8522` is compared with P1-018's. For each of the 20
   pairs listed in that report's hand check, `docs/runs/<date>-il-fragment-soundness.md` gives the
   new verdict. Any that is still Equivalent from IL is explained there, with the member it shares.

## Files
`src/Equiv.Frontend.CSharp/Lowering/Il/IlFragment.cs`, `IlLowerer.Heap.cs`, `IlKeys.cs` (only if a key
changes), their tests, a sample or integration test for criterion 1, the differential gate's generator,
`docs/tickets/IL-COVERAGE.md`, `docs/runs/<date>-il-fragment-soundness.md`.

## Tests
`IlFragmentTests.AFragmentThatNamesALambdaHasNoFingerprint`, `IlFragmentTests.AFragmentThatNamesALocalFunctionHasNoFingerprint`,
`IlLowererTests.ARuntimeChangedMethodGroupIsNotAConstant`, `IlFallbackSampleTests.ALambdaWhoseBodyDiffersIsNotEquivalent`,
and the gate operator's test for criterion 5.

## Size guard
Lowering lambda bodies so that such pairs can be proved is P2-067. Here they only stop being proved
wrongly. If the diff grows past the files above, stop.

## Out of scope
Turning `--il-fallback` on by default. Lowering lambdas (P2-067). The crash in P2-078.

## Notes
- Found 2026-10-01 by hand-checking P1-018's 21 new Equivalents at the user's request, then confirmed
  with the repro above at equiv 1d4569a.
- Until this lands, a verdict from a run with `--il-fallback` is not to be relied on where the method
  holds a lambda, a local function or a method group. The option is off by default.
- P1-017's IL gate did not catch this. Its generated pairs hold no lambda.
- No matched pair exists for a lambda or a local function: no result in the Git Extensions SARIF names
  one. Their bodies are only ever verified as part of the method that holds them.
- Decision: criterion 2, refuse or hash the referenced bodies -> refuse outright: a fragment whose text names a
  compiler-generated method or type gets no fingerprint. Alternatives: hash every body reachable through generated
  members and apply M3-015's rule to each call in them. Rule: 4 (the smaller change; reading the bodies is P2-067's
  lowering of them, and a refusal cannot prove a pair wrongly).
