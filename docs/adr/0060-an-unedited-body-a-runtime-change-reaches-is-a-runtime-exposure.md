# ADR 0060: An unedited body that a runtime change reaches is a runtime exposure (EQ007), not a Divergent or an Unknown

Status: accepted (2026-10-17). Supersedes in part ADR 0040 decision 2 and VERIFICATION-MODEL section 6 (what a pair a
runtime rule alone keeps out of congruence is reported as), and ADR 0058 decision 4 and ADR 0054 decision 4 (the rule id
of an unedited `extern` function a runtime change reaches). Extends ADR 0019's `unprovenAssumptions` to a suppressed
runtime-changes row.

## Context
P2-212's verdict (`docs/runs/2026-10-15-precision-verdict.md`, "Per pair") counts, over the five large pairs, 880 of
2,405 changed pairs (36.6%) whose `properties.notCongruent` is `runtime-rule` (P2-173): the two bound texts are equal,
and only a `runtime-changes.json` row, a built-in runtime rule or a `marshalling` entry keeps them out of congruence
(ADR 0040, ADR 0058). They are 259 of the 341 Divergent (every one EQ006), 620 of the 1,298 Unknowns (timeout 183,
abstraction 270, opaque 153, unaligned loop 14; the per-pair SUMMARYs), and the solver proves 1. The Unknowns name
`Path.Combine(string,string)` 79, `extern` 76, `String.StartsWith(string)` 68, `File.Delete` 26 and
`String.EndsWith(string)` 21 first. Every EQ006 the audit decided was a false positive (0 confirmed). Precision
tickets attack the class one route at a time (P2-074, P2-188, P2-200, P2-201, P2-207, P2-208, P2-217, P2-218, P2-228,
P2-231; open P2-222, P2-223, P2-229, P2-236), and each clears 1 to 5 results. Meanwhile EQ006 tells a reviewer "this
pair diverges" about code nobody edited, at `error`, and the Divergent precision row scores the runtime table's reach
as if it were the edit's.

## Decision
1. **The class.** A matched pair with bodies whose `notCongruent.component` is `runtime-rule` is a **runtime
   exposure**: its texts are equal, so under ADR 0024 the only thing that can make its two sides behave differently is
   the runtime change the rule names. Nothing else is: a pair whose text differs and that also calls a row's member keeps
   EQ002, EQ003 or EQ006 as today, because the edit is in play.
2. **The analysis does not change.** The pair is lowered and sent to the solver exactly as today, with each
   runtime-changed member a function per side. Only the outcome's mapping changes:
   - Equivalent stays EQ001 (the 1 today);
   - what is EQ006 today becomes **EQ007** with `properties.model`, the input on which the runtime-changed call decides
     the outcome (a *witnessed* exposure; 259 today);
   - what is EQ003 today, any reason, becomes **EQ007** without a model, keeping `properties.unknownReason` as the
     reason the solver did not decide (620 today), and an unedited `extern` function a rule reaches (ADR 0058 decision
     4, ADR 0054 decision 4) is one of these, never `no-body`;
   - an EQ002 on such a pair would mean a difference the equal texts cannot hold; it stays EQ002 and is a soundness or
     modelling finding that gets a ticket.
3. **The result.** EQ007 (`RuntimeExposure`, "an unedited body a runtime change reaches"): SARIF `kind: fail`,
   `level: warning`, a rule descriptor beside EQ001 to EQ006. The message says the body is unedited and names each rule
   and the interval: `Unedited body; the runtime change between net48 and net5.0 reaches it through
   System.IO.Path::Combine(string,string) (<url>)`. `properties.runtimeExposure` lists one entry per rule
   `notCongruent` names, legacy side first: the rule (the row's `member`, `x87`, `float-to-integer`, `extern` or
   `extern:<id>`), its `changedIn`, its breaking-change `url`, and the row's `precondition` name when it has one
   (`invalidPath`, `cultureSensitiveText`, ...), so a reviewer reads what the call must receive for the change to reach
   it. It carries `assumedCallees`, `unprovenAssumptions` and every other property a non-Equivalent result carries.
