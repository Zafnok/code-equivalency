# P2-137 The callee pairs behind `rebound-call` are counted, and the largest ones that are one call get a catalogue entry
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
ADR 0042 makes a call opaque, with reason `rebound-call`, when the same source text binds to another
callee on the modern side, until an API-equivalence entry (ADR 0020) says the two callees are one
call. P2-070 added three entries and is done. No ticket owns the rest. P1-028's row
(`docs/runs/2026-10-07-opaque-tail.md`), over the three large runs' 2,246 changed pairs:

| Reason | gitextensions-8522: bodies (legacy / modern), in, alone | gitextensions-9860 | jellyfin-13023 | Sum: in | Sum: alone | Marginal unlock |
|---|---|---|---|---|---|---|
| `rebound-call` | 194 / 194, 156, 113 | 25 / 25, 25, 25 | 69 / 69, 67, 11 | 248 | 149 (6.6%) | 191 (8.5%) |

Alone it is 11.9% of `gitextensions-8522`'s changed pairs, over ADR 0028's 5% line, and the later
censuses in the same report put its marginal unlock at 182 of 1,743 (10.4%), the largest of any
unowned reason. The three runs' `properties.reboundCalls` hold 66, 17 and 15 distinct callee pairs.
The largest, by results that name them:
- `gitextensions-8522`: `System.InvalidOperationException::.ctor()` to
  `System.Runtime.CompilerServices.SwitchExpressionException::.ctor()` 41 (the throw the compiler
  adds to a switch expression with no default arm); FluentAssertions `ActionAssertions::Throw<T>` to
  `DelegateAssertions<Action>::Throw<T>` 46 over three exception types; `System.IO.Abstractions`
  `FileBase::Exists` to `IFile::Exists` 11; `String::TrimEnd(char[])` to `TrimEnd(char)` 15, which
  P2-070's entry has since removed (it is gone from the 2026-10-06 run), and to `TrimEnd()` 7,
  which is still there.
- `jellyfin-13023`: `String::Format(IFormatProvider,string,object[])` to the `ReadOnlySpan<object>`
  overload 16; `TimeSpan::FromHours(double)` to `FromHours(int)` 16, `FromMilliseconds(double)` to
  `FromMilliseconds(long,long)` 14, `FromMinutes(double)` to `FromMinutes(long)` 10,
  `FromSeconds(double)` to `FromSeconds(long)` 9.

Some of these are one call under another name and some are not: `SwitchExpressionException` is a
different exception type, and most of what is left on Git Extensions is third-party test libraries,
so the base class library pairs are mostly Jellyfin's. Count the callee pairs, decide for each large
one whether the two callees compute the same thing, and add an entry for those that do.

## Spec references
ADR 0042; ADR 0020 (the API-equivalence catalogue and its adapters); ADR 0034 (per-ticket unlock
rule); `docs/tickets/done/P2-069-call-rebound-by-a-dependency-upgrade.md`;
`docs/tickets/done/P2-070-bcl-overload-rebinding.md`; `docs/runs/2026-10-07-opaque-tail.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Before any code, from the `reboundCalls` of `gitextensions-8522`'s and `jellyfin-13023`'s
   results (the runs' SARIF, or a new `--lower-only` run if the property is absent there), a table
   in `## Notes`: each callee pair in at least five results, the results that name it, the changed
   pairs it alone keeps opaque, and one of: the two callees are one call on every argument (say
   why, from the documented behaviour of both); they agree only on some arguments (say which); they
   differ; they are third-party members whose bodies are not in the run.
2. Every pair of base class library callees that criterion 1 calls one call on every argument gets
   an entry in `src/Equiv.Core/ApiEquivalences/api-equivalences.json`, with an adapter where the
   argument lists differ. A pair that agrees only on some arguments gets none.
3. A unit test per new entry and adapter, and a sample pair under `samples/` with one method per new
   entry that is Equivalent with the entry listed in `properties.equivalencesApplied`, and one method
   that calls the two callees with arguments on which they differ and stays Divergent or Unknown.
4. On a re-run, the changed pairs of `jellyfin-13023` that hold `rebound-call` fall by at least 5%
   of its changed pairs, or `## Notes` records why not.
5. `## Notes` gives, for `gitextensions-8522`, the changed pairs that only third-party callee pairs
   and the `SwitchExpressionException` pair keep opaque, so that the next ticket has its count.

## Files
`src/Equiv.Core/ApiEquivalences/`, its tests, `samples/` and the integration test that runs the new
sample, `docs/ROADMAP.md` only if a follow-up ticket is filed.

