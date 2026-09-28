# P1-011 Spike: would equality saturation close changed pairs that congruence and Z3 cannot?
Status: in-progress
Effort: M
Model: Opus, high effort. If you are not Opus or Fable, stop before doing anything else and tell the user to switch models; do not attempt this ticket.
Depends on: M3-015, M4-004

## Goal
Equality saturation proves two programs equal by growing an e-graph under rewrite rules until
both land in one equivalence class. It was used for translation validation by Peggy (Tate, Stepp
and Lerner, POPL 2009; Stepp, Tate and Lerner, CAV 2011), and egg (POPL 2021) and egglog (PLDI
2023) have since made it practical. ADR 0024's congruence is its degenerate case: fingerprint
equality with no rules. The gain over Z3 would come from one case only: changed pairs that keep
a non-shared opaque fragment because the two fragments differ only in a rewritable operand, for
example `F(a + b)` against `F(b + a)` inside a construct the frontend cannot lower. Nobody knows
how many such pairs exist. This spike measures that number on the corpus and then does one of
two things:
- if it clears ADR 0028's bar, writes the ADR and the implementation ticket;
- if not, records that it did not, and stops.

## Spec references
ADR 0024 (fingerprints, shared fragments); ADR 0028 (5% bar); ADR 0034 (changed pairs, reason
sets); ADR 0036 (a rewrite rule is a hypothesis until Z3 proves it).

## Acceptance criteria (all must hold; nothing beyond them)
1. A throwaway analysis under `tools/spikes/egraph/` (not `src/`, no new package) reads the
   census's changed pairs on Git Extensions. For each pair still Unknown(opaque) after M4-004, it
   compares the two sides' ADR 0024 canonical serialisations. It counts the pairs where every
   differing subtree is closed by a fixed rule set: commutativity and associativity of `+ * & |
   ^ && ||`, `x - y` ↔ `x + (-y)`, comparison flips (`a < b` ↔ `b > a`), `!(a == b)` ↔ `a != b`,
   double negation, and `if (c) A else B` ↔ `if (!c) B else A`. A minimal hand-written e-graph of
   under 400 lines is enough.
2. `docs/runs/<date>-egraph-spike.md` reports that count as a share of changed pairs, the ten most
   frequent closing rules, and the ten most frequent residual differences that no rule closed.
   Reason names and node kinds only, no source text.
3. If the share is at least 5% of changed pairs (ADR 0028's bar), write
   `docs/adr/00nn-congruence-modulo-verified-rewrites.md` (proposed) and ticket
   `P1-0nn-rewrite-congruence.md`. Every rule must carry a checked-in Z3 proof over IR (ADR 0036),
   and `proofMethod` must be `rewrite-congruence`. Otherwise add one line to ROADMAP's post-MVP
   list saying the idea was measured and at what share.
4. Nothing under `src/` changes.

## Files
`tools/spikes/egraph/**`, `docs/runs/<date>-egraph-spike.md`, and the ADR and ticket or the
ROADMAP line from criterion 3.

## Tests
None required: it is a spike. The measurement is reproducible from the command in the report.

## Size guard
A spike larger than about 800 lines means you are building the feature. Stop and write the
ADR.

## Out of scope
Rules over constructs that do not lower (`foreach` and friends), which cannot be proved by Z3
over IR. Any engine integration.

## Notes
- Result: 0 of 1,195 changed pairs (0.0%) close under the rule set; below the 5% bar, so criterion 3 is the ROADMAP post-MVP line, not an ADR. 735 of the 1,195 (61.5%) have identical serialisations and are changed only because a side is runtime-sensitive. The other 460 differ by callee, overload, literal or statement, never by an operand reordering. Report: `docs/runs/2026-09-28-egraph-spike.md`.
- Decision: population -> every changed pair, split by whether either body holds an opaque, instead of the SARIF-joined Unknown(opaque) set. Alternatives: wait for a full solver run to join against. Rule: the Unknown(opaque) pairs are a subset of the pairs holding an opaque, so zero closures over the superset is the exact count; a full run at 516413c on this 4-core Linux box was 5% done after 22 minutes. The SARIF join is implemented and was checked on `samples/business-layer` (6 changed pairs = census, 1 Unknown(opaque)).
- Decision: reaching `BodyFingerprinter.Text` without touching `src/` -> the spike's assembly is named `Equiv.Tests.Integration`, which already has `InternalsVisibleTo` on `Equiv.Frontend.CSharp`, and it wraps the production lowering through `CSharpFrontend`'s internal lowering seam. Alternatives: an `InternalsVisibleTo` for the spike (changes `src/`, criterion 4); copying `BoundSerialiser` (313 lines, over the size guard, and a copy is not the canonical text). Rule: criterion 4.
- Decision: rule guards -> each rule fires only where it is an IR identity: integral or `bool` operators with no operator method; no associativity under `checked`; operands reordered (commutativity, comparison flips) only when both are pure, since C# evaluates left to right; `x-y` only for `int`/`long`; no rule over floating point, which ADR 0025 makes uninterpreted. Alternatives: the rules unguarded. Rule: criterion 3 wants rules Z3 can prove over IR. The guards cannot hide a closure here: no residual is a reordering.
- Decision: `a == b` ↔ `b == a` counts as a comparison flip. Rule: criterion 1's list names comparison flips without restricting them to `<`/`>`.
- Decision: the spike is not in `Equiv.slnx`, has no lock file, and builds with style analyzers off. It is throwaway measurement code that no gate builds. Compiler warnings remain errors.
- Linux: Git Extensions loads and lowers on Linux with `EnableWindowsTargeting=true` and the `-Prepare` reference assemblies. `corpus.ps1 -Env` prints `MSBuildSDKsPath=/usr/local/bin/sdk/...` on this box, which is the dotnet symlink's directory, not the SDK's. The run left it unset. Not fixed here (out of scope).
- `.corpus/` SARIF files start with a UTF-8 BOM, so `JsonDocument.Parse(byte[])` rejects them; read them as text.
