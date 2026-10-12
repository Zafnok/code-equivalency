# P2-160 Stryker shards split a file, so a PR that changes one large file is not one shard
Status: done (PR #480)
Effort: S
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-133

## Goal
P2-133 sharded the mutation run by file: `.github/scripts/mutation-plan.sh` puts whole files into at most 4
shards on a PR and 8 on the nightly sweep. A project of which a PR changes one file therefore still has one
shard, and Stryker's `--since` tests every mutant of a changed file, not only the changed lines.

PR #475 (P2-081) changes one file of `src/Equiv.Verify.Z3`, `ModelDecoder.cs` (34 kB, 133 mutants to test).
Its `stryker-shard (Equiv.Verify.Z3, 1/1)` job (run 38183078133, job 114599662828) was still running after
2 h 07 min. The same file as one shard took 47 min on an earlier push of that PR (run 38167046535) and
2 h 09 min on PR P1-020 (run 38131808065, shard 1/3). The nightly sweep of 2026-10-11 (run 38134477611) had
8 shards for the project's 419 kB: one finished in 2 h 59 min, five were cancelled at the 180-minute limit.

When this is done the plan cuts a large file into pieces that go to different shards, and gives a project as
many shards as its bytes call for.

## Spec references
`docs/QUALITY-GATES.md` (Mutation row), ticket P2-133 (its Out of scope names this split and Stryker's
character spans).

## Acceptance criteria (all must hold; nothing beyond them)
1. `mutation-plan.sh` takes an optional bytes-per-shard figure per project (`<src>=<test>:<bytes>`, 48000
   without one), plans the files' bytes over it shards, rounded up and at most `<max-shards>`, and cuts a file
   larger than one shard's share into equal pieces that it packs like files.
2. A shard that holds some pieces of a file is given the file's `--mutate` glob and one `--mutate` exclusion
   with character spans per run of pieces it does not hold. No mutant is left to no shard, wherever the cuts
   fall: a mutant that crosses a cut is tested by the shards on both sides.
3. `mutation-score.sh` counts a mutant that more than one shard tested once, and reads one report at a time.
4. `mutation.yml` plans at most 8 shards per project on a PR or a dispatched run and 32 on the nightly sweep,
   with 6000 bytes per shard for `Equiv.Verify.Z3` and 24000 for `Equiv.Core`. The six required check names
   are unchanged.
5. `docs/QUALITY-GATES.md` describes the split in the Mutation row.
6. `## Notes` holds what a run of the new workflow on PR #475's changes took, per shard.

## Files
`.github/workflows/mutation.yml`, `.github/scripts/mutation-plan.sh`, `.github/scripts/mutation-score.sh`,
`docs/QUALITY-GATES.md`, `docs/ROADMAP.md`, this ticket.

## Tests
No `src/` or `tests/` file changes. The proof is workflow runs: this PR's own run (no `.cs` change: six passing
`stryker` checks and no shard), and a dispatched run on a branch that carries this workflow and PR #475's
changes.

## Size guard
A change to a test, to the 90 threshold, to `.github/stryker-pr-config.json` or to any file under `src/` means
the ticket has been misread: stop.

## Out of scope
Changing the 90% threshold. Changing any test. Changing branch protection or the ruleset. Making a mutant
cheaper: the coverage capture that names a different number of covering tests from run to run, and the mutants
that time out (both in Notes).

