# ADR 0024: Identical bound code is Equivalent by congruence, and an unlowerable fragment present on both sides is shared

Status: proposed (2026-09-21). Would supersede ADR 0014 in part: reaching an `IrOpaque` stops
making the outcome unknown when the same fragment is on both sides.

## Context
Under ADR 0014, an input that reaches an `IrOpaque` has an unknown outcome, and a whole-body opaque
makes the entire procedure Unknown. The coverage table (`docs/tickets/IOPERATION-COVERAGE.md`) still
makes lambdas, `await`, type patterns, floating point, `decimal`, interpolated strings, `lock`,
iterators and `ref` arguments opaque. So a method nobody touched is Unknown only because of what it
contains. In a 4.8-to-10 migration, and on almost every ordinary PR, most method bodies are
unchanged. The pre-M3 review predicted the first real run would be "almost all Unknown", and M3-010
and M3-011 only cover part of that. Opacity is caused by the construct, not by the change, so the
Unknown rate follows the size of the codebase instead of the size of the diff.

## Decision
The frontend computes a **bound fingerprint** for every body and for every opaque fragment. It is a
SHA-256 over a canonical serialisation of the Roslyn `IOperation` tree, which holds:
- operation kinds;
- every referenced symbol as its normalised identity, using the same normaliser, rename maps and
  applied `api-equivalences.json` rewrites as matching (ADR 0020);
- locals and lambda parameters numbered by first occurrence, and source parameters by position (ADR 0021);
- constants by type and value;
- the kind and normalised target type of every conversion and operator;
- the `checked` context.

Trivia, comments, formatting and local names cannot affect it. The fingerprint also records whether
the tree is **runtime-sensitive**, meaning it contains any of:
- a member listed in `runtime-changes.json`;
- a floating-point to integer conversion (made saturating in .NET 9);
- floating-point arithmetic when the legacy project runs on the 32-bit x87 JIT.

1. **Congruence.** A matched pair whose body fingerprints are equal, and neither of which is
   runtime-sensitive, is Equivalent without calling the solver. `proofMethod` is `congruence`, and
   the result lists `assumedCallees` exactly as ADR 0019 requires.
2. **Shared fragments.** An `IrOpaque` carries its fragment's fingerprint and the IR variables it
   reads. When a fragment's fingerprint occurs on both sides of a pair and is not runtime-sensitive,
   the encoder treats both occurrences as one opaque call. Its identity is `opaque:<fingerprint>`, its
   arguments are the variables it reads, and it takes the heap at that point and its trace position
   (ADR 0018). A fragment found on one side only, or runtime-sensitive, keeps ADR 0014's meaning.
   A fragment that writes a local other than its result, or that holds a lambda capturing a local
   assigned after the fragment, gets no fingerprint.

## Why
- The claim is the same one every `IrCall` already makes. Identical bound trees run the same
  operations in the same order. They can only behave differently through a callee that behaves
  differently between runtimes, and that is exactly the shared-callee assumption of ADR 0018 and
  ADR 0019, with the runtime-changes table as the listed exception. The runtime-sensitive flag
  carries that exception over to congruence.
- Fingerprinting the *bound* tree, not the source text, catches the drift that makes identical text
  mean different things. That covers a different overload chosen under .NET 10, an interpolated
  string bound to `DefaultInterpolatedStringHandler`, and a different conversion. Each of these
  changes the fingerprint and falls through to the solver.
- Congruence changes Unknown from depending on codebase size to depending on diff size, and it
  removes the solver from most pairs on a PR. That also keeps Z3 time inside a CI budget.
- A shared fragment is modelled as a call event, not as a pure function, because a fragment can
  contain calls. Keying it by position and heap keeps it sound when both sides execute it in a
  different order.
- A lambda that captures a variable written later sees a value that its reads at creation do not
  determine. Excluding it is the one case where a function of the reads would be unsound.

## Rejected
- **Text or syntax-tree diff.** Identical text can bind differently across frameworks, which is the
  whole migration risk.
- **Comparing compiled IL.** The legacy and modern compilers and reference assemblies differ, so IL
  differs even when behaviour does not, and IL loses the source spans that SARIF needs.
- **A fragment as a pure uninterpreted function with no trace event.** Unsound when the fragment
  contains calls whose order is observable.
- **Lowering every construct before shipping.** It is never finished, and the Unknown rate would
  still grow with codebase size in the meantime.

## Consequences
- The Unknown rate of an unchanged method becomes zero unless it is runtime-sensitive. The census
  (ADR 0027) reports the congruent share.
- A counterexample that involves a shared fragment cannot be replayed concretely. ADR 0026 decides
  what such a result becomes, and it must land before shared fragments.
- The fingerprint becomes part of what matching and api-equivalences must keep stable, and a
  normaliser change can invalidate many congruences at once. The sample snapshots pin this.
- VERIFICATION-MODEL sections 1, 2 and 5 gain the congruence rule and the fragment encoding when
  this ADR is accepted, and ADR 0014 gains "superseded in part by 0024".
- Tickets: M3-015 (fingerprints and congruence), M3-017 (shared fragments).
