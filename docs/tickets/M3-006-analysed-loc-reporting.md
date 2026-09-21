# M3-006 Report analysed lines of code per codebase
Status: todo
Effort: S
Model: Sonnet, high effort.
Depends on: M3-003

## Goal
The BUSL free tier in `LICENSE` permits production use only while no analysed codebase exceeds
50,000 lines, measured per codebase rather than summed across a comparison. Nothing today tells
a user which side of that line they are on, which makes the cap unenforceable and, worse,
impossible to comply with in good faith. Emit a line count for the legacy and modern codebases
in the CLI summary and in the SARIF run properties.

## Spec references
LICENSE (Additional Use Grant, limit (b)); docs/adr/0017-licensing-and-ip.md (Consequences);
docs/VERIFICATION-MODEL.md; docs/adr/0006-output-and-surface.md

## Acceptance criteria (all must hold; nothing beyond them)
1. `equiv compare` prints the analysed line count for the legacy and modern codebases as two
   separate numbers. They are never summed or presented as a single total — the licence measures
   each codebase separately, and a combined figure would misstate compliance in both directions.
2. The same two counts appear in the SARIF output under the run's `properties`, with names that
   say what they measure.
3. The counting rule is stated in one place, documented, and applied to both sides identically:
   which files are counted, and whether blank and comment lines are included. The number that
   appears must be the number the licence means.
4. `--dry-run` reports the counts without running verification, so a user can check where they
   stand before committing to a full run.
5. The counts appear whether the verdict set is empty or not.
6. No licence enforcement, nag, telemetry or behaviour change is attached to the numbers. They
   are reported and nothing else.
7. `./build.ps1 -Integration` is green, 100% line and branch coverage holds, and the updated
   `.verified.txt` snapshots are reviewed rather than blindly accepted.

## Files
- `src/Equiv.Cli/` (summary output)
- `src/Equiv.Core/Reporting/SarifReportWriter.cs` (run properties)
- wherever the frontend already knows the loaded document set, for the count itself
- `README.md` (document the counting rule), `docs/VERIFICATION-MODEL.md`
- affected `.verified.txt` snapshots

## Tests
- Unit: counts for a known fixture pair match a hand-computed number.
- Unit: legacy and modern counts are reported independently and are not summed.
- Snapshot: SARIF run properties carry both counts.
- Integration: `--dry-run` against a `samples/` pair reports counts.

## Size guard
Six files. This is a reporting change; if it is touching the IR or the matcher, it has drifted.

## Out of scope
- Enforcing the limit, gating on it, phoning home, or printing a licence warning. The tool
  reports a fact; compliance is the user's obligation under `LICENSE`.
- Counting seats. The three-individual cap is not observable from inside the tool and must not
  be guessed at.
- A separate `equiv loc` command.

## Notes
Decision to make when implementing, per `.claude/skills/equiv-decide`: whether "lines of code"
means physical lines in the files the frontend loaded, or non-blank non-comment lines. Pick one,
state it in `README.md` next to the licence summary, and use the same rule on both sides. The
figure only has to be honest and reproducible; it does not have to match any other tool's
definition. Whichever is chosen, `LICENSE` and the README summary must not contradict it.