4. **Execution confirms upward only (ADR 0035).** Under `--execute`, a witnessed exposure is replayed as EQ006 is
   today. `replay: reproduced`, or a differential test that observes a difference on an unwitnessed one, makes the
   result EQ006 with that observation (`proofMethod: observed`): the runtime change is then shown, not reached. Equal
   outcomes leave it EQ007 (`replay: not-applicable`). Execution never makes an exposure Equivalent.
5. **Gate, review list, baseline.** The exit code of every run is what it is today: a witnessed EQ007 counts as a
   Divergent (exit 1), an unwitnessed one as an Unknown (exit 0, or 2 under `--fail-on unknown`). EQ007 is a flagged
   result: its review group key is EQ006's, `runtime-change:<member>`, and its tier sits after EQ006's and before
   EQ003's (six tiers, the rank formula keeping its shape). A baseline written before this ADR shows each moved result
   as `new` once, since the rule id changed (section 6).
6. **Path rows are not an assumption by default.** `invalidPath` stays a precondition the row checks; equiv does not
   assume that every path a program builds is one .NET Framework accepts. A user who knows it may say so: an entry of
   `equiv.config.json`'s `suppressRuntimeChanges`, a member as today or a precondition name (`precondition:invalidPath`,
   every row with that precondition), is an assumption the result names. Every result whose body calls a member the
   entry suppresses lists the entry in `properties.unprovenAssumptions` and in `properties.runtimeChangesAssumed`, and
   an Equivalent's message says "Assumes the runtime change does not reach: <entry>". Today the same suppression is
   silent.
7. **ADR 0028.** Its criteria read an EQ007 as the verdict it had before this ADR (witnessed as Divergent, otherwise
   Unknown), so no success criterion moves by relabelling. Seeded recall cannot meet an EQ007: a seed edits the text.

## Why
- The claim "this edit changes behaviour" is false of a body nobody edited, and "the solver could not decide" hides what
  is known: the only possible difference is a documented runtime change, named, at a call site. EQ007 states what equiv
  proved (equal texts, ADR 0024) and what it did not (whether the change reaches).
- It is sound by construction. Nothing becomes Equivalent: the 879 stay flagged, `kind: fail`, in the review list, and
  every exit code is unchanged. The class is exactly the set congruence would have proved but for a runtime rule, so its
  definition rests on the same fingerprint as every congruence proof.
- It separates two measures that one row mixed. Divergent precision then measures the edit analysis; the runtime
  table's reach gets its own count, which the route-by-route tickets move. Those tickets stay worth doing: each turns
  exposures into Equivalent by congruence, a proof.
- `kind: fail` with `level: warning` is SARIF 2.1.0's own "a problem was found" below `error`: code scanning shows it,
  and `level: none` (which `kind: review` would force) would hide 259 results that are `error` today.
- Keeping the solver keeps the one proof and the 259 witnesses that replay needs. Precision outranks run time.
- A built-in valid-path assumption would make 79 `Path.Combine` and 26 `File.Delete` Unknowns Equivalent on a fact equiv
  cannot check, and code that relies on .NET Framework's `ArgumentException` for an invalid path is the behaviour the row
  exists to show. A declared assumption, named on each result, is ADR 0059's stance applied to suppression, which today
  is the one unnamed assumption a user can make.

## Rejected
- **A property on EQ006 and EQ003, rule ids unchanged.** Every SARIF consumer, code scanning and `--fail-on` reads the
  rule id, so 259 unedited bodies would still be shown as `error` Divergent, and the scoreboard would split one rule by
  a property no consumer sees.
- **Make every exposure exit 1, or every one exit as an Unknown.** The first fails runs that pass today on 620 results
  nobody can decide; the second lets 259 that fail today pass. The exit code keeps what each result was.
