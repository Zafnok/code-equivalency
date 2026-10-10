# P2-073 A runtime-change row fires only when the call's constant arguments can reach the change
Status: in-progress
Effort: M
Model: Opus, medium effort. If you are a weaker model family than named, or the named family at a lower effort, stop before doing anything else and tell the user to switch.
Depends on: P2-047

## Goal
P2-047's audit found 15 false EQ006 results, the largest single cause, where a
`runtime-changes.json` row fires on a call whose constant arguments cannot reach the documented
change:
- a `Regex` pattern without `IgnoreCase` or a case-insensitive range, under the .NET 7 regex-range row (9);
- a four-digit-year `ParseExact` format under the two-digit-year row;
- ASCII constants compared under the ICU row;
- a constant path under the path-validation row;
- a constant `Bitmap` size under the GDI+ row;
- an `XmlSerializer` for a type without `[Obsolete]` members.

Minimal repro, as a sample pair (identical file, legacy net48):

```csharp
static string[] Words(string s) => System.Text.RegularExpressions.Regex.Split(s, @"\s+");
static string Cache() => System.IO.Path.Combine("C:\\data", "Images");
```

Today both methods are EQ006. They should be Equivalent. Give a row an optional precondition on its
arguments, evaluated only when the argument is a compile-time constant (a pattern and its options,
a format string, a path literal). A constant that fails the precondition does not fire the row. A
non-constant argument fires it as today.

## Spec references
VERIFICATION-MODEL section 3 (runtime-changed APIs), P2-054 (row fields), P2-055 (rules by
interval), P2-047's audit.

## Acceptance criteria (all must hold; nothing beyond them)
1. The regex-range, two-digit-year, ICU comparison and path-validation rows carry a precondition, and
   the table's schema documents the field.
2. `samples/runtime-row-constant-args` (the pair above) is Equivalent. A variant with pattern
   `(?i)[a-z]` and one with path literal `"a|b"` stay EQ006.
3. A non-constant argument fires the row as before.

## Tests
- Integration test on `samples/runtime-row-constant-args`.
- Unit tests per precondition.

## Out of scope
Values that are not compile-time constants (P2-074 covers paths from known BCL sources). Rows
other than those four, unless the precondition is a one-liner.

## Notes
- Found by P2-047: 8 on Git Extensions and 7 on the Tomas pair (audit rows under P2-073).
- Decision: the field is `precondition`, one of four names, each a fixed rule in `RuntimeChangeReach` (Core);
  the frontend only hands it the call's operands (`CallArguments`). A free-form expression in the JSON was
  the alternative; four named rules are testable one by one and need no evaluator.
- Decision: a precondition answers conservatively. `caseInsensitivePattern` is met by any inline `i` option
  or `RegexOptions.IgnoreCase`, with or without a character range. `invalidPath` also counts wildcards, a
  misplaced colon, a blank path and a path of 248 characters or more, which .NET Framework rejects up front
  too. A null constant always meets its precondition.
- Decision: `cultureSensitiveText` needs the receiver and every string argument to be a constant of ASCII
  letters and digits only. A constant argument alone is not enough: `"\r\n".IndexOf("\n")` and
  `string.Compare("co-op", "coop")` are all-ASCII calls that differ between NLS and ICU, and a receiver that
  is not a constant can hold anything. So the row still fires on `s.StartsWith("abc")`; P2-075 owns the
  ordinal overloads. Not checked: a culture whose tailoring orders ASCII digraphs (Danish `aa`) differently
  under NLS and ICU. The replay and differential oracles run under the invariant culture and `tr-TR` only.
- The four families are 43 rows: 1 regex, 4 date parsing, 8 `String` comparison and search, 30 path
  validation (every measured row whose reason is the invalid-path-characters check). `GetInvalidPathChars()`
  takes no path and has none. Every measured witness of those rows meets its row's precondition (a test).
- Scope: the arguments are read where the call is lowered from source and where the bound fingerprint is
  taken. A call resolved to a forwarder's target, a call through an API-equivalence entry, an instance
  `Regex` method (the pattern went to the constructor) and the IL lowering read none and fire as before.
  The GDI+ and `XmlSerializer` cases in the audit are not constants of an argument and are left.
- `VerdictRule` picks the row an EQ006 message cites by the callee's identity alone. If a member had two
  rows, the first with a precondition the call does not meet, the message would cite the first. No member
  has that today.
- `LoweringCensus` counts runtime-change calls by identity, without arguments, so its counts are unchanged.
