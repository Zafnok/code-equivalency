# P2-136 The delegate conversions P2-067 left opaque are counted, and the largest cause is removed
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A lambda or method group converted to a delegate that has no shareable fingerprint is an opaque node
with reason `DelegateCreation`. P2-067 made the shareable ones an `IrPure` and is done, and the
reason is still the largest one without an open owner. P1-028's row
(`docs/runs/2026-10-07-opaque-tail.md`), over the three large runs' 2,246 changed pairs:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `DelegateCreation` | 275 / 271, 107, 50 | 233 / 237, 75, 22 | 197 / 197, 156, 53 | 338 | 125 (5.6%) | 257 (11.4%) |

The marginal unlock is the changed pairs that hold this reason and otherwise only reasons an open
ticket owns. The same report's later censuses put it at 135 of 1,743 (7.7%), 133 of them on the two
Git Extensions pairs: Jellyfin's share was lambdas made runtime-sensitive by rows that had no change
point, and P2-113 removed it.

P2-067's split of 213 pairs, made before its own fix, named the causes: a runtime-sensitive lambda
body (41), a capture written after the delegate is created or by another lambda or local function
(15), a lambda whose body differs between the sides (12), a method group of a local function (5),
a lambda calling a local function declared outside it (4). Nobody has counted them since. Count
them, and remove the largest.

## Spec references
`docs/tickets/IOPERATION-COVERAGE.md` row `DelegateCreation` and the shared-fragments paragraph above
the table; ADR 0024 decision 2; ADR 0034 (per-ticket unlock rule);
`docs/tickets/done/P2-067-delegate-creation-owner.md` (its split and its decisions);
`docs/tickets/done/P2-127-soundness-local-function-call-is-an-unverified-callee.md`;
`docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, split the `DelegateCreation` opaque nodes of `gitextensions-8522`'s changed
   pairs (a `--lower-only` run) by why the conversion has no shared fingerprint: runtime-sensitive
   body, capture stored after creation, capture written by a lambda or local function, body that
   differs between the sides, method group with an evaluated receiver (`o.M`), method group of a
   local function, lambda that calls a local function, other. For each cause give the nodes, the
   changed pairs it is in, and the changed pairs it alone keeps opaque. Counts in `## Notes` (causes
   and delegate types only).
2. The cause with the most changed pairs alone is removed: the conversion lowers to IR, or shares its
   fragment, without the pair being reported Equivalent on anything the two sides do not both
   compute. Decide the representation with `equiv-decide` and log it; if it changes what a shared
   fragment may hold (ADR 0024 decision 2), go through `equiv-adr`'s bar test first.
3. A test per removed cause in `tests/Equiv.Frontend.CSharp.Tests`, one pair that must stay
   Unknown or Divergent because the two delegates differ, the form added to
   `LoweringOracleTests.LoweredIrAgreesWithCompiledCSharp`'s generator where it lowers, and the
   `IOPERATION-COVERAGE.md` row updated.
4. On a re-run, `changedReasonSets["DelegateCreation"]` on `gitextensions-8522` falls by at least 2%
   of changed pairs, or `## Notes` records why not.
5. Each remaining cause at or above 5% of changed pairs alone is filed as a ticket.

## Files
`src/Equiv.Frontend.CSharp/Lowering/`, its tests, `docs/tickets/IOPERATION-COVERAGE.md`,
`docs/ROADMAP.md` (criterion 5 only).

## Tests
Named in criterion 3.

## Size guard
If criterion 1 shows no cause above a third of the changed pairs alone, remove none: record the
split and stop. More than one cause removed means the ticket was misread.

## Out of scope
The IL fallback. Comparing two lambdas whose bodies differ as a pair of procedures, unless criterion
1 makes that the largest cause, in which case it starts with `equiv-adr`'s bar test. The
`LocalFunction` reason P2-127 added.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): the largest unowned reason by marginal
  unlock.
