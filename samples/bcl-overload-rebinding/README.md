# bcl-overload-rebinding

Identical source text that the modern reference assemblies bind to an added overload or a moved
member (ADR 0020, ticket P2-070; found by P2-047's audit):

- `s.TrimEnd('/')` binds `String.TrimEnd(params char[])` on .NET Framework 4.8 and
  `String.TrimEnd(char)` on .NET 10.
- `s.TrimStart()` binds `String.TrimStart(params char[])` with an empty array on .NET Framework 4.8
  and `String.TrimStart()` on .NET 10.
- `d.FullName` on a `DirectoryInfo` binds `DirectoryInfo.FullName` on .NET Framework 4.8, where the
  class overrides the property, and `FileSystemInfo.FullName` on .NET 10.

The shipped API-equivalence catalogue rewrites each legacy call to its modern member, and each result
lists the entry applied in `properties.equivalencesApplied`. The rewrite happens before call sites are
compared, so none of these is a rebound call (ADR 0042). `StripOther` trims `'/'` on the legacy side
and `'.'` on the modern side: the entry still applies, and the two calls of `TrimEnd(char)` have
different arguments. A rewritten call is closed exactly when a direct call of its modern member is
(ADR 0041, ticket P2-184), so both calls are closed and the replay taints what the solver chose for
them (ADR 0056). The sides differ only in those answers, so the pair is Unknown (`abstraction`),
naming `String.TrimEnd(char)` on both sides (ticket P2-203 owns giving it back its Divergent).

## Expected verdicts

| Procedure | Verdict | `equivalencesApplied` |
|---|---|---|
| `Paths.Strip(string)` | Equivalent | `bcl.string-trim-end-one-char` |
| `Paths.Indent(string)` | Equivalent | `bcl.string-trim-start-no-chars` |
| `Paths.Full(DirectoryInfo)` | Equivalent | `bcl.directory-info-full-name` |
| `Paths.StripOther(string)` | Unknown (`abstraction`; equivalent when `s == null`) | `bcl.string-trim-end-one-char` |

Exit code: 0 (Unknown does not fail the run without `--fail-on unknown`).
