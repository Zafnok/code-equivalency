# P2-239 A suppressed runtime change is an assumption the result names, and a precondition can be suppressed
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-212

## Goal
ADR 0060 decision 6. `equiv.config.json`'s `suppressRuntimeChanges` makes a member's row never match (ticket M3-001),
and no result says so: an Equivalent that rests on it is a silent assumption. Under ADR 0060 an entry is a declared
assumption, as ADR 0059 made a mapped callee one. An entry is a member prefix, as today, or `precondition:<name>`
(`precondition:invalidPath`), which suppresses every row with that precondition. Every result whose body calls a
member an entry suppresses lists the entry in `properties.unprovenAssumptions` and in
`properties.runtimeChangesAssumed` (sorted, without duplicates, not part of the fingerprint), and an Equivalent's
message gains "Assumes the runtime change does not reach: <entry>". Path rows are not assumed by default.

When done, a user who knows their paths are valid can say so in one entry, and every result that rests on it says so.

## Spec references
`docs/adr/0060-*.md` decision 6, ADR 0059, ADR 0019, VERIFICATION-MODEL sections 1, 3 ("Runtime-changed APIs") and 6,
the `runtime-changes.json` header (preconditions), `EquivConfig.SuppressRuntimeChanges`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A member entry and a `precondition:` entry both suppress, in the bound fingerprint and the lowered body alike, and
   an unknown precondition name is a config diagnostic (`CFG` id beside CFG007).
2. Results carry `runtimeChangesAssumed` and the `unprovenAssumptions` entries; the Equivalent message names them.
3. VERIFICATION-MODEL sections 1, 3 and 6 and the README's config text say what this decides.
4. The commit that changes the SARIF carries `Release: minor`.

## Tests
Config loader tests for both entry forms and the diagnostic; unit tests that a suppressed call is congruent and listed;
a Verify snapshot of a `samples/` pair with a `Path.Combine` on a parameter, with `precondition:invalidPath` declared.

## Size guard
If naming the assumption needs the contracts pass (ADR 0036) to change, stop: a suppressed row has no callee pair.

## Out of scope
Corpus configs: no `tools/corpus/configs/` file gains a suppression (ADR 0059's P2-213 clarification: an entry is
committed only where the pair's pull request shows it). EQ007 (P2-238).

## Notes
- Filed 2026-10-17 with ADR 0060.
