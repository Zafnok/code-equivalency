# P1-026 proof-certificate spike (throwaway)

Measures whether a solver Equivalent can be checked by an independent proof checker: cvc5 writes an
Alethe proof of each query the Equivalent rests on, and Carcara checks it. The result is in
`docs/runs/2026-10-14-proof-certificate-spike.md`.

- `ProofQueries.cs` rebuilds the queries of a `bounded` or `lockstep-induction` proof as
  `LoopLadder.Climb` builds them: the pair with its shared fragments made calls, then rung 1's
  `divergence`, `opaque` and (for a looping pair) `bound` queries on the unrolled product, whose
  call traces compare by position, or rung 2's base and per-loop step obligations on the cut
  procedures, whose traces compare as sequences. Each is asked of Z3 with the production solver and
  limits; a query Z3 does not answer unsatisfiable at this commit means the proof is not rebuilt,
  and the result is counted apart. Each query is printed by `SecondSolver.Print`, the exporter
  P1-025 measured and ADR 0050 made the product's, with `produce-proofs` in place of
  `produce-models` and no `get-value`.
- `Certifier.cs` runs cvc5 for an Alethe proof and Carcara on that proof against the query, with
  the flags cvc5's own regression tests use against the same Carcara commit, less
  `--proof-alethe-testing` (cvc5 then refuses every bit-vector proof) and less `--allowed-rules`
  (Carcara then counts unchecked rules as checked). A rule Carcara does not know is a hole
  (`--ignore-unknown-rules`), as are `hole` and `lia_generic` steps.
- `Program.cs` reads the SARIF, loads both solutions through the production frontend, certifies
  each result's queries, and prints the tables the report quotes.
- `SelfTest.cs` is the ticket's test: one sample pair (`a + b` against `b + a`), whose two queries
  export, cvc5 proves, and Carcara accepts with no hole.

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grants, so `src/` is unchanged.

## The tools, none of them committed

- cvc5 1.4.1: `tools/cvc5/fetch.ps1` (hash-pinned, ADR 0050) puts it in `.cvc5/`.
- Carcara: no Windows release, and its newest release (1.1.0, 2023) predates cvc5 1.4.1's Alethe
  output. Build commit `20bf49c5ce31089ac83118574c888669355aaae1` of github.com/ufmg-smite/carcara,
  the commit cvc5's CI pins (`contrib/get-carcara-checker`), under `.corpus/carcara/`. On Windows
  with the GNU Rust toolchain, its `rug` dependency builds GMP from source unless told otherwise; the
  spike's build linked the MSYS2 UCRT64 GMP (`mingw-w64-ucrt-x86_64-gmp` 6.3.0) instead, by adding
  `gmp-mpfr-sys = { version = "1.7.1", default-features = false, features = ["use-system-libs"] }`
  to the local copy's `Cargo.toml`, giving the build a `pkg-config` that prints
  `-LC:/msys64/ucrt64/lib`, and setting `RUSTUP_TOOLCHAIN` to an installed toolchain newer than the
  one `rust-toolchain.toml` pins. `libgmp-10.dll` goes beside `carcara.exe`.
- The RARE rule file Carcara checks `rare_rewrite` steps against: `proofs/alethe/rewrites.eo` of
  github.com/cvc5/cvc5 at `f0bc3373ef032a3fc5006f22778aaae7b274f429`, the file cvc5's CI hands
  Carcara.

## Reproduce

Windows, with `corpus.ps1 -Env`'s variables loaded from the checkout whose `.corpus/` holds the
restored `gitextensions-9860`:

```powershell
dotnet build tools/spikes/proof-certificates -c Release
$spike = 'tools/spikes/proof-certificates/bin/Release/net10.0/Equiv.Tests.Integration.dll'
$tools = '--cvc5', '.cvc5/cvc5-Win64-x86_64-static/bin/cvc5.exe', '--carcara', '.corpus/carcara/bin/carcara.exe', '--rare', '.corpus/carcara/rewrites.eo'
dotnet $spike @tools --self-test .corpus/p1-026/self-test
dotnet $spike @tools <run>/equiv.sarif <legacySolution> <modernSolution> .corpus/p1-026/run --threads 6
```

`--cvc5-ms` (default 60,000, Z3's `timeoutMs`) is cvc5's `--tlimit`; `--check-ms` (default
300,000) is Carcara's wall clock; `--only 3,17` keeps the results at those positions.

`<outDir>` gets one `.smt2` file, `.alethe` proof and Carcara output per query, and `results.tsv`.
They hold names and constants from the analysed code, so point it at `.corpus/`, never at `docs/`.
