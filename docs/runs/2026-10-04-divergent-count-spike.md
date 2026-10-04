# P1-027 Counting the inputs a Divergent pair diverges on (2026-10-04)

A Divergent gives one input. This spike asks on what share of its inputs the pair diverges, by
approximate model counting on the formula the verdict came from, for the EQ002 results of the three
large runs. Nothing under `src/` changed and no package was added. It records identities and counts
only, per `docs/runs/README.md`. The tool is `tools/spikes/divergent-count/`, at equiv `a74f2e0`.

**Answer: the share is computed for 12 of 80 EQ002 results (15.0%), and every one of the 12 is 1.
11 have no source parameter at all, so there is nothing to count. The number does not separate one
result from another. Below criterion 5's half, so no ADR; one line in ROADMAP's post-MVP list.**

A share here is over the parameter bit space, uniformly. It is not a probability that production
traffic diverges.

## Inputs

| Pair | Run read | EQ002 in that run |
|---|---|---|
| gitextensions-8522 | `20261002-1839-full-after-again` | 68 |
| gitextensions-9860 | `20261003-0055-full` | 3 |
| jellyfin-13023 | `20261003-0055-full` | 9 |

`gitextensions-8522`'s run is the latest plain `full` run on this box, not the `20260929-1918`
run that `docs/runs/2026-09-30-full-gitextensions-8522` and the P2-047 audit describe (84 EQ002).
The audit's 30 Git Extensions EQ002 rows were added by identity, so each is measured whether or not
the later run still reports it; 13 of the 30 are no longer EQ002 in that run.

Each pair was lowered again by the production frontend at `a74f2e0`, with the default bound,
`timeoutMs` (60,000) and `resourceLimit`.

## Method

- **Countable** (criterion 1): every source parameter (a shared input that is not synthesised) is
  `Bool`, a bitvector, or a `Sort` that each body uses only as the key of a read of its `null.*`
  map. The space is one bit per `Bool` and per null shadow, and the width of each bitvector.
- Decision: a `Sort` parameter with any other use makes the pair not countable, and the blocker is
  that first use, in parameter order, legacy body first: `call-result` (it is an argument of a call
  or a read of an opaque fragment, whose result depends on it), `heap-map` (a field, array or length
  map is read or written at it) or `sort-by-value` (an operator, pure function, cast, type test,
  merge or exit reads its value). These are the ticket's three, read as uses of the parameter.
- **Formula**: rung 1's divergence query on the pair with its shared fragments as calls, unrolled to
  the bound: the product's assertions, some observable differs, and neither side reaches an opaque
  node or the bound. Everything that is not a source parameter bit (heap maps, call functions, the
  receiver) is existential. A count is therefore of the solver's formula. Whether each counted
  input's model is untainted (ADR 0026) is not checked.
- **Count** (criterion 2): ApproxMC2's scheme (Chakraborty, Meel and Vardi, "Algorithmic
  Improvements in Approximate Counting for Probabilistic Inference: From Linear to Logarithmic SAT
  Calls", IJCAI 2016) with tolerance 0.8 and confidence 0.8, on Z3. Fewer than 72 assignments are
  enumerated, which is exact. Otherwise each of 17 iterations draws random parity rows over the
  bits, searches for the number of rows that leaves a cell of fewer than 72, and the answer is the
  median of `cell * 2^rows`. One count has the pair's `timeoutMs`; each check has its
  `resourceLimit`.
- Citation check: the title, authors and venue are confirmed from the proceedings page. The
  threshold `1 + 9.84 (1 + e/(1+e)) (1 + 1/e)^2` is confirmed against the reference implementation
  (`meelgroup/approxmc`, `counter.cpp`). The paper's text could not be read from this box, so the
  bound that one iteration is within tolerance with probability at least 0.6 is from memory. The 17
  iterations follow from it exactly (the least odd count whose majority is right with probability
  0.8). The self-test below is the evidence that the estimates hold.
- Decision: the parity rows are reduced by Gaussian elimination before Z3 sees them, and a model is
  blocked on the bits that are no row's pivot. The constraint is unchanged. Unreduced, Z3 gave up on
  every 32-bit pair of the self-test.
- Decision: the count uses Z3's incremental solver, and the verdict's own tactic pipeline only when
  that gives up. The pipeline starts over at every check: on the self-test it is two to five times
  slower and ran one pair out of its budget.

## Self-test (criterion 3)

`dotnet <spike> --self-test`: ten hand-written pairs whose share is known exactly. All ten are
within the tolerance (a factor of 1.8, 0.85 in log2).

