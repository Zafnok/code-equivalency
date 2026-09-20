---
name: equiv-package-api
description: Find where a NuGet dependency lives on disk and look up its real API shape (types, members, XML docs, bundled schemas). Use whenever you are about to search the filesystem for an assembly, wonder what properties a Sarif.Sdk/Roslyn/Z3/System.CommandLine type has, or consider writing a throwaway probe app to discover behaviour.
---

# Looking up a package's API

Every restored package sits at a path you can compute in one line. Computing it beats
searching for it, so reach for the formula below first — but filesystem search is a
normal tool and this skill does not take it away from you in general.

**The one hard rule: never filesystem-search for `Sarif.Sdk`.** Not `find`, not
`Get-ChildItem -Recurse`, not from `C:\`, not from the repo root. That search is
*guaranteed* to return zero results — nothing on the machine carries that name (see the
name trap below). Because it finds nothing, it has nothing to stop early on, so it
traverses the whole drive and then comes back empty: roughly 13s for `find`, 37s for
`Get-ChildItem -Recurse -Filter`, 72s once you drop `-Filter` for `-Include` (measured on
this box). An empty result reads as "I searched wrong", so the next move is to widen it,
chain another drive, or loosen the pattern — and two or three of those in one tool call
is what blows the 120s timeout. That is the loop this skill exists to break.

## Where packages are

```
<global-packages>/<package id lower-cased>/<version>/lib/<tfm>/<assembly>.dll
                                                    /lib/<tfm>/<assembly>.xml   <- the API reference
```

- `<global-packages>` is `$env:NUGET_PACKAGES` if set, else `$env:USERPROFILE\.nuget\packages`.
  This repo sets no `nuget.config` and no override, so on the dev box it is
  `C:\Users\<you>\.nuget\packages`. Confirm it in one command:

  ```bash
  dotnet nuget locals global-packages --list
  ```

- `<version>` is pinned centrally in `Directory.Packages.props` (central package
  management). Read the version from there; do not guess and do not glob for it.
- `<tfm>` is `netstandard2.0` unless you specifically need the `net462` build.

The `.xml` beside the `.dll` is the whole public API with documentation comments.
**That file is the answer to almost every "what does this type look like" question.**
`Sarif.xml` alone is 757 KB and documents 1840 members.

## The Sarif.Sdk name trap (why searches come back empty)

Three different names. This is the single reason agents keep hunting for this package:

| Thing | Name |
|---|---|
| NuGet package id | `Sarif.Sdk` |
| Assembly / file on disk | `Sarif.dll`, `Sarif.xml` |
| Root namespace | `Microsoft.CodeAnalysis.Sarif` |

There is **no file anywhere on the machine called `Sarif.Sdk.dll`.** Searching for one
traverses all of `C:\` (~37s with `-Filter`, ~72s with `-Include`) and finds nothing.

Resolved path on this box, for copy-paste:

```
C:\Users\<you>\.nuget\packages\sarif.sdk\5.7.0\lib\netstandard2.0\Sarif.xml
C:\Users\<you>\.nuget\packages\sarif.sdk\5.7.0\lib\netstandard2.0\Sarif.dll
C:\Users\<you>\.nuget\packages\sarif.sdk\5.7.0\Schemata\sarif-2.1.0.json
```

That last one is the authoritative SARIF 2.1.0 JSON schema, shipped in the package. When
a ticket asks what a SARIF object may legally contain, read it — do not infer from the
object model and do not go to the web.

Also note `Microsoft.CodeAnalysis.Sarif` shares Roslyn's namespace prefix by coincidence
and is **not** Roslyn. ADR 0010 exists because an architecture rule confused the two.

## Recipes

Resolve the XML doc path in three commands (verified; substitute any package id):

```bash
V=$(grep -oP 'Include="Sarif\.Sdk"\s+Version="\K[^"]+' Directory.Packages.props)
G=$(dotnet nuget locals global-packages --list | sed 's/^global-packages: //' | tr -d '\r')
X="$(cygpath -u "$G")sarif.sdk/$V/lib/netstandard2.0/Sarif.xml"
```

`cygpath` is only needed from the Bash tool; in PowerShell use `$G` as-is. Then:

**List every member of a type** (answers "what properties does it have"):

```bash
grep -o 'name="[PTMF]:Microsoft\.CodeAnalysis\.Sarif\.LogicalLocation[^"]*"' "$X" \
  | sed 's/name="//;s/"$//'
```

**Read the docs for one member:**

```bash
awk -v m='P:Microsoft.CodeAnalysis.Sarif.LogicalLocation.Kind' \
  '$0 ~ "<member name=\""m"\">",/<\/member>/' "$X"
```

**Find a type when you only know part of the name:**

```bash
grep -o 'name="T:[^"]*Location[^"]*"' "$X" | sed 's/name="//;s/"$//' | sort -u
```

Prefixes: `T:` type, `P:` property, `M:` method, `F:` field, `E:` event.

## Other packages in this repo

`Sarif.Sdk` is the only id/assembly mismatch. The rest ship an assembly matching the
package id, each with its `.xml` beside it: `System.CommandLine`, `Newtonsoft.Json`
(transitive via Sarif.Sdk), `Microsoft.CodeAnalysis.CSharp.Workspaces`,
`Microsoft.CodeAnalysis.Workspaces.MSBuild`. Ignore `*.resources.dll` under
`lib/<tfm>/<locale>/` — those are localisation satellites, not the assembly.

`Microsoft.Z3` is **not restored yet** (M3 has not started), so it is absent from the
cache — if you look for it before M3, `dotnet restore` first rather than concluding it is
missing from the machine. Its solver is a native `libz3` that must be copied next to the
test host; that is a build/runtime concern, not an API-lookup one, and M3-001's Notes
already cover it. Re-check its assembly and XML doc names here once it is restored.

## When the XML docs genuinely are not enough

Runtime *behaviour* (what actually serializes, which exception type is really thrown) is
not always documented. M1-004 and M1-005 each burned time on a throwaway probe console
app for exactly this. That is a legitimate last resort, but only after you have read the
XML docs and the bundled schema. If you do it:

- Keep it outside the solution (use the scratchpad directory), never in `src/` or `tests/`.
- Timebox it to the CLAUDE.md 15-minute rule.
- Write what you learned into the ticket's `## Notes` as a `Toolchain:` line, so the next
  agent reads it instead of rediscovering it. Both existing probes did this correctly —
  that is why their findings survived.
