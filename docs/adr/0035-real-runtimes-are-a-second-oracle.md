# ADR 0035: The real runtimes are a second oracle: execution measures, confirms and bounds, and never proves

Status: proposed (2026-09-24)

## Context
The 2026-09-24 census (`docs/runs/2026-09-24-census-verdict.md`) found that Z3 sees little of the
problem. 75.6% of Git Extensions' changed pairs contain an opaque node. The three agent pairs are
pure retargets, and on those congruence decides nearly every verdict. Congruence rests on
`runtime-changes.json`, which has 14 hand-picked rows. Nothing checks whether a Z3 counterexample
reproduces on a real CLR: M4-007 criterion 5 asks the user which Divergents they believe. Section
7's soundness harness runs over IR, and the lowering oracle covers only straight-line integer
methods. Seeded recall, the one criterion that can stop the project, has never been measured.

## Decision
Running the code on the two real runtimes becomes a second oracle. It sits beside the solver and
is never a replacement for it. It has three uses, landed in this order:
1. **Measure the runtime.** `tools/runtime-diff` calls BCL members with generated arguments under
   .NET Framework 4.8 and under .NET 10 and compares the outcomes. A member that differs becomes a
   `runtime-changes.json` row with `source: measured` and a witness input. The row then works like
   every other row (EQ006, runtime-sensitive for ADR 0024).
2. **Confirm counterexamples.** With `--execute`, every Divergent from the solver has its model
   replayed on both runtimes. `properties.replay` is one of `reproduced`, `not-reproduced` or
   `not-constructible`. Replay never changes the verdict. A `not-reproduced` result in a corpus run
   is a soundness or modelling finding and gets a ticket.
3. **Bound an Unknown.** With `--execute`, an Unknown pair is run on generated inputs. It stays
   EQ003 Unknown and gains `properties.differentialTesting`: the input count, the species definition
   used, and the estimated discovery probability (Böhme et al., FSE 2021), stated for the
   generator's input distribution. If a run observes a divergence, the result is EQ002 with
   `proofMethod: observed` and the input as the model.

Execution never yields Equivalent. The mechanism:
- `Equiv.Core` owns the contract types (`ExecutionRequest`, `ExecutionOutcome`).
- `Equiv.Frontend.CSharp` generates a driver program per member or pair and compiles it with
  Roslyn: once against .NET Framework 4.8 reference assemblies and once against .NET 10.
- The new project `Equiv.Execute` depends on `Equiv.Core` only. It runs each driver as a child
  process on its own runtime, runs every input twice per side (Diffy's noise cancellation) and
  under a fixed culture set, and compares the canonical outcomes.

There is no reflection anywhere: the driver is generated source.

## Why
- About a quarter of changed code can be lowered today. Execution needs no lowering, so it reaches
  the other three quarters, and the whole BCL.
- On retargets, the runtime table is the product. A table measured with witnesses beats one
  chosen by hand, and each row can be reproduced.
- A counterexample that reproduces on the CLR is the strongest Divergent there is. One that does
  not reproduce is a free soundness alarm, and today nothing raises it.
- A stated discovery probability is the only honest "likelihood" figure for an Unknown. Folding it
  into Equivalent would break section 1's claim.
- Out-of-process drivers are the only way to host .NET Framework 4.8 next to a .NET 10 engine.
  They also keep the rule against reflection.

## Rejected
- **In-process loading (`AssemblyLoadContext`, `MethodInfo.Invoke`):** it cannot host .NET
  Framework 4.8, and it needs reflection.
- **Pex or IntelliTest:** closed source, Visual Studio Enterprise only, and .NET Framework only.
- **The repository's own tests as the oracle:** M4-007 criterion 4 already uses them, and they
  cover only what their authors thought to test.
- **Probabilistic Equivalent** (for example "Equivalent with 99.9%"): a sampled claim would sit on
  the same rule id as a proof.
- **Coverage-guided fuzzing now (SharpFuzz):** it adds a dependency and instruments user
  assemblies. It can come later as its own ADR 0002 row, once plain generation's discovery
  probability shows a plateau.

## Consequences
- `--execute` runs the user's code: static constructors, file I/O, network. It is opt-in, printed
  on stderr when on, and never on by default.
- It needs Windows with .NET Framework 4.8. On Linux it exits 3 with a message. The hosted tier
  (ADR 0032, Linux Container Apps) cannot offer it. ADR 0031's parity requirement covers runs
  without `--execute`; runs with it are Windows-only by design.
- `runtime-changes.json` rows gain `source` (`curated`, `documented`, `measured`) and an optional
  `witness` (M2-007 adds `source`; M3-033 adds the first `measured` rows).
- New project `src/Equiv.Execute` and one new architecture rule: it references `Equiv.Core` only.
- VERIFICATION-MODEL.md changes once this ADR is accepted: section 1 (execution never proves),
  section 6 (`replay`, `differentialTesting`, `proofMethod: observed`) and section 7 (a replay
  obligation). ARCHITECTURE.md gains `Equiv.Execute`, and the CLI section gains `--execute`.
- Tickets: M3-032 (harness and `tools/runtime-diff`), M3-033 (measure the members the corpus
  calls), M4-009 (replay), P1-007 (tested Unknowns), M5-002 (an MCP `probe` tool). M0-012 does
  not use this machinery: it runs generated net10 code in-process inside a test project, as the
  lowering oracle already does.
