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
            Assembly.Load("Equiv.Verify.Cvc5"),
            Assembly.Load("Equiv.Cli"),
            Assembly.Load("Equiv.Execute"))
        .Build();

    [Fact]
    public void CoreDoesNotDependOnOtherEquivComponents()
    {
        IArchRule rule = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Core(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^(Equiv\.Frontend|Equiv\.Verify|Equiv\.Cli|Equiv\.Execute)(\.|$)"))
            .Because("Equiv.Core is the shared contract; ARCHITECTURE.md forbids it depending on the frontend, backend, runner, or CLI.");

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
    public void Execute_ReferencesOnlyCore()
    {
        Assert.Equal(
            ["Equiv.Core"],
            Assembly.Load("Equiv.Execute").GetReferencedAssemblies().Select(static a => a.Name!).Where(static n => n.StartsWith("Equiv.", StringComparison.Ordinal)),
            StringComparer.Ordinal);

        IArchRule rule = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Execute(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(
                @"^(Equiv\.Frontend|Equiv\.Verify|Equiv\.Cli)(\.|$)|^Microsoft\.CodeAnalysis(\.|$)|^Microsoft\.Z3(\.|$)"))
            .Because("ADR 0035: Equiv.Execute runs drivers the frontend compiled and depends on Equiv.Core only.");

        rule.Check(SystemArchitecture);
    }

    /// <summary>
    /// ADR 0050 decision 1 (ticket P1-033): the second solver's contract is in <c>Equiv.Core</c>, cvc5 implements it
    /// from <c>Equiv.Core</c> alone, and the backend that prints the query does not know which solver answers it.
    /// </summary>
    [Fact]
    public void Cvc5_ReferencesOnlyCore_AndTheZ3BackendDoesNotReferenceIt()
    {
        Assert.Equal("Equiv.Core", typeof(Equiv.Core.ISmtSolver).Assembly.GetName().Name);
        Assert.Equal(
            ["Equiv.Core"],
            Assembly.Load("Equiv.Verify.Cvc5").GetReferencedAssemblies().Select(static a => a.Name!).Where(static n => n.StartsWith("Equiv.", StringComparison.Ordinal)),
            StringComparer.Ordinal);
        Assert.DoesNotContain("Equiv.Verify.Cvc5", Assembly.Load("Equiv.Verify.Z3").GetReferencedAssemblies().Select(static a => a.Name), StringComparer.Ordinal);

        IArchRule cvc5 = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Verify\.Cvc5(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(
                @"^(Equiv\.Frontend|Equiv\.Verify\.Z3|Equiv\.Cli|Equiv\.Execute)(\.|$)|^Microsoft\.CodeAnalysis(\.|$)|^Microsoft\.Z3(\.|$)"))
            .Because("ADR 0050: Equiv.Verify.Cvc5 runs a solver process behind Equiv.Core's ISmtSolver and depends on Equiv.Core only.");
        IArchRule z3 = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Verify\.Z3(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Equiv\.Verify\.Cvc5(\.|$)"))
            .Because("ADR 0050: Equiv.Verify.Z3 prints the query and reads the answer through ISmtSolver; it does not know which solver is behind it.");

        cvc5.Check(SystemArchitecture);
        z3.Check(SystemArchitecture);
    }

    /// <summary>
    /// ADR 0039 (ticket P1-014): the IL fallback lives in the C# frontend, which alone references the decompiler, so the
    /// identity and sort code it shares with the IOperation lowering is a call and not a contract between projects.
    /// </summary>
    [Fact]
    public void OnlyTheCSharpFrontendReferencesTheDecompiler()
    {
        Assert.Contains("ICSharpCode.Decompiler", Assembly.Load("Equiv.Frontend.CSharp").GetReferencedAssemblies().Select(static a => a.Name), StringComparer.Ordinal);
        foreach (string other in (string[])["Equiv.Core", "Equiv.Verify.Z3", "Equiv.Cli", "Equiv.Execute"])
        {
            Assert.DoesNotContain("ICSharpCode.Decompiler", Assembly.Load(other).GetReferencedAssemblies().Select(static a => a.Name), StringComparer.Ordinal);
        }

        IArchRule rule = Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^(Equiv\.Core|Equiv\.Verify|Equiv\.Cli|Equiv\.Execute)(\.|$)")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^ICSharpCode\.Decompiler(\.|$)"))
            .Because("ADR 0039: only Equiv.Frontend.CSharp reads IL back as ILAst.");

        rule.Check(SystemArchitecture);
    }

    [Fact]
    public void ExecutionContractStartsNoProcesses()
    {
        IArchRule rule = Types(includeReferenced: true).That().ResideInNamespace("Equiv.Core.Execution")
            .Should().NotDependOnAny(Types(includeReferenced: true).That().ResideInNamespace("System.Diagnostics"))
            .Because("ADR 0035: the execution contract is records only; Equiv.Execute owns the processes (ticket M3-032).");

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
