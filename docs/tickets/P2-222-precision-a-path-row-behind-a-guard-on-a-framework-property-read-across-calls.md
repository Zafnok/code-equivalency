# P2-222 Precision: a path row fires behind a `File.Exists` guard on a framework property read again after calls
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-217

## Goal
P2-217 follows a `File.Exists` guard on a field or auto-property read when nothing between the guard and the call runs
code. P2-212's false EQ006 `Duplicati.Server.SingleInstance::m_filewatcher_Created(object,System.IO.FileSystemEventArgs)`
is still EQ006 after it (P2-217's Notes), for three reasons together:
- the guarded value is `e.FullPath`, a property of the framework's `FileSystemEventArgs`, whose getter has no source
  body to read; on .NET Framework and .NET it returns a field only the constructor writes;
- calls lie between the early return on `!File.Exists(e.FullPath)` and the reads (`new StreamReader(...)`,
  `new FileStream(e.FullPath, ...)`, `ReadLine`, `ToArray`), and P2-217 stops at any call;
- the second `File.Delete(e.FullPath)` sits in a `while (retrycount > 0 && File.Exists(e.FullPath))` body, and a loop
  condition is not a guard P2-074 reads.

When done, a member no call can change is guarded across calls, and the loop shape is decided.

## Spec references
`docs/tickets/done/P2-217-*.md` (the member guard and its soundness), `P2-074-*.md`, `P2-201-*.md` (which members only
a constructor writes), the `validPaths` header of `runtime-changes.json`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A guard on a read of a member no call can write between the guard and the use (a `readonly` field read outside its
   type's constructors, a get-only auto-property, or a framework property the table lists as returning what its
   constructor stored, `FileSystemEventArgs.FullPath` among them) guards a later read on the same receiver across calls;
   Notes give the soundness argument, including what a listed framework property's two runtimes' sources show.
2. `equiv-decide` decides whether a `while` condition's guard covers its body (the body must not write the member before
   the read, and a loop's later turns re-check the condition).
3. Rerun `duplicati-3124` in compare mode quick; Notes give the procedure's new result.

## Tests
Unit tests: a listed framework property guarded across a call stays unflagged; an unlisted framework property, and a
mutable field across a call, stay flagged.

## Out of scope
A mutable property with a body. Paths from callers (P2-207).

## Notes
- Filed 2026-10-15 by P2-217 (criterion 3).
