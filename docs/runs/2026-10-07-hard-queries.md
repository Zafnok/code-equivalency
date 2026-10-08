# P2-101 the timeout Unknowns a larger budget does not decide: gitextensions-8522 (2026-10-07)

Question: P2-050 found 99 pairs that time out at the default solver budget and still time out at 20
times it (`docs/runs/2026-10-01-timeout-budget.md`). What do their queries have in common, and does
a different tactic pipeline or encoding answer them?

**Answer: no alternative proves at least 5 of the 99. None proves one.** Of the 99, 48 are no longer
timeouts on `main`, 34 of them proved Equivalent, by work that landed since P2-050. The other 51
still time out, and on their queries no solver built another way returns unsatisfiable at the
default `resourceLimit`, and none of six does at 15 times it. What one alternative does is find a model
where the production pipeline finds none: Z3's newer core (`sat` with `euf=true`) answers 33 of the
51 inside the default limit, and every answer is a Divergent (4), an Unknown(abstraction) (22) or an
Unknown(opaque) (7). That is what P2-050 saw of a larger budget and P1-025 saw of cvc5. The 51 stay
Unknown until an encoding change has its own evidence.

## Setup
- Pair: human, gitextensions/gitextensions PR #8522, legacy 3f4ed21998af, modern 5190ba5c1a5f
  (`tools/corpus/pairs.csv`).
- The 99 pairs: P2-050's harness wrote one line a pair and budget, and its output is still on this
  box. They are the pairs of its 184 that are Unknown(timeout) at 5,000 ms and at 100,000 ms in the
  run its report quotes (`budget20x`): 79 on rung 1's first query, 7 on rung 2's base obligation and
  13 on rung 2's step obligation of loop 1. All 99 identities still match a pair.
- equiv: `main` at `c59fa0fc`, win-x64, loaded through the production frontend with the default
  config: bound 3, `resourceLimit` 2,000,000, `timeoutMs` 60,000, no second solver. The default
  resource limit was 5,000,000 when P2-050 chose it and is 2,000,000 since ADR 0049.
- Tool: `tools/spikes/hard-queries/` (throwaway, not in `Equiv.slnx`). It asks what the ladder asks,
  in the ladder's order, with the production solver and limits; writes out the first query of each
  rung Z3 gives up on; counts what the query is made of; and asks the written query again of
  solvers built other ways. It also runs `Z3Backend.Verify` on each pair, without the failure
  refinement, for the verdict the pair has at this commit.
- Every comparison is at the same resource limit, not the same time: the limit is a count of Z3's
  own steps, and the box was shared. Every check was run twice, in two processes, and gave the same
  answer both times (11 variants, 63 queries).
- Machine: one Windows box, 24 logical cores, four solver threads, other work beside it. Loading and
  lowering the pair took 127 s.

## The 99 on `main` (criterion 1)

| verdict at `c59fa0fc`, default budget | pairs |
|---|---|
| Unknown(timeout) | 51 |
| Equivalent, every rung 1 query unsatisfiable | 31 |
| Equivalent, by rung 2 after a rung 1 query timed out | 3 |
| Unknown(opaque) | 6 |
| Divergent | 4 |
| Unknown(abstraction) | 4 |

48 of the 99 are decided now, at a budget below the one P2-050 measured them at, and 34 are proofs.
P2-050 ran `ef79ff6`. Since then rung 1 compares the call traces by position and its query holds no
sequence, datatype or integer (P1-038), a rung 1 query that hits its budget is asked again with its
hard arithmetic abstracted (P1-031), and an abstraction Unknown is refined (P1-030). Which of them
decided which pair was not measured. P2-050's own finding stands for the budget: it decided none of
the 99, and a change of encoding decided 34.

The 51 that remain, by the query that stands between the pair and a verdict:

| rung and query | pairs | the pair |
|---|---|---|
| rung 1, `divergence` (the first query) | 35 | has no loop |
| rung 1, `opaque` (the second query; `divergence` is unsatisfiable) | 7 | has no loop |
| rung 2, the step obligation of loop 1 (the base is unsatisfiable) | 6 | has a loop; its rung 1 times out too |
| rung 2, the base obligation | 3 | has a loop; its rung 1 times out too |

For a looping pair the query recorded is rung 2's, since rung 1 proves such a pair only when no
input goes past the bound.

Size of the assertion set, over the 51. A term is counted once however often it is used.

| | least | median | 90th percentile | most |
|---|---|---|---|---|
| assertions | 1,014 | 3,016 | 7,169 | 25,979 |
| distinct terms | 5,484 | 12,710 | 28,238 | 86,413 |
| distinct terms once the query has the definitions inlined (`Z3Backend.Inline`) | 7,081 | 14,529 | 34,563 | 90,823 |
| distinct sorts | 8 | 43 | 58 | 102 |
| of them uninterpreted sorts (one for each reference type) | 3 | 19 | 27 | 46 |
| of them array sorts (heap maps) | 3 | 20 | 29 | 54 |
| seconds for the production solver to exhaust the limit | 0.8 | 1.8 | 10.1 | 32.2 |

Theories, over the 51:

