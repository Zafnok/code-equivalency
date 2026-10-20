# P2-252 Execution reach: a driver awaits a `Task` result, so an `async` member's outcome is observed
Status: in-progress
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
