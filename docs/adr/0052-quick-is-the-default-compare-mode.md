# ADR 0052: quick is the default mode of `equiv compare`; thorough is asked for (reverses the default of ADR 0049 decision 1)

Status: accepted (2026-10-06). Decided by the user the same day, from ticket P1-032's measurement.

## Context
ADR 0049 gave `compare` a thorough and a quick mode and made thorough the default, on an estimate: about 28,500 s
for the budget pass on Git Extensions PR #8522, on one thread, taken from a run at `bound` 3. Ticket P1-032 built
the modes and measured them on that pair with `--jobs 4` (the ticket's Notes hold the tables):

- quick takes 999 s; thorough takes 37,184 s, 37 times as long. Its budget pass alone is 30,222 s;
- for that time thorough proves 1 more pair Equivalent (12,728 against 12,727), reports 41 more Divergent (7 EQ002,
  34 EQ006) and leaves 42 fewer Unknown (577 against 619);
- the 41 Divergents are not adjudicated. 12 come from the IL pass, and ADR 0039's measurement found none of that
  lowering's 21 Divergents reproduced by replay. One is checked in: `samples/business-layer`'s `Describe`, whose
  true verdict is Equivalent, is EQ002 in thorough and Unknown in quick (P2-135);
- the first thorough run did not finish: one pair unrolled at `bound` 8 for 11.5 hours and 89 GB before it was
  stopped (P1-032 added a limit);
- against the default before the modes, quick leaves 11 results Unknown, all EQ006, and loses no Equivalent.

The project's goal is to prove that code which differs in text behaves the same. Ten hours bought one such proof.

## Decision
The default mode is `quick`: `equiv compare` with no `--mode` and no `mode` in the config runs the first pass and
stops, and so do the MCP `compare` tool and the GitHub Action. `--mode thorough` (and `"mode": "thorough"`) runs
everything ADR 0049's table gives thorough. Nothing else in ADR 0049 changes: the two modes, its table and every
value in it, decision 2's replacement rule, the soundness rule, what a run records, and decision 7 (yield orders and
places a technique; it does not veto it).

## Why
- The cost is 37 times for one proof on the one large pair measured. A default is what a first run and a CI check
  get, and neither can wait ten hours.
- ADR 0049 chose thorough because "an Unknown that more machine time would have decided is the costlier mistake".
  The measurement says more machine time decides few of them, and decides them Divergent: the budget pass moved 29
  of 255 pairs to Divergent and none to Equivalent.
- The default must not report a Divergent that quick would not and that is known to be false in a checked-in sample.
  Until P2-135 adjudicates the IL pass's Divergents, that pass is for a run someone asked for.
- Quick costs almost nothing against the behaviour before the modes: 11 EQ006 results on 13,742, no Equivalent.
- Thorough stays, because it is sound and it never changes a result quick decides (0 of 12,941 on the pair), so a
  migration sign-off that can spend a night may still ask for it. Its remaining Unknowns are also better described:
  99 fewer `timeout`, and every remaining one carries ADR 0037's answers.

## Rejected
- **Keep thorough the default and lower its budgets:** ADR 0049's values were chosen from measurements this ticket
  could not redo per knob; P2-134 measures each. A default should not wait on that.
- **Remove thorough:** it is sound, it is built, and decision 7 of ADR 0049 says a low yield places a technique in
  thorough, not out of the tool.
- **A default that depends on the size of the solutions:** results would differ between two runs of the same
  command for a reason no option names, which a baseline cannot live with.
- **Thorough without the IL pass as the default:** still 8.4 hours for the budget pass on this pair.

## Consequences
- Public surface: the default behaviour of `compare` against the release before the modes is a first pass at
  `resourceLimit` 2,000,000 (was 5,000,000), no contracts pass, no failure refinement on a `timeout` Unknown, and no
  rung 5 local proposer; under `--execute` every Divergent is replayed and no Unknown is tested. Each of those is
  one `--mode thorough` away. `Release: minor` on P1-032's PR, as ADR 0049 already required.
- ADR 0049's row "`--execute`: differential testing of every Unknown" now means a default `--execute` run tests no
  Unknown. README says so beside `--execute`.
- A run with `--il-fallback` or `--invariant-model` and no `--mode` is a quick run with that step added, as ADR
  0049 decision 4 says.
- The samples' `expected.sarif.json` are quick runs. Their READMEs say what `--mode thorough` changes.
- Corpus runs are unaffected: `equiv-corpus-run` already makes every verifying run name its compare mode, and its
  rule for which to use stands (thorough for runs whose numbers describe what `equiv` decides over a whole pair).
  Such a run now takes about ten hours on a pair this size; P2-134 is where that comes down.
- P2-134 (the later passes' budgets) and P2-135 (the IL pass's Divergents) decide whether thorough becomes cheap
  and precise enough for this ADR to be revisited.
- ARCHITECTURE.md, VERIFICATION-MODEL.md section 6, README, `action.yml` and ADR 0049's status line change in
  P1-032's PR.
