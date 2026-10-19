# P2-245 Execution reach: a replay or `probe` driver calls a private member, builds a receiver with no public parameterless constructor from the model's fields, and builds the arguments the model determines
Status: in-progress
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-240, P2-242

## Goal
ADR 0062. P2-237 (`docs/runs/2026-10-17-precision-execute-verdict.md`) found two things no other ticket owns:
1. Every EQ002 replay of the three migration pairs (37) was `not-constructible`: replay never built a solver model's
   inputs for any sampled result, and the 16 confirmations came from `probe` on inputs a hand trace chose. Over the 277
   `not-constructible` replays, 59 name a member that is not public, 49 a model that constrains a field, a cast or an
   array, 27 a receiver with no public parameterless constructor, 5 an argument the driver cannot build.
2. Of P2-212's 71 results still undetermined, 41 are not callable by a driver (private, a receiver type with no public
   parameterless constructor, or abstract) and 10 need an argument no driver builds (`CancellationToken`, a delegate, an
   array, a struct, an object).

When done, replay and `probe` drivers (a) call a non-public method or property getter of the solution's own code,
(b) obtain a receiver for a type with no public parameterless constructor, from the model's fields in replay and from a
named constructor or named fields in `probe`, and (c) build the argument kinds of finding 2 that the model determines, so
that a solver Divergent's replay is constructible wherever its inputs are. Each input says how it was built, and the 51
results of finding 2 are re-adjudicated. Nothing here changes a verdict, a rule id, a fingerprint or an exit code:
execution confirms, never proves (ADR 0035).

The 41 (P2-237 appendix, pair and sample number): Git Extensions 0, 3, 7, 11, 12, 13, 14, 18, 19, 20, 22, 23, 33, 37,
39, 41, 44, 45, 47, 49, 51; Duplicati 0, 3, 14, 22, 24, 29, 32, 33, 34, 42, 44, 47, 50, 51; OpenRA 4, 5, 6, 21, 23, 25.
The 10: Git Extensions 16 (`IWin32Window`), 25 (`Exception`), 40 (`Action<string>`); Duplicati 6 (`MethodInfo`,
`string[]`), 30, 36, 45, 48 (`CancellationToken`); OpenRA 7 (`RSAParameters`), 20 (`Stream`, `Encoding`).

## Spec references
ADR 0062 (all decisions), ADR 0035 decision 2 and its 2026-09-28 and 2026-09-29 clarifications, ADR 0056 (open-call
answers in the replay), ADR 0026, VERIFICATION-MODEL section 6 (the replay paragraph), tickets M4-009, M5-002, P2-052,
P2-242, `src/Equiv.Frontend.CSharp/Execution/ReplayArguments.cs` (`CallObstacle`, `Obstacle`, `Constructible`),
`ProbeArguments`, `DriverSource`, `ReplayDriverFactory`, `ProjectEmitter`, `src/Equiv.Cli/Mcp/ProbeTool.cs`,
`docs/runs/2026-10-17-precision-execute-verdict.md` (Replay, the "still undetermined, by obstacle" paragraph, Appendix).

## Acceptance criteria (all must hold; nothing beyond them)
1. A `private`, `protected` or `private protected` method or property getter of the solution, or one of a type so hidden,
   is a driver target on both runtime kinds (.NET Framework 4.8 or Mono, and .NET 5 or later), reached as ADR 0062
   decision 1 says: bound once by its exact signature, its exception reaching the outcome unwrapped, both sides by one
   mechanism when either needs it. A member not bound is `not-constructible` with a `replayReason` naming it.
2. A replay's receiver is built by ADR 0062 decision 2: `new T()` or a constructor-free default instance when the model
   constrains no field the bodies read, and otherwise a constructor-free instance carrying the model's field values,
   with reference-typed values, arrays and cast-named types built by the same rule, one instance per model element. A
   framework or third-party type is never built without a constructor.
