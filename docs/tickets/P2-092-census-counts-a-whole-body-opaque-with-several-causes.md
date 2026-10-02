# P2-092 The census counts a body that is whole-body opaque for several causes
Status: in-progress
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`LoweringCensus.IsWholeBodyOpaque` asks for a shape: one block whose only instruction is an
`IrOpaque`. That was M3-014's test, written before M3-025 gave the IR a flag for it
(`IrOpaque.WholeBody`, spelled `opaque body` in the text format).

The shape is wrong in both directions:
- A method whose bound code is erroneous is lowered by `IrLowerer.Opaque` to one block holding one
  `IrOpaque` per cause, each flagged `WholeBody`, reason `unbound` (ADR 0029 decision 2: the causes
  are the diagnostics' spans). With two or more errors the block has several instructions, so the
  pair is in neither `pairsWholeBodyOpaque` nor `changedPairsWholeBodyOpaque`.
- A body that is one expression-level opaque and nothing else has the shape but not the flag. It is
  a `line`-scoped Unknown (ADR 0029 decision 4), and the census calls it whole-body.

Observed in P2-085's `full` run of `eshop-upgrade-assistant`: 17 pairs are Unknown(unbound),
`pairsWholeBodyOpaque` is 6, and the 6 are exactly the unbound pairs with a single error. Since
P2-085 a modern project that does not compile is loaded, so on a tool pair most unbound bodies have
several errors. ADR 0029 decision 3 and every corpus SUMMARY read this count.

Read the flag. One predicate, its tests, and one sentence of VERIFICATION-MODEL.md.

## Spec references
VERIFICATION-MODEL.md section 6 (the census paragraph: "A body is whole-body opaque when"), ADR 0027
(the census), ADR 0029 decisions 2 to 4, ADR 0034 (`changedPairsWholeBodyOpaque`),
`src/Equiv.Core/Ir/IrOpaque.cs` (`WholeBody`), `src/Equiv.Frontend.CSharp/Lowering/IrLowerer.cs`
(`Opaque`), P2-085's Notes (the run).

## Acceptance criteria (all must hold; nothing beyond them)
1. `LoweringCensus.Compute` counts a pair under `PairsWholeBodyOpaque`, and under
   `Changed.WholeBodyOpaque` when it is not congruent, when either body is one block with at least
   one instruction, every one of them an `IrOpaque` with `WholeBody` set.
2. A body of two `opaque body "unbound"` instructions, the first without a target, counts. Unit test.
3. None of these counts: one block whose only instruction is an `IrOpaque` without the flag; a block
   that holds a flagged `IrOpaque` and any other instruction, flagged or not; two blocks; a block
   with no instruction. Unit test.
4. VERIFICATION-MODEL.md's census paragraph gives the definition of criterion 1 and names this
   ticket.
5. No committed census changes: every `samples/*/expected.sarif.json` and every `*.verified.*` and
   `*.execute.sarif` under `tests/` is byte-identical. `## Notes` says how that was checked.

## Files
`src/Equiv.Cli/LoweringCensus.cs`, `tests/Equiv.Cli.Tests/LoweringCensusTests.cs`,
`docs/VERIFICATION-MODEL.md`, `docs/ROADMAP.md` (the P2-092 line).

## Tests
`LoweringCensusTests`: `WholeBodyOpaqueCountsOnce` (its body now says `opaque body`),
`AnUnboundBodyWithSeveralCausesIsWholeBodyOpaque`, `OnlyABodyOfFlaggedOpaquesIsWholeBodyOpaque`
(replaces `ABodyWithMoreThanTheOneOpaqueIsNotWholeBodyOpaque`), `AChangedPairIsOneThatIsNotCongruent`
(its whole-body side now says `opaque body`).

## Size guard
More than four files changed, or any change under `src/Equiv.Frontend.CSharp/`, `src/Equiv.Core/` or
`src/Equiv.Verify.Z3/`, means the ticket has been misread.

## Out of scope
How an unbound body is lowered (one opaque per cause stays). `opaqueByReason` and
`changedReasonSets`, which already count such a body once under `unbound`. A new census key. Past
`docs/runs/` summaries: they are records of the equiv that wrote them and are not recomputed.
`IlFallback`'s own shape test, which wants exactly one whole-body opaque with a read-failure reason.

## Notes
- Found by P2-085 (its Notes: "11 of the 17 unbound pairs ... are missing from `pairsWholeBodyOpaque` (6). Not changed here.").
- Decision: what whole-body opaque means to the census -> one block, at least one instruction, every instruction an `IrOpaque` with `WholeBody` set. That is `IrOpaque.WholeBody`'s own wording ("stands for the whole method") and exactly what `IrLowerer.Opaque`, the only code that sets the flag, emits. Alternatives: any flagged opaque anywhere in the body (what `LoopLadder`'s scope reads; the same answer on every body a frontend emits, but it would count a body where the opaque sits beside real instructions and so stands for less than the whole); one block of opaques whatever their flag (keeps counting a lone expression-level opaque, and starts counting two of them). Rule: 1.
- Decision: where the definition is written -> the sentence in VERIFICATION-MODEL.md section 6, no ADR clarification. `equiv-adr`'s third row fits: ADRs 0027, 0029 and 0034 say "whole-body opaque" and never give the shape; the shape was M3-014's sentence, older than the flag. Alternatives: a dated bullet under ADR 0029 (its Decision text needs no reading aid here, and P2-085's open PR edits the same section). Rule: the bar test's first row that fits.
- No `Release:` footer: the SARIF shape is unchanged and a count is corrected, which `equiv-release` files under patch.
