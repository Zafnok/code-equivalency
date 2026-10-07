# P2-137 The callee pairs behind `rebound-call` are counted, and the largest ones that are one call get a catalogue entry
Status: todo
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
