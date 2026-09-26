# runtime-diff

Calls a BCL member on .NET Framework 4.8 and on .NET 10 with generated arguments, and reports
every case whose outcome differs (ADR 0035, the second oracle; ticket M3-032). It is a thin
console over `Equiv.Execute` and the C# frontend's `DriverFactory`.

It needs Windows with the .NET Framework 4.8 targeting pack (it ships with Visual Studio and its
Build Tools) and the .NET 10 SDK. On any other OS it exits 3 with
`runtime-diff needs Windows and .NET Framework 4.8 (ADR 0035)`.

## Command line

```
runtime-diff --member "<CallIdentity prefix or exact>" [--seed n] [--cases n] --out report.json
```

- `--member`: a `CallIdentity`, as the frontend spells one and as `runtime-changes.json` rows
  use it: `Namespace.Type::Member(ParamType,...)`, with C# keywords for built-in types
  (`System.String::IndexOf(string)`). A prefix selects every overload that starts with it
  (`System.String::IndexOf(`), and one ending right after `::` selects every member of the
  type. Only public members present on both runtimes are run. A property getter is `get_Name`,
  and a constructor is `.ctor`.
- `--seed`: the generator seed (default 0). The same seed gives the same inputs.
- `--cases`: the inputs per overload (default 64). Each runs under every culture.
- `--out`: where to write the report.

From the repository root:

```
dotnet run --project tools/runtime-diff -- --member "System.String::IndexOf(" --out indexof.json
```

Exit codes: 0 when no overload diverges, 1 when one does, 3 on a usage error (a bad argument,
no matching member, or a non-Windows OS). Nondeterminism and not-constructible overloads do not
change the exit code.

## What runs

For each overload, the frontend compiles two drivers. One is a .NET Framework 4.8 executable
with an `app.config`. The other is a .NET 10 assembly run through `dotnet`. Each driver's C#
source is written beside it as `EquivDriver.cs`. Both read one JSON line per case on stdin:
`[culture,arg0,arg1,...]`, with an instance member's receiver as `arg0`. They set the current
culture and UI culture, call the member, and write `["Kind",canonical]` on stdout.

- **Cultures:** `invariant`, `en-US`, `tr-TR`, `de-DE` and `ja-JP`.
- **Runs:** each side runs every case twice, each time in a fresh process. A case gets 10 seconds
  and a process 1 GiB of private memory. A case that gets no answer is `NotComparable`, with the
  value `"no answer"`.
- **Inputs:** each parameter type has edge values, which come first, in every combination.
  Random values follow.
  - Integers: 0, 1, -1, minimum and maximum.
  - `float` and `double`: zeros, ±1, 0.1, NaN, the infinities, the extremes and epsilon.
  - `decimal`: 0, ±1, 0.1, 1.0 and the extremes.
  - `char`: ASCII, Latin-1, the Turkish dotted and dotless i, combining marks and surrogate
    halves.
  - `string`: `"i"`, `"I"`, `"ß"`, `"ss"`, `"\u0000"`, a soft hyphen, `"æ"`, `"ae"`, empty and
    null.
  - Enums: every defined value on either runtime, plus one undefined value.
  - `bool`, and `null` for any other reference type.

  A member that takes any other parameter type, or a `ref`, `in` or `out` parameter, is not
  run. The same goes for a generic member, an operator, a setter, an event accessor, a
  constructor of an abstract type, and a member returning a pointer. The report lists why.

## Canonical outcomes

The driver writes the canonical form with its own code, never with a runtime's `ToString`,
because a formatting change between runtimes is not a behaviour change:

| Value | Canonical form |
|---|---|
| integer, enum | decimal integer (an enum as its underlying value) |
| `char` | its code point, as an integer |
| `float`, `double` | its IEEE bit pattern as a hex string: `"0x3FB999999999999A"` |
| `decimal` | its four `decimal.GetBits` integers: `[1,0,0,65536]` |
| `string` | a JSON string, ASCII only: every other character is a `\uXXXX` escape |
| `bool`, `null` | `true`, `false`, `null` |
| array, `List<T>` of the above | a JSON array, element by element |
| exception | `Threw` with the type's full name only |
| `void` | `null` |

Any other return type is `NotComparable`, and the value is the type's name. A case whose
arguments or culture cannot be built on a runtime is `NotConstructible`, and the value is the
exception type.

## Report

```json
{
  "member": "System.String::IndexOf(",
  "seed": 0,
  "cases": 64,
  "cultures": ["invariant", "en-US", "tr-TR", "de-DE", "ja-JP"],
  "overloads": [
    {
      "member": "System.String::IndexOf(string)",
      "casesRun": 320,
      "divergent": 65,
      "witnesses": [
        {
          "input": ["ss", "ß"],
          "culture": "invariant",
          "legacy": { "kind": "Returned", "value": 0 },
          "modern": { "kind": "Returned", "value": -1 }
        }
      ],
      "nondeterministic": { "legacy": 0, "modern": 0, "both": 0 },
      "notComparable": 0,
      "notConstructible": []
    }
  ]
}
```

- `casesRun`: inputs times cultures. It is 0 for an overload that was not run.
- `divergent`: the cases where both sides were deterministic and returned or threw, and the
  outcomes differ. `witnesses` holds the first five.
- `nondeterministic`: the cases whose two runs differed on one side only (`legacy`, `modern`),
  and on both sides (`both`). A one-sided count is a finding in itself. `String.GetHashCode()`,
  for example, is randomised per process on .NET 10 only. A case counted here is never counted
  as divergent.
- `notComparable`: the cases where a side's outcome was `NotComparable` or `NotConstructible`.
- `notConstructible`: why the overload was not run: each unsupported parameter type, the reason
  generated source cannot call it, or the compiler error.
