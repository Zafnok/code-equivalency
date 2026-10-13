# P2-124 Rerun the two version-upgrade pairs once runtime rows have change points, and refresh the upgrade verdict
Status: done (PR #483)
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-066, P2-105, P2-113, P2-117, P2-118, P2-122, P2-095, P2-127, P2-077, P1-032

## Goal
P2-066 ran `jellyfin-13023` (net8.0 to net9.0) and `gitextensions-9860` (net5.0 to net6.0). Its
verdict's main finding is that runtime-change rows with no change point fire on every .NET-to-.NET
pair: 158 of 161 EQ006, 120 of them false, and 353 of Jellyfin's 573 changed pairs are unedited bodies
that hold a call one `String.Equals` row matches. The solver proves 0 of Jellyfin's 573. P2-066 files
a ticket that gives every row a change point (P2-113 in pull request 371). That ticket's criteria are
a table test and a sample; none measures the corpus. Rebinding (P2-070) and effect-free calls
(P2-071) also merged after P2-066's build. Once the runtime-row ticket is done, run both pairs again
on one commit of `main` and write the verdict again, so the gain is a measured number.

The other tickets on the `Depends on` line are the scoreboard milestone P2-130 sets (2026-10-04):
the upgrades' remaining false-positive causes, the opaque reasons with a measured share, the open
soundness ticket and parallel verification.

## Spec references
ADR 0040 (pair kinds, the interval rule), ADR 0034 (changed pairs), ADR 0028;
`.claude/skills/equiv-corpus-run`; P2-066's verdict and its two SUMMARY files.

## Acceptance criteria (all must hold; nothing beyond them)
1. Both pairs are run in `full` mode at one commit of `main`, through `equiv-corpus-run`, one after
   the other. Each has a `docs/runs/<date>-full-<slug>/SUMMARY.md`.
2. `docs/runs/<date>-upgrade-verdict.md` has P2-066's tables again, each number next to P2-066's:
   congruent pairs, changed pairs, changed pairs in edited and in byte-identical files, proved
   Equivalent by `proofMethod`, Unknown by reason, EQ002, EQ006 by row with each row's `changedIn`.
3. Every EQ002 and EQ006 is adjudicated by P2-066's method. A result P2-066 adjudicated whose
   procedure and row are unchanged keeps its classification without a new trace.
4. The verdict says, for the 353 Jellyfin bodies and the 120 false EQ006, how many are now congruent,
   proved, Unknown or Divergent.
5. Each crash, and each false-positive cause or opaque reason at or above 5% of a pair's changed
   pairs with no open ticket, is filed as a ticket.
6. `equiv-scoreboard` is applied: the scoreboard rows and "Where it stands" numbers whose value
   changed are rewritten, and no other line of `README.md` is.

## Files
`docs/runs/`, `README.md`, `docs/ROADMAP.md`, new ticket files (criterion 5). Nothing under `src/`
or `tests/`.

## Tests
None.

## Size guard
A change under `src/`, `tests/` or `tools/` means the ticket was misread: file what you found.

## Out of scope
`--execute` where a pair's runtimes are not installed. New pairs. Fixing anything the run finds.

## Notes
- Found by the 2026-10-03 review of the open tickets: the runtime-row fix is the largest expected gain
  on upgrade pairs and no ticket measures it.
- Decision: compare mode -> both. The ticket names none; `equiv-corpus-run` asks for thorough for a verdict and
  `equiv-scoreboard` takes only quick runs, and criterion 6 applies the scoreboard. The verdict's tables are the
  quick runs and the thorough runs have their own section. Alternatives: quick only (no word on what the later
  passes add), thorough only (no scoreboard source). Rule: the skill's own ("this rule decides and the ticket's
  Notes says which was used").
- Decision: `--jobs 4` -> the value the 2026-10-08 runs of these pairs used, so times compare. Alternative: the
  default (24 on this box).
- Results (equiv `1d767f54`, 2026-10-12; `docs/runs/2026-10-12-upgrade-verdict.md`): EQ006 161 -> 4, Divergent
  173 -> 15, of which 6 confirmed and 9 false positives; `jellyfin-13023` proves 62 of 122 changed pairs, from 0 of
  573. Of the 353 Jellyfin bodies (350 found again by the text scan) 334 are congruent, 6 proved, 10 Unknown; of
  the 120 false EQ006, 118 are congruent and 2 proved.
- `.corpus/` did not exist in this worktree: `-Prepare`, `-Fetch` and restore were done new. Jellyfin's restore
  still needs `--configfile <this repo>/NuGet.config` (P2-115, open).
- P2-066's list of the 353 bodies and its analysis script were not kept; its two SARIF files were, in the
  `version-upgrade-pairs-corpus-1c9943` worktree's `.corpus/`, and criterion 4 was answered from them.
- `corpus.ps1 -Packages` fails on both pairs ("The property 'Files' cannot be found on this object"), so the
  summaries have no package-change line. Tooling, not filed as a ticket here (Size guard: nothing under `tools/`).
- Filed: P2-163, P2-164, P2-165 (criterion 5: `no-body`, 8.8% of `gitextensions-9860`'s changed pairs), P2-166.
  P2-163, P2-164 and P2-166 are below criterion 5's 5%; they are the causes of every false Divergent the runs
  have that no open ticket owned, filed as `equiv-corpus-run` asks of a false positive.
- A real behaviour change that P2-066's run reported EQ002, `Emby.Server.Implementations.ApplicationHost::GetCertificate(string,string)`,
  is Unknown (`abstraction`) at this commit. Not Equivalent, so not a soundness finding; recorded in the verdict.
