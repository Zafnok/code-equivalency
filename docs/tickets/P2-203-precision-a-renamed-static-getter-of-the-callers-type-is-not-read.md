# P2-203 Precision bug: a static property getter of the caller's own type that exists on one side only is not read
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-192's rerun of `duplicati-3124` (its Notes) declared `HashAlgorithmHelper::Create(string)` the same call as
`HashAlgorithm::Create(string)`, and `Duplicati.Library.Utility.Utility::CalculateHash(System.IO.Stream)` stayed
EQ002 for a second reason. The legacy side passes `Utility.HashAlgorithm`, a `public static string` property whose
getter is `=> "SHA256"`. The modern side passes `Utility.BlockHashAlgorithm`, a `private static` property with the
same getter. Each getter exists on one side only, so the two calls are two unrelated functions
(`notCongruent` names the callee), and the counterexample differs only in them.

ADR 0045 replaces a call to a one-sided helper by its body, but only for a `private` ordinary method: an accessor is
excluded, and so is any other accessibility, because such a member can be virtual or start another type's
initializer. Neither reason holds for a static, non-virtual getter of the caller's own type.

When done, a call from a method of type T to a static, non-virtual property getter of T that exists on one side only
is replaced by its body, as ADR 0045 replaces a private helper's.

## Spec references
ADR 0045 (its Rejected "Any accessibility" and the accessor exclusion), ADR 0047, ADR 0019, VERIFICATION-MODEL
section 1.

## Acceptance criteria (all must hold; nothing beyond them)
1. Decide through `equiv-adr` whether this is a clarification of ADR 0045 or a new ADR, and say why a public static
   getter of the caller's own type meets ADR 0045's reasons (no dispatch, no other type's initializer, and its own
   result still reported as Removed or Added).
2. Implement it with a sample pair: a getter renamed between the sides, both returning one constant, is Equivalent; one
   whose renamed getter returns another constant stays Divergent.
3. Rerun `duplicati-3124` in compare mode quick with P2-192's config (its Notes); Notes give `CalculateHash`'s result.

## Tests
As the decision requires; an instance getter, a virtual or abstract one, and one of another type are not covered.

## Size guard
An instance property or a property with a setter: stop, that is another ticket.

## Out of scope
Properties of another type, and getters that exist on both sides (ADR 0019 assumes them).

## Notes
- Filed 2026-10-15 by P2-192 (its rerun of `duplicati-3124`).
