# P2-112 An interrupt that throws on the timer thread can no longer end the process
Status: done (PR #370)
Effort: S
Model: Sonnet, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
`Z3Backend.Interruptible` (P2-076 criterion 3, commit 8e0ed3c) ends a query no limit ended with a
timer whose callback is `_ => context.Interrupt()`. `Context.Interrupt()` can throw
`Microsoft.Z3.Z3Exception: canceled`. The callback runs on a thread-pool timer thread, where nothing
catches it, so the exception is unhandled and the runtime terminates the whole process.

Seen on 2026-10-03 in CI on PR #368 (job `sonar`, run 37102677853, attempt 1, ubuntu). The
`Equiv.Verify.Z3.Tests` host died with:

```
Unhandled exception. Microsoft.Z3.Z3Exception: canceled
   at Microsoft.Z3.Native.Z3_interrupt(IntPtr a0)
   at Microsoft.Z3.Context.Interrupt()
   at Equiv.Verify.Z3.Z3Backend.<>c__DisplayClass19_0`1.<Interruptible>b__0(Object _) in src/Equiv.Verify.Z3/Z3Backend.cs:line 185
   at System.Threading.TimerQueueTimer.Fire(Boolean isThreadPool)
```

Every test reported `failed: 0`; the run failed only because the host crashed. PR #368 does not touch
`Equiv.Verify.Z3`, so this is an intermittent failure of `main`. The same crash in `equiv compare`
ends a corpus run part-way through with no SARIF, and no per-pair `try` can stop it (ADR 0023): the
exception is not on the pair's thread.

Make the timer callback unable to let an exception escape. The query still ends with the result a
timeout has, as `Interruptible`'s doc comment says.

## Spec references
`src/Equiv.Verify.Z3/Z3Backend.cs` (`Interruptible`, `Check`), `src/Equiv.Verify.Z3/ChcEncoder.cs`
(the fixedpoint query, the other caller), P2-076 criterion 3, ADR 0023 (a crashing pair does not end
the run).

## Acceptance criteria (all must hold; nothing beyond them)
1. An interrupt that throws `Z3Exception` on the timer thread does not end the process, and
   `Interruptible` returns what the query returned. A test proves it; without the fix that test
   crashes the test host.
2. What a throwing interrupt means for the query is decided and logged in Notes, and
   `Interruptible`'s doc comment says it.
3. An interrupt that does not throw behaves as before: `ACheckNoLimitEndsIsInterruptedAfterTheSlackAndAnswersUnknown`
   and `InterruptedQueryIsATimeoutAndTheLadderContinues` pass unchanged.

## Files
`src/Equiv.Verify.Z3/Z3Backend.cs`, `tests/Equiv.Verify.Z3.Tests/Z3BackendTests.cs`.

## Tests
- `Z3BackendTests.AnInterruptThatThrowsDoesNotEndTheProcessAndTheQueryReturns`

## Size guard
More than `Interruptible` and its test: stop.

## Out of scope
P2-082: that is a pair whose weighing throws on the run's own thread, before the per-pair `try`. This
is a different thread and a different fix. When the interrupt fires, and what an interrupted query
reports. Any other thread-pool callback.

## Notes
- Cause: `Native.Z3_interrupt` calls the native interrupt and then reads the context's error code, as every
  generated wrapper does. The native call does not reset that code, so the timer thread reads whatever error the
  query's thread last left on the context. A query that is giving up at that moment leaves `canceled`, and the
  wrapper throws it on the timer thread. The interrupt itself has already been made by then.
- The test reproduces it without a race: a tactic that does not exist leaves an error on the context, and
  `Context.Interrupt()` then throws every time. With the `catch` disabled the test host dies with the stack in the
  Goal (exit code -532462766, "Zero tests ran").
- Decision: a throwing interrupt changes nothing about the query. The native interrupt ran before the wrapper
  threw, and the exception only repeats an error the query's own thread reports through its result (unknown, or a
  fixedpoint giving up). So the exception is dropped and the query ends with the result it has. Chosen over
  recording the exception and turning the result into a timeout by hand: that would overrule a sat or unsat the
  query reached just before the timer fired.
- Decision: only `Z3Exception` is caught. It is the only exception the wrapper throws; anything else on that
  thread is a bug in this repo and should still be loud.
- Decision: the seam is an `Interruptible` overload taking the interrupt as an `Action`, so the test can see the
  interrupt throw and hold the query until it has. `Context.Interrupt` is not virtual.