| what the assertion set holds | pairs | median count in a pair that has it |
|---|---|---|
| heap maps: array reads (`select`) | 51 | 107 |
| heap maps: array writes (`store`) | 32 | 36 |
| uninterpreted calls: applications of a call's result, `threw`, heap and by-reference functions | 51 | 814, of 239 functions |
| strings as a sort (an uninterpreted sort for `string`) | 51 | 1 |
| if-then-else terms | 51 | 2,666 |
| pure functions (`IrPure`: tuples, conversions, operators kept abstract) | 22 | 8, of 2 functions |
| sequences and datatypes (the call trace as a `Seq` of events) | 9 | 1,880 sequence and 799 datatype operators |
| bit-vector multiplication of two non-constants | 3 | 2 |
| bit-vector division or remainder | 2 | 12 |
| overflow tests of a multiplication, floating point, quantifiers | 0 | |

- The encoder has no datatype for a tuple: a tuple's items are pure functions and are counted there.
  The only datatypes are the trace's events, and only rung 2's obligations hold them.
- Every bit-vector is 32 bits wide except in 7 pairs (16 bits in 6, 64 in 1).
- Hard arithmetic is rare here: P1-031's abstraction applies to 2 of the 51.
- Preprocessing is not where the budget goes. The pipeline's four steps before `smt` spend a median
  132,000 of the 2,000,000 units on a rung 1 query (at most 781,000). The search spends the rest.

## Groups (criterion 2)

By the set of theories a pair's query uses:

| theories | pairs | median distinct terms | queries |
|---|---|---|---|
| heap maps, calls | 26 | 11,218 | rung 1: `divergence` 21, `opaque` 5 |
| heap maps, calls, pure functions | 15 | 13,692 | rung 1: `divergence` 13, `opaque` 2 |
| heap maps, calls, sequence trace | 3 | 13,147 | rung 2: step 2, base 1 |
| heap maps, calls, pure functions, sequence trace | 3 | 13,921 | rung 2: step 2, base 1 |
| heap maps, calls, pure functions, sequence trace, multiplication or division | 3 | 10,447 | rung 2: step 2, base 1 |
| heap maps, calls, pure functions, multiplication | 1 | 35,691 | rung 1: `divergence` 1 |

**The three largest groups:**
1. **Heap maps and calls, nothing else** (26 pairs).
2. **Heap maps, calls and pure functions** (15 pairs).
3. **The sequence trace** (9 pairs): the three sets of three above, taken together. They are the nine
   looping pairs, and what sets their query apart is the theory of the trace, which only rung 2 has.

One pair is in none of them.

No group is defined by arithmetic. Every one of the 51 queries is, at bottom, the same problem:
hundreds of uninterpreted call functions over heap maps, under thousands of if-then-else terms.

## Alternatives (criterion 3)

Every member of each group was tried, not ten, since a check takes seconds: 26, 15 and 9 pairs. The
first ten of each group, by identity, give the same picture and are the last column. Each cell is
**unsatisfiable (a proof) / satisfiable (a model)**, at `resourceLimit` 2,000,000.

