using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;

using Microsoft.CodeAnalysis;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// ADR 0042 (ticket P2-069): the lowering records every call it emits at a syntax node, and lowers a call to an identity it
/// is told is rebound as an opaque with reason <c>rebound-call</c>, in both lowerings. The two sides here are one source
/// whose library types sit in another namespace, so every call site has the same text and binds to another callee.
/// </summary>
public sealed class ReboundCallLoweringTests
{
    private const string Run = "Old.Thing::Run(int)";
    private const string Read = "Old.Thing::Read(string,out int)";

    /// <summary>Every kind of call site is recorded under its member's name: a constructor, both accessors, an event's, a method and an await.</summary>
    [Fact]
    public void EveryCallTheLoweringEmitsIsACallSite()
    {
        CallSites legacy = new();
        CallSites modern = new();
        Lower("Old", "M", legacy);
        Lower("New", "M", modern);

        ImmutableArray<ReboundCall> rebound = CallSites.Rebound(legacy, modern, []);

        Assert.Equal(
            [
                "Old.Thing::.ctor(int)",
                "Old.Thing::Later()",
                Run,
                "Old.Thing::add_Changed(System.Action)",
                "Old.Thing::get_Size()",
                "Old.Thing::set_Size(int)",
                "await:System.Runtime.CompilerServices.TaskAwaiter`1<Old.Thing>",
            ],
            rebound.Select(static r => r.Legacy),
            StringComparer.Ordinal);
        Assert.All(rebound, static r => Assert.Equal(r.Legacy.Replace("Old.", "New.", StringComparison.Ordinal), r.Modern));
    }

    /// <summary>A body lowered with no rebound identity is what it was before: its sites are recorded, and nothing else changes.</summary>
    [Fact]
    public void RecordingCallSitesChangesNoBody() =>
        Assert.Equal(IrText.Dump(Lower("Old", "Run", new CallSites())), IrText.Dump(Lower("Old", "Run", sites: null)));

    /// <summary>
    /// The rebound call is one opaque where the call would be: after the receiver's null check, with the call's span, no
    /// fingerprint to share and no <c>threw</c> edge. Its value is what the method returns.
    /// </summary>
    [Fact]
    public Task AReboundCallIsAnOpaqueAtTheCall()
    {
        IrProcedure procedure = Lower("Old", "Run", new CallSites([Run]));

        IrOpaque opaque = Assert.Single(Lowered.Opaques(procedure));
        Assert.Equal(ReboundCall.OpaqueReason, opaque.Reason);
        Assert.Null(opaque.Fingerprint);
        Assert.Null(opaque.Threw);
        Assert.False(opaque.WholeBody);
        Assert.Equal(new IrBitVec(32), opaque.Target!.Type);
        Assert.Equal(opaque.Span.StartColumn + "thing.Run(3)".Length, opaque.Span.EndColumn);
        Assert.Empty(Lowered.Calls(procedure));
        Assert.Equal(["System.NullReferenceException"], procedure.Blocks.Select(static b => b.Terminator).OfType<IrThrow>().Select(static t => t.ExceptionType), StringComparer.Ordinal);
        Assert.Equal(opaque.Target, Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrReturn>()).Value);
        return Verify(IrText.Dump(procedure));
    }

    /// <summary>Only the calls to a rebound identity are opaque; every other call of the body is still a call.</summary>
    [Fact]
    public void ACallToAnotherIdentityIsStillACall()
    {
        IrProcedure procedure = Lower("Old", "M", new CallSites([Run]));

        Assert.Equal(ReboundCall.OpaqueReason, Assert.Single(Lowered.Opaques(procedure)).Reason);
        Assert.DoesNotContain(Lowered.Calls(procedure), static c => string.Equals(c.Callee.Value, Run, StringComparison.Ordinal));
        Assert.Contains(Lowered.Calls(procedure), static c => string.Equals(c.Callee.Value, "Old.Thing::get_Size()", StringComparison.Ordinal));
    }

    /// <summary>A rebound call's <c>out</c> argument is unknown too: a second opaque, whose value the variable holds afterwards.</summary>
    [Fact]
    public void AReboundCallsOutArgumentIsAnOpaqueToo()
    {
        IrProcedure procedure = Lower("Old", "Read", new CallSites([Read]));

        ImmutableArray<IrOpaque> opaques = Lowered.Opaques(procedure);
        Assert.Equal(2, opaques.Length);
        Assert.All(opaques, static o => Assert.Equal(ReboundCall.OpaqueReason, o.Reason));
        Assert.Equal([new IrBool(), new IrBitVec(32)], opaques.Select(static o => o.Target!.Type));
        Assert.Equal(opaques[1].Target, Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrReturn>()).Value);
        Assert.Empty(Lowered.Calls(procedure));
    }

    /// <summary>
    /// The IL lowering makes the same call the same opaques (ADR 0039): no fingerprint, no call, one more per <c>out</c>
    /// argument, and one that defines nothing for a call with no result.
    /// </summary>
    [Theory]
    [InlineData("Run", Run, 1)]
    [InlineData("Read", Read, 2)]
    [InlineData("Fire", "Old.Thing::Reset()", 1)]
    public void TheIlLoweringMakesAReboundCallTheSameOpaques(string name, string identity, int opaques)
    {
        (IMethodSymbol method, Compilation compilation) = Method("Old", name);

        IrProcedure rebound = IlLowerer.Lower(method, compilation, Runtimes.Migration, new CallSites([identity]));
        IrProcedure plain = IlLowerer.Lower(method, compilation, Runtimes.Migration);

        Assert.Empty(IrValidator.Validate(rebound));
        Assert.Equal(opaques, Lowered.Opaques(rebound).Length);
        Assert.All(Lowered.Opaques(rebound), static o =>
        {
            Assert.Equal(ReboundCall.OpaqueReason, o.Reason);
            Assert.Null(o.Fingerprint);
            Assert.Null(o.Threw);
        });
        Assert.Empty(Lowered.Calls(rebound));
        Assert.Equal(Lowered.Opaques(Lower("Old", name, new CallSites([identity]))).Select(static o => o.Target?.Type), Lowered.Opaques(rebound).Select(static o => o.Target?.Type));
        Assert.Equal(identity, Assert.Single(Lowered.Calls(plain)).Callee.Value);
        Assert.Empty(Lowered.Opaques(plain));
    }

    private static IrProcedure Lower(string library, string name, CallSites? sites)
    {
        (IMethodSymbol method, Compilation compilation) = Method(library, name);
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], [], Runtimes.Migration, sites).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    private static (IMethodSymbol Method, Compilation Compilation) Method(string library, string name)
    {
        Compilation compilation = RoslynTestCompilations.Compile(
            $$"""
            using System.Threading.Tasks;
            using {{library}};
            namespace {{library}}
            {
                public class Thing
                {
                    public Thing(int size) { }
                    public int Size { get => 0; set { } }
                    public event System.Action Changed { add { } remove { } }
                    public int Run(int x) => x;
                    public bool Read(string p, out int size) { size = 0; return true; }
                    public void Reset() { }
                    public Task<Thing> Later() => Task.FromResult(this);
                }
            }
            class C
            {
                static async Task<int> M(Thing given, System.Action handler)
                {
                    Thing made = new Thing(1);
                    made.Size = given.Size;
                    made.Changed += handler;
                    await given.Later();
                    return made.Run(2);
                }
                static int Read(Thing thing, string p) { thing.Read(p, out int size); return size; }
                static int Run(Thing thing) => thing.Run(3);
                static void Fire(Thing thing) { thing.Reset(); }
            }
            """);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return (compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation);
    }
}
