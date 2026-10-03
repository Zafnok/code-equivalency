# ADR 0044: Evaluating a hole of an interpolated string leaves the current culture's integer formatting as it found it

Status: accepted (2026-10-02)

## Context
P2-086 lowers an interpolated string as the concatenation of its parts, the same IR for the
`string.Format` binding (.NET Framework) and the `DefaultInterpolatedStringHandler` binding (.NET 6 and
later). It left one shape opaque: an integer hole followed by a hole that runs code, such as
`$"{a.Count()} of {b.Count()}"`. `string.Format` formats the first integer after `b.Count()` has
returned, the handler before `b.Count()` is called. Both format it with the current culture, so the two
texts differ only if `b.Count()` changes how the current culture formats an integer. Nothing in the
model said it does not, so the shape stayed `IrOpaque`. After P2-086 it is the whole of
`InterpolatedString` on `eshop-manual`: 2 of 26 changed pairs (7.7%), over ADR 0028's 5% line, and
both pairs have this shape (P2-086's split). Ticket P2-102.

## Decision
The model assumes that evaluating a hole of an interpolated string does not change what the current
culture formats an integer as: the hole does not set the thread's current culture, and it does not
write the number format of a current culture that is writable. Under that assumption the two bindings
compute the same string whatever the later holes do, so P2-086's lowering covers an interpolated
string of `string` and 8- to 64-bit integer holes with no format or alignment clause, with no
condition on the holes after the first integer hole. The lowering stays as it is: each part in order,
an integer hole's closed `ToString()` call directly after the hole's value, which is the handler's
order. The assumption is named in VERIFICATION-MODEL section 1. It is leaned on only by a pair whose
two sides bind the same text differently; two sides with one binding format in one order.

## Why
- The two orders differ in one read. Formatting an integer with no format string reads the current
  culture's number format and nothing else, and writes nothing. Its only culture-dependent output is
  the negative sign: the digits are ASCII under every culture. So moving the formatting across a later
  hole changes the text only if that hole changes the negative sign the thread formats with, and only
  when the integer is negative.
- A later hole that throws leaves no trace of the difference either. Under the handler the earlier
  integers are already formatted and under `string.Format` they are not, but the formatted text is
  discarded and formatting has no other effect.
- The code that breaks the assumption is an expression inside a string that switches the thread's
  culture, or edits a writable culture's `NumberFormat`, part of the way through building that string.
  Neither measured pair that holds the shape does it (`eshop-manual`'s 2 pairs call `Count()`), and a
  program that did would already print a string half in one culture and half in another.
- It is the same kind of assumption ADR 0041 already makes about the current culture (no user code
  runs from its getters), and narrower: it speaks only about the holes of one string.
- Without it the shape has no route to a verdict. The IL fallback is off by default (P1-018), and the
  reason stays over the 5% line on the one pair where the bindings really differ.

## Rejected
- **Keep the shape opaque (the ticket's criterion 3).** Sound with no assumption, but unchanged source
  text stays Unknown on exactly the pairs that cross the binding, which is the migration this tool is
  for.
- **Assume a call never changes the current culture, anywhere.** Wider than anything measured needs.
  It would also let a closed call's result drop its position (ADR 0041), which this ADR does not touch.
- **Cover the shape only when the integer is provably not negative.** The model has no range for
  `Count()`'s result, so it covers neither measured pair.
- **Lower each binding in its own order.** The same text then has two trace orders and is Divergent
  (P2-086's decision, unchanged).
- **Thread the culture as state through every open call.** A closed call would then read it, the two
  orders would be two different bodies again, and nothing is proved that the opaque did not already say.
- **A row in the runtime-changes table.** It would make every such string runtime-sensitive and so
  Unknown on a pair that crosses the binding, which is where it is today.
- **Name the assumption on each result that leans on it.** No section 1 assumption other than
  `assumedCallees` is marked per result, and it would add a SARIF property for a case with no measured
  instance.

## Consequences
- VERIFICATION-MODEL section 1 gains the assumption in this PR. Section 3's interpolated string rule,
  the `IOPERATION-COVERAGE.md` row, and `InterpolatedStrings.IsConcatenation` (which drops its
  condition on the holes after the first integer hole) change in P2-102's implementation PR, with a
  snapshot test of both bindings.
- A false Equivalent is now possible for a pair that crosses the binding and whose string holds a
  possibly negative integer hole before a hole that changes the culture's negative sign. Ticket
  P2-102's Notes record the `eshop-manual` figure after the change.
- A pair that rewrites `$"{a.Count()} of {b.Count()}"` into two locals and a `+` chain still differs
  from it in trace order (both `Count()` calls before the first `ToString()`), and is a false alarm,
  not a false proof. No measured pair does this.
- Other hole types and format or alignment clauses stay opaque: they are formatted through members
  the two bindings do not share, which this assumption does not reach.
