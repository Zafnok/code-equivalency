# P2-242 Execution reach: a replay or `probe` driver never calls a constructor, passes a struct, or compares a struct or `IntPtr` result
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-237

## Goal
P2-237 replayed every Divergent of the three migration pairs and ran `probe` on P2-212's undetermined sample. Three limits
of the drivers stopped results that a single call would have decided:
- `ReplayArguments.CallObstacle` accepts only ordinary methods and property getters, so a constructor is "not a method or
  a property getter" and its receiver-free call is never made (OpenRA's `Folder::.ctor(string)` and
  `ImmutablePalette::.ctor(string,int[])`, each one call from the path witness that confirmed `MiniYaml::FromFile`).
- A struct parameter (OpenRA's `float2`) is `Unsupported`, so `float2::Max` and `::Min` cannot be given the signed zero
  their EQ006 names.
- A struct or `IntPtr` result is `NotComparable` on both sides, and `probe` reports two `NotComparable` outcomes as equal
  (`float2::FromAngle(float)`, EasyHook's `GetProcAddress` on a loaded module), which says nothing about the values.

When done, a constructor is called like a static method that returns the new object, a struct of supported fields can be
built from a JSON object and compared field by field, an `IntPtr` compares as its integer, and two outcomes that cannot be
compared are never reported equal.

## Spec references
ADR 0035 decisions 2 and 3, tickets M3-032, M4-009 and M5-002, `ReplayArguments`, `ProbeArguments`, `DriverSource`,
`RuntimeComparison`, `docs/runs/2026-10-17-precision-execute-verdict.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A constructor of a callable type is a driver target: its outcome is the new object's comparable form (or what it
   threw); a replay of a constructor's model binds its parameters as a method's.
2. A struct whose instance fields are all of supported kinds is a parameter kind (`probe`: a JSON object by field name)
   and a comparable result (field by field, in declaration order); `IntPtr` and `UIntPtr` compare as integers.
3. `RuntimeComparison` never calls two `NotComparable` outcomes the same; `probe`'s `equal` is false or absent for them,
   and its result says why.
4. Rerunning `probe` on OpenRA's sampled `Folder::.ctor(string)`, `float2::Max`, `float2::Min` and `float2::FromAngle`
   (with P2-241's config, if it is done; otherwise those in `OpenRA.Game`) records each outcome in Notes.

## Tests
Unit tests for each kind (constructor, struct argument, struct result, `IntPtr`, two `NotComparable`), and an integration
test against a `samples/` pair with a struct-returning method whose two sides differ.

## Size guard
If a struct needs nested structs or reference-typed fields to be useful, do the flat case and file the rest.

## Out of scope
Object (class) arguments other than through a constructor, delegates, `CancellationToken`. P2-240, P2-241.

## Notes
- Filed 2026-10-17 by P2-237. In its three runs, 30 `not-constructible` replays name a receiver type with no public
  parameterless constructor or a member that is not a method or a getter.
- Decision: a struct parameter is not a new `ExecutionTypeKind`. That enum feeds `InputGenerator`, which has no field
  list to generate from, so `DriverFactory.Classify` still says `Unsupported` for a struct and generated-input testing
  and the BCL `runtime-diff` keep refusing struct parameters. The frontend's own `DriverStruct` makes a flat struct a
  `probe` argument and a comparable result for every driver; a replay model gives no struct values, so a replay with a
  struct parameter stays not constructible.
- Decision: a struct is built as a parameter by an object initializer when no field is `readonly`, else by a visible
  constructor whose parameters are its fields in order, by type and by name ignoring case (`float2(float x, float y)`);
  otherwise it is read but not built. Generated source cannot assign a `readonly` field without reflection, which
  CLAUDE.md forbids; the constructor may normalise, but `probe` is a hypothesis, never a verdict (ADR 0036).
- Decision: a flat struct needs at least one instance field and no special type (`DateTime` stays `NotComparable`).
  .NET reference assemblies keep a struct's private fields (as `_dummy` fields), so a framework struct with hidden state
  is never flat on the modern side; if an older reference assembly stripped them on the legacy side, the two sides
  would give `Returned` against `NotComparable`, which is never reported equal (criterion 3), not a false agreement.
- Decision: `RuntimeComparison.Same` is "the same behaviour" and needs both outcomes comparable; run-to-run repetition
  (`Compare`'s nondeterminism counts, `DifferentialTester.Repeats`) uses the new `Identical`, so a side that gives the
  same `NotComparable` twice is not counted as nondeterministic.
- Decision: `probe` omits `equal` (rather than `false`) for an incomparable pair and adds `reason`, the side and outcome
  that cannot be compared, in `Replayer`'s own wording (`the legacy side gave NotComparable "T"`). A client reading
  `equal` as a boolean must now handle its absence: `Release: minor` (0.x).
- Decision: the `samples/` integration test is `samples/float-arithmetic`'s new `Warmer(Celsius, Celsius)` (`>=`
  against `>`, so `+0` against `-0` differs), not a new sample: the sample already has the `Celsius` struct, and the
  pair is EQ002 `bounded+refined` in its snapshot.
- Size guard: tripped as the ticket foresaw. Structs with array or object fields are P2-245's (criterion 3); auto-property
  state and nested flat structs are filed as P2-246.
- Locally, `MatchesTheCheckedInSarifSnapshotAndItsReadmesExitCode` fails for the 12 SDK-style samples whose restore this
  worktree never ran (no SARIF is written); the .NET Framework samples, `float-arithmetic` included, pass.
