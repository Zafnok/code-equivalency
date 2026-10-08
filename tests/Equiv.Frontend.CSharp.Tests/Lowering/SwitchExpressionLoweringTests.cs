using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// The throw the compiler adds to a <c>switch</c> expression for a value no arm matches (ticket P2-144; ADR 0024 as
/// clarified by it). It constructs <c>SwitchExpressionException</c> where the reference assemblies have the type and
/// <c>InvalidOperationException</c> where they do not, so the constructor is a runtime-changed callee on a pair that
/// crosses .NET Core 3.0. An expression with an arm that always matches has no such throw in its lowered body.
/// </summary>
public sealed class SwitchExpressionLoweringTests
{
    private const string NoMatchType = "System.Runtime.CompilerServices.SwitchExpressionException";

    private const string NoMatch = NoMatchType + "::.ctor()";

    private const string Open = "static int M(int a) => a switch { 1 => 10, 2 => 20 };";

    [Fact]
    public void AnExpressionThatMayMatchNoArmConstructsAndThrowsTheNoMatchException()
    {
        IrProcedure procedure = Method(Open);

        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal(NoMatch, call.Callee.Value);
        Assert.True(call.Callee.RuntimeChanged);
        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: NoMatchType });
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>The type first shipped in .NET Core 3.0, so the row applies only to a pair that crosses it (ADR 0040 decision 2).</summary>
    [Theory]
    [InlineData("net48", "net10.0", true)]
    [InlineData("netcoreapp2.1", "netcoreapp3.0", true)]
    [InlineData("netcoreapp3.1", "net10.0", false)]
    [InlineData("net10.0", "net10.0", false)]
    public void TheConstructorIsRuntimeChangedOnlyAcrossNetCore3(string legacy, string modern, bool expected)
    {
        IrProcedure procedure = Source($"class C {{ {Open} }}", runtime: Runtimes.Between(legacy, modern));

        Assert.Equal(expected, Assert.Single(Calls(procedure)).Callee.RuntimeChanged);
    }

    [Fact]
    public void ASuppressedRowLeavesTheConstructorUnflagged()
    {
        IrProcedure procedure = Source($"class C {{ {Open} }}", suppressedRuntimeChanges: [NoMatchType + "::.ctor("]);

        Assert.False(Assert.Single(Calls(procedure)).Callee.RuntimeChanged);
    }

    /// <summary>Where the reference assemblies have no <c>SwitchExpressionException</c>, the same text constructs and throws another type.</summary>
    [Fact]
    public void WithoutTheTypeTheSameTextThrowsInvalidOperationException()
    {
        IrProcedure procedure = WithoutTheNoMatchType(Open);

        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal("System.InvalidOperationException::.ctor()", call.Callee.Value);
        Assert.False(call.Callee.RuntimeChanged);
        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.InvalidOperationException" });
    }

    /// <summary>Criterion 4: a discard arm always matches, so the body has no throw, no constructor call and no test of the arm.</summary>
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(7, 0)]
    public void AnExpressionWithADiscardArmHasNoThrow(int input, int expected)
    {
        IrProcedure procedure = Method("static int M(int a) => a switch { 1 => 10, 2 => 20, _ => 0 };");

        Assert.Empty(Calls(procedure));
        Assert.Empty(Opaques(procedure));
        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow or IrBranch);
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, input)));
    }

    /// <summary>
    /// A <c>var</c> arm always matches too. Its pattern is still evaluated, since it binds a variable, and stays the opaque
    /// it was (<c>switch-pattern</c>); only the throw behind it is gone.
    /// </summary>
    [Fact]
    public void AnExpressionWithAVarArmHasNoThrow()
    {
        IrProcedure procedure = Method("static int M(int a) => a switch { 1 => 10, var b => b };");

        Assert.Empty(Calls(procedure));
        Assert.NotEmpty(Opaques(procedure));
        Assert.All(Opaques(procedure), static o => Assert.Equal("switch-pattern", o.Reason));
        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow);
    }

    /// <summary>
    /// Arms that leave no value out cover every value with no discard: <c>true</c> and <c>false</c> are one switch whose
    /// last case takes every other value, and with <c>null</c> for a nullable the last test is a jump to its arm.
    /// </summary>
    [Theory]
    [InlineData("static int M(bool b) => b switch { true => 1, false => 2 };")]
    [InlineData("static int M(bool? b) => b switch { true => 1, false => 2, null => 3 };")]
    public void AnExpressionWhoseArmsCoverEveryValueHasNoThrow(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Empty(Calls(procedure));
        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void TrueAndFalseAreOneSwitchWithNoFallOut(bool input, int expected)
    {
        IrProcedure procedure = Method("static int M(bool b) => b switch { true => 1, false => 2 };");

        Assert.Single(Assert.IsType<IrSwitch>(Assert.Single(procedure.Blocks, static b => b.Terminator is IrSwitch).Terminator).Cases);
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, new IrBoolValue(input)));
    }

    /// <summary>An arm with a <c>when</c> clause does not always match, whatever its pattern: the throw stays.</summary>
    [Theory]
    [InlineData("static int M(int a) => a switch { 1 => 10, _ when a > 5 => 0 };")]
    [InlineData("static int M(int a) => a switch { 1 => 10, var b when b > 5 => 0 };")]
    public void AnArmWithAWhenClauseKeepsTheThrow(string members) =>
        Assert.Equal(NoMatch, Assert.Single(Calls(Method(members))).Callee.Value);

    /// <summary>A <c>finally</c> is lowered as a copy on each path that runs it; the copy leaves the throw out as the main pass does.</summary>
    [Fact]
    public void AnExpressionWithADiscardArmInAFinallyHasNoThrow()
    {
        IrProcedure procedure = Method("static int M(int a) { int r = 0; try { r = a; } finally { r = a switch { 1 => 10, _ => r }; } return r; }");

        Assert.Empty(Calls(procedure));
        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow);
        Assert.Equal(new IrReturned(Bits(32, 10)), Run(procedure, Bits(32, 1)));
        Assert.Equal(new IrReturned(Bits(32, 4)), Run(procedure, Bits(32, 4)));
    }

    /// <summary>An <c>is</c> test outside a <c>switch</c> expression is lowered as before, whatever its pattern.</summary>
    [Fact]
    public void AVarPatternOutsideASwitchExpressionIsStillATest()
    {
        IrProcedure procedure = Method("static int M(int a) { if (a is var b) { return b; } return 0; }");

        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrBranch);
    }

    [Fact]
    public void TheNoMatchConstructorIsTheOneTheCompilationHas()
    {
        Compilation modern = RoslynTestCompilations.Compile("class C { }");
        Compilation legacy = Bare("namespace System { public class InvalidOperationException { public InvalidOperationException(string message) { } public InvalidOperationException() { } } }");

        Assert.Equal(NoMatchType, SwitchExpressions.NoMatchConstructor(modern)!.ContainingType.ToDisplayString());
        Assert.Empty(SwitchExpressions.NoMatchConstructor(modern)!.Parameters);
        Assert.Equal("System.InvalidOperationException", SwitchExpressions.NoMatchConstructor(legacy)!.ContainingType.ToDisplayString());
        Assert.Empty(SwitchExpressions.NoMatchConstructor(legacy)!.Parameters);
        Assert.Null(SwitchExpressions.NoMatchConstructor(Bare("class C { }")));
    }

    /// <summary>
    /// Lowers <c>M</c> against reference assemblies that have no <c>SwitchExpressionException</c>, as .NET Framework's have
    /// none: a library that declares the few types the snippet needs, <c>InvalidOperationException</c> among them.
    /// </summary>
    private static IrProcedure WithoutTheNoMatchType(string members)
    {
        const string Library = """
            namespace System
            {
                public class Object { public Object() { } }
                public abstract class ValueType { }
                public abstract class Enum : ValueType { }
                public struct Void { }
                public struct Int32 { }
                public struct Boolean { }
                public class String { }
                public class Attribute { }
                public class Exception { public Exception() { } }
                public class InvalidOperationException : Exception { public InvalidOperationException() { } }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(Library + $"\nclass C {{ {members} }}", cancellationToken: TestContext.Current.CancellationToken)],
            [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = IrLowerer.Lower(method, compilation, Equiv.Core.Configuration.RenameMap.Empty, [], Runtimes.Migration);
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    private static CSharpCompilation Bare(string source) =>
        CSharpCompilation.Create("Bare", [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)]);
}
