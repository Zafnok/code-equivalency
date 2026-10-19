# package-runtime-asset

A NuGet package whose compile asset is a reference assembly and whose implementation is elsewhere in
the package (ticket P2-240). The legacy side references .NET Framework 4.8's own `System.Management`;
the modern side, on .NET 10, takes the `System.Management` 4.7.0 package, which compiles against
`ref/netstandard2.0` (a reference assembly), runs `lib/netstandard2.0` (which throws
`PlatformNotSupportedException`) on any runtime identifier, and runs `runtimes/win/lib/netcoreapp2.0`
on Windows. `Wmi.Query(string)` is the same text on both sides: it builds a `SelectQuery` and returns
its text, which needs no WMI service.

The sample is for execution: `equiv mcp --execute`'s `probe` of `Query("Win32_Battery")` runs the
package's Windows runtime asset beside the modern driver, as the project's build output would, so
both sides return `select * from Win32_Battery`. Before P2-240 nothing of the package was deployed
beside the driver (its folder holds a reference assembly, so it was taken for a pack the runtime
supplies), and the modern side threw `FileNotFoundException`.

## Expected verdicts

| Procedure | Verdict |
|---|---|
| `Wmi.Query(string)` | Equivalent (`proofMethod: congruence`): the two bodies are the same text over the same calls |

Exit code: 0 (no Divergent or Unknown result).
