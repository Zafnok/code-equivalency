using System.Reflection;

using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using DomainArchitecture = ArchUnitNET.Domain.Architecture;

namespace Equiv.Tests.Architecture;

public sealed class DependencyRuleTests
{
    private static readonly DomainArchitecture SystemArchitecture = new ArchLoader()
        .LoadAssemblies(
            Assembly.Load("Equiv.Core"),
            Assembly.Load("Equiv.Frontend.CSharp"),
            Assembly.Load("Equiv.Verify.Z3"),
            Assembly.Load("Equiv.Cli"))
        .Build();

    [Fact]
    public void CoreDoesNotDependOnOtherEquivComponents()
    {
        IArchRule rule = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Core(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^(Equiv\.Frontend|Equiv\.Verify|Equiv\.Cli)(\.|$)"))
            .Because("Equiv.Core is the shared contract; ARCHITECTURE.md forbids it depending on the frontend, backend, or CLI.");

        rule.Check(SystemArchitecture);
    }

    [Fact]
    public void CoreDoesNotDependOnRoslynOrZ3()
    {
        // Excludes Microsoft.CodeAnalysis.Sarif (Sarif.Sdk, an approved Equiv.Core dependency)
        // from the Roslyn namespace prefix it happens to share; see ADR 0010.
        IArchRule rule = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Core(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(
                @"^Microsoft\.CodeAnalysis$|^Microsoft\.CodeAnalysis\.(?!Sarif(\.|$))|^Microsoft\.Z3(\.|$)"))
            .Because("Equiv.Core must have no Roslyn or Z3 dependency; those are frontend/backend concerns.");

        rule.Check(SystemArchitecture);
    }

    [Fact]
    public void FrontendDoesNotDependOnVerify()
    {
        IArchRule rule = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Frontend\.CSharp(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Verify\.Z3(\.|$)"))
            .Because("Only Equiv.Cli is allowed to depend on both the frontend and the backend.");

        rule.Check(SystemArchitecture);
    }

    [Fact]
    public void VerifyDoesNotDependOnFrontend()
    {
        IArchRule rule = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Verify\.Z3(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Frontend\.CSharp(\.|$)"))
            .Because("Only Equiv.Cli is allowed to depend on both the frontend and the backend.");

        rule.Check(SystemArchitecture);
    }

    [Fact]
    public void IrPrefixedTypesResideInIrNamespace()
    {
        IArchRule rule = Types().That().ResideInNamespaceMatching(@"^Equiv\.Core(\.|$)").And().HaveNameStartingWith("Ir")
            .Should().ResideInNamespace("Equiv.Core.Ir")
            .Because("IR types are namespaced under Equiv.Core.Ir (see CLAUDE.md).")
            .WithoutRequiringPositiveResults();

        rule.Check(SystemArchitecture);
    }

    [Fact]
    public void TypesInIrNamespaceArePrefixedIr()
    {
        IArchRule rule = Types().That().ResideInNamespace("Equiv.Core.Ir")
            .Should().HaveNameStartingWith("Ir")
            .Because("Types in Equiv.Core.Ir are IR nodes and must be prefixed Ir (see CLAUDE.md).")
            .WithoutRequiringPositiveResults();

        rule.Check(SystemArchitecture);
    }
}
