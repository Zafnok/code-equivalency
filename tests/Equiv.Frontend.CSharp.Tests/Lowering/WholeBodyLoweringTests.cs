using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.TestSupport;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket M4-008 (ADR 0029 decision 3): arrow-bodied and auto-property accessors, a bare <c>catch</c>, a <c>when</c> filter,
/// and constructors that run field initializers, are lowered instead of being one whole-body opaque.
/// </summary>
public sealed class WholeBodyLoweringTests
{
    private static readonly IrBitVec Int = new(32);

    /// <summary>Acceptance criterion 5: none of these constructs leaves a whole-body opaque, or an opaque with the old reasons.</summary>
    [Theory]
    [InlineData("int f; int M => f + 1;", "get_M")]
    [InlineData("int this[int i] => i * 2;", "get_Item")]
    [InlineData("int M { get; }", "get_M")]
    [InlineData("static int M { get; set; }", "set_M")]
    [InlineData("static int M(int n) { try { return 10 / n; } catch { return 0; } }", "M")]
    [InlineData("static int M(int n) { try { return 10 / n; } catch (Exception) when (n > 0) { return 0; } }", "M")]
    [InlineData("static int s = 1; static int t; static C() { t = s; }", ".cctor")]
    [InlineData("int f = 1; C() { }", ".ctor")]
    public void NoWholeBodyReasonLeftForTheseConstructs(string members, string name)
    {
        ImmutableArray<IrOpaque> opaques = Opaques(Method(members, name));

        Assert.DoesNotContain(opaques, static o => o.WholeBody);
        Assert.DoesNotContain(opaques, static o => o.Reason is "Block" or "no-body" or "catch-filter" or "field-initializer" or "ConstructorBodyOperation");
    }

    /// <summary>Acceptance criterion 1: the getter's graph is its expression's, lowered like a method body.</summary>
    [Fact]
    public void ArrowAccessorLowersThroughTheBlockGraph()
    {
        IrProcedure getter = Method("int f; int P => f + 1;", "get_P");

        Assert.Empty(Opaques(getter));
        Assert.Equal(new IrReturned(Bits(32, 5)), Run(getter, Fields("C", 4), This));
        Assert.Equal(new IrReturned(Bits(32, 6)), Run(Method("int this[int i] => i * 2;", "get_Item"), Bits(32, 3)));
    }

    /// <summary>Acceptance criterion 2: the getter reads the backing field's map, named for the property, at the receiver.</summary>
    [Fact]
    public void AutoPropertyGetReadsTheBackingFieldMap()
    {
        IrProcedure getter = Method("int P { get; }", "get_P");

        Assert.Equal(["field.C.P", "this"], getter.Parameters.Select(static p => p.Var.Name), StringComparer.Ordinal);
        Assert.Equal(new IrReturned(Bits(32, 7)), Run(getter, Fields("C", 7), This));
        Assert.Equal(new IrReturned(Bits(32, 9)), Run(Method("static int P { get; set; }", "get_P"), Fields("C", 9, Token)));
    }

    /// <summary>Acceptance criterion 2: the setter writes <c>value</c> to the map at the receiver, and the map is an out.</summary>
    [Fact]
    public void AutoPropertySetWritesTheBackingFieldMap()
    {
        IrProcedure setter = Method("int P { get; set; }", "set_P");

        IrRun run = IrInterpreter.Run(setter, new IrInputs([Bits(32, 3), Fields("C", 0), This]), IrGenOracle.Instance, IrGen.StepBudget);

        Assert.Equal(new IrReturned(Value: null), run.Outcome);
        Assert.Equal(Bits(32, 3), ((IrMapValue)Assert.Single(run.Outs)).Read(This));
    }

    /// <summary>An init accessor writes the map as a setter does.</summary>
    [Fact]
    public void AnInitAccessorWritesTheBackingFieldMap() =>
        Assert.Single(Method("int P { get; init; }", "set_P").Blocks.SelectMany(static b => b.Instructions).OfType<IrMapWrite>());

    /// <summary>A struct's <c>this</c> is not modelled, so its auto-property accessors are opaque there, not whole-body.</summary>
    [Theory]
    [InlineData("get_P")]
    [InlineData("set_P")]
    public void AStructsAutoPropertyAccessorIsOpaqueAtThis(string name)
    {
        IrOpaque opaque = Assert.Single(Opaques(Source("struct C { int P { get; set; } }", name)));

        Assert.Equal("InstanceReference", opaque.Reason);
        Assert.False(opaque.WholeBody);
    }