| Pair | Bits | Exact log2 share | Estimate | How | Checks | Seconds |
|---|---|---|---|---|---|---|
| every `int` | 32 | 0 | 0.00 | hashed | 2925 | 1.3 |
| one `int` | 32 | -32 | -32.00 | enumerated | 2 | 0.0 |
| both `bool` values | 1 | 0 | 0.00 | enumerated | 3 | 0.0 |
| the negative `int`s | 32 | -1 | -1.00 | hashed | 2876 | 1.3 |
| a `bool` and a negative `int` | 33 | -2 | -2.00 | hashed | 2876 | 1.4 |
| 100 of 256 bytes | 8 | -1.36 | -1.36 | hashed | 923 | 0.4 |
| a low byte of zero | 32 | -8 | -8.00 | hashed | 2852 | 2.0 |
| a non-null reference (null shadow) | 1 | -1 | -1.00 | enumerated | 2 | 0.0 |
| 1000 of 2^17 | 17 | -7.03 | -7.05 | hashed | 2523 | 1.0 |
| the positive `int`s, for some call result | 32 | -1 | -1.00 | hashed | 2868 | 1.3 |

One more pair is printed as the measured limit and is not one of the ten: two `int` parameters that
diverge when they are equal (exact log2 share -32 of 64 bits). Under parity rows that is a dense
linear system over GF(2), which Z3 has no reasoning for, and the check gives up at the resource
limit after 506 checks and 9.5 s. Any divergence that relates two parameters will do the same. An
external counter with native parity reasoning is what the size guard rules out.

## Results (criteria 1, 2 and 4)

### Countable share and share counted in time

| Run | EQ002 | Countable | Counted in time | Not countable |
|---|---|---|---|---|
| gitextensions-8522 | 68 | 14 (20.6%) | 9 (13.2%) | 54 (79.4%) |
| gitextensions-9860 | 3 | 2 (66.7%) | 1 (33.3%) | 1 (33.3%) |
| jellyfin-13023 | 9 | 2 (22.2%) | 2 (22.2%) | 7 (77.8%) |
| **All** | **80** | **18 (22.5%)** | **12 (15.0%)** | **62 (77.5%)** |

Every pair was found and encodable at this commit.

First blocker of the 62 that are not countable:

| Blocker | Results | Share of EQ002 |
|---|---|---|
| `call-result` | 55 | 68.8% |
| `heap-map` | 4 | 5.0% |
| `sort-by-value` | 3 | 3.8% |

Outcome of the 18 countable:

| Outcome | Results | Share of EQ002 |
|---|---|---|
| counted | 12 | 15.0% |
| not Divergent at this commit (the divergence query is unsatisfiable) | 4 | 5.0% |
| solver gave up at the resource limit on the unconstrained query | 2 | 2.5% |
| timeout | 0 | 0% |

The two the solver gave up on have no source parameter. Both solvers hit the resource limit on the
query alone, with no parity row, at the default options.

### Distribution of the 12 counted shares

