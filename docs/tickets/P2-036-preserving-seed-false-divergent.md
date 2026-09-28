# P2-036 A behaviour-preserving rename is reported Divergent
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-010

## Goal
M4-007's first real run applied M4-010's mechanical seeds to `pmb-shiningrush__serviceant`. One
seed used the `RenameLocals` operator (`tools/corpus/seeder/seeds.md`'s "Preserving" family — a
local-variable rename can never change behaviour) on
`ServiceAnt.IocInstaller.Castle.Test.ServiceAntInstaller_Test::CanHandleEventByIocHandler()`, and
`equiv` reported it **Divergent** (`EQ002`, with a counterexample model over
`Castle.MicroKernel.Registration.IWindsorInstaller[]` inputs), not Equivalent. Per
`equiv-corpus-run`'s own rule for this exact situation: "Divergent, operator in the Preserving
family ... is a precision bug, not a soundness one — the two methods behave identically by
construction, so a Divergent verdict is `equiv` wrongly disagreeing." This is exactly that case,
and it is the only mechanical seed across all three agent pairs that came back wrong (the other
~29 combined were all correct: `Equivalent` for every other `Preserving` seed, `Divergent`/`Unknown`
for every `Changing` one).

## Spec references
The seeded-mech run at `.corpus/pairs/pmb-shiningrush__serviceant/runs/*-seeded-mech/equiv.sarif`
(not committed — third-party code) has the counterexample model; reproduce it standalone instead
of reading that file for the fix.

## Acceptance criteria (all must hold; nothing beyond them)
1. Reproduce with a small standalone repro: a test method that constructs an array of a
   `Castle.Windsor`-style registration parameter (or, more generally, any array-of-reference-type
   parameter) where legacy and modern differ only in a local variable's name.
2. Find why the rename affects the verdict — a renamed local should never change the IR's bound
   fingerprint or its lowering, so either the two bodies are not actually congruent-checked first
   (a congruence/fingerprint bug letting a rename slip through to the solver with a spurious
   difference), or the solver call itself is given non-equivalent encodings for some other reason
   coincident with this particular array-of-installers shape.
3. The repro is Equivalent after the fix.

## Size guard
If the root cause turns out to be the same bug as P2-031 or P2-032 (both about array/reference-type
sort handling), fix it there instead and close this ticket as a duplicate — check those first.

## Out of scope
Other `Preserving`-family operators unless the same fix happens to cover them.

## Notes
- This may well be the same underlying array/reference-type sort bug as P2-031 or P2-032 — the
  counterexample model is over `IWindsorInstaller[]`, an array of a reference type, which is
  exactly the shape both of those tickets are about. Check there first before treating this as a
  fourth distinct bug.
