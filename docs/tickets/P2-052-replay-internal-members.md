# P2-052 Replay and differential testing reach `internal` methods
Status: todo
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: M4-009, P1-008

## Goal
The second oracle is the only thing that checks a Divergent verdict against the real runtimes. On
Git Extensions it could build only 21 of 352 Divergent results (M4-007). The second-largest reason
was "legacy method not public" (105), from `ReplayArguments.CallObstacle`: the generated driver can
call only public members. Much line-of-business logic is `internal`. `ProjectEmitter` already emits
each project's `Compilation` itself. Adding `[assembly: InternalsVisibleTo("<driver assembly>")]`
to that compilation before it emits lets the driver call `internal` and `protected internal`
members on both runtimes, with no reflection. `private` members stay out of reach.

## Spec references
ADR 0035 decision 2 (replay); VERIFICATION-MODEL.md section 6 (`replay`, `differentialTesting`);
tickets M4-009, P1-008.

## Acceptance criteria (all must hold; nothing beyond them)
1. `ProjectEmitter` adds one `InternalsVisibleTo` attribute, naming the driver's assembly, to each
   emitted project compilation. The attribute is a new syntax tree, and no source file on disk
   changes. For a strong-named project, the driver is signed with a key generated per run, and the
   attribute carries its public key.
2. `CallObstacle` returns `"not public"` only for `private`, `protected` and `private protected`
   members, and for members of a type that is not visible from the driver. `internal` and
   `protected internal` members pass.
3. A replay of an `internal` static method and of an `internal` instance method on an internal type
   with a public parameterless constructor reproduces on both runtimes.
4. The replay target's `not-constructible` detail distinguishes `not public (private)` from the other
   obstacles, so the next corpus run can count what remains.
5. No reflection in `src/` (CLAUDE.md). The driver source calls the member directly.

## Files
`src/Equiv.Frontend.CSharp/Execution/ProjectEmitter.cs`,
`src/Equiv.Frontend.CSharp/Execution/ReplayArguments.cs`,
`src/Equiv.Frontend.CSharp/Execution/DriverSource.cs`,
`src/Equiv.Frontend.CSharp/Execution/ReplayDriverFactory.cs`, one new sample or an integration
fixture with an internal divergent method, tests.

## Tests
`ProjectEmitterTests.EmitsInternalsVisibleToTheDriver`,
`ProjectEmitterTests.StrongNamedProject_GrantsTheSignedDriver`,
`ReplayArgumentsTests.InternalMethod_HasNoCallObstacle`,
`ReplayArgumentsTests.PrivateMethod_IsNotPublic`,
`ReplayIntegrationTests.InternalDivergentMethod_Reproduces` (Windows only, like M4-009's).

## Size guard
If a route to `private` members seems necessary, it needs reflection or `UnsafeAccessor`, which
.NET Framework 4.8 lacks. Stop and route it through `equiv-adr`. More than one new sample: stop.

## Out of scope
The 125 results whose divergence is in the call trace, which replay does not observe by design.
Non-parameterless constructors.

## Notes