| Share | Results |
|---|---|
| 1, no source parameter | 11 |
| 1, more than half (2 bits: two references' null shadows) | 1 |
| `2^-1..2^-8` and every smaller band | 0 |
| a single input | 0 |

All 12 were enumerated, so all are exact. No corpus result needed a parity row. The largest count
took 6 solver checks and 2.7 s.

Countable parameter spaces by size, over the 18: 0 bits 16, 2 bits 1, and the audit adds one of 1
bit and one of 32 bits that are not Divergent at this commit.

### Audited results of P2-047: do confirmed and false-positive results differ?

The 30 Git Extensions EQ002 rows of `docs/runs/2026-09-30-divergent-audit.md`:

| Classification | Results | Countable | Counted | Not Divergent at this commit | Solver gave up | Shares |
|---|---|---|---|---|---|---|
| confirmed | 1 | 1 | 1 | 0 | 0 | 1 (no source parameter) |
| false positive | 25 | 8 | 1 | 6 | 1 | 1 (no source parameter) |
| undetermined | 4 | 0 | 0 | 0 | 0 | none |

They do not differ: the one confirmed result and the one counted false positive both have share 1
over a space of one point. The other 17 false positives and all 4 undetermined results are not
countable (10 `call-result`, 4 `heap-map`, 3 `sort-by-value`; 4 `call-result`). 6 false positives
are no longer Divergent at this commit, which is what their tickets fixed.

## Reading

- The measure is not computable on these Divergents. 55 of 80 pass a `string` or an object to a
  call, and a call's result is a free function of its argument, so no parameter space of bits
  exists for them.
- Where it is computable it is 1. 16 of the 18 countable pairs take no parameter the count can
  range over: parameterless methods, test methods, property getters. A divergence there is on the
  heap or the call trace, and "on what share of inputs" has one answer.
- The differences a migration makes on this corpus are a call replaced, added or removed, which
  differs on every input that reaches it. A guard that differs on a few integer values, the case
  the hashing scheme is for, did not occur once in 80.
- The scheme itself works on Z3 at this size: 9 self-test pairs of up to 33 bits in 0.4 to 2.0 s.
  It fails where the divergence relates two parameters.

## Outcome (criterion 5)

12 of 80 counted in time is 15.0%, below half. No ADR and no `properties.divergentShare`. ROADMAP's
post-MVP list has the measured line. `tools/spikes/divergent-count/` is deleted if nothing builds
on it.

## Appendix: every countable result

Columns: run, procedure identity, whether the run read reports it EQ002, the P2-047 classification,
bits, outcome, log2 share, solver checks, seconds.

| Run | Procedure | EQ002 in run | Audit | Bits | Outcome | log2 share | Checks | Seconds |
|---|---|---|---|---|---|---|---|---|
| gitextensions-8522 | `` ConEmu.WinForms.ConEmuStartInfo::get_BaseConfiguration() `` | no | false positive | 0 | not Divergent at this commit | n/a | 1 | 0.2 |
| gitextensions-8522 | `` EasyHook.LocalHook::Release() `` | yes | confirmed | 0 | counted | 0 (exact) | 1 | 0.0 |
| gitextensions-8522 | `` GitCommands.AppSettings::GetResourceDir() `` | yes | n/a | 0 | counted | 0 (exact) | 1 | 0.1 |
| gitextensions-8522 | `` GitCommands.CommitMessageManager::ResetCommitMessage() `` | no | false positive | 0 | not Divergent at this commit | n/a | 1 | 0.0 |
| gitextensions-8522 | `` GitCommands.Config.ConfigFile.ConfigFileParser::NewValue() `` | yes | n/a | 0 | not Divergent at this commit | n/a | 1 | 0.0 |
| gitextensions-8522 | `` GitCommands.Config.ConfigSection::ToString() `` | yes | n/a | 0 | not Divergent at this commit | n/a | 1 | 0.0 |
| gitextensions-8522 | `` GitCommands.FileAssociatedIconProvider::DeleteFile(string) `` | no | false positive | 1 | not Divergent at this commit | n/a | 1 | 0.0 |
| gitextensions-8522 | `` GitCommands.GitPushAction::ToString() `` | yes | n/a | 0 | not Divergent at this commit | n/a | 1 | 0.0 |
| gitextensions-8522 | `` GitCommandsTests.FileAssociatedIconProviderTests::Setup() `` | no | false positive | 0 | not Divergent at this commit | n/a | 2 | 0.3 |
| gitextensions-8522 | `` GitCommandsTests.Git.GitRevisionTesterTests::Setup() `` | no | false positive | 0 | not Divergent at this commit | n/a | 2 | 0.2 |
| gitextensions-8522 | `` GitCommandsTests.SshPathLocatorTest::File_system_access_throwing_should_return_empty_string() `` | yes | false positive | 0 | counted | 0 (exact) | 2 | 0.6 |
| gitextensions-8522 | `` GitCommandsTests.SshPathLocatorTest::Find_on_gitBinDir_parent_throwing_should_return_empty_string() `` | yes | n/a | 0 | counted | 0 (exact) | 2 | 0.6 |
| gitextensions-8522 | `` GitUI.CommandsDialogs.FormCleanupRepository::AddPath_Click(object,System.EventArgs) `` | yes | n/a | 2 | counted | 0 (exact) | 6 | 2.7 |
| gitextensions-8522 | `` GitUI.Hotkey.HotkeySettingsManager::LoadSerializedSettings() `` | yes | n/a | 0 | not Divergent at this commit | n/a | 1 | 0.0 |
| gitextensions-8522 | `` GitUI.Theming.ThemeModule::ResetGdiCaches() `` | yes | n/a | 0 | counted | 0 (exact) | 1 | 0.0 |
| gitextensions-8522 | `` GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::get_StartSegments() `` | yes | n/a | 0 | counted | 0 (exact) | 1 | 0.0 |
| gitextensions-8522 | `` GitUITests.Editor.RichTextBoxXhtmlSupportExtensionTests::GetLink_should_return_null_if_right_of_link() `` | yes | false positive | 0 | solver gave up | n/a | 2 | 2.9 |
| gitextensions-8522 | `` GitUITests.Editor.RichTextBoxXhtmlSupportExtensionTests::GetLink_should_return_uri_if_ends_with_link_without_link_text() `` | yes | n/a | 0 | counted | 0 (exact) | 1 | 0.0 |
| gitextensions-8522 | `` GitUITests.Editor.RichTextBoxXhtmlSupportExtensionTests::GetLink_should_return_uri_if_without_link_text() `` | yes | n/a | 0 | counted | 0 (exact) | 1 | 0.0 |
| gitextensions-8522 | `` ICSharpCode.TextEditor.TextAreaControl::JumpTo(int) `` | no | false positive | 32 | not Divergent at this commit | n/a | 1 | 0.0 |
| gitextensions-9860 | `` BugReporter.Program::Main() `` | yes | n/a | 0 | solver gave up | n/a | 2 | 20.5 |
| gitextensions-9860 | `` GitExtensions.Program::Main() `` | yes | n/a | 0 | counted | 0 (exact) | 1 | 0.1 |
| jellyfin-13023 | `` Jellyfin.Data.Entities.User::AddDefaultPreferences() `` | yes | n/a | 0 | counted | 0 (exact) | 2 | 0.6 |
| jellyfin-13023 | `` Jellyfin.Server.Helpers.StartupHelpers::PerformStaticInitialization() `` | yes | n/a | 0 | counted | 0 (exact) | 1 | 0.0 |
