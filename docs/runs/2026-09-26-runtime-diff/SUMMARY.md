# runtime-diff run: gitextensions-8522, pmb-lethek__signalr.extras.autofac, pmb-shiningrush__serviceant, pmb-tomasjohansson__adapters-shortest-paths-dotnet

ADR 0035 decision 1 (ticket M3-033): `tools/corpus/corpus.ps1 -RuntimeDiff <slug> -Top 200` on the four
pairs of the 2026-09-24 census (`docs/runs/2026-09-24-census-verdict.md`), against a fresh `--lower-only`
census run with M3-033's `externalCallees` field. `equiv`: PR branch for M3-033, `tools/runtime-diff`
seed 0, 64 cases per overload, the fixed culture set (invariant, en-US, tr-TR, de-DE, ja-JP).

## Members, by pair

| Pair | kind | externalCallees (legacy / modern) | members attempted | no matching symbol | ran (cases executed) | not constructible | divergent overloads |
|---|---|---|---|---|---|---|---|
| gitextensions-8522 | human | 4677 / 4705 | 201 | 131 | 53 | 17 | 2 |
| pmb-lethek__signalr.extras.autofac | agent | 15 / 15 | 13 | 2 | 6 | 5 | 0 |
| pmb-shiningrush__serviceant | agent | 34 / 34 | 34 | 8 | 20 | 6 | 0 |
| pmb-tomasjohansson__adapters-shortest-paths-dotnet | agent | 333 / 332 | 134 | 67 | 45 | 22 | 4 |

"members attempted" is the union of both sides' top 200 `externalCallees` (fewer than 200 per side where a
side has fewer than 200 distinct external callees), with the equiv-only `<T1,T2>` generic-instantiation
suffix stripped. "no matching symbol" is `runtime-diff`'s own "no public member on both runtimes matches":
on gitextensions-8522, 127 of 131 are `System.Windows.Forms.*` (plus a few `System.Drawing.*`), because
`Equiv.Frontend.CSharp.Execution.DriverFactory` (M3-032) resolves a member against the BCL only, not
against WinForms/GDI+ — the same "BCL only" scope M3-032 states, not a new gap this run found. The rest are
generic methods called with concrete type arguments (e.g. `ConcurrentDictionary<IHub,ILifetimeScope>`),
which `--member` cannot resolve because the generic-instantiation suffix that would disambiguate them is
exactly what gets stripped (see above); `runtime-diff` itself reports the corresponding open generic as
`not constructible (generic)` instead.

Combined over all four pairs (distinct member identities, not overload files):

| | count |
|---|---|
| Distinct members with a symbol on both runtimes, cases executed | 98 |
| Divergent | 5 |
| Nondeterministic on the modern side only (no other divergence) | 1 |
| Agreed (ran, no divergence, no one-sided nondeterminism) | 92 |
| Not constructible | 42 (50 overload-occurrences across pairs; parameter-reason tally: `generic` 34, `System.Drawing.{Rectangle,Point,Size}` 12, a type parameter `T` 3, an enumerator struct 4, an event's `Invoke` 2, `System.Guid`/`System.DateTime` 2) |

The 92 agreed members are not written as `runtime-changes.json` rows; they are recorded here only, per the
ticket's Goal ("A member that agrees is recorded as tested, with its case count, in the run summary only").
Their reports (case counts, `notComparable`) are the checked-in JSON under `.corpus/runs/<slug>/runtime-diff/`
locally — not committed, per ADR 0028 (nothing from a corpus repo other than this file and `docs/runs/*-verdict.md`
equivalents is committed; the report JSON holds only generated inputs, listed in `docs/runs/README.md`'s
allowed contents, so none of it is corpus-code-derived, but it stays out of git to keep this run reproducible
from the table and this summary alone).

## Divergent members

| Member | Divergent / cases run | Cultures | Pair(s) | Already covered? |
|---|---|---|---|---|
| `System.Double::ToString()` | 280 / 320 | invariant, en-US, tr-TR, de-DE, ja-JP | tomasjohansson | Yes — documented row `System.Double::ToString(` (floating-point formatting, .NET Core 3.0) |
| `System.String::StartsWith(string)` | 50 / 320 | invariant, en-US, tr-TR, de-DE, ja-JP | gitextensions-8522 | Yes — curated row `System.String::StartsWith(` (ICU vs NLS) |
| `System.IO.Path::Combine(string,string)` | 70 / 320 | invariant, en-US, tr-TR, de-DE, ja-JP | tomasjohansson, gitextensions-8522 | **No — new measured row** |
| `System.IO.Path::GetDirectoryName(string)` | 60 / 320 | invariant, en-US, tr-TR, de-DE, ja-JP | tomasjohansson | **No — new measured row** |
| `System.IO.StreamReader::.ctor(string)` | 25 / 320 | invariant, en-US, tr-TR, de-DE, ja-JP | tomasjohansson | **No — new measured row** |

`System.String::GetHashCode()` diverged only in `nondeterministic.modern` (315 of 320 cases; 5 null
receivers throw on both sides) — nondeterministic on .NET 10 only, no divergent cases. Already covered by
the curated row `System.String::GetHashCode(`, so it gets no new row (ADR 0035 decision 1's "gets a row
too" is read together with the acceptance criterion that only a member *no existing row covers* becomes a
new row; a duplicate `Member` string is rejected by `RuntimeChangeTableTests.EveryRowHasASource`).

### New measured rows

All three share one root cause: .NET Core removed upfront validation of "invalid" path characters (an
embedded NUL among them) that .NET Framework's `Path`/file APIs performed; the file system (or, for
`StreamReader`, the OS open call) is now the only thing that can reject a bad path. This predates .NET
Core 3.0, the earliest release `docs/runtime-changes-review.md` covers, so no page there named it — this
is exactly the gap ADR 0035 exists to close.

