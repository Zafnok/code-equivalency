using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// ADR 0047 (ticket P2-068): a call to a forwarder is lowered as the same call to its target, by both lowerings and in the
/// bound fingerprint, and the lowering says which forwarder it resolved. The ticket's pair is the fixture: <c>Legacy</c>
/// calls <c>Text.Blank</c>, whose body is <c>string.IsNullOrWhiteSpace</c>, and <c>Modern</c> calls that directly.
/// </summary>
public sealed class ForwarderLoweringTests
{
    private const string Blank = "Text::Blank(string)";
    private const string IsNullOrWhiteSpace = "System.String::IsNullOrWhiteSpace(string)";

    private const string Source = """
        using System;
        public static class Text
        {
            public static bool Blank(string s) => string.IsNullOrWhiteSpace(s);
            public static bool Trimmed(string s) => string.IsNullOrWhiteSpace(s?.Trim());
            public static bool Same(string a, string b, StringComparison c) => string.Equals(a, b, c);
            public static bool Old(string s) => T.Old(s);
            public static int Size(string s) => T.Count(s);
            public static bool IsBlank(this string s) => string.IsNullOrWhiteSpace(s);
        }
        public static class T
        {
            public static bool Old(string s) => s is null;
            public static int Count(string s) => 0;
        }
        class C
        {
            static string Legacy(string s) => Text.Blank(s) ? "none" : s;
            static string Modern(string s) => string.IsNullOrWhiteSpace(s) ? "none" : s;
            static string Changed(string s) => Text.Trimmed(s) ? "none" : s;
            static bool Compared(string a, string b) => Text.Same(a, b, StringComparison.CurrentCulture);
            static bool Ordered(string a, string b) => Text.Same(a, b, StringComparison.Ordinal);
            static bool Catalogued(string s) => Text.Old(s);
            static Func<string, bool> Group() => Text.Blank;
            static int? Lifted(int? a, string s) => a + Text.Size(s);
            static bool Extension(string s) => s.IsBlank();
        }
        """;

    /// <summary>The ticket's pair lowers to one body: the forwarder's call is the target's, closed as a call to a string member is.</summary>
    [Fact]
    public void ACallToAForwarderIsTheCallToItsTarget()
    {
        CallSites sites = new();

        IrProcedure legacy = Lower("Legacy", sites);

        IrCall call = Assert.Single(Lowered.Calls(legacy));
        Assert.Equal(new CallIdentity(IsNullOrWhiteSpace), call.Callee);
        Assert.True(call.Closed);
        Assert.Equal(Calls(Lower("Modern", new CallSites())), Calls(legacy));
        Assert.Equal([new ResolvedForwarder(Blank, IsNullOrWhiteSpace)], CallSites.Forwarders(sites, new CallSites()));
    }

