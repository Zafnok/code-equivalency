# P2-251 Execution reach: a replay builds an enum argument, which the model holds as a sort element
Status: todo
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
