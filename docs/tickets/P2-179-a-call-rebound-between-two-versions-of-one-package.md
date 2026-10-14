# P2-179 A call rebound between two versions of one third-party package is decided from the two versions' code, not left Unknown
Status: todo
Effort: M
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: none

## Goal
ADR 0042 makes a call opaque, with reason `rebound-call`, when the same source text binds to another
callee on the modern side, because "the library's code is not in the run". P2-137 counted the callee
pairs behind the reason and gave the base class library ones that are one call a catalogue entry. The
third-party pairs got none, and no open ticket owns them.

P2-130's run of `gitextensions-8522` (equiv `e98fd1b9`, compare mode quick,
`docs/runs/2026-10-13-full-gitextensions-8522/SUMMARY.md`) has `rebound-call` alone in 121 of 938
changed pairs (12.9%), and in 108 Unknown results (11.5% of changed pairs): the pair's largest opaque
reason, above ADR 0028's 5%. P2-137's table names who they are: FluentAssertions 5.2.0 to 5.10.3
(`ActionAssertions::Throw<T>` to `DelegateAssertions<Action>::Throw<T>`, 46 changed pairs alone),
System.IO.Abstractions 2.0.0.144 to 11.0.6 (`FileBase` to `IFile`, `DirectoryBase` to `IDirectory`)
and NSubstitute 3.1.0 to 4.2.1 (`Returns<FileBase>` to `Returns<IFile>`). On `duplicati-3124` the
reason is alone in 14 of 707 (2.0%).

Both versions of each package are restored on disk for the run (the corpus restores both sides), so
"not in the run" is a choice, not a fact. When done, a rebound pair whose two callees are members of
two versions of one package is decided from those members' code where it can be, and stays Unknown
where it cannot.

## Spec references
ADR 0042, ADR 0020 (API equivalences), ADR 0039 (IL lowering), ADR 0019 (named callee assumptions);
`docs/tickets/done/P2-137-*.md` (its tables); `.claude/skills/equiv-adr`; `.claude/skills/equiv-corpus-run`.

## Acceptance criteria (all must hold; nothing beyond them)
1. Count, on `gitextensions-8522` at `main`'s head, the rebound pairs whose two callees are in one
   package (by assembly name) at two versions, by package, with the changed pairs that hold only them.
2. Decide, through `equiv-adr`, how such a pair may be decided: for example by lowering both callees
   from IL and verifying them as a pair the same run checks (ADR 0019's named assumption, discharged),
   or by a per-package equivalence entry, or not at all. The decision says why it cannot prove a pair
   whose two library versions behave differently.
3. Implement the decision for the largest package of criterion 1, with a unit test and a sample pair
   under `samples/` that has two versions of one local library.
4. Rerun `gitextensions-8522` in compare mode quick and record, in Notes, `rebound-call`'s changed
   pairs alone and its Unknowns next to 121 and 108, and that no result that was Divergent became
   Equivalent without the library's two bodies being proved equal.

## Tests
Unit and integration tests for the chosen rule; the sample pair of criterion 3.

## Size guard
A change to how a base class library rebound pair is decided: stop, that is P2-137's catalogue.

## Out of scope
Catalogue entries for base class library pairs; P2-143's span overloads.

## Notes
- Filed 2026-10-14 by P2-130 (criterion 6): the largest opaque reason of `gitextensions-8522` with no
  open owner.
