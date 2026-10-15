# P2-203 Precision: two sides that call a closed string member with another argument, or another overload of it, are Unknown and not Divergent
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-184

## Goal
ADR 0056 taints a closed call in the replay, so a divergence both sides reach only through a closed
call's answer is Unknown(Abstraction). Until P2-184 a call an API-equivalence entry rewrote was open,
so a `String.TrimEnd` rewritten on the legacy side was not tainted, and two samples kept a Divergent
that rested on it. P2-184 closes the rewritten call, as a direct call of its modern member is, and
both are now Unknown(Abstraction), equivalent when `s == null`:
- `samples/bcl-overload-rebinding`'s `Paths.StripOther(string)`: `TrimEnd('/')` against `TrimEnd('.')`;
- `samples/bcl-rebound-overloads`' `Calls.TrimTailOther(string)`: `TrimEnd()` against `TrimEnd('.')`.

Both differ on real inputs (`"a."` gives `"a."` and `"a"`). The replay does not know that: a string
in a model is an element with no text, and ADR 0056 rejected running the real member. When done, a
pair whose two sides call `String.TrimEnd`, `TrimStart` or `Trim` (no argument or one `char`) with
other arguments or another overload, and differ in nothing else, is Divergent with a witness that
holds for the real members, or the ticket's Notes say why no sound rule reaches it.

## Spec references
ADR 0056 (Consequences: "a divergence both sides reach only through a closed call's answer"), ADR
0026, ADR 0041, ADR 0025 (shared functions), `docs/tickets/P2-159-*.md` (the throw list, which does not
cover a member's result).

## Acceptance criteria (all must hold; nothing beyond them)
1. Route the design through `.claude/skills/equiv-adr` first: a rule that gives a closed member
   meaning in the replay or the encoding amends ADR 0056.
2. `StripOther` and `TrimTailOther` are EQ002, and each sample's README and `expected.sarif.json` say so.
3. The differential gate's rules 1 to 3 hold on the PR budget under both lowerings.

## Tests
`SamplesEndToEndTests` for the two procedures, and a unit test per member the rule covers.

## Size guard
A string theory for every `string` value, or a rule for members other than the trims: stop and split.

## Out of scope
P2-210's null source. Open calls.

## Notes
- Filed 2026-10-15 by P2-184 (its sample results).
