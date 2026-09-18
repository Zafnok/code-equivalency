# ADR 0003: Lower from Roslyn's IOperation control-flow graph

Status: accepted (2026-09-17)

## Decision
The C# frontend lowers from `ControlFlowGraph.Create(IOperation)`
(`Microsoft.CodeAnalysis.FlowAnalysis`), not from syntax trees and not from IL.

## Why
- IOperation is the bound, type-resolved tree, and the CFG builder already desugars
  `foreach`, `using`, `?.`, `??`, patterns, `try/finally` and interpolation into basic
  blocks. That is the "deconstruct syntactic sugar into decomposable ASTs" step, done by
  the compiler team and kept in sync with every language version. Gemini's suggestion
  was right here, for the right reason.
- IL rejected: legacy and modern compile the same source to different IL (different
  compilers, different BCL), and IL loses nullability and pattern structure.
- Syntax rejected: we would be re-implementing binding.

## Consequences
- One Roslyn (5.x) parses C# 1 through 14, so it handles both sides.
- Unsupported IOperation kinds lower to `IrOpaque`; coverage grows by ticket and is
  tracked in `docs/tickets/IOPERATION-COVERAGE.md`.
