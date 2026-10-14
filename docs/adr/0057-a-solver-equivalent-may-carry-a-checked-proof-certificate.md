# ADR 0057: A solver Equivalent may carry a proof certificate, cvc5's Alethe proof checked by Carcara, opt-in and never part of the verdict

Status: accepted (2026-10-14)

## Context
A solver Equivalent rests on a solver answering unsatisfiable, and P2-059 caught Z3's Spacer
giving that answer wrongly. ADR 0050 says of a wrong `unsat` from cvc5 that "P1-026 (proof
certificates) is the check". P1-026 measured it on the 302 `bounded` and `lockstep-induction`
Equivalents of `gitextensions-9860` (`docs/runs/2026-10-14-proof-certificate-spike.md`):
- 171 pairs: cvc5 1.4.1 writes an Alethe proof of every query, and Carcara (commit `20bf49c`)
  accepts each one with no hole. That costs a median of 170 ms a pair, and 35 s at most.
- 25 pairs: accepted only with holes, which are bit-blasting steps Carcara does not check.
- 12 pairs: Carcara stops at a bit-vector rewrite rule its rule file lacks.
- 94 pairs: there is no proof to check (cvc5 cannot read the query, Alethe cannot express it, or
  cvc5 runs out of time).

Carcara rejected no proof. Carcara is a small academic project, about 47 stars and one research
group. Its maintenance base is thin.

## Decision
1. **Opt-in.** `equiv compare --certify` turns certification on. It needs three settings in
   `equiv.config.json`: `solvers.cvc5.path` (ADR 0050), `solvers.carcara.path`, and
   `solvers.carcara.rules` (the RARE rule file). Without them `--certify` is a usage error. Without
   `--certify` nothing changes.
2. **What is certified.** Each query a solver Equivalent rests on, in its proving rung:
   - rung 1's `divergence`, `opaque` and `bound` queries, in whichever round proved the pair;
   - rung 2's base and step obligations;
   - rung 3's obligations.

   Each query is printed as ADR 0050 prints it for cvc5. cvc5 proves it with an Alethe proof
   (`--dump-proofs --proof-format-mode=alethe --proof-granularity=dsl-rewrite`), and Carcara checks
   the proof against the printed query, with no rule counted as checked that it does not check.
   Rungs 4 and 5 are out: their answers are already checked rule by rule (P2-059). A Divergent is
   out too, since its check is the replay. An Equivalent by congruence has no query, and says so.
3. **Where it goes.** The result's `properties.certificate` is an object with these fields:
   - `status`, one of four:
     - `certified`: every query was accepted with no hole;
     - `holey`: every query was accepted, at least one with holes;
     - `uncertified`: some query has no checked proof, because it could not be printed, cvc5 gave
       no proof, the proof was over budget, or Carcara stopped at a rule it lacks;
     - `rejected`: Carcara found a step wrong;
   - `prover`: cvc5 and its version;
   - `checker`: Carcara and its commit;
   - the SHA-256 of the rule file;
   - a `queries` array with each query's name, status, holes (rule names with counts) and reason,
     and the SHA-256 of the printed query and of the proof.

   `run.properties.certification` counts results by status. Proofs are not put in SARIF: one
   reached 627 MB. `--certify-out <dir>` writes each printed query and proof there, named by its
   hash. SARIF stays 2.1.0. No rule id, no `proofMethod` and no new result kind is added.
4. **What a certificate is worth.** It never changes a verdict, a rule id, an exit code or
   `--fail-on`. A `holey` certificate is not a certificate: wherever results are counted it counts
   with `uncertified`, and its holes are listed by rule name so a reader sees what went unchecked.
   A `rejected` certificate also raises a SARIF tool notification at level `error`, naming the
   query and the step's rule. It is a finding for a person, since cvc5, Carcara or the printer is
   wrong, and it is never a verdict.
5. **What it covers.** The printed query and nothing upstream of it. The lowering, `IrUnroller`,
   `IrFragmenter`, the product encoder and the printer stay trusted. M0-012's differential gate is
   what checks them. VERIFICATION-MODEL.md says this beside the property.
6. **Budgets.** cvc5 gets `timeoutMs` (`--tlimit`). Carcara gets `timeoutMs` of wall clock. A
   proof over 128 MiB is not checked, and its query is `uncertified` ("proof over budget"). Every
   proof P1-026 saw accepted was 65 MB or less. The build
   ticket confirms the numbers by measurement.