| solver | heap maps and calls (26) | with pure functions (15) | sequence trace (9) | first ten of each |
|---|---|---|---|---|
| production: `solve-eqs`, `simplify`, `propagate-values`, `solve-eqs`, `smt`, query inlined | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0, 0 / 0, 0 / 0 |
| Z3's default solver, query inlined | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0, 0 / 0, 0 / 0 |
| Z3's default solver, query as written | 0 / 1 | 0 / 0 | 0 / 0 | 0 / 1, 0 / 0, 0 / 0 |
| the production pipeline without `Inline` | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0, 0 / 0, 0 / 0 |
| chosen for heap maps: `qfaufbv`, query as written | 0 / 13 | 0 / 2 | 0 / 0 | 0 / 4, 0 / 1, 0 / 0 |
| `qfaufbv` behind the pipeline's preprocessing | 0 / 11 | 0 / 5 | 0 / 0 | 0 / 4, 0 / 3, 0 / 0 |
| chosen for uninterpreted functions: `sat` with `euf=true` (Z3's newer core) behind the pipeline's preprocessing | 0 / 22 | 0 / 11 | 0 / 0 | 0 / 10, 0 / 8, 0 / 0 |
| chosen for the sequence trace: the obligation with its traces compared by position (P1-038's encoding), production solver | n/a | n/a | 0 / 1 | n/a, n/a, 0 / 1 |
| `smt` alone, query as written | 0 / 12 | 0 / 7 | 0 / 0 | 0 / 7, 0 / 5, 0 / 0 |
| `smt` alone, query inlined | 0 / 12 | 0 / 5 | 0 / 0 | 0 / 6, 0 / 3, 0 / 0 |
| `simplify`, `smt` | 0 / 13 | 0 / 7 | 0 / 0 | 0 / 7, 0 / 5, 0 / 0 |
| `solve-eqs`, `smt` | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0, 0 / 0, 0 / 0 |
| the pipeline with `elim-uncnstr` before `smt` | 0 / 1 | 0 / 0 | 0 / 0 | 0 / 0, 0 / 0, 0 / 0 |
| the pipeline with `smt`'s relevancy off | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0, 0 / 0, 0 / 0 |

- **No cell has a proof.** 14 ways of asking, 51 queries, no unsatisfiable answer.
- Each model above was checked against the assertions as written, and holds. `qfaufbv` returned one
  more "model", on a pair of the first group and in both its rows, that makes an assertion false; it
  is not counted.
- The one pair outside the groups was answered by nothing.
- Z3's default solver exhausts the limit in a median 68 ms, where the pipeline takes 0.9 s to: at
  this limit it hardly searches.
- Without `Inline` the pipeline answers exactly what it answers with it.
- **`smt` without `solve-eqs` before it does not keep to the limit, and that is where its models come
  from.** 15 of the 51 checks of `smt` alone spent over 10% more than 2,000,000 units, one of them
  31,700,000, and so did those of `simplify` then `smt`. Of the 19 models `smt` alone found, 4 were
  found within the limit. The rest are answers of a larger budget, which the production pipeline
  finds too when it is given one (next section). `solve-eqs`
  then `smt` keeps to the limit and finds none, like the whole pipeline.
- `qfaufbv` and `sat` with `euf=true` found every one of their models inside the limit (the largest
  spent 2,050,000 and 1,490,000).
- The trace by position does not help rung 2. Of the nine obligations, eight still time out and one
  becomes satisfiable, which makes its rung fail, not prove. On the three looping pairs of the 99
  that rung 2 proves today, the positional form proves two and times out on a step of the third.
- On rung 2's obligations in their own encoding, every solver gives up.

What the models are worth. Rung 1 was run again on each of the 51 with one alternative asked only
after the production solver gave up, and a model of `divergence` replayed as rung 1 replays its own:

| asked after the production solver | Divergent | Unknown(abstraction) | Unknown(opaque) | still Unknown(timeout) |
|---|---|---|---|---|
| `smt` alone, query as written (overruns the limit) | 5 | 13 | 2 | 31 |
| `qfaufbv`, query as written | 2 | 9 | 4 | 35 (one more: the model that does not hold replays to no difference) |
| `qfaufbv` behind the preprocessing | 1 | 10 | 6 | 33 (one more replays to no difference) |
| `sat` with `euf=true` behind the preprocessing | 4 | 22 | 7 | 18 |
| any of the four | 5 | 24 | 7 | 15 |

No pair becomes Equivalent. 36 of the 51 get another answer from some alternative, and 24 of those
are Unknown(abstraction): the model differs only where something the encoding abstracts answers
differently, which is the encoding's question and not the solver's. The 15 nothing answers are
eight of the nine looping pairs and seven others. The ninth looping pair gets a model of rung 1's
`opaque` query, which still leaves it to rung 2.

## A larger limit

The budget pass of thorough mode asks again at `resourceLimit` 30,000,000 (ADR 0049), so six of the
solvers were given that too, with the same 60 s backstop.

| solver | rung 1 queries (42): unsatisfiable / model holds / "model" that does not hold / gave up | rung 2 obligations (9): the same |
|---|---|---|
| production pipeline | 0 / 29 / 0 / 13 | 0 / 0 / 0 / 9 |
| Z3's default solver, query inlined | 0 / 28 / 0 / 14 | 0 / 1 / 0 / 8 |
| Z3's default solver, query as written | 0 / 26 / 0 / 16 | 0 / 0 / 0 / 9 |
| `smt` alone | 0 / 36 / 0 / 6 | 0 / 0 / 0 / 9 |
| `qfaufbv` | 0 / 27 / 4 / 11 | 0 / 0 / 0 / 9 |
| `sat` with `euf=true` behind the preprocessing | 0 / 39 / 0 / 3 | 0 / 0 / 0 / 9 |

- Still no proof: 306 checks, no unsatisfiable answer.
- With 15 times the limit the production pipeline finds a model of 29 of the 42 rung 1 queries, which
  is P2-050's finding again on today's encoding: more budget buys models. These models were not
  replayed.
- At this limit the alternatives are no longer far ahead of the pipeline, and `qfaufbv` returns four
  "models" that do not hold.
- Rung 2's obligations stay out of reach: one model in 54 checks, and a model of an obligation makes
  the rung fail.
- This limit is not cheap. The checks themselves took 7,500 s, 48 of them over 60 s and none over
  90 s. The run took three and a quarter hours on four threads: on the largest queries most of the
  time went to the harness's own work around a check (reading the file back, inlining the query,
  checking a model), which was not timed apart.

## Conclusion (criterion 4)

**No alternative proves at least 5 of the 99: none of the 14 proves any of the 51 that still time
out at the default resource limit, and none of the six asked at 15 times it does either. No ticket
is filed to adopt one, and the 51 stay Unknown until an encoding change has its own evidence.**

## Findings
- 34 of the 99 are proved on `main` today and 14 more have another answer. The evidence for what
  helps these pairs is on the encoding's side: the same Z3 and pipeline, with less budget, proves
  them on today's encoding. No ticket: the work is done (P1-038, P1-030, P1-031).
- `sat` with `euf=true` finds a model of 33 of the 51 queries inside the default limit, where the
  pipeline finds none. They are answers of the kind a second solver gives (ADR 0050, P1-033), at no
  new dependency, and none is a proof. No ticket here: criterion 4 files one only for an alternative
  that proves, and a second ask that turns a timeout into Unknown(abstraction) proves nothing.
  Whoever picks it up should know two things measured here: it is the core Z3 describes as
  preliminary, so its answers were accepted only after each model was checked against the
  assertions; and `qfaufbv` returned a model that does not hold.
- `smt` with no tactic before it is not bounded by `rlimit`: one check spent 16 times the limit.
  The production pipeline starts with `solve-eqs` and kept to the limit on every check here; a
  pipeline that starts with `smt` or `simplify` would not.
- 24 of the 36 pairs some alternative answers are Unknown(abstraction). For those 24 of the 51 the
  answer behind the timeout is not "the solver ran out" but "it depends on something abstracted",
  and what would move them is the abstraction work (P1-030's refinement, callee contracts), not the
  solver.
- The looping pairs are answered by nothing: not by another solver, and not by the positional
  trace. Their rung 2 obligations hold a median 1,880 sequence operators.
- The default `resourceLimit` P2-050 chose (5,000,000) is not today's (2,000,000, ADR 0049). This
  report uses today's.

## The 99 pairs
The query is the one that times out at this commit, with its size; a pair with none has the verdict
it now gets. `mul` is a multiplication of two non-constants, `call` an application of a call
function, `pure` of a pure function, `seq` and `datatype` the trace's operators.

| # | procedure identity | timed out in P2-050 at 20 times the budget | verdict now | rung, query that times out now | assertions | terms | sorts | operators |
|---|---|---|---|---|---|---|---|---|
| 0 | `BugReporter.BugReportForm::ShowDialog(System.Windows.Forms.IWin32Window,BugReporter.Serialization.SerializableException,string,bool,bool,bool)` | rung 1 first query | Unknown(timeout) | 1, `opaque` | 5502 | 19749 | 46 | select 161, store 7, call 1248 |
| 1 | `ConEmu.WinForms.ConEmuSession::Init_MakeConEmuCommandLine(ConEmu.WinForms.ConEmuStartInfo,ConEmu.WinForms.ConEmuSession.HostContext,ConEmu.WinForms.AnsiLog,System.IO.DirectoryInfo)` | rung 1 first query | Unknown(opaque) | none |  |  |  |  |
| 2 | `ConEmu.WinForms.ConEmuSession::Init_MakeConEmuCommandLine_EmitConfigFile(System.IO.DirectoryInfo,ConEmu.WinForms.ConEmuStartInfo,ConEmu.WinForms.ConEmuSession.HostContext)` | rung 2 base obligation | Unknown(timeout) | 2, `base` | 4391 | 18787 | 44 | select 146, call 940, pure 6, seq 2203, datatype 977 |
| 3 | `GitCommands.AppSettings::.cctor()` | rung 1 first query | Unknown(timeout) | 1, `opaque` | 4386 | 13123 | 28 | select 14, store 28, call 962 |
| 4 | `GitCommands.Executable.ProcessWrapper::.ctor(string,string,string,bool,bool,bool,System.Text.Encoding,bool)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2460 | 9074 | 25 | select 56, store 12, call 664 |
| 5 | `GitCommands.ExecutableExtensions::ExecuteAsync(GitUIPluginInterfaces.IExecutable,GitExtUtils.ArgumentString,System.Action<global::System.IO.StreamWriter>,System.Text.Encoding,GitCommands.CommandCache,bool)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 1857 | 10620 | 47 | select 84, store 16, call 440, pure 2 |
| 6 | `GitCommands.GitModule::GetFetchArgs(string,string,string,bool?,bool,bool,bool)` | rung 1 first query | Divergent | none |  |  |  |  |
| 7 | `GitCommands.GitModule::HandleConflictsSaveSide(string,string,string)` | rung 1 first query | Unknown(abstraction) | none |  |  |  |  |
| 8 | `GitCommands.Patches.PatchProcessor::CreatePatchFromString(string[],System.Lazy<global::System.Text.Encoding>,ref int)` | rung 2 base obligation | Unknown(timeout) | 2, `step1` | 1629 | 8123 | 30 | select 32, call 320, seq 1220, datatype 375 |
| 9 | `GitCommands.PathUtil::FindInFolders(string,System.Collections.Generic.IEnumerable<string>)` | rung 2 step obligation of loop 1 | Divergent | none |  |  |  |  |
| 10 | `GitCommandsTests.Git.Commands.GitCommandsHelperTest::CanGetRelativeDateString()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 11 | `GitCommandsTests.Git.Commands.GitCommandsHelperTest::CanGetRelativeNegativeDateString()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 12 | `GitCommandsTests.Git.Gpg.GitGpgControllerTests::Validate_GetTagVerifyMessage(int,string)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 1822 | 7656 | 33 | select 51, store 34, call 500 |
| 13 | `GitCommandsTests.Git.IndexLockManagerTests::Resolve_submodule_real_filesystem()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 1956 | 7796 | 29 | select 98, store 26, call 496 |
| 14 | `GitCommandsTests.Git.IndexLockManagerTests::Setup()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 1620 | 5845 | 58 | select 59, store 54, call 540 |
| 15 | `GitCommandsTests.Git.SubmoduleHelpersTest::GetSubmoduleNamesFromDiffTest()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 1426 | 5484 | 15 | select 116, call 458 |
| 16 | `GitCommandsTests.GitModuleTests::ParseGitBlame()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2674 | 10234 | 38 | select 143, call 636 |
| 17 | `GitCommandsTests.Settings.FileSettingsCacheTests::SaveImpl_should_create_folder_if_absent()` | rung 1 first query | Divergent | none |  |  |  |  |
| 18 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_second_nested_module_changes()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 4334 | 16676 | 55 | select 184, store 48, call 1232 |
| 19 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_top_module_changes()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2940 | 11218 | 51 | select 126, store 36, call 876 |
| 20 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_first_nested_module_precommit()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 4343 | 16786 | 57 | select 206, store 48, call 1164, pure 8 |
| 21 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_prechanges_noupdate()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2654 | 10118 | 47 | select 107, store 12, call 760, pure 24 |
| 22 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_second_nested_module_changes()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 3094 | 11899 | 54 | select 156, store 40, call 868 |
| 23 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_change()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 3534 | 13497 | 54 | select 160, store 44, call 1004 |
| 24 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_change_commit()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 4253 | 16318 | 59 | select 184, store 48, call 1210, pure 8 |
| 25 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_commit_second_nested_module_change()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 7169 | 27687 | 59 | select 300, store 80, call 2024, pure 8 |
| 26 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_no_forced_changes()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 3574 | 13692 | 59 | select 162, store 48, call 1046, pure 4 |
| 27 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_second_nested_module_change()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 4302 | 16525 | 54 | select 194, store 52, call 1208 |
| 28 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_top_module_change()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 3262 | 12473 | 54 | select 158, store 44, call 926 |
| 29 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_top_module_changes()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2620 | 9955 | 53 | select 124, store 36, call 736 |
| 30 | `GitExtUtils.GitUI.Theming.TabControlPaintContext::RenderTabBackground(int)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 31 | `GitExtensions.Plugins.Bitbucket.BitbucketRequestBase`1::SendAsync()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 32 | `GitExtensions.Plugins.DeleteUnusedBranches.DeleteUnusedBranchesForm::Delete_Click(object,System.EventArgs)` | rung 1 first query | Unknown(opaque) | none |  |  |  |  |
| 33 | `GitExtensions.Plugins.FindLargeFiles.FindLargeFilesForm::FindLargeFilesFunction()` | rung 2 step obligation of loop 1 | Unknown(timeout) | 2, `step1` | 2867 | 12710 | 54 | select 61, store 4, call 664, pure 8, seq 1880, datatype 799 |
| 34 | `GitExtensions.Plugins.GitFlow.GitFlowForm::RunCommand(GitExtUtils.ArgumentString)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 35 | `GitExtensions.Plugins.GitStatistics.PieChart.PieChartControl::DoDraw(System.Drawing.Graphics)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 36 | `GitExtensions.Plugins.Gource.GourcePlugin::Execute(GitUIPluginInterfaces.GitUIEventArgs)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 5926 | 24141 | 34 | select 141, call 1476 |
| 37 | `GitExtensions.Plugins.Gource.GourcePlugin::SearchForGourceUrl()` | rung 1 first query | Unknown(opaque) | none |  |  |  |  |
| 38 | `GitExtensions.Plugins.ReleaseNotesGenerator.ReleaseNotesGeneratorForm::buttonGenerate_Click(object,System.EventArgs)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 3552 | 13292 | 53 | select 106, store 10, call 960, pure 4 |
| 39 | `GitExtensions.Program::HandleConfigurationException(System.Configuration.ConfigurationException)` | rung 1 first query | Unknown(abstraction) | none |  |  |  |  |
| 40 | `GitUI.Avatars.InitialsAvatarProvider::DrawText(string,System.Drawing.Color,int)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 41 | `GitUI.CommandsDialogs.BrowseDialog.DashboardControl.UserRepositoriesList::.ctor()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 42 | `GitUI.CommandsDialogs.BrowseDialog.DashboardControl.UserRepositoriesList::listView1_DrawItem(object,System.Windows.Forms.DrawListViewItemEventArgs)` | rung 1 first query | Unknown(timeout) | 1, `opaque` | 2204 | 8175 | 36 | select 62, call 684, pure 46 |
| 43 | `GitUI.CommandsDialogs.FormCheckoutBranch::RecalculateSizeConstraints()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 44 | `GitUI.CommandsDialogs.FormClone::OkClick(object,System.EventArgs)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 8506 | 33176 | 62 | select 132, call 2256, pure 10 |
| 45 | `GitUI.CommandsDialogs.FormClone::OnRuntimeLoad(System.EventArgs)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 6948 | 35691 | 43 | mul 2, select 170, call 2016, pure 4 |
| 46 | `GitUI.CommandsDialogs.FormClone::ToTextUpdate(object,System.EventArgs)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2774 | 12858 | 26 | select 93, call 644 |
| 47 | `GitUI.CommandsDialogs.FormFileHistory::.ctor(GitUI.GitUICommands,string,GitUIPluginInterfaces.GitRevision,bool,bool)` | rung 1 first query | Unknown(timeout) | 1, `opaque` | 25979 | 86413 | 102 | select 238, store 24, call 7656 |
| 48 | `GitUI.CommandsDialogs.FormFileHistory::UpdateSelectedFileViewers(bool)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 49 | `GitUI.CommandsDialogs.FormFileHistory::saveAsToolStripMenuItem_Click(object,System.EventArgs)` | rung 1 first query | Unknown(abstraction) | none |  |  |  |  |
| 50 | `GitUI.CommandsDialogs.FormFormatPatch::FormatPatch_Click(object,System.EventArgs)` | rung 2 base obligation | Unknown(timeout) | 2, `base` | 8976 | 37493 | 55 | select 162, call 2184, seq 4796, datatype 2280 |
| 51 | `GitUI.CommandsDialogs.FormRemotes::.ctor(GitUI.GitUICommands)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 52 | `GitUI.CommandsDialogs.FormResolveConflicts::InitMergetool()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2485 | 10305 | 29 | select 65, store 30, call 522, pure 8 |
| 53 | `GitUI.CommandsDialogs.FormResolveConflicts::SaveAs(string)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 1604 | 6665 | 28 | select 42, call 418 |
| 54 | `GitUI.CommandsDialogs.FormVerify.LostObject::TryParse(GitCommands.GitModule,string)` | rung 1 first query | Unknown(timeout) | 1, `opaque` | 4419 | 16129 | 30 | select 80, store 20, call 1250 |
| 55 | `GitUI.CommandsDialogs.RevisionDiffControl::saveAsToolStripMenuItem1_Click(object,System.EventArgs)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2190 | 8398 | 37 | select 52, call 482 |
| 56 | `GitUI.CommandsDialogs.SettingsDialog.FormAvailableEncodings::LoadEncoding()` | rung 1 first query | Unknown(abstraction) | none |  |  |  |  |
| 57 | `GitUI.CommandsDialogs.SettingsDialog.Pages.AppearanceSettingsPage::SettingsToPage()` | rung 1 first query | Unknown(timeout) | 1, `opaque` | 6220 | 22160 | 37 | select 127, call 2084, pure 2 |
| 58 | `GitUI.CommandsDialogs.SettingsDialog.Pages.ShellExtensionSettingsPage::InitializeComponent()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 11251 | 44352 | 56 | select 514, store 92, call 3620 |
| 59 | `GitUI.CommandsDialogs.SettingsDialog.Pages.SshSettingsPage::AutoFindPuttyPathsInDir(string)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 1014 | 6134 | 8 | select 44, call 248 |
| 60 | `GitUI.CommitInfo.CommitInfo::InitializeComponent()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 61 | `GitUI.CommitInfo.CommitInfo::OnLayout(System.Windows.Forms.LayoutEventArgs)` | rung 2 step obligation of loop 1 | Equivalent | none |  |  |  |  |
| 62 | `GitUI.Editor.Diff.DiffLineNumAnalyzer::Analyze(string)` | rung 2 step obligation of loop 1 | Unknown(timeout) | 2, `step1` | 3254 | 13147 | 31 | select 38, store 54, call 514, seq 1099, datatype 499 |
| 63 | `GitUI.Editor.Diff.DiffViewerLineNumberControl::Paint(System.Drawing.Graphics,System.Drawing.Rectangle)` | rung 2 step obligation of loop 1 | Unknown(timeout) | 2, `step1` | 4508 | 20170 | 47 | mul 1, div 2, select 91, call 1270, pure 8, seq 2808, datatype 1311 |
| 64 | `GitUI.Editor.FileViewer::ResetNoncommittedSelectedLines()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 65 | `GitUI.Editor.RichTextBoxExtension.RichTextBoxXhtmlSupportExtension::ProcessTags(System.Windows.Forms.RichTextBox,System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<int, string>>,bool)` | rung 2 step obligation of loop 1 | Equivalent | 1, `bound` | 19137 | 67371 | 30 | div 6, select 130, store 12, call 5472, pure 60 |
| 66 | `GitUI.GitUICommands::RunCommandBasedOnArgument(System.Collections.Generic.IReadOnlyList<string>,System.Collections.Generic.IReadOnlyDictionary<string, string>)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 3797 | 17184 | 26 | select 54, store 2, call 670, pure 172 |
| 67 | `GitUI.Help.HelpImageDisplayUserControl::UpdateControlSize()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 68 | `GitUI.Script.ScriptOptionsParser::ParseScriptArguments(string,string,System.Windows.Forms.IWin32Window,GitUI.Script.IScriptHostControl,GitUIPluginInterfaces.IGitModule,System.Collections.Generic.IReadOnlyList<global::GitUIPluginInterfaces.GitRevision>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<string>,GitUIPluginInterfaces.GitRevision,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,GitUIPluginInterfaces.GitRevision,string)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 7316 | 28238 | 41 | select 172, call 998, pure 140 |
| 69 | `GitUI.Script.SplitButton::OnPaint(System.Windows.Forms.PaintEventArgs)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 70 | `GitUI.SpellChecker.SpellCheckEditControl::CustomPaint()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 71 | `GitUI.SpellChecker.TextBoxHelper::GetBaselineOffsetAtCharIndex(System.Windows.Forms.TextBoxBase,int)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 72 | `GitUI.UserControls.RevisionGrid.Columns.AvatarColumnProvider::OnCellPainting(System.Windows.Forms.DataGridViewCellPaintingEventArgs,GitUIPluginInterfaces.GitRevision,int,GitUI.UserControls.RevisionGrid.CellStyle)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 73 | `GitUI.UserControls.RevisionGrid.FormQuickItemSelector::Init(System.Collections.Generic.IReadOnlyList<global::GitUI.UserControls.RevisionGrid.FormQuickItemSelector.ItemData>,string)` | rung 2 base obligation | Unknown(timeout) | 2, `step1` | 3016 | 13921 | 36 | select 152, call 814, pure 25, seq 1982, datatype 894 |
| 74 | `GitUI.UserControls.RevisionGrid.FormQuickItemSelector::InitializeComponent()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2186 | 8709 | 43 | select 174, store 42, call 634 |
| 75 | `GitUI.UserControls.RevisionGrid.RevisionDataGridView::OnCellPainting(object,System.Windows.Forms.DataGridViewCellPaintingEventArgs)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 76 | `GitUI.WindowPositionManager::IsDisplayedOn10Percent(System.Drawing.Rectangle,System.Drawing.Rectangle)` | rung 1 first query | Unknown(opaque) | none |  |  |  |  |
| 77 | `GitUIPluginInterfaces.ManagedExtensibility::CreateExportProvider(string)` | rung 1 first query | Divergent | none |  |  |  |  |
| 78 | `GitUITests.Avatars.AvatarPersistentCacheTests::GetAvatarAsync_uses_inner_if_file_expired()` | rung 1 first query | Unknown(timeout) | 1, `opaque` | 2026 | 7602 | 51 | select 72, store 16, call 645 |
| 79 | `GitUITests.CancellationTokenSequenceTests::Concurrent_callers_to_Next_only_result_in_one_non_cancelled_token_being_issued()` | rung 1 first query | Unknown(opaque) | none |  |  |  |  |
| 80 | `GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::LoadItemsInTreeView_should_add_IsCommit_as_submodule()` | rung 2 step obligation of loop 1 | Equivalent | none |  |  |  |  |
| 81 | `GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::LoadItemsInTreeView_should_add_IsTree_as_folders()` | rung 2 step obligation of loop 1 | Equivalent | none |  |  |  |  |
| 82 | `GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::LoadItemsInTreeView_should_add_all_none_GitItem_items_with_1st_level_nodes()` | rung 2 step obligation of loop 1 | Equivalent | none |  |  |  |  |
| 83 | `GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::LoadItemsInTreeView_should_add_icon_for_file_extension_only_once()` | rung 2 base obligation | Unknown(opaque) | none |  |  |  |  |
| 84 | `GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::LoadItemsInTreeView_should_not_add_icons_for_file_if_none_provided()` | rung 2 step obligation of loop 1 | Equivalent | none |  |  |  |  |
| 85 | `GitUITests.CommandsDialogs.RevisionFileTreeControllerTests::LoadItemsInTreeView_should_not_load_icons_for_file_without_extension()` | rung 2 step obligation of loop 1 | Equivalent | none |  |  |  |  |
| 86 | `GitUITests.UserControls.RevisionGrid.Graph.LaneInfoProviderTests::Setup()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 87 | `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2164 | 10344 | 18 | select 44, store 24, call 508, pure 4 |
| 88 | `ICSharpCode.TextEditor.Document.HighlightColor::.ctor(System.Xml.XmlElement,ICSharpCode.TextEditor.Document.HighlightColor)` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2260 | 10620 | 18 | select 53, store 30, call 492, pure 4 |
| 89 | `ICSharpCode.TextEditor.FoldMargin::DrawFoldMarker(System.Drawing.Graphics,System.Drawing.RectangleF,bool,bool)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 90 | `ICSharpCode.TextEditor.FoldMargin::Paint(System.Drawing.Graphics,System.Drawing.Rectangle)` | rung 2 step obligation of loop 1 | Equivalent | 1, `divergence` | 5701 | 33882 | 29 | mul 6, div 6, select 270, call 1550 |
| 91 | `ICSharpCode.TextEditor.FoldMargin::PaintFoldMarker(System.Drawing.Graphics,int,System.Drawing.Rectangle)` | rung 2 base obligation | Unknown(timeout) | 2, `base` | 2383 | 10447 | 38 | div 12, select 78, call 524, pure 8, seq 1268, datatype 562 |
| 92 | `ICSharpCode.TextEditor.TextEditorControl::DrawLine(System.Drawing.Graphics,ICSharpCode.TextEditor.Document.LineSegment,float,System.Drawing.RectangleF)` | rung 2 step obligation of loop 1 | Unknown(timeout) | 2, `step1` | 1777 | 8055 | 35 | mul 2, select 32, call 498, pure 8, seq 1366, datatype 574 |
| 93 | `ICSharpCode.TextEditor.TextView::PaintDocumentLine(System.Drawing.Graphics,int,System.Drawing.Rectangle)` | rung 2 base obligation | Equivalent | 1, `bound` | 13864 | 48389 | 48 | select 256, store 2, call 3592, pure 16 |
| 94 | `ICSharpCode.TextEditor.TextView::PaintFoldingText(System.Drawing.Graphics,int,int,System.Drawing.Rectangle,string,bool)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 95 | `ResourceManagerTests.CommitDataRenders.CommitDataHeaderRendererTests::Render_should_render_commit_parents()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 96 | `ResourceManagerTests.CommitDataRenders.CommitDataHeaderRendererTests::Render_should_render_committer_if_different_from_author()` | rung 1 first query | Equivalent | none |  |  |  |  |
| 97 | `ResourceManagerTests.CommitDataRenders.CommitDataHeaderRendererTests::Render_should_render_minimal_info_for_artificial_commits(string)` | rung 1 first query | Equivalent | none |  |  |  |  |
| 98 | `ResourceManagerTests.CommitDataRenders.CommitDataHeaderRendererTests::Setup()` | rung 1 first query | Unknown(timeout) | 1, `divergence` | 2249 | 8457 | 28 | select 76, store 66, call 828 |

## Pairs an alternative answers
Rung 1 with the alternative asked after the production solver gave up. `smt` alone and `sat` with
`euf=true` are listed; every pair `qfaufbv` answers is among them.

| # | procedure identity | `smt` alone | `sat` with `euf=true` |
|---|---|---|---|
| 0 | `BugReporter.BugReportForm::ShowDialog(System.Windows.Forms.IWin32Window,BugReporter.Serialization.SerializableException,string,bool,bool,bool)` | Unknown(opaque) | Unknown(opaque) |
| 3 | `GitCommands.AppSettings::.cctor()` | timeout | Unknown(opaque) |
| 4 | `GitCommands.Executable.ProcessWrapper::.ctor(string,string,string,bool,bool,bool,System.Text.Encoding,bool)` | timeout | Unknown(abstraction) |
| 5 | `GitCommands.ExecutableExtensions::ExecuteAsync(GitUIPluginInterfaces.IExecutable,GitExtUtils.ArgumentString,System.Action<global::System.IO.StreamWriter>,System.Text.Encoding,GitCommands.CommandCache,bool)` | Unknown(abstraction) | timeout |
| 12 | `GitCommandsTests.Git.Gpg.GitGpgControllerTests::Validate_GetTagVerifyMessage(int,string)` | Unknown(abstraction) | Unknown(abstraction) |
| 13 | `GitCommandsTests.Git.IndexLockManagerTests::Resolve_submodule_real_filesystem()` | Divergent | Divergent |
| 14 | `GitCommandsTests.Git.IndexLockManagerTests::Setup()` | Divergent | Divergent |
| 15 | `GitCommandsTests.Git.SubmoduleHelpersTest::GetSubmoduleNamesFromDiffTest()` | Divergent | Divergent |
| 16 | `GitCommandsTests.GitModuleTests::ParseGitBlame()` | Unknown(abstraction) | Unknown(abstraction) |
| 18 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_second_nested_module_changes()` | timeout | Unknown(abstraction) |
| 19 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_first_nested_module_with_top_module_changes()` | Unknown(abstraction) | Unknown(abstraction) |
| 20 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_first_nested_module_precommit()` | timeout | Unknown(abstraction) |
| 21 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_prechanges_noupdate()` | Unknown(abstraction) | Unknown(abstraction) |
| 22 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_second_nested_module_with_second_nested_module_changes()` | Unknown(abstraction) | Unknown(abstraction) |
| 23 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_change()` | timeout | Unknown(abstraction) |
| 24 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_change_commit()` | timeout | Unknown(abstraction) |
| 25 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_first_nested_module_commit_second_nested_module_change()` | timeout | Unknown(abstraction) |
| 26 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_no_forced_changes()` | timeout | Unknown(abstraction) |
| 27 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_second_nested_module_change()` | timeout | Unknown(abstraction) |
| 28 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_top_module_change()` | timeout | Unknown(abstraction) |
| 29 | `GitCommandsTests.Submodules.SubmoduleStatusProviderTests::Submodule_status_changes_for_top_module_with_top_module_changes()` | Unknown(abstraction) | Unknown(abstraction) |
| 38 | `GitExtensions.Plugins.ReleaseNotesGenerator.ReleaseNotesGeneratorForm::buttonGenerate_Click(object,System.EventArgs)` | Unknown(abstraction) | Unknown(abstraction) |
| 42 | `GitUI.CommandsDialogs.BrowseDialog.DashboardControl.UserRepositoriesList::listView1_DrawItem(object,System.Windows.Forms.DrawListViewItemEventArgs)` | Unknown(opaque) | Unknown(opaque) |
| 46 | `GitUI.CommandsDialogs.FormClone::ToTextUpdate(object,System.EventArgs)` | Divergent | Divergent |
| 52 | `GitUI.CommandsDialogs.FormResolveConflicts::InitMergetool()` | Unknown(abstraction) | timeout |
| 53 | `GitUI.CommandsDialogs.FormResolveConflicts::SaveAs(string)` | Unknown(abstraction) | Unknown(abstraction) |
| 54 | `GitUI.CommandsDialogs.FormVerify.LostObject::TryParse(GitCommands.GitModule,string)` | timeout | Unknown(opaque) |
| 55 | `GitUI.CommandsDialogs.RevisionDiffControl::saveAsToolStripMenuItem1_Click(object,System.EventArgs)` | Unknown(abstraction) | Unknown(abstraction) |
| 57 | `GitUI.CommandsDialogs.SettingsDialog.Pages.AppearanceSettingsPage::SettingsToPage()` | timeout | Unknown(opaque) |
| 59 | `GitUI.CommandsDialogs.SettingsDialog.Pages.SshSettingsPage::AutoFindPuttyPathsInDir(string)` | Divergent | timeout |
| 66 | `GitUI.GitUICommands::RunCommandBasedOnArgument(System.Collections.Generic.IReadOnlyList<string>,System.Collections.Generic.IReadOnlyDictionary<string, string>)` | Unknown(abstraction) | Unknown(abstraction) |
| 68 | `GitUI.Script.ScriptOptionsParser::ParseScriptArguments(string,string,System.Windows.Forms.IWin32Window,GitUI.Script.IScriptHostControl,GitUIPluginInterfaces.IGitModule,System.Collections.Generic.IReadOnlyList<global::GitUIPluginInterfaces.GitRevision>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<string>,GitUIPluginInterfaces.GitRevision,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,System.Collections.Generic.IList<global::GitUIPluginInterfaces.IGitRef>,GitUIPluginInterfaces.GitRevision,string)` | Unknown(abstraction) | Unknown(abstraction) |
| 74 | `GitUI.UserControls.RevisionGrid.FormQuickItemSelector::InitializeComponent()` | timeout | Unknown(abstraction) |
| 78 | `GitUITests.Avatars.AvatarPersistentCacheTests::GetAvatarAsync_uses_inner_if_file_expired()` | timeout | Unknown(opaque) |
| 91 | `ICSharpCode.TextEditor.FoldMargin::PaintFoldMarker(System.Drawing.Graphics,int,System.Drawing.Rectangle)` | timeout | Unknown(opaque) |
| 98 | `ResourceManagerTests.CommitDataRenders.CommitDataHeaderRendererTests::Setup()` | timeout | Unknown(abstraction) |