## Notes
- Why the one shard takes hours, from the JSON reports of the two earlier `ModelDecoder.cs` runs (the same
  132 and 133 mutants, 116 killed and 16 and 17 timed out in both):

  | Run | Coverage capture | Tests covering a mutant (least / median / most) | Whole Stryker run |
  |---|---|---|---|
  | 38167046535 | 4 min | 37 / 62 / 256 | 47 min |
  | 38131808065, shard 1/3 | 12 min | 204 / 226 / 369 | 2 h 09 min |

  Three things add up. One file is one shard. The MTP runner's per-test coverage capture is not stable: on the
  slow run it charged every mutant with at least 204 of the 760 tests, on the fast run with a median of 62, and
  a mutant is run against every test that covers it. And one mutant in eight times out, each costing a full
  timeout and a test-server restart (P2-133's Notes say the same of its run). The split addresses the first;
  the two others make a shard's time vary threefold and are not touched here.
- Checked in Stryker.NET's source at tag `dotnet-stryker@5.0.0`: `ExclusionPattern` reads a leading `!` and
  trailing `{from..to}` groups off a `--mutate` value; `FilePattern.IsMatch` is true when the glob matches and
  one of the pattern's spans contains the mutant's whole span (`TextSpan.Contains`), a pattern without spans
  having the span 0 to `int.MaxValue`; `FilePatternMutantFilter` keeps a mutant that an include pattern matches
  and no exclude pattern matches. So a mutant that lies across the edge of an exclusion is kept.
- Decision: how a shard is held to its pieces -> include the file, exclude the other shards' pieces.
  Alternative: include only the shard's own spans (`File.cs{a..b}`), which drops a mutant that crosses a cut
  from every shard, since no shard's span contains it. Rule: soundness of the gate first. With exclusions such
  a mutant is tested twice instead of never.
- Decision: an exclusion stops one character short of the shard's own pieces. A zero-width span exactly on a
  cut is contained in both neighbours' exclusions otherwise. Whether Stryker ever makes such a mutant was not
  looked into; the gap makes the question moot.
- Decision: cuts are byte offsets (`wc -c`), Stryker's spans are characters. Every shard computes a file's
  cuts from the same numbers, so a file with multi-byte characters only has its cuts a little further along.
- Decision: the score counts distinct mutants by file, location, mutator and replacement, not by Stryker's
  mutant id, and keeps the undetected status when two shards disagree. Alternative: the id, which rests on
  every shard numbering mutants alike.
- Decision: the shard count comes from bytes over a per-project figure, where P2-133 planned one shard per
  file up to the cap. A PR that changes four small files of a cheap project now has one shard, not four
  builds. The figures: `Equiv.Verify.Z3` 6000 (30 kB was 132 mutants and 38 to 111 minutes of mutation after
  12 to 17 minutes of build, initial run and capture), `Equiv.Core` 24000 (a 51 kB nightly shard took 53 min),
  48000 otherwise (`IrLowerer.cs`, 184 kB, 719 mutants, 58 min). Bytes are a rough stand-in: on PR #475
  `ClosedCalls.cs` (5 kB) took as long as `IlLowerer.cs` (73 kB).
- Checked before any run, with the plan's two `awk` stages and Stryker's containment rule replayed over them:
  for the nightly `Equiv.Verify.Z3` file list (32 shards, 28 exclusions) and PR #475's `Equiv.Frontend.CSharp`
  files (6 shards), 320,610 and 27,497 spans, among them the zero-width spans on every cut and every span within three
  characters of a cut, are each tested by at least one shard.
- Measured 2026-10-12, run 38191684234: a dispatch on branch `P2-160-proof-pr475` (this workflow plus PR #475's
  five files, merged onto `main` at `624718ae`). Wall-clock was 63 min, the slowest shard. PR #475's own run on
  the old plan (38183078133) had the one `Equiv.Verify.Z3` shard cancelled at the 180-minute limit with no
  result, and 66 min for the slowest of four `Equiv.Frontend.CSharp` shards.

  | Shard | Patterns | Mutants tested | Job start to first mutant | Whole job |
  |---|---|---|---|---|
  | Z3 1/6 | `ModelDecoder.cs`, piece 1 | 7 | 19 min | 27 min |
  | Z3 2/6 | piece 2 | 21 | 20 min | 44 min |
  | Z3 3/6 | piece 3 | 49 | 10 min | 31 min |
  | Z3 4/6 | piece 4 | 15 | 20 min | 38 min |
  | Z3 5/6 | piece 5 | 37 | 20 min | 57 min |
  | Z3 6/6 | piece 6 | 38 | 20 min | 63 min |
  | Frontend 1/6 | `IrLowerer.cs`, piece 1 | 207 | 8 min | 30 min |
  | Frontend 2/6 | piece 2 | 153 | 8 min | 27 min |
  | Frontend 3/6 | piece 3 | 191 | 6 min | 17 min |
  | Frontend 4/6 | piece 4 | 188 | 7 min | 14 min |
  | Frontend 5/6 | `IlLowerer.cs` piece 1, `SsaBuilder.cs` | 255 | 6 min | 39 min |
  | Frontend 6/6 | `IlLowerer.cs` piece 2, `ClosedCalls.cs` | 260 | 7 min | 46 min |

  Stryker took the exclusions as written, and no shard needed its second attempt. The shards' own counts add up
  to 167 and 1,254 mutants; the `stryker` jobs counted 166 (killed 147, timeout 19, score 100%) and 1,244
  (killed 1,029, timeout 188, survived 27, score 97.82%), so 1 and 10 mutants crossed a cut, were tested on both
  sides and counted once. The counts were not compared with an unsharded run of the same commit: there is none,
  the old run of these files having been cut off.
- What is left of a shard's time is not the split's to fix. Five of the six `Equiv.Verify.Z3` shards spent 19 to
  20 minutes before their first mutant, 13 of them in the coverage capture, and shard 6 lost 10 of its 38 mutants
  to timeouts. Equal bytes are not equal mutants either: the six pieces of one file hold 7 to 49.