    /// <summary>
    /// Acceptance criterion 2, at the caller: an auto-property no override can replace is read and written as its backing
    /// field, with no call, so reading back what was written yields it.
    /// </summary>
    [Theory]
    [InlineData("int P { get; set; } static int M(C c, int v) { c.P = v; return c.P; }")]
    [InlineData("int P { get; set; } static int M(C c, int v) { c.P = v > 0 ? v : v; return c.P; }")]
    [InlineData("int P { get; set; } static int M(C c, int v) { c.P = 0; c.P += v; return c.P; }")]
    public void ACallerReadsAndWritesAnAutoPropertyAsItsBackingField(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Empty(Calls(procedure));
        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, 5)), Run(procedure, This, Bits(32, 5), Fields("C", 0), Nulls("C", 1, isNull: false)));
    }

    /// <summary>An auto-property an override may replace, or one with an accessor body, is still called.</summary>
    [Theory]
    [InlineData("class B { public virtual int P { get; set; } } class C : B { public override int P { get; set; } static int M(C c) => c.P; }")]
    [InlineData("class C { int P { get; set => field = value; } static int M(C c) => c.P; }")]
    [InlineData("class C { int f; int P { get { return f; } } static int M(C c) => c.P; }")]
    [InlineData("record R(int P); class C { static int M(R r) => r.P; }")]
    public void AnAutoPropertyThatMayBeReplacedOrHasABodyIsCalled(string source) =>
        Assert.EndsWith("::get_P()", Assert.Single(Calls(Source(source))).Callee.Value, StringComparison.Ordinal);

    /// <summary>The <c>field</c> keyword and a bodiless accessor of the same property name one map.</summary>
    [Fact]
    public void AFieldKeywordAccessorSharesTheAutoAccessorsMap()
    {
        const string source = "class C { int P { get; set => field = value; } }";

        Assert.Equal(
            Assert.Single(Source(source, "get_P").Parameters, static p => p.Var.Name.StartsWith("field.", StringComparison.Ordinal)).Var.Name,
            Assert.Single(Source(source, "set_P").Parameters, static p => p.Var.Name.StartsWith("field.", StringComparison.Ordinal)).Var.Name);
    }

    /// <summary>Acceptance criterion 3: a bare <c>catch</c> takes every thrown type, after the clauses written before it.</summary>
    [Theory]
    [InlineData(5, 2, 3)]
    [InlineData(5, 0, 1)]
    [InlineData(int.MaxValue, 1, 2)]
    public void BareCatchCatchesEverything(int a, int b, int expected)
    {
        IrProcedure procedure = Method("static int M(int a, int b) { try { return checked(a + 1) / b; } catch (DivideByZeroException) { return 1; } catch { return 2; } }");

        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a), Bits(32, b)));
    }

    /// <summary>Acceptance criterion 4: a filter that is false passes the exception to the next clause, or out of the method.</summary>
    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(-1, 0, 2)]
    [InlineData(4, 2, 2)]
    public void FalseFilterPassesTheExceptionOn(int a, int b, int expected)
    {
        IrProcedure procedure = Method("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException) when (a > 0) { return 1; } catch { return 2; } }");
        IrProcedure unguarded = Method("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException) when (a > 0) { return 1; } }");

        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a), Bits(32, b)));
        Assert.Equal(b == 0 && a <= 0 ? new IrThrew("System.DivideByZeroException") : new IrReturned(Bits(32, b == 0 ? 1 : a / b)), Run(unguarded, Bits(32, a), Bits(32, b)));
    }

    /// <summary>Acceptance criterion 4: a filter that throws counts as false, as .NET makes it.</summary>
    [Theory]
    [InlineData(5, 1)]
    [InlineData(0, 2)]
    [InlineData(-5, 2)]
    public void ThrowingFilterCountsAsFalse(int a, int expected)
    {
        IrProcedure procedure = Method("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException) when (10 / a > 0) { return 1; } catch { return 2; } }");

        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a), Bits(32, 0)));
    }

    /// <summary>
    /// .NET runs a filter in its first pass, before the finallys between the throw and the handler; the handler runs after
    /// them. Two throw sites of one type share the filter's copy.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 21)]
    [InlineData(0, 1, 21)]
    [InlineData(1, 1, -1)]
    public void AFilterRunsBeforeTheFinallysItsHandlerLeaves(int a, int b, int expected)
    {
        IrProcedure procedure = Method("""
            static int M(int a, int b)
            {
                int t = 0;
                try
                {
                    try { int q = 1 / a; q = 1 / b; return -1; }
                    finally { t = t * 10 + 1; }
                }
                catch (DivideByZeroException) when ((t = t * 10 + 2) > 0) { return t; }
            }
            """);

        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a), Bits(32, b)));
    }

    /// <summary>A filter that reads the exception object is opaque where it reads it, not across the body (out of scope for M4-008).</summary>
    [Fact]
    public void AFilterThatReadsTheExceptionIsOpaqueThere()
    {
        IrOpaque opaque = Assert.Single(Opaques(Method("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException e) when (e.Message.Length > 0) { return 1; } }")), static o => o.Reason is "CaughtException");

        Assert.False(opaque.WholeBody);
    }

    /// <summary>An exception of unknown type, from a call, that one filtered clause could take is tried against its filter.</summary>
    [Fact]
    public void ACallThatThrowsIsTriedAgainstTheOneFilter() =>
        Assert.DoesNotContain(
            Opaques(Method("static void N() { } static int M(int a) { try { N(); return 0; } catch (Exception) when (a > 0) { return 1; } }")),
            static o => o.Reason is "call-throw-in-try");

    /// <summary>A constructor runs its type's field and property initializers, in declaration order, before its base call and body.</summary>
    [Fact]
    public void ConstructorRunsTheInitializersBeforeItsBaseCall()
    {
        IrProcedure procedure = Source("class B { public B(int n) { } } class C : B { int f = 1; int P { get; } = 2; int Q { get; set; } C(int a) : base(a) { f = a; } }", ".ctor");

        ImmutableArray<IrInstruction> instructions = [.. procedure.Blocks.SelectMany(static b => b.Instructions)];
        ImmutableArray<IrMapWrite> writes = [.. instructions.OfType<IrMapWrite>()];
        Assert.Empty(Opaques(procedure));
        Assert.Equal(["field.C.f", "field.C.P", "field.C.f"], writes.Select(static w => string.Join('.', w.Map.Name.Split('.').Take(3))), StringComparer.Ordinal);
        Assert.True(instructions.IndexOf(writes[1]) < instructions.IndexOf(Assert.Single(Calls(procedure))));
        Assert.True(instructions.IndexOf(Calls(procedure)[0]) < instructions.IndexOf(writes[2]));
    }

    /// <summary>A static constructor runs the static initializers, and only those, first.</summary>
    [Fact]
    public void StaticConstructorRunsTheStaticInitializersFirst()
    {
        IrProcedure procedure = Method("static int s = 3; int i = 5; static int t; static C() { t = s + 1; }", ".cctor");

        IrRun run = IrInterpreter.Run(procedure, new IrInputs([Fields("C", 0, Token), Fields("C", 0, Token)]), IrGenOracle.Instance, IrGen.StepBudget);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(["field.C.s", "field.C.t"], procedure.Parameters.Select(static p => p.Var.Name), StringComparer.Ordinal);
        Assert.Equal([Bits(32, 3), Bits(32, 4)], run.Outs.Select(static o => ((IrMapValue)o).Read(Token)));
    }

    /// <summary>A primary constructor runs the initializers, which read its parameters, then calls the base with its arguments.</summary>
    [Fact]
    public void APrimaryConstructorWithBaseArgumentsIsLowered()
    {
        IrProcedure procedure = Source("class B(int n) { } class C(int p) : B(p) { int f = p + 1; }", ".ctor", parameters: 1);

        Assert.Empty(Opaques(procedure));
        Assert.Equal("B::.ctor(int)", Assert.Single(Calls(procedure)).Callee.Value);
        Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapWrite>());
    }

    /// <summary>An accessor with no body and no backing field, an abstract one, has nothing to lower.</summary>
    [Fact]
    public void AnAbstractAccessorIsOneOpaque() =>
        Assert.Equal("no-body", Assert.Single(Opaques(Source("abstract class C { public abstract int P { get; } }", "get_P"))).Reason);

    /// <summary>A record's primary constructor writes its positional properties where no operation shows it, so it stays whole-body.</summary>
    [Fact]
    public void ARecordsPrimaryConstructorIsOneOpaque() =>
        Assert.Equal("no-body", Assert.Single(Opaques(Source("record C(int X, int Y);", ".ctor", parameters: 2))).Reason);

    /// <summary>An initializer that does not bind makes the constructor Unknown(Unbound), as an error in its body does.</summary>
    [Fact]
    public void AnUnboundInitializerIsUnbound() =>
        Assert.Equal("unbound", Assert.Single(Opaques(Source("class C { int f = Missing; C() { } }", ".ctor", allowErrors: true))).Reason);

    private static readonly IrSortValue This = Reference(1, "C");

    private static readonly IrSortValue Token = new("C", 0);

    /// <summary>A <c>field.C.*</c> map of <c>int</c> that holds <paramref name="value"/> at <paramref name="key"/>, the receiver by default.</summary>
    private static IrMapValue Fields(string sort, int value, IrValue? key = null) =>
        new(new IrMap(new IrSort(sort), Int), Bits(32, 0), ImmutableDictionary<IrValue, IrValue>.Empty.Add(key ?? This, Bits(32, value)));
}
