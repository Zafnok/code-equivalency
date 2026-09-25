# M4-010 Seeded at scale: mechanical seeds on corpus methods, so seeded recall is measured over hundreds of changes, not a handful
Status: todo
Effort: M
Model: Sonnet, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M0-012

## Goal
ADR 0028's seeded-recall criterion must be 100%, and a miss preempts all other work. The seeds in
`tools/corpus/seeds.md` are a short hand-written list, so 100% on them is weak evidence. This
ticket gives the corpus a seeder:
- it applies M0-012's `Changing` and `Preserving` mutation operators to methods of a pair's
  modern side, inside a copy under `.corpus/`;
- it writes a manifest of what it changed.

M4-007's `seeded` mode then reports recall over the mechanical seeds as well as the hand-written
ones. Behaviour-changing seeds can be equivalent mutants, so a seed counts as a miss only when it
is reported Equivalent **and** the repo's own tests, or M4-009's replay, show the behaviour
changed. Every other seed reported Equivalent is listed as "unconfirmed".

## Spec references
ADR 0028 (seeded recall; corpus rules); M0-012 (operators); `.claude/skills/equiv-corpus-run`
(`seeded` mode); `tools/corpus/seeds.md`.

## Acceptance criteria (all must hold; nothing beyond them)
1. The mutation operators move from `tests/Equiv.TestSupport/Mutations` into a small tool,
   `tools/corpus/seeder/` (a console project using Roslyn syntax rewriting only). `Equiv.TestSupport`
   references the tool's library, so M0-012 and the seeder share one implementation.
2. `corpus.ps1 -SeedMechanical <slug> [-Count 300] [-Seed n]` copies the modern side to
   `.corpus/pairs/<slug>/seeded-mech/`. It picks methods at random, weighted towards changed pairs
   when the census lists any, applies one operator per method, and writes
   `.corpus/pairs/<slug>/seeded-mech/seeds.json`: method identity, operator, and line.
3. Every seeded copy must build. An operator application that fails to compile is dropped and
   counted, not kept.
4. The `equiv-corpus-run` skill's `seeded` mode runs the mechanical copy too. Its SUMMARY template
   gains:
   - seeds applied, dropped, and reported per verdict;
   - recall = (Divergent + line-scoped Unknown with a cause on the seed's line) / confirmed
     behaviour-changing seeds;
   - Preserving seeds reported Divergent (a precision bug each);
   - the unconfirmed list's size.
5. The skill states that a confirmed miss is a `P2-nnn-soundness-<slug>.md` ticket and is
   reported to the user first, as in M4-007 criterion 3.
6. No seeded source text is committed: only counts and identities, per `docs/runs/README.md`.

## Files
`tools/corpus/seeder/**` (new), `tools/corpus/corpus.ps1`, `tools/corpus/README.md`,
`tests/Equiv.TestSupport/*` (the reference), `.claude/skills/equiv-corpus-run/SKILL.md`,
`docs/runs/README.md` (the template fields).

## Tests
`Seeder_AppliesOneOperatorPerMethod`, `Seeder_DropsUncompilableMutants`,
`Seeder_ManifestListsEverySeed`, `Seeder_IsDeterministicForASeed`.

## Size guard
Any change under `src/` is out of place. The seeder is tooling.

## Out of scope
Running the seeded mode (M4-007 does). New operators beyond M0-012's. Seeding the legacy side.

## Notes
