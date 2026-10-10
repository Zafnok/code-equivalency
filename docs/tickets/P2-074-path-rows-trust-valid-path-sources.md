# P2-074 A path-validation row does not fire on a path from a source that yields only valid paths
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-073

## Goal
P2-047's audit found 7 false EQ006 results where a path row (`Path.Combine`,
`Path.GetDirectoryName` or `StreamReader` argument validation) fires on a path that cannot hold an
invalid character on the real runtime, for one of two reasons. The value came from a BCL member
that returns a real file-system path (`Assembly.Location`, `AppContext.BaseDirectory`,
`Application.UserAppDataPath`, a test framework's test directory). Or a guard ran first that
returns early for invalid paths (`File.Exists(p)` before `new StreamReader(p)`). Minimal repro, as a
sample pair (identical file, legacy net48):

```csharp
static string? BaseDir() => System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location);
static string? Load(string p) => System.IO.File.Exists(p) ? new System.IO.StreamReader(p).ReadToEnd() : null;
```

Today both methods are EQ006. They should not be. Extend P2-073's precondition so that a path row
does not fire for an argument that is, by data flow in the method, the result of a listed
valid-path source, or that is dominated by a listed validating guard on the same value.

## Spec references
VERIFICATION-MODEL section 3 (runtime-changed APIs), P2-073, P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. The table lists the valid-path sources and the validating guards, with the path rows referring to them.
2. `samples/runtime-row-valid-path-source` (the pair above) has no EQ006. A variant that passes a
   parameter straight to `Path.Combine` stays EQ006.

## Tests
- Integration test on `samples/runtime-row-valid-path-source`.
- Unit tests for the source and guard checks.

## Out of scope
Interprocedural data flow (a path passed in from a caller).

## Notes
- Found by P2-047: 5 on Git Extensions and 2 on the Tomas pair.
- Decision: the lists are a `validPaths` object at the top of `runtime-changes.json` (`sources`, `transforms`,
  `guards`, each a member prefix). The path rows refer to it through their `invalidPath` precondition, whose
  definition in the header now says a valid path does not meet it. A per-row list was the alternative; the 31 rows
  would have repeated one list.
- Decision: `transforms` is a third list the ticket does not name. `Path.GetDirectoryName(Assembly.Location)` is a
  source passed to a path row, but the usual shape nests them (`Path.Combine(Path.GetDirectoryName(location), "x")`),
  and the outer call's argument is then the result of a path member, not of a source. A transform yields a valid
  path only when its own call cannot reach `invalidPath`, so a variable or an `a|b` literal among its arguments
  keeps the row.
- Decision: the frontend marks an operand (`CallArgument.ValidPath`) and Core decides (`RuntimeChangeReach`), as
  P2-073 split it. The analysis (`ValidPaths`) reads the method where the compilation binds the call's syntax, since
  an operation of a control-flow graph has no parent to walk; it runs only for a call a row still flags after
  P2-073's constants, so most calls never pay for it.
- Decision: data flow is by assignment, not by program point. A local is a valid path when its initializer and
  every assignment to it in the method is one; a compound assignment, a `ref` or `out` argument, a tuple it is part
  of, or no initializer makes it unknown. A guard counts only for a parameter or local that nothing assigns after
  its declaration, so the guard and the use read the same value without tracking order. The guard shapes are: the
  branch of an `if` or conditional expression, the right operand of `&&` or `||`, and the rest of a block after an
  `if` without `else` that ends in `return` or `throw`. A loop condition, a `switch` section and `continue` or
  `break` as the early exit are not read.
- The `System.IO.StreamReader::.ctor(string)` row had no `invalidPath` precondition, though its reason is the same
  up-front validation. It has one now (31 path rows), without which the ticket's `Load` repro could not be decided
  by its argument. Its measured witness meets the precondition.
- Known gaps, all on the side of not flagging. An empty string has no invalid character, but
  `Path.GetDirectoryName("")` and `Path.GetFullPath("")` throw on .NET Framework and not on .NET; `Assembly.Location`
  is empty for an assembly loaded from bytes and `Environment.GetFolderPath` for a folder that does not exist, and
  the ticket lists the first as a source. A `ref` parameter that another alias writes between the guard and the use
  is not seen, nor is a `goto` that jumps past an early exit. `File.Exists` is true for a path with a wildcard or a
  stray colon on neither runtime, so those are covered.
- Not removed by this ticket: a path held in a field or returned by one of the solution's own methods (a `Lazy`
  application-data path, for one) is interprocedural, which is out of scope. No corpus run was made here, so how
  many of the audit's 7 results this clears is not measured.