- **Skip the solver for the class.** Loses the 1 proof and the witnesses replay confirms, for run time, which ranks last.
- **`kind: review`.** SARIF requires `level: none` for any kind but `fail`, which hides the result from code scanning.
- **Assume valid paths by default, or per row in the shipped table.** Above: a false Equivalent on exactly the change.
- **Keep fixing routes only.** Each ticket clears 1 to 5 of 880; after fourteen the class is still a third of the
  changed pairs, reported under the wrong claim. Routes remain the way an exposure becomes a proof.
- **ADR 0037's per-property EQ007.** That proposal doubled results; this one moves each result to one rule id, so the
  result count is unchanged.

## Consequences
The README scoreboard under this decision, from P2-212's numbers (the per-pair SUMMARYs give the split). **No row that
counts proofs moves: 879 results that were EQ006 or EQ003 are relabelled EQ007, and are not proofs.**

| Row | P2-212 | Under ADR 0060 |
|---|---|---|
| Proved Equivalent | 97.2% (56,838 of 58,477) | 97.2% (56,838 of 58,477), unchanged |
| Equivalent by congruence | 95.9% (56,072 of 58,477) | 95.9%, unchanged |
| Changed pairs the solver proves | 31.9% (766 of 2,405) | 31.9%, unchanged |
| Unknown | 2.2% of pairs (1,298), 54.0% of changed pairs; opaque 573, abstraction 362, budget 326, unaligned loop 33, recursion 4 | 1.2% of pairs (678), 28.2% of changed pairs; opaque 420, abstraction 92, budget 143, unaligned loop 19, recursion 4 |
| Divergent | 0.6% of pairs (341), 14.2% of changed pairs | 0.1% of pairs (82: EQ002 53, EQ006 29), 3.4% of changed pairs |
| Runtime exposure (new row) | | 1.5% of pairs (879), 36.5% of changed pairs; 259 with a witness input, 620 without |
| Divergent precision | migrations 2.2% (1 of 46), upgrades 85.7% (6 of 7), cleanups none left to decide | migrations between 2.2% and 4.5% (1 of 22, EQ002 alone): the sampled EQ006 that are exposures leave the row, and P2-238 counts which; upgrades and cleanups unchanged (no EQ006 decided) |
| Left for a reviewer | 2.8% of pairs (1,639) | 2.8% of pairs (1,639: Unknown 678, Divergent 82, exposure 879), unchanged |
| Soundness, large pairs that run, run time, repeatability | | unchanged |

Per pair, exposures are 475 on `gitextensions-8522`, 6 on `gitextensions-9860`, 10 on `jellyfin-13023`, 236 on
`duplicati-3124` and 152 on `openra-17989`; Divergent becomes 36, 6, 1, 38 and 1, and Unknown 205, 200, 48, 182 and 43.
The work a reviewer is handed is the same size, now sorted by claim.

- Ticket P2-238 implements decisions 1 to 5 and 7, reruns the five large pairs and applies `equiv-scoreboard`, adding
  the Runtime exposure row to the README and to the skill's row table. Ticket P2-239 implements decision 6. Neither
  needs the other.
- SARIF gains a rule (EQ007) and two properties (`runtimeExposure`, `runtimeChangesAssumed`): `Release: minor`
  (0.x). No parallel schema.
- The changes land with the tickets: VERIFICATION-MODEL sections 1, 3 (runtime-changed APIs), 6 (the table, the review
  tiers) and 7; README's exit-code paragraph and scoreboard; ARCHITECTURE.md's exit codes; the `equiv-corpus-run`
  SUMMARY template, which counts EQ007; ADR 0040, 0054 and 0058's Status lines.
- The open valid-path and row-reach tickets keep their order; their Notes count exposures cleared instead of EQ006.
- Harder: a SARIF consumer that filtered on EQ006 to find runtime findings must add EQ007.
