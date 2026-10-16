# P2-217 Precision: a path row fires behind a `File.Exists` guard on a field or property read
Status: in-progress
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-074 (done) stops a path-validation row from firing on a path a validating guard checked: `File.Exists(p)`
returns false for any path .NET Framework rejects, so a call under `if (File.Exists(p))` never sees one. It reads the
guard on a parameter or a local nothing reassigns. P2-212's adjudication (`docs/runs/2026-10-15-precision-verdict.md`)
found 3 false EQ006 in its fresh sample of `duplicati-3124` where the guarded value is a member read twice, at the
guard and at the call, with nothing between:
- `Duplicati.UnitTest.BorderTests::TearDown()`: `if (File.Exists(this.f)) File.Delete(this.f);` on a `readonly` field;
- `Duplicati.Library.Utility.TempFile::Dispose(bool)`: `File.Exists(m_path) && ...` then `File.Delete(m_path)` on a
  private field;
- `Duplicati.Server.SingleInstance::m_filewatcher_Created(...)`: an early return unless `File.Exists(e.FullPath)`, a
  property of a parameter, then `File.Delete(e.FullPath)`.

And 1 in `gitextensions-8522`'s sample whose legacy throw a catch-all swallows where the modern side reaches only
`File.Exists`, which returns false for the same path: `GitCommands.PathUtil::TryFindShellPath(string,out string)`,
`try { var p = Path.Combine(root, "x", arg); if (File.Exists(p)) return true; } catch { } return false;`, so both
sides return false for an invalid `arg`. The solver leaves `File.Exists` unconstrained.

When done, a guard on a member read that nothing between the guard and the call can write is followed, and the
largest remaining shape is decided.

## Spec references
`docs/tickets/done/P2-074-*.md` (the guard rule and its soundness), `docs/tickets/done/P2-201-*.md` (which member
reads are stable), ADR 0040.

## Acceptance criteria (all must hold; nothing beyond them)
1. A `File.Exists` (or other listed guard) on a field of `this`, a `readonly` field, or a property of a parameter
   whose getter is an auto-property, guards a later read of the same member when no write to it, no call and no
   `await` lies between: Notes give the soundness argument, and why a call between breaks it.
2. `equiv-decide` decides whether the swallowed-throw shape (`TryFindShellPath`) is taken here: `File.Exists`
   returning false for every path the row's precondition names is a fact of both runtimes.
3. Sample pairs, and `duplicati-3124` and `gitextensions-8522` rerun in compare mode quick; Notes give the four
   procedures' new results.

## Tests
The path-row tests; a sample where a call between guard and use writes the field and the row still fires.

## Out of scope
A mutable property with a body. Paths from callers (P2-207).

## Notes
- Filed 2026-10-15 by P2-212 (criterion 6).
- Decision: which reads are followed -> one rule for any field (mutable or `readonly`, instance or static) and any
  auto-property whose getter is the one that runs (a getter with no body, P2-200's dispatch, a setter allowed), of a
  class's `this`, of a parameter passed by value that nothing in the method assigns, or of no receiver. The ticket's
  three kinds (a field of `this`, a `readonly` field, an auto-property of a parameter) are cases of it, and the argument
  below does not depend on which. Alternatives: the three kinds alone; also locals as receivers. Rule: 4.
- Decision: "nothing between" -> an allowlist (`ValidPaths.QuietKinds`, plus operators and conversions with no operator
  method, an instance field's read, and an assignment to a local or parameter that is not a `ref`, another field, an
  array element or a discard). Alternatives: a denylist of calls, awaits and writes. Rule: 2; an operation kind nobody
  listed then keeps the row instead of passing unread.
- Decision: the guard shapes are P2-074's (the branch of an `if` or conditional expression, the right operand of `&&`
  or `||`, the rest of a block after an `if` that returns or throws), now each also giving what runs between the guard
  and the use: the guard condition's later operands, and at each level from the use up to the guard the operations
  before it. A loop between counts whole. Where several early exits guard a use, the last one is read. Alternatives: a
  control-flow graph's paths. Rule: 4; the bound tree is what P2-074 reads.
- Decision: a lambda, a local function and an iterator body are tested through the fingerprint
  (`BodyFingerprinterTests`), which serialises them with the call sites marked; the lowering does not lower them into
  the method's own calls, so `ValidPathsTests` cannot reach those three cases.
- Criterion 1, soundness. A read of a field returns its storage's current value; an auto-property's getter, when it is
  the one that runs, returns its backing field, which nothing but the property's own accessors and its type's
  constructors (through the property's name) can name. Both reads name the same member of the same object: a class's
  `this` (or `base`), a parameter passed by value that nothing assigns, or none. A struct's `this` and a `ref` or `in`
  parameter are not read: each can alias storage (a field `holder.s`, an argument `ref this.g`) that a plain assignment
  to another field replaces between the reads, which the rule below would let through. So the second read returns what
  the first did unless the storage is written between. What can write it, and how the rule refuses each:
  - an assignment naming the member, through any receiver, `c.f = x` with `c` being `this` among them: any assignment to
    the same field is refused whatever its receiver, and an assignment to a property is a setter call;
  - a write through a `ref` alias, a `ref` local or a `ref` parameter taken before the guard: an assignment to a `ref`
    local or parameter is refused, and a `ref` or `out` argument is a call;
  - code that runs between: a method, constructor, property or indexer getter or setter, event accessor, user-defined
    operator or conversion, delegate invocation, string interpolation's formatting, `using`, `lock` or `foreach`'s
    calls, and a static constructor, which a static field's read can trigger (so a static field's read between is
    refused; the guarded member's own read already ran its type's). None is on the allowlist;
  - `await` and `yield return`, after which any code may run before the method resumes: not on the allowlist;
  - control flow back into the region: a loop between counts whole, since its earlier turns run before the use; a
    method with any `goto` is not read; a use in a lambda or local function, which runs whenever it is called, is not
    read. A `throw` or `return` between leaves the use unreached; a `catch` or `finally` between is part of the region.
  Another thread or a finalizer writing the field between the two reads is a data race, outside the sequential model in
  which the encoder reads every heap location, as it is for the reads P2-201 follows. The two reads then return one
  value, and P2-074's argument applies to it: the guard returned true, so .NET Framework's validation accepts it.
