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
