# P2-047 Divergent audit: how many Divergent results are real?
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-046

## Goal
Seeded recall says `equiv` does not miss behaviour changes. Nothing yet says how often a Divergent
result is wrong. In M4-007, replay confirmed 5 of 352 Divergent results on Git Extensions, refuted 16
and could not build 331. ServiceAnt reports 4 EQ002 while all 11 of its tests pass on both sides,
and nobody has looked at them. A user who gets false alarms stops reading the report, so Divergent
precision is the number that decides whether the tool is usable. Adjudicate a fixed set of Divergent
results by hand, report the precision, and turn every false positive into a ticket.

## Spec references
ADR 0028 (corpus rules, what may be committed), ADR 0026 (taint), ADR 0035 (replay),
`docs/runs/README.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The audit set is taken from P2-046's `full` SARIF (plain run, not `--execute`):
   - every EQ002 and EQ006 on the three agent pairs;
   - 30 EQ002 and 30 EQ006 from Git Extensions, drawn uniformly by a seeded shuffle of their
     fingerprints (seed recorded).
2. Each result is classified exactly once:
   - **confirmed**: `replay: reproduced`, `proofMethod: observed`, or a test the auditor writes
     under `.corpus/` that passes on legacy and fails on modern;
   - **false positive**: replay `not-reproduced` with a cause found, or a hand trace showing the
     model's input cannot reach the claimed difference on the real runtime;
   - **undetermined**: neither, with the obstacle named (for example "not constructible:
     non-public").
3. `docs/runs/<date>-divergent-audit.md` gives a table per pair of rule by classification, and
   Divergent precision = confirmed / (confirmed + false positive), overall and per rule. It gives
   identities, rules and classifications only. No source text, and no model values.
4. Every distinct false-positive cause is one P2 ticket with a minimal repro in this repo's own code,
   sketched in its Goal. An existing ticket that already owns the cause gets a Notes line instead.
5. `.claude/skills/equiv-corpus-run/SKILL.md`'s verdict section gains one optional line, "Divergent
   precision (from the latest audit)", which cites the audit file. It is reported only and sets no
   threshold.

## Files
`docs/runs/<date>-divergent-audit.md`, new `docs/tickets/P2-nnn-*.md`,
`.claude/skills/equiv-corpus-run/SKILL.md`, `docs/ROADMAP.md`.

## Tests
None. Tests written to confirm a result live under `.corpus/` and are never committed.

## Size guard
More than 150 results audited, or any edit under `src/`, means the ticket has been misread.

## Out of scope
Fixing a false positive. Making precision a pass/fail gate (a new ADR against ADR 0028).

## Notes
- The audit read P2-046's runs in the `corpus-runs-debug-progress` worktree's `.corpus/`: Git
  Extensions `20260929-1918-full-rerun` (the void first pair excluded) and each agent pair's
  `20260929-1608-full`; replay from the matching `--execute` runs.
- ServiceAnt's 4 EQ002 the Goal names were M4-007's. P2-046 has 0 EQ002 and 0 EQ006 on ServiceAnt, and 0
  on lethek, so the agent pairs contribute only the Tomas pair's 17.
- Replay settled 1 of 77 (`reproduced`). The other 76 were `not-constructible`, mostly because the
  divergence is in the call trace or the method is not public (P2-052), so almost everything was
  classified by hand trace.
- Decision: an EQ006 is a false positive only when the documented change cannot be reached through the
  call's arguments. EQ006's model havocs the API result, so its values are no witness. For EQ002 the
  model's input is the witness.
- Decision: a `GetHashCode` EQ006 is undetermined, not a false positive. The method's return value does
  differ between runtimes. Whether anyone relies on it is a ranking question (P2-064).
- Decision: Git Extensions sample seed 47 (the ticket number), `random.Random(47).shuffle` over the
  fingerprint-sorted results of each rule.
- Existing owners checked: no open ticket owned any of the eight causes.