- `System.IO.Path::Combine(string,string)` — witness: input `["\u0000", "i"]`, culture `invariant`,
  legacy `Threw System.ArgumentException`, modern `Returned "\u0000\\i"`.
- `System.IO.Path::GetDirectoryName(string)` — witness: input `["\u0000"]`, culture `invariant`, legacy
  `Threw System.ArgumentException`, modern `Returned ""`.
- `System.IO.StreamReader::.ctor(string)` — witness: input `["ä\"̈9aH"]` (a path with a
  combining-diaeresis character and an embedded quote), culture `invariant`, legacy
  `Threw System.ArgumentException`, modern `Threw System.IO.IOException` — a different exception *type*,
  not just a different outcome, because .NET 10 lets the OS reject the path instead of validating it first.

Full witnesses (input, culture and both canonical outcomes) are in `src/Equiv.Core/RuntimeChanges/runtime-changes.json`;
`docs/runtime-changes-review.md` gains the "Measured" section covering all three.

## Congruent pairs that lose congruence (Git Extensions)

Using M3-030's token-identical proxy (a pair counts as congruent only if both bodies' token sequences
match, ignoring trivia, **and** neither lowered body calls a `runtime-changes.json` member; ADR 0034):

| | `pairsCongruent` | `changedPairs` |
|---|---|---|
| Before this run's rows | 12455 | 1085 |
| After adding the 3 measured rows | 12343 | 1197 |

**112 pairs** that were silently congruent (byte-for-byte in `equiv`'s sense, so `equiv` would have said
nothing) call `Path.Combine`, `Path.GetDirectoryName` or the `StreamReader(string)` constructor and now
count as changed instead: 112 former silent Equivalents on the human pair.

## Findings

- `System.IO.Path::Combine(string,string)`, `System.IO.Path::GetDirectoryName(string)` and
  `System.IO.StreamReader::.ctor(string)` diverge on an embedded-NUL/invalid path character; added as
  `source: measured` rows (this ticket, M3-033).
- `runtime-diff --member` cannot resolve a generic method's exact instantiation (the equiv-only `<T1,T2>`
  suffix is stripped before the call, and the open generic is what gets tried); those overloads report
  `not constructible (generic)` or, when the stripped identity still names concrete non-BCL type arguments
  in its parameter list (e.g. `ConcurrentDictionary<IHub,ILifetimeScope>::TryAdd`), "no public member on
  both runtimes matches". No ticket: this is the documented behaviour of resolving a member against real
  Roslyn symbols (M3-032), not a new bug, and generic members are explicitly out of `tools/runtime-diff`'s
  scope (its README's "What runs" section).
- WinForms and GDI+ members are never resolved by `runtime-diff`, because `DriverFactory` (M3-032) targets
  the BCL only. Git Extensions' externalCallees are dominated by them (127 of 131 unmatched members), so
  the census's most-called members are mostly untestable today. No ticket: `Equiv.Execute`/`DriverFactory`
  scope is M3-032's, and the Size guard here forbids touching its engine.
