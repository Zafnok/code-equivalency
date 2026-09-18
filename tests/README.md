# tests/

Mirror of `src/`: `Equiv.Core.Tests`, `Equiv.Frontend.CSharp.Tests`, `Equiv.Verify.Z3.Tests`,
`Equiv.Cli.Tests`, plus `Equiv.Tests.Integration` (runs the CLI end to end on `samples/`)
and `Equiv.Tests.Architecture` (ArchUnitNET rules).

Test kinds and when each is required are in [docs/QUALITY-GATES.md](../docs/QUALITY-GATES.md).
Every test project uses xUnit v3 on Microsoft.Testing.Platform, coverlet.MTP, Verify, CsCheck.