3. The argument kinds of ADR 0062 decision 3 are built: `CancellationToken`, an array, a delegate answering by the
   model's interpretation of its invocation, an object by decision 2, a struct with array, string or object fields
   (extending P2-242). Window and desktop-session types and `extern` methods stay not constructible.
4. `probe` takes a receiver as a constructor with JSON arguments or as a solution type with JSON fields, and the
   argument kinds of criterion 3 in JSON (ADR 0062 decision 4). The tool's description and the README's MCP section say
   so; the schema-changing commit carries the `Release:` footer `equiv-release` says.
5. Every replay result carries `properties.replayInputs` and every `probe` result its inputs' provenance (`value`,
   `constructor <signature>`, `uninitialized <fields>`, `delegate`; ADR 0062 decision 5). The commit that adds it
   carries `Release: minor`.
6. Finding 2, as counts against P2-237's verdict, recorded in `docs/runs/<date>-execution-reach.md` with one row per
   result (pair, number, procedure, how it was called, outcome, now, basis):
   - at least 36 of the 41 are callable (a driver is built and the member called, by replay with the model's inputs or
     by `probe` with inputs the implementer chooses); every one that is not names its reason, and that reason is not
     "private", "no public parameterless constructor" or an argument kind criterion 3 builds (expected: Git Extensions
     14 and 45 by ADR 0035's desktop rule, Duplicati 50 an abstract receiver, at most two more by the same rules);
   - at least 7 of the 10 are callable (expected not: Git Extensions 16, a window; Duplicati 6, `MethodInfo`; OpenRA
     20, abstract framework types the model does not name), with the same rule for the rest;
   - each callable result is re-adjudicated by P2-237's method (confirmed, false positive or undetermined, with the
     obstacle), a difference on an `uninitialized` input confirming only with a hand trace that the solution reaches that
     state (ADR 0062 decision 5); the file gives the migrations' Divergent precision over P2-237's 48 next to its 35.4%,
     and the ceiling next to its 73.9%. A result found not to run safely on the host (a message loop, a URL, a live
     service, a dialog) is callable but not run, and says so.
7. Finding 1, as counts: rerun `gitextensions-8522`, `duplicati-3124` and `openra-17989` in `full --execute` (compare
   mode quick, `--jobs 4`) and give, next to P2-237's Replay table and reason list, the replay values per pair and rule
   and the `not-constructible` reasons. "Not public" (59), "no public parameterless constructor" (27) and "an argument
   the driver cannot build" (5) are left only with reasons ADR 0062 leaves out, each counted; the field and array part of
   "the model constrains" (49) is split out and falls. List every EQ002 replay that is now constructible with its value,
   and every `reproduced` with an `uninitialized` input. A `not-reproduced` gets a ticket (ADR 0035 decision 2).
8. VERIFICATION-MODEL section 6's replay paragraph says what ADR 0062 decides; ADR 0062 is accepted, ADR 0035's Status
   line names it, and CLAUDE.md's "No reflection" bullet points to ADR 0062 for generated driver source.

## Design
- **Access.** Prefer a typed delegate bound with `Delegate.CreateDelegate` when every type in the signature can be named
  by the driver (a direct call: nothing to unwrap); fall back to `MethodInfo.Invoke` with the inner exception rethrown
  through `ExceptionDispatchInfo` when a hidden type appears in the signature. `BindingFlags.DoNotWrapExceptions` is
  .NET Core 3.0 and later only, so it cannot be the one mechanism. Lookup is by name and exact parameter types, nested
  types through `GetNestedType(name, NonPublic)`; an ambiguity or a miss is `not-constructible`, never a nearest match.
- **Instances.** The legacy driver compiles at C# 7.3 against .NET Framework 4.8; `FormatterServices.GetUninitializedObject`
  exists there and on .NET 5 and later (obsolete from .NET 8 as SYSLIB0050, a warning in generated code). Fields are
  written with `FieldInfo.SetValue`, which writes `readonly` instance fields; a struct is boxed, written, unboxed. Auto-
  property backing fields are named by Roslyn's `<Name>k__BackingField`; read the field symbol, do not build the name.
