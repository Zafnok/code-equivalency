using Equiv.Core.Ir;
using Equiv.Core.RuntimeChanges;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Tests.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Fingerprinting;

/// <summary>
/// A fragment both sides of a pair hold is shared only if both fingerprint it alike (ADR 0024 decision 2), so both sides
/// decide its runtime sensitivity inside the one interval the pair has (ADR 0040 decision 2; ticket P2-055).
/// </summary>
public sealed class FragmentFingerprinterTests
{
    /// <summary>
    /// A method group on an evaluated receiver, which the lowerer leaves an opaque fragment (ticket P2-067), of
    /// <c>String.IndexOf</c>, whose behaviour changed in .NET 5.
    /// </summary>
    private const string Source = "using System;\nclass C { Func<char, int> M(string s) => s.IndexOf; }";

    [Theory]
    [InlineData("net8.0", "net10.0", true)]
    [InlineData("net10.0", "net10.0", true)]
    [InlineData("net10.0", "net8.0", true)]
    [InlineData("net48", "net10.0", false)]
    [InlineData("netcoreapp3.1", "net5.0", false)]
    public void SharedFragmentUsesOneInterval(string legacyRuntime, string modernRuntime, bool shared)
    {
        Compilation compilation = RoslynTestCompilations.Compile(Source);
        (SideRuntime legacy, SideRuntime modern) =
            SideRuntime.Of(SideRuntimeTests.On(legacyRuntime), compilation, SideRuntimeTests.On(modernRuntime), compilation, RuntimeChangeTable.Load());

        IrOpaque legacyFragment = Assert.Single(Lowered.Opaques(Lowered.Source(Source, runtime: legacy)));
        IrOpaque modernFragment = Assert.Single(Lowered.Opaques(Lowered.Source(Source, runtime: modern)));

        Assert.Same(legacy.Interval, modern.Interval);
        Assert.Equal(shared, legacyFragment.Fingerprint is not null);
        Assert.Equal(legacyFragment.Fingerprint, modernFragment.Fingerprint);
    }

    /// <summary>
    /// Ticket P2-145: a lambda that declares an <c>extern</c> local function is named by a fingerprint that holds the
    /// function's attributes, so two lambdas that import from different libraries are not one shared function.
    /// </summary>
    [Fact]
    public void ALambdasLocalExternFunctionsAttributesAreInItsFingerprint()
    {
        static string Delegate(string library) => Assert.Single(
            Lowered.Source($"using System;\nclass C {{ Func<int> M() => () => {{ return F(); [System.Runtime.InteropServices.DllImport(\"{library}\")] static extern int F(); }}; }}")
                .Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>().Select(static p => p.Function),
            static f => f.StartsWith("delegate:", StringComparison.Ordinal));

        Assert.Equal(Delegate("a.dll"), Delegate("a.dll"), StringComparer.Ordinal);
        Assert.NotEqual(Delegate("a.dll"), Delegate("b.dll"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-149: <c>sizeof</c> of a user-defined struct is an opaque fragment, shared when its fingerprint is on both
    /// sides. The fingerprint holds the struct's declaration, so two sides whose structs differ do not share it.
    /// </summary>
    [Fact]
    public void TheDeclarationUnderASizeOfIsInItsFragmentsFingerprint()
    {
        static string? SizeOf(string fields) =>
            Assert.Single(Lowered.Opaques(Lowered.Source($"struct S {{ {fields} }} class C {{ unsafe int M() => sizeof(S); }}"))).Fingerprint;

        Assert.NotNull(SizeOf("byte A; int B;"));
        Assert.Equal(SizeOf("byte A; int B;"), SizeOf("byte A; int B;"), StringComparer.Ordinal);
        Assert.NotEqual(SizeOf("byte A; int B;"), SizeOf("byte A; int B; int C;"), StringComparer.Ordinal);
    }

    /// <summary>The pitfall the one interval avoids: sides lowered with different intervals do not share the fragment.</summary>
    [Fact]
    public void SidesWithDifferentIntervalsDoNotShareTheFragment()
    {
        IrOpaque inside = Assert.Single(Lowered.Opaques(Lowered.Source(Source, runtime: Runtimes.Between("net48", "net10.0"))));
        IrOpaque outside = Assert.Single(Lowered.Opaques(Lowered.Source(Source, runtime: Runtimes.Between("net8.0", "net10.0"))));

        Assert.Null(inside.Fingerprint);
        Assert.NotNull(outside.Fingerprint);
    }
}