7. **Where it sits.** `Equiv.Core` gains `IProofCertifier`: a printed query in, a query certificate
   out. `Equiv.Verify.Cvc5` implements it as cvc5 followed by Carcara, both run as processes.
   `Equiv.Verify.Z3` hands each unsatisfiable query of the proving rung to the certifier when one
   is configured, and does not know what is behind the interface. A different checker for cvc5's
   own proof format (Ethos and CPC) would be a second implementation, with nothing else changing.
8. **Distribution.** Carcara is Apache-2.0. Built on Windows it links GMP, which is LGPL-3.0, so
   under ADR 0017 it is a standalone executable, run as a process and never redistributed: no
   Carcara file goes into a binary, the image or the action. `tools/carcara/fetch.ps1` fetches the
   source tarball of the pinned commit (cvc5's CI pins the same one) and the rule file
   `proofs/alethe/rewrites.eo` of cvc5 (BSD-3-Clause), checks both against pinned SHA-256s, and
   builds Carcara into the git-ignored `.carcara/`. Neither file is committed.

ADR 0002 rows, added by the ticket that builds this:

| Package | Version | Used by | Licence | Why this one |
|---|---|---|---|---|
| Carcara (executable, not a package) | commit `20bf49c5ce31089ac83118574c888669355aaae1` | Verify.Cvc5, as a process, only under `--certify` | Apache-2.0; built against GMP (LGPL-3.0), so never redistributed (ADR 0017) | the independent checker for cvc5's Alethe proofs, and the one cvc5's own CI checks against; measured by P1-026 |
| cvc5 `proofs/alethe/rewrites.eo` (data file) | cvc5 commit `f0bc3373ef032a3fc5006f22778aaae7b274f429` | Verify.Cvc5, read by Carcara | BSD-3-Clause | the RARE rules Carcara checks cvc5's `rare_rewrite` steps against; not in Carcara's repository |

## Why
- It is measured. 171 of 302 solver Equivalents get a certificate with no hole at a median cost of
  170 ms a pair, and no proof was rejected. For those pairs, neither Z3's answer nor cvc5's has to
  be trusted.
- It closes what ADR 0050 left open: cvc5's `unsat` answers are trusted as Z3's are, and a
  certificate is their check.
- It is the smallest trusted step available. Carcara checks a proof against the query; it does not
  search. ADR 0036's rule, that a checker admits and a proposer does not, applies to it.
- Nothing rests on Carcara. Because a certificate never changes a verdict, a Carcara that stalls,
  breaks or disappears costs `--certify` and nothing else. That is the answer to its thin
  maintenance base: it is pinned, optional and replaceable behind `IProofCertifier`.

## Rejected
- **An Equivalent only when certified:** 131 of 302 solver Equivalents would become Unknown on this
  pair, mostly for gaps in the checker, and Carcara would become load-bearing.
- **Z3's own proof objects:** they have no independent checker, and P1-026's size guard excluded
  them.
- **Ethos with cvc5's CPC proofs instead of Carcara:** not measured, and Ethos is written by the
  team that writes cvc5, so it is less independent. It is the first replacement if Carcara stalls,
  and the interface leaves room for it.
- **Proofs embedded in SARIF:** proofs run up to 627 MB.
- **Lean:** ADR 0005 and ADR 0051. Lean checks a proof someone writes; here cvc5 writes it.
- **Certifying congruence:** there is no query. The differential gate is that check.

## Consequences
- P1-041 builds it, with the ADR 0002 rows and `tools/carcara/fetch.ps1`. VERIFICATION-MODEL.md
  section 6 gains `certificate` in that ticket, and ARCHITECTURE.md gains `IProofCertifier`.
- Bit-vector reasoning is where certificates fail today. No accepted proof holds a bit-blasting
  step: cvc5 emits `bv_bitblast_step_*` rules Carcara does not check, and the rule file lacks some
  bit-vector rewrites. A run reports such pairs `holey` or `uncertified` until upstream closes
  those gaps. No work here closes them.
- Two encoding questions decide how far certification can reach, and each has its own ticket.
  P2-194: queries with a constant array the printer declines, 76 pairs here, reach no solver but
  Z3. P1-042: rung 2 compares call traces as sequences, which Alethe cannot express, so 12 pairs
  here cannot be certified.
- Like cvc5, Carcara runs on Windows only until someone builds it elsewhere. The Linux parity gate
  (ADR 0031) compares runs with the same options, `--certify` included.