- **The model's heap.** A field the bodies read is a synthesised heap-map input (`IrParameterNames.IsSynthesised`), whose
  model value maps receiver elements to field values. Build the receiver element's entries; an element reached twice is
  one instance; a cycle is fine because instances are made before fields are written. A field the model does not give
  stays at its default, which the model allows.
- **Delegates.** An invoked delegate parameter is an open call; its answers are the model's function (ADR 0019's modular
  assumption, the same on both sides). Generate a lambda that looks the argument tuple up in the model's table and
  answers the model's `else` value otherwise, and throws the model's `threw` answer as one fixed exception type.
- **Pitfalls.** A static constructor runs on first touch of a type, constructor-free instance or not; that is the
  program's own behaviour and needs no handling. Two sides must get equal inputs: build both from the one bound model as
  replay does today. Generated code may not reference a type that one side lacks; a field missing on one side is a
  `not-constructible` reason, never a skipped write.

## Files
`docs/adr/0062-a-driver-may-reach-a-hidden-member-and-build-a-receiver-from-the-model.md`, `docs/adr/0035-real-runtimes-are-a-second-oracle.md`,
`docs/adr/README.md`, `docs/VERIFICATION-MODEL.md`, `CLAUDE.md`, `README.md` (MCP section);
`src/Equiv.Frontend.CSharp/Execution/ReplayArguments.cs`, `ProbeArguments.cs`, `DriverSource.cs`,
`ReplayDriverFactory.cs`, and at most two new files there (receiver and argument builders); the Core execution
contract and the SARIF writer for `replayInputs`; `src/Equiv.Cli/Mcp/ProbeTool.cs`; their test files;
`docs/runs/<date>-execution-reach.md` and the three pairs' run summaries.

## Tests
Unit tests: a private method, a private getter, a member of a private nested type, a protected member, each called on
both runtime kinds with its exception unwrapped; one side private and the other public uses one mechanism; a miss is
`not-constructible`. A receiver from model fields (a value field, a reference field shared by two fields, a cycle, an
array field, a cast-named type); a framework-typed field with no public constructor is `not-constructible`. Each
argument kind of criterion 3, and a window parameter still refused. `probe`'s receiver by constructor and by fields.
`replayInputs` for each provenance. An integration test against a `samples/` pair whose Divergent method is private on a
class with no public parameterless constructor and reads a field, whose replay is `reproduced` with an `uninitialized`
receiver.

## Size guard
More than four new files under `src/`, or any change in `Equiv.Verify.*` (the model must be read, never re-asked), means
the ticket is misread: stop and record why. If a struct with object fields needs P2-242's flat case reworked, do this
ticket's part on top of it and file the rest.

## Out of scope
Changing any verdict, rule id, fingerprint or exit code. Abstract receivers whose concrete type the model does not name,
framework or third-party instances without a constructor, window and desktop-session types, `ref`/`out` parameters,
generic members. The pair config in `probe` (P2-241), package runtime assets (P2-240), constructors as targets and flat
structs (P2-242). The second execution re-adjudication of all 71 (after this group). Making the runs faster (P2-244).

## Notes
- Filed 2026-10-17 with ADR 0062, from P2-237's findings 1 and 2. The 71 still undetermined split 41 not callable, 10
  arguments, 8 P2-241 (OpenRA 0, 2, 12, 14, 17, 26, 27, 28; 0, 2, 17 and 27 are also private, so they become callable
  here only once P2-241 is done, and are not counted in criterion 6), 5 P2-242, 4 not safe or host-dependent, 1 field
  written, 1 equal under `probe`, 1 P2-240.
- Decision (filing): non-public access -> reflection in generated driver source only, bound once by exact signature.
  Alternatives: `UnsafeAccessor` (.NET 8 and later; no side of the three migrations has it), raising accessibility in
  the emitted compilation (changes the program under test), `IgnoresAccessChecksToAttribute` with Roslyn's internal
  binder flag (needs reflection in `src/`; not a .NET Framework contract). Rule: soundness first, then 1 (mirror the
  consumer: the runtimes the drivers run on). Bar test: reverses ADR 0035's "no reflection anywhere", so a new ADR.
