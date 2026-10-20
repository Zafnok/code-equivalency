# P2-249: the replays whose model calls a delegate argument (2026-10-20)

Ticket P2-249 criterion 3 asks for a rerun of the pairs whose replays P2-245 (`docs/runs/2026-10-19-execution-reach.md`)
counted under "the model's runs call `<parameter>`, and the counterexample holds none of the model's answers to that
call", and for each such replay's value now.

**This takes no part in ADR 0028's rule table.** This file measures; it has no continue, re-scope or stop line.

## The count

P2-245 counted **none**. Its "why a replay is not constructible" table (256 replays over `gitextensions-8522`,
`duplicati-3124` and `openra-17989`) has no row for that reason, and neither do the three pairs' summaries
(`docs/runs/2026-10-19-full-execute-<slug>/SUMMARY.md`). The SARIF of every `--execute` run made on those checkouts on
2026-10-19 (P2-245's, P2-251's and P2-253's: Git Extensions `0012`, `0134`, `1220`, `1231`, `2046`, `2104`; Duplicati
`0018`, `0140`, `1220`, `1221`, `2053`; OpenRA `0028`, `0149`) holds the phrase "the model's runs call" zero times; the
only "the model's runs ..." reason in them is "both sides threw ..., which the model's runs do not". P2-237's one
delegate argument (Git Extensions row 40, `FileInfoExtensions::MakeFileTemporaryWritable(string, Action<string>)`) was
built by P2-245 as a delegate the model's runs never call, and its replay ran.

So no pair has a replay this ticket changes, and none was rerun: the decoder records answers only for calls of a
delegate input's `Invoke`, and a replay reads them only where it builds such a delegate, which none of these runs
reached. No `not-reproduced` arises, so no ticket is filed (ADR 0035 decision 2).

## Where the change shows

`samples/delegate-argument-answers` is a pair whose Divergent passes a `Func<int, int>` the model's runs call, the
model answering a negative unit price. Under `--execute` its replay is `reproduced` on .NET Framework 4.8 and on .NET 10,
`replayInputs` giving `price` as `delegate` (`ReplayTests.Replay_ADelegateTheModelCalls_AnswersAsTheModelAndReproduces`).
P2-245's rule, which refused any delegate a call event of the model's trace carries, makes the same replay
`not-constructible` with the reason above.
