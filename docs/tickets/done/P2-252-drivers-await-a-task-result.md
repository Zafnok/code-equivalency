# P2-252 Execution reach: a driver awaits a `Task` result, so an `async` member's outcome is observed
Status: done (PR #556)
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-245

## Goal
A replay or `probe` driver compares a result by its static type, and a `Task` or `Task<T>` has no canonical form, so an
`async` member's outcome is `NotComparable` and the exception its body throws is never seen. P2-245's re-adjudication
(`docs/runs/2026-10-19-execution-reach.md`) left Duplicati's `AzureBlobWrapper::AddFileStream(...)` and
`SharePointBackend::uploadFileSlicePerSlice(...)` (P2-212 sample rows 14 and 47) undetermined for that reason alone. When
done, a driver waits for a returned `Task` (with the per-case timeout), and its outcome is the task's: its result's
canonical form for a `Task<T>`, `null` for a `Task`, or the first inner exception's type when it faults; a cancelled task
is `System.Threading.Tasks.TaskCanceledException`. `ValueTask` and `ValueTask<T>` likewise.

## Spec references
ADR 0035 decision 2, M3-032 (the canonical form), `src/Equiv.Frontend.CSharp/Execution/DriverSource.cs` (`Answer`,
`Canonical`), VERIFICATION-MODEL section 6.

## Acceptance criteria (all must hold; nothing beyond them)
1. A driver's outcome of an `async` member is its task's, as the Goal says, on both runtime kinds; tests cover a result,
   a fault, a cancellation and `ValueTask<T>`.
2. Duplicati's rows 14 and 47 are probed again and re-adjudicated.

## Notes
- Filed 2026-10-19 by P2-245.
- Decision: the wait is `Task.Wait` in the driver with no timeout of its own; the parent's per-case timeout (`RuntimeDiff.CaseTimeout`, 10 s) already kills a driver that gives no answer, so a task that never completes is a timed-out case, as a member that never returns is.
- Decision: a cancelled task is `TaskCanceledException` whatever cancelled it. `GetAwaiter().GetResult()` would rethrow the body's own `OperationCanceledException` (from `ThrowIfCancellationRequested`), so the driver checks `IsCanceled` itself, as the Goal says.
- Decision: a faulted task is answered as `Threw` with its first inner exception, so a fault and a synchronous throw of the same type are one outcome; the Windows Desktop check (P2-253) sees that inner exception.
- Decision: a `Task<T>` whose `T` no driver can name is not waited for and stays `NotComparable` of the task type: casting a `ValueTask<T>` of a hidden `T` to wait for it needs reflection the driver does not have, and its result has no canonical form anyway.
- Decision: the wait is shared code in `DriverSource`, so runtime-diff and differential-testing drivers wait too; their outcome of an `async` member was `NotComparable` before.
- Decision: the task helper (`A`) is emitted only into a driver whose member returns a task, so every other driver's source is unchanged.
- Decision: `ValueTask<T>` is covered on real drivers with both sides on .NET; the .NET Framework 4.8 reference assemblies have no `ValueTask` (it comes from a package there), and the generated `.AsTask()` call is the same either way.
- Decision: no `Release:` footer. Like P2-251, this widens what a driver observes without changing a CLI, SARIF shape, rule or verdict meaning.
- Criterion 2: `docs/runs/2026-10-20-async-replay.md`. Both rows are confirmed by `probe`: row 47 (`a|b.txt`: legacy `ArgumentException` from `Path.GetFileName`, modern `NullReferenceException`; a plain name: both `NullReferenceException`), row 14 (a non-null container on an `.invalid` address and a null stream: legacy `ArgumentNullException`, modern `NullReferenceException` from the new `source.Length` read). Migration precision over P2-212's sample 58.0% (47 of 81), from 57.0% after P2-250 and P2-253; ceiling unchanged at 71.4%.
- The probes reused P2-245's `probe.py` against the `.corpus/` of the P2-245 worktree (checkouts and restores P2-237 made); this worktree has no `.corpus/`, so the script took the pairs folder as a fourth argument.