## Tests
Named in criterion 3.

## Size guard
More than eight new entries: stop and file the rest. Any change to when a call counts as rebound
(ADR 0042's decision) is a different ticket.

## Out of scope
Entries for third-party libraries (FluentAssertions, NSubstitute, `System.IO.Abstractions`): the
catalogue holds base class library members, and their place is the config's call-identity map.
The compiler's `SwitchExpressionException` throw, unless criterion 1 finds the two sides reach it on
the same inputs, in which case file it.

## Notes
- Found by P1-028 (`docs/runs/2026-10-07-opaque-tail.md`): the second largest unowned reason by
  marginal unlock, and the largest on the later censuses.

### Criterion 1: the callee pairs (2026-10-07)
- Runs: `equiv compare --lower-only` of both pairs, default config, on `main` at `c59fa0fc`, before
  this ticket's change. A census has no results, and no SARIF holds a pair's reason set next to its
  `reboundCalls`, so each run also wrote, through a local patch that is not in this PR, one line per
  matched pair: identity, congruent or not, reason set, rebound pairs. The "results that name it"
  column agrees with the latest full SARIF of each pair (`gitextensions-8522` quick on `main`,
  2026-10-07, 179 results with `reboundCalls`; `jellyfin-13023` full, 2026-10-03).
- Decision: the baseline is today's `main`, not the runs the Goal quotes -> the counts below.
  Alternatives: the 2026-10-02 and 2026-10-03 SARIF files. Rule: 3. Since those runs congruence
  has grown, so `jellyfin-13023` has 162 changed pairs today where it had 573, and
  `gitextensions-8522` 859 where it had 951. `rebound-call` is in 69 of the 162 (42.6%) and alone in
  12 (7.4%); on `gitextensions-8522` it is in 140 of 859 (16.3%) and alone in 107 (12.5%).
- "Alone" below: the changed pairs whose reason set is exactly `rebound-call` and whose
  `reboundCalls` is exactly this callee pair. A callee pair is counted by its whole identity, so a
  generic method is one pair per type argument.

`gitextensions-8522`: 179 matched pairs carry `reboundCalls` (39 of them congruent), 66 distinct
callee pairs, 12 in at least five results.

| Legacy callee -> modern callee | Results that name it (changed pairs among them) | Alone | Finding |
|---|---|---|---|
| `System.InvalidOperationException::.ctor()` -> `System.Runtime.CompilerServices.SwitchExpressionException::.ctor()` | 41 (2) | 0 | They differ: two exception types, and the type is an observable. It is the throw of a switch expression that matches no arm, so with the same source both sides reach it on the same inputs. Filed as P2-144, as the out-of-scope list says. 39 of the 41 are Equivalent by congruence. |
| `FluentAssertions.Specialized.ActionAssertions::Throw<ArgumentNullException>(string,object[])` -> `DelegateAssertions<Action>::Throw<ArgumentNullException>(string,object[])` | 26 (26) | 26 | Third-party members whose bodies are not in the run. |
| the same, `Throw<ArgumentException>` | 12 (12) | 12 | Third-party. |
| the same, `Throw<ArgumentOutOfRangeException>` | 8 (8) | 8 | Third-party. |
| `FluentAssertions.Specialized.AsyncFunctionAssertions::Throw<ArgumentNullException>(string,object[])` -> `DelegateAssertions<Func<Task>>::Throw<ArgumentNullException>(string,object[])` | 7 (7) | 7 | Third-party. |
| `System.IO.Abstractions.FileBase::Exists(string)` -> `System.IO.Abstractions.IFile::Exists(string)` | 11 (11) | 2 | Third-party. |
| `System.IO.Abstractions.FileBase::Delete(string)` -> `System.IO.Abstractions.IFile::Delete(string)` | 7 (7) | 2 | Third-party. |
| `System.IO.Abstractions.DirectoryBase::Exists(string)` -> `System.IO.Abstractions.IDirectory::Exists(string)` | 5 (5) | 2 | Third-party. |
| `NSubstitute.SubstituteExtensions::Returns<FileBase>(FileBase,FileBase,FileBase[])` -> `Returns<IFile>(IFile,IFile,IFile[])` | 8 (8) | 4 | Third-party. |
| `NSubstitute.SubstituteExtensions::Returns<DirectoryBase>(...)` -> `Returns<IDirectory>(...)` | 8 (8) | 2 | Third-party. |
| `System.String::TrimEnd(char[])` -> `System.String::TrimEnd()` | 7 (7) | 0 | One call on every argument. The source passes no element, so the `params` overload is given an empty array, for which it removes the trailing white-space characters; `TrimEnd()` is documented as exactly that. Neither throws, and a null receiver fails the receiver check on both sides before either is invoked. Entry `bcl.string-trim-end-no-chars`. |
| `System.StringExtensions::Contains(string,string,System.StringComparison)` -> `System.String::Contains(string,System.StringComparison)` | 5 (5) | 4 | Not a base class library pair: the legacy callee is the repository's own extension method, declared in namespace `System`, and its body is in the run. No entry. |

`jellyfin-13023`: 71 matched pairs carry `reboundCalls` (2 congruent), 15 distinct callee pairs, all
base class library members, 5 in at least five results.

| Legacy callee -> modern callee | Results that name it (changed pairs among them) | Alone | Finding |
|---|---|---|---|
| `System.String::Format(System.IFormatProvider,string,object[])` -> `System.String::Format(System.IFormatProvider,string,System.ReadOnlySpan<object>)` | 16 (16) | 0 | One call on every list of elements: both format the same arguments and throw `ArgumentNullException` for a null format and `FormatException` for a bad one; the array overload's exception for a null array cannot happen when the compiler builds the array. No entry can be written: see the Deviation below. Filed as P2-143. |
| `System.TimeSpan::FromHours(double)` -> `System.TimeSpan::FromHours(int)` | 16 (16) | 2 | They agree only on some arguments: the same value while the result is in the range of `TimeSpan`; outside it the `double` overload throws `OverflowException` and the integer overload `ArgumentOutOfRangeException`. No entry. Filed as P2-142. |
| `System.TimeSpan::FromMilliseconds(double)` -> `System.TimeSpan::FromMilliseconds(long,long)` | 15 (15) | 5 | The same: in range only. No entry. P2-142. |
| `System.TimeSpan::FromMinutes(double)` -> `System.TimeSpan::FromMinutes(long)` | 10 (10) | 3 | The same: in range only; a `long` above 2^53 also loses bits on its way to `double`. No entry. P2-142. |
| `System.TimeSpan::FromSeconds(double)` -> `System.TimeSpan::FromSeconds(long)` | 10 (10) | 1 | The same. No entry. P2-142. |

Under the floor of five results and so not judged: on `gitextensions-8522`
`String::TrimStart(char[])` -> `TrimStart(char)` 3 and `String::Trim(char[])` -> `Trim(char)` 2
(each the one-element form P2-070 added for `TrimEnd`), `StringBuilder::Append(object)` ->
`Append(StringBuilder)` 4; on `jellyfin-13023` `IReadOnlyCollection<T>::get_Count()` ->
`List<T>::get_Count()` 4, `TimeSpan::FromDays(double)` -> `FromDays(int)` 1 and three more
`params` array to `params ReadOnlySpan<T>` pairs (`String::Join` 2, `StringBuilder::AppendFormat` 1,
`Path::Combine` 1), which P2-143 lists.

### Criterion 2: one entry
- Added: `bcl.string-trim-end-no-chars`. Refused: the four `TimeSpan` pairs (they agree only in
  range) and the repository's own `Contains`. One entry, so the size guard of eight is far off.
- Deviation: criterion 2 asks for an entry for every base class library pair that is one call on
  every argument, and `String::Format` with `params` elements is one. It gets none. An adapter (ADR
  0020) lists argument positions and constants, so it cannot pass "the rest of the elements" on as
  a span, and the span the compiler builds on the modern side is a collection expression whose
  target is a span, which stays opaque (`CollectionExpression`; P2-120 and P2-128 leave spans out).
  All 16 changed pairs that name it hold `CollectionExpression` too, so an entry alone would prove
  none. Both halves are lowering work outside this ticket's Files; P2-143 owns them.
- Deviation: the Files list has `src/Equiv.Core/ApiEquivalences/` only, and the entry could not load
  with that alone. The lowering and the fingerprint each keyed member entries by legacy identity
  in a dictionary, and `System.String::TrimEnd(char[])` already has P2-070's one-element entry, so a
  second entry threw on every run. `IrLowerer` now tries a legacy member's entries in file order
  and takes the first whose adapter addresses the call, and `BoundSerialiser` names such a member
  by its first pass-through entry, as before. ADR 0020 has a dated clarification for it, and
  VERIFICATION-MODEL section 3 and the catalogue's header say it.
- Decision: the fingerprint, which names a callee without its arguments, keeps the first
  pass-through entry of a legacy member -> no fingerprint changes for bodies that exist today.
  Alternatives: leave a member with several entries under its legacy name; pick the entry by
  argument count. Rule: 4. The legacy tree still holds the `params` array there, so the name
  never makes two bodies congruent either way.

### Criterion 3: tests and sample
- `ApiEquivalenceTableTests` (the entry, and that entries sharing a legacy member address different
  argument counts), `ApiEquivalenceLoweringTests` (rewritten to `TrimEnd()`, receiver null check
  kept, an explicit empty array not rewritten), `BodyFingerprinterTests` (two entries for one legacy
  member).
- `samples/bcl-rebound-overloads`, legacy .NET Framework 4.8 and modern .NET 10: `TrimTail` is
  Equivalent (`bounded`) with `bcl.string-trim-end-no-chars` applied and no `reboundCalls`.
- Decision: the method "that calls the two callees with arguments on which they differ" is `Hours`,
  `TimeSpan.FromHours(hours)` with an `int` -> Unknown (`opaque`, scope `line`) with
  `FromHours(double)` and `FromHours(int)` in `reboundCalls`. Alternatives: none for the new entry,
  whose two callees differ on no argument. Rule: 3. A third method, `TrimTailOther`, trims `'.'` on
  the modern side and stays Divergent with the entry applied, as P2-070's `StripOther` does.
- The legacy `TrimTail` and `TrimTailOther` joined `IlLoweringParityTests`' known list for the
  reason P2-070's `Indent` is there: an empty `params` array is a fresh array in the IOperation
  lowering and `Array.Empty<char>()` in the IL.

### Criterion 4: `jellyfin-13023` does not move
- Re-run with the entry, `--lower-only`, same checkout and config: `rebound-call` is in 69 of 162
  changed pairs before and 69 after, alone in 12 and 12; bodies holding it 71 and 71 per side. The
  fall is 0, where the criterion asks for 5% of changed pairs (9 pairs).
- Why not: the pair is a .NET 8 to .NET 9 upgrade and none of its 15 callee pairs can have an entry
  under ADR 0020 as it stands. 52 results (49 changed pairs, 12 of them alone) are the five
  `TimeSpan` factory pairs, which agree only in range; 20 results are the four `params` span pairs,
  which need the adapter form and the span lowering of P2-143; the other 10 results are six pairs
  under the floor (`List<T>` members where the receiver's type changed from an interface, and
  `String::Contains(char)` to `Enumerable::Contains`, which is ADR 0042's consequence of the
  catalogue rewriting the legacy side only). P2-142 carries the 5% target for the `TimeSpan` pairs.
- `gitextensions-8522`, the pair the entry comes from, same re-run: `rebound-call` is in 140 changed
  pairs before and 133 after (0.8% of 859), bodies 179 and 172 per side, and the entry is applied
  in 7 bodies. The 7 pairs hold other reasons too, so the pairs it alone keeps opaque stay 107 and
  the changed pairs without opaque stay 349.

### Criterion 5: what only third-party pairs keep opaque on `gitextensions-8522`
- Of the 859 changed pairs, 95 (11.1%) have the reason set `rebound-call` exactly and name only
  third-party callee pairs (FluentAssertions, NSubstitute, `System.IO.Abstractions`, ExCSS,
  AdysTech.CredentialManager, ConEmu; a base class library generic member counts here when its
  type argument is one of their types). The `SwitchExpressionException` pair adds none: it is in 2
  changed pairs, both with `switch-pattern` as well. So the count for the next ticket is 95.
- The other 12 pairs that `rebound-call` alone keeps opaque: 6 name only the repository's own
  members (a test accessor that moved, its own extension methods) and 6 only base class library
  pairs under the floor of five.
- With other reasons next to it, 108 changed pairs name only third-party callee pairs. The four
  FluentAssertions `Throw<T>` pairs in the table alone keep 53 opaque, and every FluentAssertions
  pair together 69.
- No ticket is filed for the third-party pairs: ADR 0042 rejected sharing two callees whose bodies
  are not in the run, and the config's call-identity map is where a user says two are one.

### Other
- The corpus checkouts were not fetched again: the runs read the restored checkouts of two earlier
  worktrees (`gitextensions-8522` and `jellyfin-13023` at the commits `tools/corpus/pairs.csv`
  pins), under the machine's run lock, and wrote their run directories outside the repository.
- Follow-ups filed: P2-142 (`TimeSpan` factories, in range), P2-143 (`params` span overloads),
  P2-144 (the switch expression's throw).
