# extern-import-runtime

`[DllImport]` methods on a pair that crosses a runtime (ADR 0058, ticket P2-165). The legacy side targets
.NET 5 and the modern side .NET 6. `Native.cs` is byte-identical on both sides; `Changed.cs` is not.

- `Ticks` is spelled exactly and marshals no COM object. No entry of `runtime-changes.json`'s `marshalling`
  list reaches it anywhere from .NET Core 3.0 to .NET 7, so it is congruent by what it imports.
- `MessageBox` leaves `ExactSpelling` false, so the runtime may probe for an `A` or `W` suffix. .NET 5
  stopped doing that off Windows (`suffixProbing`), but between .NET 5 and .NET 6 no entry reaches it,
  and it is congruent too.
- `F` imports from `a.dll` on the legacy side and from `b.dll` on the modern side. The fingerprints
  differ, so it is Unknown, `no-body`, as an edited import is on one runtime (ADR 0054 decision 3).

Before ADR 0058 all three were Unknown: an `extern` method had no fingerprint across runtimes.

## Expected verdicts

| Procedure | Verdict | `proofMethod` | `unknownReason` |
|---|---|---|---|
| `Native.Ticks()` | Equivalent | `congruence` | |
| `Native.MessageBox(System.IntPtr, string, string, uint)` | Equivalent | `congruence` | |
| `Changed.F()` | Unknown | | `opaque` (`no-body`) |

Exit code: 0 (Unknown does not fail the run without `--fail-on unknown`).

## The same files from .NET Core 3.1

`netcoreapp3.1/legacy/` compiles `legacy/Native.cs` and `legacy/Changed.cs` for .NET Core 3.1. Against
`modern/` the pair crosses .NET 5, where `suffixProbing` reaches `MessageBox`, so it is Unknown, `no-body`;
`Ticks` is still congruent. The integration test `ExternImportRuntime_FromNetCore31TheSuffixProbingReachesOnlyTheInexactImport`
runs that pair; it has no snapshot.

## The same files from .NET Framework 4.8

`net48/legacy/` is an old-style .NET Framework 4.8 project that compiles the same two files. No page lists the
interop changes at the .NET Framework boundary, so ADR 0054 decision 4 stands there and every import is
Unknown, `no-body`. The integration test `ExternImportRuntime_FromNet48EveryImportStaysUnknown` runs that pair;
it has no snapshot.
