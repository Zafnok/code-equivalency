# P2-192 Precision bug: a pull request that replaces a callee one for one by another member that does the same is Divergent
Status: todo
Effort: L
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
A migration replaces APIs the new runtime lacks. P2-130's adjudication of `duplicati-3124`
(`docs/runs/2026-10-14-migrations-verdict.md`) found 11 false EQ002 where every call site of one
member is replaced, one for one, by a member that behaves the same, and no rule can say so:
- 7: `System.Runtime.Remoting.Messaging.CallContext::LogicalSetData(string,object)` and
  `::LogicalGetData(string)` (absent from net5.0) replaced by a solution-local
  `Duplicati.Logging.Duplicati.Library.Logging.CallContext` over `AsyncLocal<object>`: the same named
  slot with the same flow. It is not a forwarder (ADR 0047), so the two callees are unrelated and the
  shim's `threw` is free;
- 3: a one-line helper forwarding to the third-party `FasterHashing.FasterHash::Create(string)`
  replaced by `System.Security.Cryptography.HashAlgorithm::Create(string)`, with the constant name of
  SHA-256 on both sides;
- 1: a synchronous AWS SDK call replaced by its `Async` twin followed by `GetAwaiter().GetResult()`.

ADR 0020's catalogue maps one base class library member to another. None of these fits: the modern
member is the solution's own, or the legacy one is a third party's, or the modern side is two calls.

When done, a user (or the catalogue) can say that two such members are one call, and how that is kept
from hiding a real difference is decided.

## Spec references
ADR 0020, ADR 0047, ADR 0045, `api-equivalences.json`, `.claude/skills/equiv-adr`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, through `equiv-adr`, how such an equivalence is declared and checked: for example a
   pair-level mapping whose modern side is a solution member that is itself verified against a
   specification, or a catalogue entry for `sync = Async + GetAwaiter().GetResult()`.
2. Implement the largest case (the `CallContext` replacement), with a sample pair.
3. Rerun `duplicati-3124` in compare mode quick; Notes give the 11 procedures' new results.

## Tests
As the decision requires; a pair whose replacement behaves differently stays Divergent.

## Size guard
More than one new kind of equivalence: stop and split.

## Out of scope
Base class library one-call pairs (P2-137's catalogue).

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
