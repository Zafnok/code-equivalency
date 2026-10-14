# P2-186 Precision bug: an allocation-only constructor outside P2-071's catalogue is a call event with a free `threw`
Status: todo
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
P2-130's adjudication (`docs/runs/2026-10-14-migrations-verdict.md`) kept 2 false EQ002 on
`gitextensions-8522` that P2-047 gave to P2-071 (rows 40 and 43), still Divergent:
- `GitUI.UserControls.RevisionGrid.Graph.RevisionGraphRevision::.ctor(GitUIPluginInterfaces.ObjectId,int)`:
  legacy constructs `System.Collections.Generic.SynchronizedCollection<T>` (System.ServiceModel, not in
  the catalogue), modern `ConcurrentBag<T>` (in it), so only the legacy side has a call event;
- `GitUI.Theming.ThemeLoader::.ctor(...)`: `ExCSS.Parser::.ctor()` against
  `ExCSS.StylesheetParser::.ctor(bool,bool,bool,bool,bool,bool)` after the ExCSS 2 to 4 upgrade.

P2-071 (done, ADR 0043) lists parameterless constructors of base class library collection types only.

When done, `SynchronizedCollection<T>`'s parameterless constructor is effect-free, and how a
third-party constructor could be is decided.

## Spec references
ADR 0043, `docs/tickets/done/P2-071-*.md`, the effect-free member list.

## Acceptance criteria (all must hold; nothing beyond them)
1. Add `SynchronizedCollection<T>::.ctor()` to the effect-free list, with the documentation that it
   only allocates.
2. Decide with `equiv-decide` (or `equiv-adr` if the bar test says so) whether a third-party
   constructor may be listed, and record it.
3. Rerun `gitextensions-8522` in compare mode quick; Notes give the two procedures' new results.

## Tests
The effect-free list's table test for the new entry; a pair test where the constructor is the only
difference.

## Size guard
Listing members by pattern rather than one by one: stop, that is an ADR 0043 change.

## Out of scope
Constructors with arguments that can throw.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 4).
