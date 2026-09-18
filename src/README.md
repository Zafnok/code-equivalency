# src/

One project per component. Names and allowed references are fixed by
[docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md) and enforced by architecture tests.

| Project | Purpose | May reference |
|---|---|---|
| `Equiv.Core` | IR, verdict model, matching contracts, SARIF emission, baseline logic | nothing in `src/` |
| `Equiv.Frontend.CSharp` | Roslyn: load solutions, match symbols, lower IOperation CFG → IR | `Equiv.Core` |
| `Equiv.Verify.Z3` | Encode IR pairs to SMT, run Z3, decode counterexamples | `Equiv.Core` |
| `Equiv.Cli` | Headless entry point, routing, exit codes, config | all of the above |

Nothing else goes here in the MVP. Future: `Equiv.Frontend.Java`, `Equiv.Verify.Boogie`,
`Equiv.Server` (hosted tier).
