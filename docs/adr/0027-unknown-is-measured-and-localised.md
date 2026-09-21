# ADR 0027: The Unknown rate is measured before it is optimised, and every Unknown points at lines

Status: accepted (2026-09-21)

## Context
Whether the product works depends on its Unknown rate on real code, and nothing measures it.
M3-005 is the first real run, and it is the last ticket in M3. It sits behind the loop ladder,
packaging and every soundness ticket. The precision work (M3-010, M3-011, ADRs 0024 and 0025) is
ordered by guesswork. An Unknown result also points at the method declaration, not at the
construct that caused it. So on a 400-line method a reviewer has to reread the whole thing, and the
blast radius of one opaque node is the whole method for the human even when it is one line for the
engine.

## Decision
1. **Census on every run.** `run.properties.loweringCensus` in the SARIF records:
   - procedures analysed and matched pairs;
   - pairs with zero `IrOpaque`, pairs that are whole-body opaque, and congruent pairs (ADR 0024);
   - `IrOpaque` counts by reason, per side.

   It is a property bag in the existing SARIF, not a parallel schema.
2. **`--lower-only`.** `equiv compare --lower-only` runs loading, matching and lowering. It writes the
   census and the Added and Removed results, skips the backend, and exits 0. It needs neither Z3 nor
   the loop ladder.
3. **Census of the real pair before the rest of M3.** As soon as `--lower-only` exists, it runs on
   the user's real 4.8-to-10 solution pair (M3-022). The opaque-reason histogram then orders the
   precision tickets. The order in the roadmap is only a default.
4. **Unknown points at lines.** Every Unknown result carries each reached opaque node and
   abstraction in `relatedLocations`, with the reason as the message. The primary location is the
   first of them on the modern side, falling back to the procedure. `partialFingerprints` stay keyed
   by identity and rule (ADR 0010), so moving the location does not disturb baselines.
5. **Ratchet on samples.** A new `samples/business-layer` pair holds typical service-layer code.
   Its README lists each method's target verdict and the ticket that unlocks it, and its snapshot
   carries the census. Every precision ticket must move at least one method to its target verdict.

## Why
- One census run on real code answers the feasibility question in hours instead of at the end of
  M3. It is also the only evidence that can reorder the precision work.
- The census counts lowering outcomes, so it is exact without a solver. Its "pairs with zero
  opaque" plus "congruent" is an upper bound on the share of pairs that could be proved.
- The reviewer's cost scales with the lines they must read. Pointing at the node shrinks the human
  blast radius even where the engine's cannot shrink.
- A checked-in sample of typical code makes a precision regression show up as a snapshot diff in
  the PR that causes it.

## Rejected
- **A separate census JSON or Markdown output.** CLAUDE.md forbids a parallel result schema, and a
  SARIF property bag carries the same data.
- **Waiting for M3-005.** It depends on packaging and the ladder, neither of which affects lowering.
- **Downgrading Unknown to a note to make the rate look lower.** ADR 0011 already keeps Unknown
  visible through the exit code, and hiding it would lose the reason people adopt the tool.

## Consequences
- The M3 order changes: M3-014 and M3-022 go first, in parallel with M3-002.
- SARIF snapshots of every sample gain the census block when M3-003 lands.
- ARCHITECTURE.md (CLI options) and VERIFICATION-MODEL section 6 (locations of Unknown) changed in the PR that accepted this ADR.
- Tickets: M3-014, M3-022, M3-023.
