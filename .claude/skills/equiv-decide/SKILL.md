---
name: equiv-decide
description: Make an implementation-detail decision yourself instead of asking the user. Use whenever you are about to ask "should I use X or Y" while implementing a ticket, or when a ticket or spec leaves a representation, naming, encoding or API-shape choice open.
---

# Decide, log, continue

The user is not the tie-breaker for implementation details. Asking halts the ticket
and costs more than a wrong choice that the gates and the next review will catch.

## Is it an implementation detail?

It is, and you decide it yourself, if ALL of these hold:

- It does not add or change a NuGet package (ADR 0002).
- It does not cross a component boundary in ARCHITECTURE.md (what Core exposes to
  Frontend/Verify/Cli, what the CLI accepts, exit codes).
- It does not change a verdict's meaning, a rule id, or the SARIF shape
  (VERIFICATION-MODEL.md sections 1, 5, 6).
- It does not weaken a gate (QUALITY-GATES.md).
- It does not contradict an accepted ADR or an explicit spec table row. Filling a gap
  in a spec row is fine; reversing one is not.

Examples that are details: enum vs flag, record shape, which of two equivalent Z3 API
calls, file and class names, dump-format spelling, generator strategy, test fixture
layout, where a helper lives inside a project, exception type for an internal error.

If any bullet fails, it is not a detail: write an ADR proposal with the `equiv-adr`
skill and stop.

## How to choose (first rule that discriminates wins)

1. Mirror the consumer. Pick the shape that maps one-to-one onto the API or spec that
   will consume it (the Z3 call, the Roslyn type, the SARIF property). Fewer
   translations, fewer bugs.
2. Prefer closed types over open ones: enum over bool flag, record over tuple, sealed
   hierarchy over string tags. Exhaustive `switch` beats `if`.
3. Prefer the option the spec's tests can pin: something a snapshot or property test
   can assert directly.
4. Prefer the smaller change. Do not add a parameter "for later".
5. If still tied, pick the first option you wrote down and move on.

## How to log it

Append to the ticket's `## Notes` in this exact form, one line each, before writing
the code:

```
- Decision: <what> -> <choice>. Alternatives: <a>, <b>. Rule: <1-5 from above>.
```

If the decision fills a gap in VERIFICATION-MODEL.md or a ticket Design section, also
patch that sentence in the same PR so the next ticket does not re-decide it. Do not
open a separate PR for the doc line.

## What the reviewer does with it

Decisions are reviewed in the PR like code. A reversed decision is a small follow-up
commit, not a redo of the ticket. That is the whole point: keep moving.
