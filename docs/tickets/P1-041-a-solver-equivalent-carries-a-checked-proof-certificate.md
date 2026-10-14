# P1-041 Under `--certify`, a solver Equivalent carries cvc5's Alethe proof of each of its queries, checked by Carcara
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P1-026, P1-033

## Goal
ADR 0057. P1-026 found that on `gitextensions-9860` 171 of 302 solver Equivalents get a proof
certificate with no hole: cvc5 writes an Alethe proof of each query, and Carcara accepts it. It
costs a median of 170 ms a pair (`docs/runs/2026-10-14-proof-certificate-spike.md`). Build it as
an opt-in `--certify` that adds `properties.certificate` to every solver Equivalent and changes no
verdict.

When done, `equiv compare --certify` on that pair reports at least those 171 results `certified`,
and the results, rule ids and exit code are the same as a run without it.

## Spec references
ADR 0057 (every decision), ADR 0050 (the printer, cvc5 as a process), ADR 0017 (LGPL as a
standalone executable), ADR 0002, ADR 0036, VERIFICATION-MODEL.md sections 5.1 and 6,
`tools/spikes/proof-certificates/` (the flags, the hole rules, the Windows build of Carcara),
`src/Equiv.Verify.Z3/SecondSolver.cs` (`Print`), `src/Equiv.Verify.Cvc5/`.

## Acceptance criteria (all must hold; nothing beyond them)
1. `tools/carcara/fetch.ps1` fetches the Carcara source tarball of commit
   `20bf49c5ce31089ac83118574c888669355aaae1` and cvc5's `proofs/alethe/rewrites.eo` at
   `f0bc3373ef032a3fc5006f22778aaae7b274f429`. It checks each against a SHA-256 pinned beside
   the script, builds Carcara into the git-ignored `.carcara/`, and prints both paths, as
   `tools/cvc5/fetch.ps1` does. ADR 0057's two ADR 0002 rows are added.
2. `Equiv.Core` has `IProofCertifier` (ADR 0057 decision 7). `Equiv.Verify.Cvc5` implements it
   with the spike's cvc5 and Carcara flags, its hole rules and its budgets (decision 6). An
   architecture test keeps Carcara and the Alethe text out of every other project.
3. `--certify` with `solvers.carcara.path`, `solvers.carcara.rules` or `solvers.cvc5.path` missing
   is a usage error, exit 2, naming the missing setting. Without `--certify` the SARIF is
   byte-identical to today's (snapshot).
4. Under `--certify`, every unsatisfiable query of the proving rung of a `bounded` (every round),
   `lockstep-induction` or `k-induction` Equivalent is certified. Its result gets
   `properties.certificate` exactly as decision 3 describes, and the run gets
   `run.properties.certification`. A congruence Equivalent gets no `certificate`.
5. A `rejected` certificate raises a tool notification at level `error`. No status changes a
   verdict, a rule id, an exit code or `--fail-on` (a property test over the samples, run with
   and without `--certify`).
6. `--certify-out <dir>` writes each printed query and its proof, named by SHA-256.
7. A rerun of `gitextensions-9860` in thorough mode with `--certify` reports `certified` for every
   identity the P1-026 report lists. It changes no other property of any result. The counts by
   status go in `docs/runs/`.

## Files
`src/Equiv.Core/` (the interface, the certificate record), `src/Equiv.Verify.Cvc5/`,
`src/Equiv.Verify.Z3/` (the hand-off in the proving rungs), `src/Equiv.Cli/` (the option, the
config, SARIF), `tools/carcara/**`, `docs/adr/0002-dependencies.md`, `docs/VERIFICATION-MODEL.md`,
`docs/ARCHITECTURE.md`, `docs/runs/<date>-certify-gitextensions-9860.md`, tests.

## Tests
Unit tests of `IProofCertifier`'s result parsing for each status, from recorded outputs, not live
processes: valid, holey with each hole kind, a missing RARE rule, invalid, an unsat with no proof,
an Alethe `error`, a timeout, and a proof over budget. An integration test, skipped when Carcara is
not configured, runs one sample to `certified` and one whose proof needs bit-blasting to `holey`.
A snapshot of a `certificate` property. The property test of criterion 5.

## Size guard
Changing a verdict on a certificate's status, or certifying rungs 4 and 5, Divergents or
congruence: stop (ADR 0057 decisions 2 and 4). Teaching the printer a new rewrite is P2-194, and
changing how rung 2 compares traces is P1-042.

## Out of scope
Linux and the container image, which have no cvc5 (ADR 0050). Ethos and CPC proofs. Closing
Carcara's bit-vector gaps upstream.

## Notes
- From P1-026 (ADR 0057). The spike's `Certifier.cs` lists the flags and why two of cvc5's CI
  flags are left out: `--proof-alethe-testing` refuses every bit-vector proof, and
  `--allowed-rules` hides holes.
- Carcara is a small academic project. ADR 0057 decision 4 is what keeps it from being
  load-bearing; do not weaken that.