    /// <summary>
    /// An extension method called on its receiver is the same static call, with no null check of the receiver, so a
    /// forwarding extension resolves like any other forwarder, in both lowerings.
    /// </summary>
    [Fact]
    public void AForwardingExtensionCalledOnItsReceiverIsResolved()
    {
        (IMethodSymbol method, Compilation compilation) = Method("Extension");
        CallSites sites = new();

        IrProcedure operation = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], [], Runtimes.Migration, sites).Body;
        IrProcedure il = IlLowerer.Lower(method, compilation, Runtimes.Migration);

        Assert.Equal(IsNullOrWhiteSpace, Assert.Single(Lowered.Calls(operation)).Callee.Value);
        Assert.Equal(IsNullOrWhiteSpace, Assert.Single(Lowered.Calls(il)).Callee.Value);
        Assert.DoesNotContain(operation.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
        Assert.Equal([new ResolvedForwarder("Text::IsBlank(string)", IsNullOrWhiteSpace)], CallSites.Forwarders(sites, sites));
    }

    /// <summary>Ticket P2-068 criterion 3: a helper that changes its argument is called as itself, and nothing is recorded.</summary>
    [Fact]
    public void AHelperThatChangesItsArgumentIsStillItsOwnCall()
    {
        CallSites sites = new();

        IrCall call = Assert.Single(Lowered.Calls(Lower("Changed", sites)));

        Assert.Equal("Text::Trimmed(string)", call.Callee.Value);
        Assert.False(call.Closed);
        Assert.Empty(CallSites.Forwarders(sites, sites));
    }

    /// <summary>The IL lowering makes the same call (ADR 0039), and records the same forwarder.</summary>
    [Fact]
    public void TheIlLoweringResolvesTheSameForwarder()
    {
        (IMethodSymbol method, Compilation compilation) = Method("Legacy");
        CallSites sites = new();

        IrProcedure legacy = IlLowerer.Lower(method, compilation, Runtimes.Migration, sites);

        Assert.Empty(IrValidator.Validate(legacy));
        IrCall call = Assert.Single(Lowered.Calls(legacy));
        Assert.Equal((new CallIdentity(IsNullOrWhiteSpace), true), (call.Callee, call.Closed));
        Assert.Equal([new ResolvedForwarder(Blank, IsNullOrWhiteSpace)], CallSites.Forwarders(new CallSites(), sites));
        (IMethodSymbol changed, _) = Method("Changed");
        IrCall kept = Assert.Single(Lowered.Calls(IlLowerer.Lower(changed, compilation, Runtimes.Migration, sites)), static c => c.Callee.Value.StartsWith("Text::", StringComparison.Ordinal));
        Assert.Equal(("Text::Trimmed(string)", false), (kept.Callee.Value, kept.Closed));
        Assert.Single(CallSites.Forwarders(sites, sites));
    }

    /// <summary>The bound fingerprint spells the call as the lowering makes it, so the ticket's pair is congruent and the changed helper is not.</summary>
    [Fact]
    public void TheFingerprintSpellsTheTarget()
    {
        Assert.Equal(Fingerprint("Modern"), Fingerprint("Legacy"), StringComparer.Ordinal);
        Assert.NotEqual(Fingerprint("Modern"), Fingerprint("Changed"), StringComparer.Ordinal);
    }

    /// <summary>A method group is not a call: a delegate to the forwarder is not a delegate to its target.</summary>
    [Fact]
    public void AMethodGroupKeepsTheForwardersName()
    {
        (IMethodSymbol method, Compilation compilation) = Method("Group");
        IOperation body = compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree).GetOperation(method.DeclaringSyntaxReferences[0].GetSyntax(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)!;

        string text = BoundSerialiser.Serialise(method, compilation, [body], new BoundSerialiser.Settings(RenameMap.Empty, [], [], Runtimes.Migration)).Text;

        Assert.Contains(Blank, text, StringComparison.Ordinal);
        Assert.DoesNotContain(IsNullOrWhiteSpace, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A forwarder the sides of the pair do not agree on is kept (ADR 0047): both lowerings call it as itself and record
    /// nothing, and the fingerprint spells its name.
    /// </summary>
    [Fact]
    public void AKeptForwarderIsCalledAsItself()
    {
        (IMethodSymbol method, Compilation compilation) = Method("Legacy");
        CallSites sites = new() { KeptForwarders = [Blank] };

        IrCall operation = Assert.Single(Lowered.Calls(IrLowerer.Lower(method, compilation, RenameMap.Empty, [], [], Runtimes.Migration, sites).Body));
        IrCall il = Assert.Single(Lowered.Calls(IlLowerer.Lower(method, compilation, Runtimes.Migration, sites)));

        Assert.Equal((Blank, false), (operation.Callee.Value, operation.Closed));
        Assert.Equal((Blank, false), (il.Callee.Value, il.Closed));
        Assert.Empty(CallSites.Forwarders(sites, sites));
        string kept = BodyFingerprinter.Text(method, compilation, EquivConfig.Default, [], Runtimes.Migration, [Blank]).Text!;
        Assert.Contains(Blank, kept, StringComparison.Ordinal);
        Assert.DoesNotContain(IsNullOrWhiteSpace, kept, StringComparison.Ordinal);
        Assert.NotEqual(
            BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: true, Runtimes.Migration),
            BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: true, Runtimes.Migration, [Blank]));
        Assert.Equal(
            BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: true, Runtimes.Migration),
            BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: true, Runtimes.Migration, ["Text::Other(string)"]));
    }

    /// <summary>The fingerprint of an opaque fragment spells a forwarder call as the lowering of the body would make it, kept or resolved.</summary>
    [Fact]
    public void AFragmentsFingerprintFollowsTheBody()
    {
        (IMethodSymbol method, Compilation compilation) = Method("Lifted");

        string Fragment(CallSites sites) =>
            string.Join(' ', Lowered.Opaques(IrLowerer.Lower(method, compilation, RenameMap.Empty, [], [], Runtimes.Migration, sites).Body).Select(static o => Assert.IsType<string>(o.Fingerprint)));

        Assert.Equal(Fragment(new CallSites()), Fragment(new CallSites { KeptForwarders = ["Text::Other(string)"] }), StringComparer.Ordinal);
        Assert.NotEqual(Fragment(new CallSites()), Fragment(new CallSites { KeptForwarders = ["Text::Size(string)"] }), StringComparer.Ordinal);
    }

    /// <summary>The runtime-changes table reads the target: a forwarder to a member a row applies to is that runtime-changed call.</summary>
    [Fact]
    public void AForwarderToARuntimeChangedMemberIsRuntimeChanged()
    {
        (IMethodSymbol method, Compilation compilation) = Method("Compared");

        IrCall call = Assert.Single(Lowered.Calls(IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration)));

        Assert.Equal("System.String::Equals(string,string,System.StringComparison)", call.Callee.Value);
        Assert.True(call.Callee.RuntimeChanged);
        Assert.True(BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: true, Runtimes.Migration)!.RuntimeSensitive);
    }

    /// <summary>
    /// Ticket P2-075: an ordinal comparison handed to the forwarder is the forwarder's parameter at the target, which is not
    /// a constant there, so the call to the target stays runtime-changed.
    /// </summary>
    [Fact]
    public void AnOrdinalComparisonHandedToAForwarderDoesNotClearTheTargetsRow()
    {
        (IMethodSymbol method, Compilation compilation) = Method("Ordered");

        IrCall call = Assert.Single(Lowered.Calls(IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration)));

        Assert.Equal("System.String::Equals(string,string,System.StringComparison)", call.Callee.Value);
        Assert.True(call.Callee.RuntimeChanged);
        Assert.True(BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: true, Runtimes.Migration)!.RuntimeSensitive);
    }

    /// <summary>The catalogue reads the target too (ADR 0020): its entry rewrites the resolved call, and both rewrites are reported.</summary>
    [Fact]
    public void TheCatalogueAppliesToTheTarget()
    {
        (IMethodSymbol method, Compilation compilation) = Method("Catalogued");
        CallSites sites = new();
        ApiEquivalence entry = new("old-to-new", IsType: false, "T::Old(string)", "T::New(string)", [new ApiArgument(0)], "test", new Uri("https://example.test/"));

        (IrProcedure body, ImmutableArray<string> applied) = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], [entry], Runtimes.Migration, sites);

        Assert.Equal("T::New(string)", Assert.Single(Lowered.Calls(body)).Callee.Value);
        Assert.Equal(["old-to-new"], applied);
        Assert.Equal([new ResolvedForwarder("Text::Old(string)", "T::Old(string)")], CallSites.Forwarders(sites, new CallSites()));
    }

    /// <summary>
    /// A rebound site's key keeps the member name written at the site (ADR 0042), and its callee is the target. So two
    /// sides whose <c>Text.Blank</c> forward to the same member have nothing rebound, and a side whose <c>Text.Blank</c> is
    /// no forwarder is rebound against the other side's target.
    /// </summary>
    [Fact]
    public void AForwardersCallSiteKeepsItsNameAndBindsToTheTarget()
    {
        const string Caller = "class C { static bool M(string s) => Text.Blank(s); }";
        CallSites forwarding = Sites("public static class Text { public static bool Blank(string s) => string.IsNullOrWhiteSpace(s); }" + Caller);
        CallSites moved = Sites("public static class Text { public static bool Blank(string s) { return string.IsNullOrWhiteSpace(s); } }" + Caller);
        CallSites own = Sites("public static class Text { public static bool Blank(string s) => s is null; }" + Caller);

        Assert.Empty(CallSites.Rebound(forwarding, moved, []));
        Assert.Equal([new ReboundCall(IsNullOrWhiteSpace, Blank)], CallSites.Rebound(forwarding, own, []));
        Assert.Equal([new ResolvedForwarder(Blank, IsNullOrWhiteSpace)], CallSites.Forwarders(moved, own));
    }

    /// <summary>
    /// A forwarder in another project: its target's identity is read with that project's compilation, which is the one that
    /// references the target's assembly and so knows it is a reference assembly (ticket M3-033), in both lowerings.
    /// </summary>
    [Fact]
    public void AForwarderInAnotherProjectResolvesToItsProjectsTarget()
    {
        Compilation pack = RoslynTestCompilations.Compile(
            "[assembly: System.Runtime.CompilerServices.ReferenceAssembly] public static class R { public static bool F(string s) => s is null; }", "Pack");
        using MemoryStream image = new();
        Assert.True(pack.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
        Compilation helpers = RoslynTestCompilations.Compile(
            "public static class Text { public static bool Blank(string s) => R.F(s); }", [MetadataReference.CreateFromImage(image.ToArray())], "Helpers");
        Compilation app = RoslynTestCompilations.Compile("class C { static bool M(string s) => Text.Blank(s); }", [helpers.ToMetadataReference()], "App");
        IMethodSymbol method = app.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        IrCall operation = Assert.Single(Lowered.Calls(IrLowerer.Lower(method, app, RenameMap.Empty, [], Runtimes.Migration)));
        IrCall il = Assert.Single(Lowered.Calls(IlLowerer.Lower(method, app, Runtimes.Migration)));

        Assert.Equal(new CallIdentity("R::F(string)", External: true), operation.Callee);
        Assert.Equal(operation.Callee, il.Callee);
    }

    private static string Fingerprint(string name)
    {
        (IMethodSymbol method, Compilation compilation) = Method(name);
        return BodyFingerprinter.Compute(method, compilation, EquivConfig.Default, legacy: true, Runtimes.Migration)!.Sha256Hex;
    }

    /// <summary>The body's calls as text, which says each call's callee, arguments and whether it is closed.</summary>
    private static string[] Calls(IrProcedure procedure) => [.. Lowered.Calls(procedure).Select(static c => c.ToString())];

    private static CallSites Sites(string source)
    {
        Compilation compilation = RoslynTestCompilations.Compile(source);
        CallSites sites = new();
        _ = IrLowerer.Lower(compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single(), compilation, RenameMap.Empty, [], [], Runtimes.Migration, sites);
        return sites;
    }

    private static IrProcedure Lower(string name, CallSites sites)
    {
        (IMethodSymbol method, Compilation compilation) = Method(name);
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], [], Runtimes.Migration, sites).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    private static (IMethodSymbol Method, Compilation Compilation) Method(string name)
    {
        Compilation compilation = RoslynTestCompilations.Compile(Source);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return (compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation);
    }
}
