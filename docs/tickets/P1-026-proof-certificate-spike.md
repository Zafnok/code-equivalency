# P1-026 Spike: can a solver Equivalent be checked by an independent proof checker?
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-025

## Goal
An Equivalent from the solver rests on Z3 answering unsatisfiable. P2-059 found Z3's Spacer giving
that answer wrongly once. A proof certificate removes the solver from what has to be trusted: cvc5
writes an Alethe proof of the same query and Carcara checks it. ROADMAP's post-MVP list has this as
low priority. Measure whether it works on this tool's queries at all, and what it costs.

State the limit in the report: a certificate covers the query, not the lowering or the encoder that
produced it, and an Equivalent by congruence (94.6% of matched pairs on the three large runs) has no
query to certify. M0-012's differential gate is what covers the rest.

## Spec references
ADR 0005, ADR 0002, ADR 0017, ADR 0036 (a checker admits, a proposer does not), ROADMAP post-MVP
("A second solver ... Alethe proof certificates checked by Carcara"), P2-059, P1-025 (the exporter).

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/spikes/proof-certificates/` reuses P1-025's exporter on every result of
   `gitextensions-9860` with `proofMethod` `bounded` or `lockstep-induction` (239 results), and
   writes each unsatisfiable query of the proof as an SMT-LIB 2 file.
2. Each file is run with cvc5 producing an Alethe proof, and the proof is checked with Carcara. Per
   query: cvc5 unsatisfiable or not, proof written or not, Carcara accepts, rejects or reports holes
   (steps it does not check), cvc5 time, checking time, proof size.
3. `docs/runs/<date>-proof-certificate-spike.md` gives the counts per outcome, the median and largest
   time and size, the pairs all of whose queries are accepted with no hole, and the limit stated in
   the Goal. Identities and counts only.
4. Unless no pair is certified with no hole, write the ADR (an opt-in `--certify`, where the
   certificate goes in SARIF, the ADR 0002 rows, and what a pair with holes reports) and its ticket.
   Only if nothing certifies, add one measured line to ROADMAP's post-MVP list, replacing the
   unmeasured one.
5. Nothing under `src/` changes. No binary is committed.

## Files
`tools/spikes/proof-certificates/**`, `docs/runs/<date>-proof-certificate-spike.md`, and either
`docs/adr/NNNN-*.md` with its README row and a ticket, or `docs/ROADMAP.md`.

## Tests
A `--self-test` on one sample pair: its queries export, cvc5 proves them and Carcara accepts.

## Size guard
Z3's own proof objects, Lean, or any proof of the encoder: stop. Any edit under `src/`: stop.

## Out of scope
Certifying rung 4 and rung 5 invariants (they are already checked rule by rule, P2-059).
Certificates for Divergent results (replay is their check).

## Notes
- From the 2026-10-03 improvement review (the "Checkable proof certificates" row). The review says a
  certificate makes a result auditable without trusting the IR and the encoder; it does not, see the
  Goal.
- Result (2026-10-14, `docs/runs/2026-10-14-proof-certificate-spike.md`): 171 of 302 results are
  certified with no hole (every one `bounded`; each `divergence` and `opaque` query accepted). 25
  more are accepted only with holes, all of them cvc5's `bv_bitblast_step_*` rules, and 12 stop at
  a bit-vector RARE rule the rule file lacks. 76 have a query cvc5 cannot read (a constant array the
  printer declines), 12 lockstep pairs have no Alethe proof (`seq.unit`), 4 run out of cvc5's 60 s,
  and 2 are proved by a later round of rung 1, which the spike does not rebuild. Carcara rejected no
  proof. Median cost 170 ms a pair. ADR 0057, P1-041, P1-042 and P2-179 follow.
- Deviation: criterion 1 counts 239 results, from the 2026-10-03 run. The spike rebuilds every pair
  with today's frontend, so it reads the latest thorough run of the pair (`20261012-2040`, 302
  results: 285 `bounded`, 17 `lockstep-induction`). The older run's identities would describe an
  older lowering.
- Deviation: the Goal's 94.6% (three large runs) is now 95.4% over the five large runs (README
  scoreboard after P2-130). The report states both.
- Decision: "reuses P1-025's exporter" means `SecondSolver.Print`, the form ADR 0050 made of
  P1-025's printer. P1-025's spike no longer builds against `src/` (`Z3Backend.Check` is gone, and
  `Query` returns a `SolverQuery`). Where `Print` declines a constant array, the spike falls back to
  P1-025's own form, as Z3 prints it. cvc5 reads none of those files.
- Decision: a proof's queries are rebuilt from `LoopLadder`'s parts, which are internal and
  reached through the assembly-name trick: rung 1 on the positional product, rung 2 from
  `LockstepInduction.Loops` and `Couple`, with the cuts its constructor keeps private remade the same
  way. Each query is asked of Z3 again, and only one Z3 answers unsatisfiable counts as part of the
  proof.
- Decision: flags are those of cvc5's `AletheTester`, less `--proof-alethe-testing` (cvc5 then
  refuses any proof holding `bvadd`, so every query here) and less `--allowed-rules` (which would
  count unchecked rules as checked). `--ignore-unknown-rules` makes an unknown rule a hole rather
  than a rejection, as cvc5's own `carcara-check.sh` does.
- Decision: a missing RARE rule is counted apart from `invalid`, although Carcara prints `invalid`.
  The rule is absent, not shown wrong, and Carcara stops there, so the rest goes unchecked; it is
  neither a rejection nor a hole.
- Decision: an `unsat` with nothing after it is "no Alethe proof" (cvc5 hit `--tlimit` while
  writing the proof), not a Carcara rejection. The first pass misfiled 3 such cases as
  "does not conclude empty clause".
- Toolchain: Carcara has no Windows release, and its 2023 releases predate cvc5 1.4.1's Alethe
  output. The commit used is the one cvc5's CI pins. Building it on Windows (GNU Rust toolchain) took
  three things: `gmp-mpfr-sys`'s `use-system-libs` feature, added in the local copy's `Cargo.toml`
  to link MSYS2's GMP; a `pkg-config` stand-in that prints `-LC:/msys64/ucrt64/lib`; and
  `RUSTUP_TOOLCHAIN` set to the installed 1.98.1, because `rust-toolchain.toml` pins 1.93, which
  rustup would download. `carcara --version` prints the enclosing git repository's describe, not
  Carcara's (the tarball has no `.git`), so the report names the commit.
- Toolchain: PowerShell 5.1 under `$ErrorActionPreference = 'Stop'` ends the script at a native
  command's first stderr line, and the spike logs progress to stderr; run it through `cmd /c`.
- Carcara is a small academic project (about 47 stars). The repository owner allowed it for the spike and does not
  want the product to lean on it. ADR 0057 keeps it opt-in and outside every verdict for that reason.