- Decision (filing): constrained replay receiver -> a constructor-free instance with the model's fields, solution types
  only, provenance recorded. Alternatives: a constructor a trace chooses (its state is not the model's, so
  `not-reproduced` would lose its meaning), constructor-free framework instances (runtime-internal invariants differ
  between .NET Framework and .NET). Rule: soundness first (`not-reproduced` stays an alarm; an unreachable state never
  counts as confirmed without a trace).
- Decision (filing): Depends on P2-240 as well as P2-242: a `reproduced` caused by a missing package implementation would
  be miscounted in criterion 6. Alternative: depend on P2-242 only and recheck. Rule: soundness first.
- Deviation: ADR 0062 decision 3's delegate "answers each invocation with the model's interpretation of that open call".
  The `Counterexample` a replay gets holds no call answers; they exist only in `Equiv.Verify.Z3`'s `ModelOracle`, and
  the size guard forbids any change in `Equiv.Verify.*`. A replay builds a delegate where neither of the model's runs
  calls through it (a fixed exception if called, which no model run does) and is `not-constructible` ("the model's runs
  call ...") where one does. ADR 0062's decision 3 says so as accepted; P2-249 (new) carries the answers. `probe`'s
  delegate is the constant answer decision 4 names.
- Deviation: decision 3's "a cancelled token when the model's run observes cancellation": the counterexample shows
  cancellation only as an outcome, so a token is cancelled exactly when a model run ends in
  `OperationCanceledException`, else `default`. ADR 0062 says so as accepted.
- Decision: `replayInputs` is `{legacy: [{name, built}], modern: [...]}` on every replay whose drivers ran, and absent
  when the plan built no input (not constructible before any driver). Alternatives: one flat list with a side field; an
  empty object on every replay. Rule: 1 (mirror `replayOutcomes`, the property beside it).
- Decision: heap inputs travel as tagged JSON arrays (`@u`, `@s`, `@a`, `@c`, `@r`, `@t`) read by a generated value
  builder `V.Value(wire, Type)`, with a per-driver type table and maker table (`DriverHeap`). Alternative: bake each
  replay's values into its driver's source. Rule: 2 (one decoder shared by replay and `probe`, the driver's protocol
  unchanged). The helpers (`D`, `H`, `V`) are emitted only into a driver that uses them, so a runtime-diff, testing or
  plain replay driver carries no reflection (P2-052's assertion holds).
- Decision: both sides' inputs are built from the union of both sides' synthesised inputs, so they get one state; a
  field map a side does not declare is `the model constrains ...` there, never a skipped write. The receiver is `new
  T()` only when on both sides the model gives its object no field, names no type derived from `T`, and no other input
  refers to it, and `T` has a public parameterless constructor; otherwise both receivers are constructor-free. Rule:
  soundness first.
- Decision: an element and the elements its upcasts and its passing type tests yield are one object of the one type
  each is; casts joining two references of one sort, a type test of a type the side does not resolve, and a `new.*`
  that names an input's reference are `not-constructible`. Alternative: ignore an unresolvable test (found while testing:
  `istype.<From>.System.Collections.Generic.List_1` did not resolve, and ignoring it would build the wrong type). Rule:
  soundness first.
- Decision: the access mechanism is one per pair (`DriverSource.Reach`): direct when a friend sees both members, a
  typed delegate when both allow one, else `MethodInfo.Invoke`; the exact-signature check runs in the factory only when
  the access is reflective, so a public member with a pointer still says what it said. Rule: ADR 0062 decision 1.
- Decision: differential testing of Unknowns keeps its own call rules (`CallObstacle(reach: false)`: not public, no
  public parameterless constructor), so no new observed Divergent can appear: the ticket changes no verdict. Rule:
  ticket Goal ("Nothing here changes a verdict").
- Decision: `DriverStruct` gains `Comparable`: a struct with an array or reference field is built but never compared,
  so a runtime-diff driver's result comparison is unchanged. Rule: soundness first (no new observed difference).
- Decision: a driver creates an instance without a constructor by `RuntimeHelpers.GetUninitializedObject` when its
  project's references have it (.NET 5 and later), else `FormatterServices.GetUninitializedObject`. Alternative: by the
  side's runtime family. Rule: 2 (what the driver compiles against decides what compiles).
- Decision: four new files under `src/` (Core `ReplayInput.cs`, `ReplayInputs.cs`; frontend `DriverHeap.cs`,
  `ModelObjects.cs`), the size guard's limit; CA1034 forbids nesting `ReplayInput` in Core. `HeapInputs.Part` became
  `internal` (one word, in `Lowering/`) so that the builder spells map names as lowering does.
- Decision: the corpus runs reuse P2-237's checkouts and restores (`.corpus/` of its worktree, linked into this one by a
  directory junction), so the three pairs ran on the same inputs P2-237's did.