- **Criterion 1: the split (2026-10-07).** Census (`--lower-only`) of `gitextensions-8522`, legacy
  `3f4ed21998af`, modern `5190ba5c1a5f`, equiv `c59fa0f`, exit 0, 144 s, no project skipped. The
  census holds 859 changed pairs of 13,541 matched, 349 of them without opaque (40.6%).
  `changedReasonSets["DelegateCreation"]` is 49, and 103 changed pairs hold the reason at all
  (P1-028's 107 and 50 were of the 2026-10-06 run). Each `DelegateCreation` opaque node of those 103
  pairs was classified by a throwaway build that recorded, per conversion, every one of
  `FragmentFingerprinter`'s refusals that applies to it, and whether it was lowered as a function and
  made opaque again by `SsaBuilder`. That build was never committed, and its output stays under
  `.corpus/`. A node is one conversion on one side, so an unchanged lambda in a changed pair is two.

  | Why the conversion has no shared fingerprint | Nodes | Changed pairs it is in | Changed pairs it alone keeps opaque |
  |---|---|---|---|
  | Runtime-sensitive body (the lambda calls a `runtime-changes.json` member or converts floating point to an integer, inside the pair's runtime interval) | 149 | 67 | 29 |
  | Capture written by a lambda or local function (in every one, by the lambda that captures it) | 33 | 17 | 13 |
  | Method group of a local function | 28 | 5 | 3 |
  | Capture stored after creation | 20 | 7 | 1 |
  | Lambda that calls a local function declared outside it (10 nodes), or converts one to a delegate (2) | 12 | 6 | 2 |
  | Body that differs between the sides | 0 | 0 | 0 |
  | Method group with an evaluated receiver (`o.M`) | 0 | 0 | 0 |
  | Other: a struct's `this` (2 nodes), a struct's `this` and a runtime-sensitive body at once (2) | 4 | 1 | 0 |
  | Total | 246 | 103 | 48, and 1 pair with two causes (a runtime-sensitive body and a capture its lambda writes) |

  A changed pair counts under "alone" when its reason set is `DelegateCreation` and every such node in
  it has that one cause. The two empty rows are empty by construction since P2-067: a lambda that
  differs is two `delegate:` functions and no opaque, and no changed pair holds a method group with an
  evaluated receiver. By delegate type, over the 246 nodes: `Func<T, TResult>` 109, `Func<TResult>`
  33, `Action` 26, `Action<T>` 24, `Comparison<T>` 18, `FileSystemEventHandler` 12, `EventHandler` 8,
  `Action<T1, T2>` 4, `ErrorEventHandler` 4, and 2 each of `Predicate<T>`,
  `TabControlCancelEventHandler`, `RenamedEventHandler` and `MatchEvaluator`; 216 are lambdas and 30
  method groups. The runtime-sensitive nodes are mostly `Func<T, TResult>` (99) and `Comparison<T>`
  (16): predicates, selectors and comparers that compare or search strings.
- **Size guard.** The runtime-sensitive body alone keeps 29 of the 49 pairs opaque (59%), above a
  third, so it is the one cause removed. No other cause reaches a third (the next is 13 of 49).
- Decision: the size guard's "above a third of the changed pairs alone" -> a third of the changed
  pairs the reason alone keeps opaque (49, so 17 pairs). Alternatives: a third of all 859 changed
  pairs, which no opaque reason of any kind reaches (the whole reason is in 103), so the ticket could
  never remove a cause. Rule: 3 (the reading criteria 2 to 4 can then pin).
- Decision: a node with two causes at once -> its own row, counted under neither cause's "alone".
  Alternatives: count it under the first refusal `FragmentFingerprinter` reaches, which would make the
  split depend on the order of that method's tests. Rule: 3.
- Decision (criterion 2, `equiv-adr` bar test): a conversion that runs no code (a lambda, a static
  method, a method of `this`) and is runtime-sensitive is the same `IrPure` `delegate:<fingerprint>`
  of the same reads as any other, with `IrPure.RuntimeSensitive` set -> row 1 of the bar test, a dated
  clarification in ADR 0024 and one in ADR 0025; no new ADR. ADR 0025's Decision already makes a
  function side-specific "where behaviour differs between .NET Framework and .NET 10", and
  `PureEncoder` already names such a function `old.` and `new.`; ADR 0025's clarification of
  2026-10-01 kept a runtime-sensitive lambda opaque only as the smaller change (P2-067's own Decision
  line), and its last sentence is what the new clarification replaces. ADR 0024 decision 2 is
  unchanged for every fragment that is a call event: nothing runtime-sensitive is shared. No Core
  contract, SARIF shape, rule id, verdict meaning or gate changes. Alternatives: a new ADR; a
  function with a name of its own (`delegate-own:`), which says in the name what the flag already
  says and would need the encoder, the abstraction kinds and the corpus skill to learn it; comparing
  the two lambda bodies as a pair of procedures (out of scope unless a differing body were the
  largest cause, and it is zero). Rule: `equiv-adr` bar test, then 1 (the flag is what the encoder
  consumes).
- Decision: how the lowerer learns a fragment is runtime-sensitive -> `FragmentFingerprinter.Of`
  returns the fragment with a `RuntimeSensitive` flag, and `IrLowerer.Fragment` refuses a flagged one
  unless the caller asks for it, which only `Delegate` does. Alternatives: a second entry point on
  the fingerprinter that repeats `Of`'s tests. Rule: 4.
- Decision: a runtime-sensitive method group whose receiver is evaluated (`s.StartsWith`) -> stays
  the unshared opaque it was. Alternatives: lower it with a null check; it is a call event, which
  ADR 0024 decision 2 does not share when runtime-sensitive, and no changed pair on Git Extensions
  holds one. Rule: 4.
- Decision: criterion 3's pair "that must stay Unknown or Divergent because the two delegates
  differ" -> `SharedFragmentTests.ARuntimeSensitiveLambdaIsUnknownAbstractionNamingEachSidesOwnDelegate`
  in `Equiv.Tests.Integration`, on two pairs: both sides writing the lambda alike (the delegates
  differ because each runs on its own runtime), and the two lambdas differing. Alternatives: a test in
  `Equiv.Frontend.CSharp.Tests`, which does not reference the backend and so can give no verdict; the
  per-cause tests there are `DelegateLoweringTests`'s. Rule: 3.
- Decision: criterion 3's generator form -> `z = ((Func<int, int>)(t => unchecked((int)(double)t + a))) != null;`,
  drawn only by a second generator, `LoweringOracleGen.MethodWithLambda`, which
  `LoweredIrAgreesWithCompiledCSharp` now uses. The lambda is created and not invoked: the IR gives a
  delegate no body to run, so what the oracle compares is that creating it throws nothing, writes
  nothing and is not null, on every input. Alternatives: add the statement to `LoweringOracleGen.Method`
  itself, which `CongruenceSoundnessTests` also draws from and which asserts that every method is
  congruent with itself across .NET Framework 4.8 and .NET 10, false of a runtime-sensitive body; a
  lambda that is also invoked, which needs the test's call oracle to run the lambda's body. Rule: 4.
- **Criterion 4: the same census after the change (2026-10-08).** Same pair, same checkouts, this
  branch, exit 0, 133 s, no project skipped. `changedReasonSets["DelegateCreation"]` fell from 49 to
  21: 28 pairs, 3.3% of the 859 changed pairs, against a bar of 2% (18 pairs). Changed pairs holding
  the reason at all fell from 103 to 38. `changedPairsWithoutOpaque` rose from 349 to 377, so the
  lowerable share (ADR 0034) is 43.9%, up from 40.6%. Bodies holding the reason fell from 274 legacy
  and 270 modern to 209 and 206. Matched, congruent and changed pairs are unchanged (13,541, 12,682
  and 859), as they must be: congruence is decided on the body's fingerprint, which this ticket does
  not touch. The 37 pairs that left a mixed set moved to the set of their other reasons
  (`switch-pattern` alone is now 66, was 54; `Conversion` 8, was 3; `DefaultValue` 14, was 12). Of
  the 29 pairs the split gave to this cause alone, 28 left the set and one still holds the reason; it
  was not classified again.
- **What this does not change.** A pair is lowerable, not decided, and these never will be Equivalent
  while the runtime rule applies: the two delegates are two functions, so a pair that hands one to a
  call is Unknown(Abstraction) naming both, where it was Unknown(Opaque). No verifying run was made.
  What the pair gains is that everything else in the method is now compared, and that the Unknown
  names the delegate instead of a whole opaque region.
- **Criterion 5.** 5% of the 859 changed pairs is 43. No remaining cause keeps that many opaque
  alone: the largest is a capture its own lambda writes, 13 pairs (1.5%), then a method group of a
  local function 3, a lambda that calls a local function 2, a capture stored after creation 1. No
  ticket is filed, and `docs/ROADMAP.md` is not touched.
- Outside the ticket's Files, what pins the old behaviour or states the rule had to follow:
  `FragmentLoweringTests.RuntimeSensitiveFragmentHasNoFingerprint` (its fragment was a lambda, now a
  query), `SharedFragmentTests` in `Equiv.Tests.Integration`, `tests/Equiv.TestSupport/LoweringOracleGen.cs`,
  VERIFICATION-MODEL section 3's paragraph on delegates, and the two ADR clarifications.
