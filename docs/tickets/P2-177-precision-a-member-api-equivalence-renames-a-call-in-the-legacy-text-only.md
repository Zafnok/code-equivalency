# P2-177 Precision: a member API equivalence renames a call in the legacy side's fingerprint text only, so an unedited body that calls `TrimEnd(char[])` is never congruent
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-173

## Goal
P2-173's property (`properties.notCongruent`) names one cause for 10 of the 11 bodies in
byte-identical files that P2-110 found out of congruence on the three cleanup pairs: component
`callee`, identity `System.String::TrimEnd(char)` (9) or `System.String::TrimStart()` (1). Neither
side of any of them calls that member. Each calls `String.TrimEnd(params char[])` or
`String.TrimStart(params char[])` with two or more characters or with an array it holds, in the
same file on both sides.

The cause is in the fingerprint text (ADR 0024, "every referenced symbol ... using the same ...
applied `api-equivalences.json` rewrites as matching"). `BoundSerialiser` keeps one modern name per
legacy member for the entries whose adapter passes every argument through, and writes every call of
that legacy member under the modern name, on the legacy side only (the modern side has no
equivalences). The entries `bcl.string-trim-end-one-char` (`TrimEnd(char[])` to `TrimEnd(char)`)
and `bcl.string-trim-start-no-chars` (`TrimStart(char[])` to `TrimStart()`) are such entries, and
the first entry per legacy member wins. So:
- the rename is applied whatever the call's argument list is: the catalogue's entry is for a
  `params` call with exactly one element (or none), and the text renames a call with two, and a
  call that is handed an array;
- on a pair where both sides bind the same member, the legacy text says `TrimEnd(char)` and the
  modern text says `TrimEnd(char[])`, and the pair is never congruent. Every body that calls one of
  these members with a character list pays, on every pair, whatever the pull request edits.

On the three cleanup pairs at equiv `269c7d60` in compare mode quick that is 12 results (8 on
`powershell-19687`, 2 on each Git Extensions pair). 5 of the 10 that P2-110 listed are proved by
the solver anyway and 5 are Unknown (3 `opaque`, 2 `timeout`): Unknown results a reviewer is handed
for code nobody touched.

No soundness doubt was found: the rename alone cannot make two texts equal, because the argument
under the call is an array creation on one side and a character on the other. The cost is
congruence only.

When done, a body whose two sides bind the same member is congruent whether or not that member has
an API-equivalence entry, and a member entry is applied in the text only to a call it describes.

## Spec references
ADR 0020 (API equivalences) and ADR 0024 (what a fingerprint holds), with their clarifications;
`src/Equiv.Core/ApiEquivalences/api-equivalences.json`; `BoundSerialiser` (`members`,
`PassesArgumentsThrough`, `Method`); how the lowering decides that an entry applies to a call;
`docs/tickets/done/P2-173-*.md` (the table in Notes); `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide, with `equiv-decide` and the `equiv-adr` bar test, when the text applies a member entry:
   only to a call the lowering would rewrite by it (its argument shape included), or never where
   the rewrite cannot make the two texts equal. Log the decision and why no pair becomes congruent
   that the solver path would not also treat as the entry's call.
2. A body that calls `TrimEnd` with two characters, or with a `char[]` field, has the same
   fingerprint as the legacy side and as the modern side of a pair, and so has one that calls
   `TrimStart` so.
3. Every member entry of the catalogue is checked for the same shape, and Notes list which entries
   the text applies and to which calls.
4. Rerun the three cleanup pairs in compare mode quick. Notes record, for the 10 procedures in
   P2-173's table whose component is `callee`, the new result, and the count of results that name
   `System.String::TrimEnd(char)` or `System.String::TrimStart()` next to 12. No result that was
   Equivalent or Divergent changes its rule.

## Tests
- `BodyFingerprinterTests`: criterion 2, and a legacy call the entry does describe (one character)
  keeps the text it has.
- A soundness test: two bodies that differ in behaviour and call these members do not share a
  fingerprint under the new rule.

## Size guard
A change to how the lowering applies an entry: stop, that is another ticket.

## Out of scope
New catalogue entries. The generated commit constant (`ThisAssembly.Git::Sha`), which ADR 0046
keeps an ordinary constant.

## Notes
- Filed 2026-10-13 from P2-173's rerun of the cleanup pairs. No ticket owned it.