- Criterion 1, why a call between breaks it: a call runs code that can reach the object and write the member, as
  `Retarget` writes `path` in `samples/runtime-row-guarded-member`'s `Move`, so the use can read a path no guard saw.
  For a `readonly` field read outside its type's constructors, or a get-only auto-property, no call could write it, but
  the criterion asks for one rule; that and a framework property no call can write are P2-222, filed by this ticket for
  `SingleInstance` (criterion 3).
- Criterion 2, decision: the swallowed-throw shape is not taken here; it is P2-223, filed by this ticket. Rule: 4, and
  the shape does not decide the procedure it was filed for. In `TryFindShellPath` the modern side does not stop at
  `File.Exists`: after two `Path.Combine`/`File.Exists` pairs (the second through the solution's own `Combine`) it calls
  `TryFindFullPath(shell, out shellPath)`, a solution method the legacy side never reaches, whose result is the return
  value. Following it needs that callee shown to return false for such a `shell` as well as `File.Exists`'s result
  constrained in the encoding (`Equiv.Verify`), not in this ticket's frontend data flow. The premise also holds only in
  part: `File.Exists` returns false on .NET 10 for a path with a control character or `" < > |` (and on both runtimes
  for a wildcard or a stray colon, P2-074's Notes), but `invalidPath` also names a path of 248 characters or more, which
  .NET 10 reads as a long path, so `File.Exists` is true there for a file that exists. P2-223's criterion 1 asks for
  that per character class.
- Decision: the before runs are P2-212's on `gitextensions-8522` (`bfbf413e`) and P2-213's on `duplicati-3124`
  (`70ad5fe7`, the pair's committed config), not fresh runs at `main`: `main` since then adds only two Sonar refactors
  (#514, #516), and P2-212 adjudicated its four results on those runs. The after runs are this branch at `24ca5c39`, from
  P2-212's checkouts and restores (linked into this worktree's `.corpus`), compare mode quick, the box's processor count
  as `--jobs` (P2-212's ran with 4, so run times are not compared). Both exited 1 (a Divergent result), no project
  skipped. `4203ccb7` then narrowed the receivers (a struct's `this`, a `ref` or `in` parameter); every result that
  changed reads a class's `this`, so that commit changes none of them.
- Criterion 3, the four procedures, P2-212's run, then this branch:

  | Pair | Procedure | Before | P2-217 |
  |---|---|---|---|
  | Duplicati | `Duplicati.UnitTest.BorderTests::TearDown()` | EQ006 | Equivalent (`congruence`) |
  | Duplicati | `Duplicati.Library.Utility.TempFile::Dispose(bool)` | EQ006 | Equivalent (`congruence`) |
  | Duplicati | `Duplicati.Server.SingleInstance::m_filewatcher_Created(object,System.IO.FileSystemEventArgs)` | EQ006 | EQ006 |
  | Git Extensions | `GitCommands.PathUtil::TryFindShellPath(string,out string)` | EQ006 | EQ006 |

  `SingleInstance` reads `e.FullPath`, a framework property with no source getter, across calls and in a `while`
  condition: P2-222. `TryFindShellPath` is criterion 2's shape: P2-223.
- Criterion 3, every other result that changed, both correct by reading the source:
  `Duplicati.Library.Utility.TempFolder::Dispose()`, EQ006 to Equivalent (`congruence`), `TempFile`'s shape with
  `Directory.Exists` on the mutable field `m_folder`; and `GitCommands.Settings.FileSettingsCache::GetLastFileModificationUtc()`,
  EQ006 to Equivalent (`congruence`), `File.GetLastWriteTimeUtc(SettingsFilePath)` under `File.Exists(SettingsFilePath)`
  on a get-only auto-property of `this`. No result became Divergent or worse. Path-row EQ006 (an EQ006 whose
  `reviewGroup` names a row with the `invalidPath` precondition) and all EQ006, before and after: Duplicati 98 and 95,
  117 and 114; Git Extensions 56 and 55, 137 and 136. Run times: Duplicati 328 s, Git Extensions 422 s (another
  session's thorough run on `gitextensions-9860` shared the box).
- Tests: `ValidPathsTests` (`AGuardedReadOfAMemberThatNothingRewritesIsAValidPath`, among them `Move`'s shape: a call
  between that writes the field keeps the row; `AGuardedAutoPropertyOfAParameterIsAValidPath`;
  `AGuardedFieldOfAStructIsNotAValidPath`), `BodyFingerprinterTests.ACallOnAGuardedFieldIsNotRuntimeSensitive`, and the
  sample pair `samples/runtime-row-guarded-member` (`SamplesEndToEndTests.RuntimeRowGuardedMember_*`).
- Decision: no `Release:` footer. A precision fix that changes no surface is a patch (`equiv-release`).
