# P2-166 Precision bug: thorough's budget pass reports EQ002 on a body whose text is unchanged and whose field was retyped
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-032

## Goal
On `jellyfin-13023` (net8.0 to net9.0) the pull request changes one field of
`Jellyfin.Networking.Manager.NetworkManager` from `IReadOnlyList<IPData>` to `List<IPData>`. The
text of `NetworkManager::EnforceBindSettings(MediaBrowser.Common.Net.NetworkConfiguration)` is the
same on both sides. It reads the field (`ToList()`, `Count`, a `foreach`), so those calls bind to
`List<T>`'s members on the modern side, and the behaviour is the same.

In compare mode quick the pair is Unknown (`unaligned-loop`). In compare mode thorough the budget
pass reports it EQ002 (`decidedBy: budget-pass`, `proofMethod: none`, `divergenceKind:
undetermined`, all three failure-refinement outcomes `unknown`). In the counterexample both sides
throw the same exception after the same two calls, and the two sides differ only in the output
content of the `string[]` heap, which no statement the trace reaches writes. So the Divergent rests
on state the body does not touch, and it is a false positive
(`docs/runs/2026-10-12-upgrade-verdict.md`).

The first deliverable is the repro: the smallest sample pair on which the budget pass gives EQ002
and the first pass does not, with what makes the two heaps differ (the two sides' different loop
shapes after the rebind, a havoc the larger resource limit reaches, or something else). Then make
the result what the first pass gives or better: Unknown, or Equivalent if the cause allows it.

## Spec references
ADR 0049 (the passes of thorough mode), ADR 0018 (call traces and the heap),
`docs/runs/2026-10-08-thorough-budgets.md` (what the budget pass decides),
`docs/tickets/done/P2-081-closed-call-answers-the-member-cannot-give.md` (the same kind of check, for a
call's answer), `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `## Notes` names the cause, with the sample that reproduces it.
2. The sample is not EQ002 in compare mode thorough, with an integration test.
3. A seeded behaviour change in the same sample (one statement that writes a different value to the
   field) is still EQ002 in compare mode thorough.
4. One thorough run of `jellyfin-13023` through `equiv-corpus-run`: `EnforceBindSettings` is not
   EQ002, and no result that was EQ002 or EQ006 in P2-124's thorough run and is a confirmed
   behaviour change leaves that rule. Counts in `## Notes`.

## Tests
Named in criteria 2 and 3.

## Out of scope
Aligning the two loops (the quick-mode Unknown). Other pairs.

## Notes
- Found by P2-124. One result, in the thorough run only; the quick run, which the README's
  scoreboard rests on, does not have it.
