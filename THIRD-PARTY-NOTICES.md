# Third-party notices

`equiv` incorporates software from the projects below. This file satisfies the attribution
requirements of their licences. It does not change the terms of `equiv` itself, which is
licensed under the Business Source License 1.1 — see [LICENSE](LICENSE).

Nothing here is copyleft: every dependency is MIT or Apache-2.0, both of which permit
redistribution inside a proprietary or source-available product provided attribution is
preserved. The allowlist that keeps it that way is recorded in
[ADR 0017](docs/adr/0017-licensing-and-ip.md).

## Redistributed with the product

These packages are linked into the `equiv` binaries and container image, so their notices
travel with every release artifact.

| Component | Version | Licence | Project |
|---|---|---|---|
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.9.0 | MIT | <https://github.com/dotnet/roslyn> |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.9.0 | MIT | <https://github.com/dotnet/roslyn> |
| Sarif.Sdk | 5.7.0 | MIT | <https://github.com/microsoft/sarif-sdk> |
| Newtonsoft.Json (transitive, via Sarif.Sdk) | 12.0.1 | MIT | <https://github.com/JamesNK/Newtonsoft.Json> |
| System.CommandLine | 2.0.12 | MIT | <https://github.com/dotnet/command-line-api> |
| Microsoft.Z3 (incl. native `libz3`) | 4.12.2 | MIT | <https://github.com/Z3Prover/z3> |

Copyright notices:

- Copyright (c) .NET Foundation and Contributors — Roslyn, System.CommandLine
- Copyright (c) Microsoft Corporation — Sarif.Sdk, Microsoft.Z3
- Copyright (c) 2007 James Newton-King — Newtonsoft.Json

### MIT License

All components in the table above are licensed under the MIT License, reproduced once here:

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Not redistributed — build and test only

These are development dependencies. They are never linked into a shipped artifact, so their
attribution requirements do not attach to the product. They are listed for completeness, and
so that a future reader does not over-include them in release artifacts.

| Component | Version | Licence | Role |
|---|---|---|---|
| xunit.v3 | 4.0.1 | Apache-2.0 | test framework |
| CsCheck | 4.9.1 | Apache-2.0 | property testing |
| TngTech.ArchUnitNET.xUnitV3 | 0.13.4 | Apache-2.0 | architecture tests |
| coverlet.MTP | 10.0.1 | MIT | coverage |
| Verify.XunitV3 | 33.0.2 | MIT | snapshot testing |
| Meziantou.Analyzer | 3.0.259 | MIT | analyzer (`PrivateAssets="All"`) |
| MinVer | 8.0.0 | Apache-2.0 | versioning (`PrivateAssets="All"`) |
| dotnet-stryker | 5.0.0 | Apache-2.0 | mutation testing (local tool) |
| dotnet-sonarscanner | 11.3.0 | LGPL-3.0 | CI analysis (local tool, see note) |

Two notes on this section:

- **dotnet-sonarscanner is LGPL-3.0.** This is the only copyleft licence anywhere in the
  toolchain. It is a standalone CI executable that is invoked as a process and is never
  linked into or distributed with `equiv`, so no copyleft obligation reaches the product.
  It must stay that way: do not take a `PackageReference` on any Sonar library.
- **Verify carries a sponsorship rider.** `Verify.XunitV3` is MIT, but its SponsorCheck
  (SC021) requires sponsorship above roughly US$10k annual revenue.
  `Directory.Build.props` claims the `SmallRevenue` exemption until 2027-09. This is a cost
  obligation triggered by monetisation, not a licence restriction on the product.

## Samples only — not redistributed

`samples/` contains paired .NET Framework 4.8 / .NET 10 fixture solutions, isolated from the
root build by `samples/Directory.Build.props`.

| Component | Version | Licence |
|---|---|---|
| Microsoft.AspNet.WebApi.Core | 5.3.0 | Microsoft .NET Library EULA (`requireLicenseAcceptance=true`) |
| Microsoft.AspNetCore.Mvc.Core | 2.3.13 | Apache-2.0 |

`Microsoft.AspNet.WebApi.Core` is the only non-OSI licence in the repository. It is
referenced by a test fixture and is not redistributed. **Do not vendor these assemblies into
the container image or any release artifact.**
