# ADR 0049: `equiv compare` has a thorough mode and a quick mode, and measured yield chooses a technique's mode, not whether it exists

Status: accepted (2026-10-04). Decided by the user the same day; this ADR records the decision and
fixes the values.

## Context
`compare` has one setting for two jobs. A full migration is run once, and a missed deviation, or a
method a person must review by hand, costs more than hours of machine time. A day-to-day diff check
must come back quickly. The measurements on Git Extensions PR #8522 say what the knobs cost:
- Six times the budget (`resourceLimit` 30,000,000) turns 12 more of 184 timeout pairs into
  Divergent and 25 more into an Unknown with a cause, for 6.4 times the solver time (28,459 s
  against 4,438 s). 2,000,000 loses 1 Divergent and 4 such Unknowns and saves a third
  (`docs/runs/2026-10-01-timeout-budget.md`).
- The contracts pass is 2,468 s of a 7,491 s run and changes no verdict
  (`docs/runs/2026-10-02-pair-time.md`).
- ADR 0037 skips its two queries on `timeout` Unknowns to save time. ADR 0039 keeps the IL fallback
  off because it moved 1.6% of changed pairs.

The last two are one pattern. ADR 0028's rule is "An M4 ticket stays in M4 only if the census shows
it unlocking at least 5% of matched pairs". Later tickets and ROADMAP used it as a go/no-go for any
technique (P1-011, P1-018, P1-019, P1-021), each with its own denominator. The Unknowns that are
left are a long tail, so that reading rejects every technique, one at a time.

## Decision
1. **Two modes.** `equiv compare --mode thorough|quick`, the key `mode` in `equiv.config.json`, and
   `mode` on the MCP `compare` tool and in `action.yml`. The command line wins over the config. The
   default is `thorough`.
2. **Thorough is quick plus more work on what quick left Unknown.** Both modes begin with the same
   first pass. Thorough then runs further passes, and each one looks only at pairs that are still
   Unknown. A later pass's result replaces the earlier one when it is Equivalent or Divergent, or
   when it is an Unknown that is not `timeout` or `chc-timeout` and the earlier one was. Otherwise
   the earlier result stands.

   | | quick | thorough |
   |---|---|---|
   | First pass: `bound` / `resourceLimit` / `timeoutMs` | 3 / 2,000,000 / 60,000 | the same |
   | Budget pass: a pair left Unknown whose ladder hit a budget, or that has a loop or a self-call, is verified again | no | `bound` 8, `resourceLimit` 30,000,000, `timeoutMs` 600,000 |
   | Failure refinement (ADR 0037) on a `timeout` Unknown | no | yes, at the budget pass's budgets |
   | Loop ladder rungs 1 to 4 | yes | yes |
   | Rung 5's local proposer (runs only after a rung 4 timeout) | no | yes |
   | Contracts pass (ADR 0036 decision 2) | no; the Equivalent keeps its `unprovenAssumptions` | yes |
   | IL lowering (ADR 0039) | no | yes, as a pass: a pair still Unknown that meets ADR 0039's condition is verified again from its IL bodies |
   | `--execute`, when given: replay of every Divergent | yes | yes |
   | `--execute`, when given: differential testing of every Unknown | no | yes |

3. **A mode never does what needs consent.** `--execute` runs the solutions' code and
   `--invariant-model` sends IR text off the machine, so neither mode turns them on. Thorough without
   `--execute` says once on stderr that the Unknowns were not tested.
4. **Explicit settings win.** `bound`, `resourceLimit` and `timeoutMs` set in the config or on the
   command line replace the first pass's values in either mode. The config key `escalation`
   (`bound`, `resourceLimit`, `timeoutMs`) replaces the budget pass's. The budget pass never asks
   with less than the first pass. `--il-fallback` and `--invariant-model` add their step to quick.
5. **The soundness rule.** A mode changes budgets, the bound and which queries are asked. It never
   changes an encoding, an assumption, a taint or replay check, or what a verdict claims. So every
   verdict is as sound in one mode as in the other. Because thorough starts with quick's pass and
   only replaces Unknowns, quick answers Unknown where thorough may decide, and never Equivalent or
   Divergent where thorough would not. A step that fails in a later pass (a crash, ADR 0023) leaves
   the earlier result standing with a warning and does not make the run exit 5.
6. **Recorded in SARIF.** `run.properties.mode` is `{ name, bound, resourceLimit, timeoutMs,
   escalation?, explicit }`: the values the run used, the budget pass's when there was one, and the
   names of the settings given explicitly. A result a later pass produced carries
   `properties.decidedBy`: `budget-pass` or `il-pass`, and its `ladderTrace` holds both passes.
   Neither is part of the fingerprint, and no rule id or exit code changes. A run whose `--baseline`
   was written in another mode warns on stderr, since the difference shows as `new` results.
