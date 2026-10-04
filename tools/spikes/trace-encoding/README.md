# P1-034 trace-encoding spike (throwaway)

Measures whether the rung 1 queries Z3 gives up on get easier when "the call traces are equal" is
written without sequences and datatypes. The result is in `docs/runs/2026-10-04-trace-encoding.md`.

## The two encodings

**Sequence (production).** `TraceEncoder` makes a side's trace a `Seq` of `event(callee, args)`:
the concatenation, in reverse postorder, of each block's events when the block is reached. `callee`
is an integer per canonical identity, `args` a `Seq` of the arguments and the heap read, each boxed
in a `Value` datatype with one constructor per type. `ProductEncoder` compares the two terms with
`=`.

**Positional (this spike).** Every call site already has a position: the encoding's `cnt` term, a
bv32 that counts the calls made before it on the path taken. The trace equality becomes

- the two traces are as long: each side's length is the count at the block its path ends in (a
  return, a throw, or an `unreachable`), an `ite` over those blocks' `reach`;
- for every old call site `i` and new call site `j`:
  `reach_i and reach_j and cnt_i = cnt_j` implies the two events are equal. Two sites of different
  canonical callees, or with arguments of different types or number, are never equal, so that pair's
  conjunct is `not (reach_i and reach_j and cnt_i = cnt_j)`. Two sites of the same callee and types
  are equal when each argument, and each heap map read, is equal.

A pair of sites whose positions cannot meet is left out: each site's position has a least and a
greatest value over all paths of its side's control-flow graph, and when the two ranges do not
overlap the premise is false on every input. When both ranges are one number the `cnt` equality is
dropped.

The exception type, which production compares as an integer, is compared as a bv32 with the same
ids. Nothing else changes: the assertions are `ProductEncoder.Encode`'s own, and so are the other
conjuncts of the query. The positional query holds bit-vectors, arrays and uninterpreted functions
(and the uninterpreted sorts the product already had), and no sequence, datatype or integer.

## Why the two say the same on an unrolled pair

An unrolled pair is acyclic, so on any input each side runs one path, and reverse postorder lists
that path's blocks in the order they run. So:

1. The sequence trace is the events of the reached blocks in the order they run. Its length is the
   number of calls made, which is the `cnt` at the last block plus that block's calls.
2. A reached call site's `cnt` is the number of events before its own in that sequence, that is,
   its index. The reached sites of one side therefore have the indices 0 to length - 1, each once.
3. Two sequences are equal exactly when they are as long and equal at every index. By 2, "at every
   index" is "for every reached old site and reached new site with the same `cnt`".
4. Two events are equal exactly when their callee integers are equal (the canonical identities are)
   and their `args` sequences are: as long, each element in the same `Value` constructor (the same
   type) and of equal content. That is the per-pair conjunct above.

The argument needs each call site to be made at most once, which is what unrolling gives. Rungs 2
to 5 add cut events of loop segments; the spike does not cover them.

## How it is built

`Positional.cs` calls `ProductEncoder.Encode`, then makes two more `FragmentEncoder`s over the
encoding's own `SortMapper`, `TraceEncoder` and `PureEncoder`. They name every term as the first
two did, so they give the same terms, and they hand out what the encoding does not: each side's
call sites and exits. From those it rebuilds the query. Given production's own two conjuncts the
rebuild must be the very term `Encode` returned, and `Build` throws if it is not, on every query.
Nothing under `src/` changes.

`Program.cs` takes P1-025's `results.tsv` for which query Z3 gave up on for each pair, asks Z3 that
query in both encodings in memory with the production solver and limits, writes both as files
(`NNN.seq.smt2`, `NNN.pos.smt2`), runs every `--solver` on the positional file and every `--control`
solver on the sequence file too, and reads each satisfiable answer back as P1-025 did. The files are
what Z3 prints with each `seq.++`, `or` and `and` of one argument written as that argument. The
`opaque` and `bound` queries do not compare the traces, so they are one query in both encodings and
Z3 is asked once. For a `divergence` query some solver proves unsatisfiable, rung 1's other queries
are asked of Z3 and, where Z3 gives up, of the other solvers.

The export, the solver runs and the read-back are P1-025's, compiled in from
`../solver-portfolio/`.

`SelfTest.cs` runs nine hand-written pairs whose only observable is the trace (equal traces three
ways, a different callee, a different argument, a different length, a call under a branch on one
side only, a reordered pair of calls, a conditional call against an unconditional one) and checks
that Z3 answers both encodings the same and as expected, that the positional file's logic has no
datatypes, sequences or integers, and that Z3 answers the file's text the same again.

It is not in `Equiv.slnx` and no CI gate builds it. It takes the assembly name
`Equiv.Tests.Integration` to use that project's `InternalsVisibleTo` grants.

## Reproduce

Windows, after `equiv-corpus-run`'s fetch, prepare and restore steps for `gitextensions-8522`, with
`corpus.ps1 -Env`'s variables loaded, and P1-025's solvers and `results.tsv` at hand.

```powershell
dotnet build tools/spikes/trace-encoding -c Release
$spike = 'tools/spikes/trace-encoding/bin/Release/net10.0/Equiv.Tests.Integration.dll'
dotnet $spike --self-test
dotnet $spike <legacySolution> <modernSolution> <P1-025 results.tsv> <outDir> --threads 6 `
  --solver "cvc5=<path>/cvc5.exe|--arrays-exp" --control cvc5 --solver bitwuzla=<path>/bitwuzla.exe
```

`--only 3,17` keeps the queries at those positions of the `results.tsv`. `<outDir>` gets the `.smt2`
files and a `results.tsv` of its own. Both hold names and constants from the analysed code, so point
it at `.corpus/`, never at `docs/`.