- Decision (from the first corpus run): a replay driver reads a solution struct sent as `["@s", ...]` through the value
  builder, and a `probe` driver keeps P2-242's positional flat form (`Shape.Uninitializes`). Found as OpenRA's `float2`
  replays failing with `FormatException`: the replay sent the struct's fields while the driver read it positionally.
  Rule: soundness first (a harness failure must not read as an outcome).
- Decision (from criterion 6's first re-adjudication): ADR 0035's window rule refuses a window receiver or a window
  parameter, never a static member of a window type (Git Extensions row 41); a hidden constructor binds by its exact
  signature both as a target and as a maker (OpenRA row 6, Git Extensions row 19, Duplicati row 44); a `probe` object's
  `type` resolves a framework type too, built only by a public constructor as decision 2 says (OpenRA row 20); a struct
  may be given by its constructor (`DateTime(long)`, Duplicati row 32); constructor names in `probe` are spelled without
  nullable annotations. Alternative: leave them for a later ticket. Rule: the ticket's criterion 6 names these rows as
  callable, and each is a reading of decisions 1, 3 and 4, not a new kind.
- Decision: an EQ006 row of P2-237's appendix whose result is EQ007 here is the same result: ADR 0060 (P2-238) reports
  an unedited body a runtime change reaches as EQ007, and makes it EQ006 again when its replay is `reproduced` (decision
  4). Rule: 1 (P2-237 and P2-212 matched results by procedure identity and rule; the rule's rename is not a new result).
- Deviation: criterion 7's "an argument the driver cannot build" leaves 6, not only reasons ADR 0062 leaves out: five
  enums (P2-237's three plus two its earlier obstacles hid) and one `System.Drawing.Color`. An enum is an uninterpreted
  sort element in the model, which replay never built (ADR 0062 says "as replay builds it today"): P2-251 (new). The
  other two of P2-237's five, a delegate and an object, are built now.
- Finding (filed): four false EQ002 and nine `not-constructible` replays rest on a framework static field read as a
  free input per side (`IntPtr.Zero`, `String.Empty`): P2-250. `async` results are never awaited, so Duplicati rows 14 and
  47 stay undetermined: P2-252. A replay or `probe` driver of a Windows Desktop project runs without
  `Microsoft.WindowsDesktop.App` (Git Extensions rows 22 and 41, two EQ007 replays, and three `AppSettings` replays `reproduced` only by the modern
  side's `TypeInitializationException`): P2-253. The two drivers of a case start in different working directories, so
  three replays are `reproduced` on paths that hold them (found by `probe` while tracing criterion 7's `uninitialized`
  inputs): P2-254. No confirmation of P2-237's or this ticket's rests on either.
- Note: the `.corpus` directory of this worktree is a junction to P2-237's worktree's `.corpus`, so this ticket's runs
  (`20261019-*`) sit beside P2-237's under that worktree's `pairs/<slug>/runs/`.