7. **Yield orders and places; it does not veto.** For a sound technique, a measurement decides
   two things: where it comes in the order of work, and whether it runs in both modes or only in
   thorough. A low yield, or a cost that usually ends in a timeout, puts it in thorough only. It
   does not leave the technique unscheduled. A technique is left out for being unsound, for needing
   consent it does not have, or because another technique already decides the same pairs. Each new
   one adds its row to the mode table in VERIFICATION-MODEL.md, in its own ticket.

## Why
- The two jobs price an Unknown differently, and no single budget serves both.
- Starting thorough with quick's pass makes decision 5 true by construction, and the larger budget
  is spent only on pairs that are still Unknown. A larger budget throughout would cost more and
  could differ from quick on a pair quick decided: one pair is Divergent at 4x and
  Unknown(abstraction) at 20x.
- Thorough is the default because the stated priority is correct answers on as much code as
  possible, then speed. Someone who wants speed asks for it, and the run records that they did.
- 2,000,000 for the first pass: on the 184 timeout pairs it gives up 5 answers and no proof. The
  limit, not the clock, still ends nearly every query (95% of those that give up do so within
  50.9 s, under the 60 s backstop), so quick's results repeat, which a baseline in CI needs.
- 30,000,000 and 600,000: the largest budget measured. It found 13 Divergents where 5,000,000 found
  1, and a Divergent is exactly the deviation a migration run must not miss.
- IL as a later pass and not a replacement: with ADR 0039's replacement one Divergent became
  Unknown(timeout) and one pair crashed (P2-078). As a pass neither can happen.
- The contracts pass never changes a verdict, so quick saves a third of the run and loses no answer.
- A 5% cut-off applied to one technique at a time is the wrong test for a long tail. P1-030 and
  P1-031 were scheduled against it on 2026-10-03 for that reason.

## Rejected
- **One mode with bigger defaults:** every diff check would pay for the migration run.
- **Quick as the default:** an Unknown that more machine time would have decided is the costlier
  mistake, and it would be the one made silently.
- **A third, middle mode:** nothing measured separates it from the other two.
- **A larger budget for every query in thorough:** see Why; it also re-spends on pairs already decided.
- **A wall-clock budget for quick** (5,000 ms cost 1,881 s against 3,001 s): results then depend on
  the machine and its load. A 20,000 ms budget run twice gave 14 of 184 pairs a different outcome,
  which a baseline cannot live with.
- **`--execute` on in thorough:** it runs unsandboxed code from both solutions (ADR 0035).
- **A new percentage bar for "runs in quick":** it would repeat the mistake decision 7 removes. The
  ticket that adds a technique decides from its own measured time and yield.
- **A different rule id or exit code per mode:** the verdicts mean the same in both.

## Consequences
- Public surface: a new option, config key, MCP input and action input, new run and result
  properties, and a changed default behaviour (a first pass at 2,000,000 where it was 5,000,000, and
  the later passes on by default). Under `equiv-release` that is a `Release: minor` footer on the
  implementing PR's final commit while the version is 0.x (a breaking change bumps minor before
  1.0.0), and the PR body says so.
- Superseded in part: ADR 0037's "A `timeout` pair is not queried" holds in quick only. ADR 0039's
  "replaces the IOperation bodies" and "off by default until 5%" become "verified as a later pass"
  and "on in thorough"; `--il-fallback` in quick keeps ADR 0039's replacement.
- ADR 0028 gains a clarification: its 5% row decides which tickets stay in M4 and nothing else.
  ROADMAP's wording that cites it as a veto changes with this ADR.
- Cost, from the measurements above, on one thread: quick on Git Extensions is about an hour where
  today's run is 2h05m. Thorough adds about 28,500 s for the budget pass, plus the refinement
  queries on the pairs that still time out, which P1-021 measures: ten hours or more. `compare`
  verifies pairs one after another; verifying them in parallel is the way to shorten that and is
  not decided here.
- Corpus runs name their mode in `SUMMARY.md`, and two runs are compared only within one mode.
- P1-021's measurement no longer decides whether the queries run on `timeout` Unknowns, only whether
  quick asks them too. P1-030 and P1-031 land in thorough and state in their own Notes whether they
  also run in quick.
- Ticket P1-032 implements this. ARCHITECTURE.md and VERIFICATION-MODEL.md sections 5 and 6 change
  in that PR, with the mode table.
