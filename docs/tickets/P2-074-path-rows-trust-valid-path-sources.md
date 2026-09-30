# P2-074 A path-validation row does not fire on a path from a source that yields only valid paths
Status: todo
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
