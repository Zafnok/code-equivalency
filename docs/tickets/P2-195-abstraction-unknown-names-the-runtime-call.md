# P2-195 An `abstraction` Unknown on a body a runtime rule keeps out of congruence names the runtime-changed call its candidate starts from
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-181's count (equiv `c6d5f751`, compare mode quick, the three migration pairs; its Notes) found 348
`abstraction` Unknowns among the 1,004 unedited bodies a runtime rule keeps out of congruence. In 347
of them, every entry of `properties.abstractions` is a function both sides share: `delegate` (130),
`String.Concat(string,string)` (68), `String.IsNullOrEmpty` (51), `String.Length` (37) and others. None
is the runtime-changed member. The two texts are equal, so the shared function can differ only because
its arguments differ, and they differ only because a runtime-changed call gave each side its own
result. The reviewer is told that the result depends on `String.Concat`, and the member that is the
cause is named only in `notCongruent`.

When done, such an Unknown says which runtime-changed call its candidate's two sides first differ at,
and the reason stays `abstraction`: whether the change reaches the output is still not decided.

## Spec references
ADR 0026 and ADR 0053 (abstractions), ADR 0040, VERIFICATION-MODEL section 6 (`abstractions`,
`notCongruent`), `docs/tickets/done/P2-181-*.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. An `abstraction` Unknown whose model has a runtime-changed call with different results on the two
   sides names that call's member in the result, as an entry `properties.abstractions` can hold or as a
   property beside it, decided with `equiv-decide` and documented in VERIFICATION-MODEL section 6.
2. Rerun the three migration pairs in compare mode quick; Notes give the members named, top 15.

## Tests
A unit test with a runtime-changed call feeding a shared function; a snapshot of the SARIF result.

## Size guard
Changing a verdict: stop. This is a report, not a decision.

## Out of scope
Deciding the 348 (P2-181 decided what its rules could).

## Notes
- Filed 2026-10-14 by P2-181 (criterion 1).
- Decision: where the member goes -> `properties.runtimeChangedCall`, beside `abstractions`, a string: the
  identity of the call (as `notCongruent.identity` and an `abstractions` entry write one), and not in the
  message. The first commit also appended it to the Unknown's detail; the OpenRA rerun showed that moved
  `resultFingerprint/v1` (which hashes the detail) on every result it named, which would turn a baselined
  Unknown `new`, so it was taken out again. Alternatives: an entry of `abstractions` (that list is what ADR 0053 refines on, and a
  runtime-changed member is never interpretable, so it would stop refinement and change verdicts: the size
  guard), an object with both sides' answers (the candidate's model already holds the inputs that give them).
  Rule: 4.
- Decision: which call -> the replay's oracle records, per side and in the order it answers them, every call
  to a `RuntimeChanged` callee and every application of a pure function the encoding made side-specific (ADR
  0040: `IrPure.RuntimeSensitive`); the k-th of one side is paired with the k-th of the other, and the first
  pair with the same identity, equal arguments and different answers (value, `threw`, ref outputs, heap) is
  named. Equal arguments, because only then is the difference the runtimes' own and not an earlier one's.
  Alternatives: the first runtime-changed call whose answers differ whatever its arguments (would name a call
  that only passes on an earlier difference), calls alone without pure functions (a runtime-sensitive getter
  before the call would not be "first"). Rule: 1.
- Criterion 2, the reruns: 2026-10-15, equiv `df27b45c` (this ticket's first commit, which also named the
  call in the detail; the second commit takes it out of the detail and changes no verdict or property), compare
  mode quick, default `--jobs`, one after the other, on the checkouts P2-200 fetched and restored:
  `gitextensions-8522` 419 s, `duplicati-3124` 371 s, `openra-17989` 263 s, each exit 1. Before them, `main`'s head
  `7dc353e8` on the same checkouts: 298 s, 261 s, 231 s, each exit 1. The box ran this ticket's tests during the
  first run, so the times do not compare. Every procedure has the same outcomes in both runs (0 of 13,818, 6,486
  and 10,213 results moved; rule, Unknown reason and proof method compared per procedure identity). The size
  guard holds. Two first attempts are void: run without `corpus.ps1 -Env`, Git Extensions skipped 141 legacy
  projects and Duplicati loaded none (exit 4). `main`'s Duplicati run is also void when the checkout's path is
  too long for the net472 BuildHost (exit 5, "The system cannot find the file specified").
- Criterion 2, what is named:

  | Pair | `abstraction` Unknowns | of them `runtime-rule` | named | `runtime-rule` named |
  |---|---|---|---|---|
  | gitextensions-8522 | 220 | 177 | 198 | 177 |
  | duplicati-3124 | 64 | 48 | 54 | 48 |
  | openra-17989 | 81 | 72 | 75 | 72 |
  | All three | 365 | 297 | 327 | 297 |

  Every `runtime-rule` Unknown names a call (P2-181 counted 348 at `c6d5f751`; P2-181, P2-188, P2-189 and
  P2-200 have decided some since). Of the 297, 205 name a BCL member, 22 a float-to-integer conversion
  (`conv.*`, the .NET 9 rule) and 70 a lambda's `delegate:` function, which the lowering makes side-specific
  because the lambda calls a runtime-changed member. In 204 of the 297 the named call is not in
  `abstractions`. The 30 named Unknowns outside `runtime-rule` are edited bodies. Top 15 among the 297:

  | Named call | Unknowns |
  |---|---|
  | `System.IO.Path::Combine(string,string)` | 31 |
  | `conv.f32.i32` | 19 |
  | `System.String::StartsWith(string)` | 18 |
  | `System.IO.File::ReadAllText(string)` | 12 |
  | `System.String::EndsWith(string)` | 11 |
  | `System.String::IndexOf(string)` | 9 |
  | `System.IO.File::Delete(string)` | 7 |
  | `System.IO.Path::GetDirectoryName(string)` | 6 |
  | `System.IO.Path::IsPathRooted(string)` | 6 |
  | `System.Net.WebUtility::HtmlEncode(string)` | 6 |
  | `System.String::EndsWith(string,System.StringComparison)` | 4 |
  | `System.Text.Encoding::get_Default()` | 4 |
  | `System.Text.Encoding::GetString(byte[])` | 4 |
  | `System.Uri::EscapeDataString(string)` | 4 |
  | `System.IO.Path::GetFileName(string)` | 3 |

  The 70 `delegate:` names are spread over many lambdas, so none makes the table. Over all 327 named Unknowns
  there are 137 distinct calls.
- Finding: the 70 `delegate:` names give a reviewer a fingerprint, not the member the lambda calls (most often
  `String.StartsWith(string)`, `String.Equals(string,StringComparison)`, `String.EndsWith(string)`,
  `Path.Combine(string,string)`). New ticket P2-209.
- Not this ticket's: `main` had two tickets numbered P2-202 (filed by P2-184 and by P2-200), which failed the
  `ticket-ids` check on every PR. The one P2-184 filed is now P2-210.
