# P2-251 Execution reach: a replay builds an enum argument, which the model holds as a sort element
Status: done (PR #555)
Effort: S
Model: Opus, high effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-245

## Goal
The C# lowering maps an enum to the sort of its metadata name (`TypeMapper.Map`), and an enum constant to the element a
stable hash of its value's text gives (`TypeMapper.Constant`). Replay builds an enum only from a bitvector
(`ReplayArguments.Wire`), so every replay whose model holds an enum argument or field is `not-constructible` ("no
`System.StringComparison` argument can be built ..."): 6 in P2-245's runs (`StringComparison`, `Environment.SpecialFolder`,
`EnvironmentVariableTarget`, `GitUIPluginInterfaces.SettingLevel`, `Duplicati.Library.Interface.CompressionHint`,
`System.Drawing.Color` is a struct and not this ticket). When done, an enum sort element that is a member's constant
element is that member, and any other element is a value no member has (the type's largest member plus one, distinct per
element), so equal elements are equal values and the model's comparisons with its constants hold.

## Spec references
VERIFICATION-MODEL section 2 (constants of an uninterpreted sort), section 6 (the replay paragraph), ADR 0062 decision 3,
`src/Equiv.Frontend.CSharp/Lowering/TypeMapper.cs` (`Constant`, `Element`), `ReplayArguments.Wire`, `ModelObjects`.

## Acceptance criteria (all must hold; nothing beyond them)
1. A replay builds an enum argument or field from its sort element as the Goal says, on both runtime kinds; a unit test
   covers a member's element, two other elements, and a `[Flags]` enum.
2. The six replays above are rerun and each one's value is given.

## Notes
- Filed 2026-10-19 by P2-245 (a `not-constructible` reason ADR 0062 does not address).
- Decision: the element `0`'s constant gives (`default` of an enum lowers to it, `TypeMapper.Default`) is the value `0`
  even when no member is `0`, so a model's `default` comparison holds in the replay. Rule: 1 (the lowering's own constant).
- Decision: a `[Flags]` enum's "largest member plus one" is its members' bits together plus one, the smallest value no
  combination of members is: plus one of the largest member alone can be a combination (`1|4 = 5` over `1, 2, 4`), whose
  constant element the model holds distinct from a fresh one. Rule: 1 (the Goal's "comparisons with its constants hold").
- Decision: "distinct per element" ranks the elements of the sort that are not a member's, over every input of the
  model (maps' keys, values and defaults included), by id; the members are both sides' (the other side's enum of the
  same metadata name), so the two drivers build one value from one element even when one side's enum has a member the
  other lacks. Alternative: this side's members only (two sides could disagree on one element). Rule: 1 (soundness).
- Decision: a value past the underlying type's range (a `byte` enum whose members reach 255) is `not-constructible`
  ("past its members, its underlying type has no value left"), never a wrapped value that could be a member. Rule: 1.
- Decision: the code is one new file, `Execution/EnumElements.cs`, called from `ModelObjects.Value` before the scalar
  case; `ReplayArguments.Case` takes the other side's compilation (the factory passes it on all three calls).
- Known limit: an element that is the constant of a value no member has (`(E)42` in source) is not recognised as `42`:
  the element is a hash of the constant's text and only members' and `0`'s are tried. None of the six replays has one.
- Criterion 2 (`docs/runs/2026-10-19-enum-replay.md`, compare mode quick, `--jobs 4`, at `ee6b15a7`): `GetEnvironmentVariable`
  and `GetFolderPath` EQ007 `not-constructible` -> EQ006 `reproduced`; `ConfigFileSettings::CreateLocal` -> `not-constructible`
  (the call traces split); `StringExtensions::Contains` -> `not-constructible` (both sides threw `ArgumentException`, which
  the model's runs do not); `PieSlice::CreateBrushForSide` unchanged (`Color` is a struct); Duplicati's `CreateFile`
  builds its enum and stops at `DateTime` (P2-257, filed here). OpenRA holds none of the six and was not rerun.
- Found while running: the modern Git Extensions drivers need `Microsoft.WindowsDesktop.App` 5.0 since P2-253, which the
  machine install lacks; the values come from a second run on P2-253's private `dotnet` root, still in that session's
  scratch folder and only read here (the run on the machine's runtimes gave "runtime net5.0-windows not installed").
- Observation: in all five enum replays the model's element was a fresh one (the bodies compare the enum with no
  constant), so replay built a value no member defines, which takes the runs into the runtimes' argument validation:
  the two `reproduced` ones are .NET Framework's `ArgumentException` against .NET's `ArgumentOutOfRangeException` for an
  undefined value, a real runtime difference on an input the model allows.
