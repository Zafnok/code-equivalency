# relative-full-path

A member whose result holds the working directory (ticket P2-254). `Paths.Full(string)` is the same text on both sides:
it returns `Path.GetFullPath` of its argument, so a relative argument comes back prefixed with the process's working
directory, on .NET Framework 4.8 and on .NET 10 alike.

The sample is for execution: `equiv mcp --execute`'s `probe` of `Full("s1\s2\")` gives the same path on both sides,
because both drivers of a case start in the same working directory. Before P2-254 each driver started in its own
`cwd-<random>` folder, so the two sides returned different paths, and a replay of such a member was `reproduced` on any
pair of runtimes.

`Path.GetFullPath` has a row in `runtime-changes.json` (path validation: .NET Framework rejects a wildcard and a stray
colon that .NET 10 accepts), and the argument is not a constant, so the row applies to the unedited body. `s1\s2\` holds
no such character, which is why the probe's two sides agree.

## Expected verdicts

| Procedure | Verdict | `proofMethod` |
|---|---|---|
| `Paths.Full(string)` | Runtime exposure with a witness (EQ007: unedited body; exits as a Divergent) | none |

Exit code: 1 (a new Divergent result).
