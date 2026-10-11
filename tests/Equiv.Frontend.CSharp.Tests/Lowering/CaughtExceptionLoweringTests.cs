using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.TestSupport;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-140: the exception of a <c>catch</c> whose variable is only read as <c>e.Message</c> is a value. After a call
/// threw it is the <c>thrown.System.Exception</c> cell as that call left it; after a <c>throw new T(...)</c> in the same
/// <c>try</c> it is the object made; after anything else it is opaque on that path. Every other use of the variable leaves
/// the exception opaque with reason <c>CaughtException</c>, as before.
/// </summary>
public sealed class CaughtExceptionLoweringTests
{
    private const string Cell = "thrown.System.Exception";

    private const string N = "static void N() { } ";

    private static readonly IrSortValue Token = new("System.Exception", 0);

    private static readonly IrSortValue Thrown = Reference(7, "System.Exception");

    /// <summary>Acceptance criterion 2: the exception is the cell the call that threw left, read where it threw.</summary>
    [Fact]
    public Task MessageOfAnExceptionACallThrew() =>
        Verify(IrText.Dump(Method(N + "static string M() { try { N(); return \"\"; } catch (Exception e) { return e.Message; } }")));

    /// <summary>The cell is a by-ref input every call reads and writes, and <c>get_Message</c> is handed what the call that threw left in it.</summary>
    [Fact]
    public void TheExceptionIsWhatTheCallThatThrewLeftInTheCell()
    {
        IrProcedure procedure = Method(N + "static string M() { try { N(); return \"\"; } catch (Exception e) { return e.Message; } }");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(IrParameterKind.Ref, Assert.Single(procedure.Parameters, static p => p.Var.Name is Cell).Kind);
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name is "null.System.Exception");
        Assert.All(Calls(procedure), static call => Assert.Equal(Cell, Assert.Single(call.Heap).Map));
        Messages messages = new(throws: "N");
        Assert.Equal(new IrReturned(Messages.Text), IrInterpreter.Run(procedure, new IrInputs([Empty()]), messages, IrGen.StepBudget).Outcome);
        Assert.Equal([Thrown], messages.Read);
    }

    /// <summary>A call in a <c>finally</c> on the way to the handler writes the cell too: the exception is read before it runs.</summary>
    [Fact]
    public void TheCellIsReadBeforeAFinallyOnTheWayMakesItsOwnCall()
    {
        IrProcedure procedure = Method(N + "static void K() { } static string M() { try { try { N(); } finally { K(); } return \"\"; } catch (Exception e) { return e.Message; } }");

        Assert.Empty(Opaques(procedure));
        Messages messages = new(throws: "N");
        Assert.Equal(new IrReturned(Messages.Text), IrInterpreter.Run(procedure, new IrInputs([Empty()]), messages, IrGen.StepBudget).Outcome);
        Assert.Equal([Thrown], messages.Read);
    }

    /// <summary>A clause of another type reads the cell through the <c>cast</c> map to its variable's type; a filter that does not read the exception changes nothing.</summary>
    [Theory]
    [InlineData("catch (InvalidOperationException e) { return e.Message; }")]
    [InlineData("catch (InvalidOperationException e) when (a > 0) { return e.Message; }")]
    public void AClauseOfAnotherTypeTakesTheCellThroughTheCastMap(string clause)
    {
        IrProcedure procedure = Method(N + "static string M(int a) { try { N(); return \"\"; } " + clause + " }");

        Assert.Empty(Opaques(procedure));
        Assert.Contains(procedure.Parameters, static p => p.Var.Name is "cast.System.Exception.System.InvalidOperationException");
    }

    /// <summary>
    /// Acceptance criterion 2: a <c>throw new T(...)</c> in the same <c>try</c> hands the clause the object it made, through
    /// the <c>cast</c> map when the clause's variable has another type. The constructor is a call that may throw as well,
    /// and that exception is the cell's, or of unknown route when two clauses could take it.
    /// </summary>
    [Theory]
    [InlineData("catch (InvalidOperationException e) { return e.Message; }", false)]
    [InlineData("catch (Exception e) { return e.Message; }", true)]
    [InlineData("catch (InvalidOperationException e) when (a > 1) { return e.Message; } catch (Exception f) { return f.Message; }", true)]
    public void AThrowInTheSameTryHandsOverTheObjectItMade(string clauses, bool upcast)
    {
        IrProcedure procedure = Method("static string M(int a) { try { if (a > 0) throw new InvalidOperationException(); return \"\"; } " + clauses + " }");

        Assert.DoesNotContain(Opaques(procedure), static o => o.Reason is "CaughtException");
        Assert.Equal(upcast, procedure.Parameters.Any(static p => p.Var.Name is "cast.System.InvalidOperationException.System.Exception"));
        IrVar made = Assert.Single(Calls(procedure), static c => c.Callee.Value.Contains(".ctor", StringComparison.Ordinal)).Target!;
        IrInstruction[] instructions = [.. procedure.Blocks.SelectMany(static b => b.Instructions)];
        bool Made(IrVar value) => value == made || instructions.OfType<IrMapRead>().Any(cast => cast.Target == value && cast.Key == made);
        Assert.Contains(
            Calls(procedure).Where(static c => c.Callee.Value.Contains("get_Message", StringComparison.Ordinal)).Select(static c => c.Args[0]),
            read => Made(read) || instructions.OfType<IrPhi>().Any(phi => phi.Target == read && phi.Incoming.Any(i => Made(i.Value))));
    }

    /// <summary>
    /// An exception nothing names is opaque where it is raised, not across the handler: a division by zero, a closed
    /// call, which has no heap pairs to leave the cell in (ADR 0041), and a user-defined operator.
    /// </summary>
    [Theory]
    [InlineData("static string M(int a, int b) { try { return a / b > 0 ? \"t\" : \"f\"; } catch (Exception e) { return e.Message; } }")]
    [InlineData("static string M(string s) { try { return bool.Parse(s) ? \"t\" : \"f\"; } catch (Exception e) { return e.Message; } }")]
    [InlineData("static string M(decimal a, decimal b) { try { return a + b > 0 ? \"t\" : \"f\"; } catch (OverflowException e) { return e.Message; } }")]
    public void AnExceptionNothingNamesIsOpaqueOnItsOwnPath(string method)
    {
        IrProcedure procedure = Method(method);

        ImmutableArray<IrOpaque> opaques = Opaques(procedure);
        Assert.NotEmpty(opaques);
        Assert.All(opaques, opaque =>
        {
            Assert.Equal("CaughtException", opaque.Reason);
            Assert.False(opaque.WholeBody);
            Assert.Equal([opaque], Assert.Single(procedure.Blocks, block => block.Instructions.Contains(opaque)).Instructions);
        });
    }

    /// <summary>An exception that more than one clause could take has no known route, so nothing is handed over.</summary>
    [Fact]
    public void AnExceptionOfUnknownRouteNamesNothing() =>
        Assert.Equal(
            ["call-throw-in-try"],
            Opaques(Method(N + "static string M() { try { N(); return \"\"; } catch (InvalidOperationException e) { return e.Message; } catch (Exception f) { return f.Message; } }"))
                .Select(static o => o.Reason),
            StringComparer.Ordinal);

    /// <summary>Acceptance criterion 2, the size guard: every use but <c>e.Message</c> in the handler leaves the exception opaque.</summary>
    [Theory]
    [InlineData("catch (Exception e) { return e.ToString(); }")]
    [InlineData("catch (Exception e) { return e.StackTrace; }")]
    [InlineData("catch (Exception e) { return e.Message + e.StackTrace; }")]
    [InlineData("catch (Exception e) { Log(e); return \"\"; }")]
    [InlineData("catch (Exception e) { throw new InvalidOperationException(\"\", e); }")]
    [InlineData("catch (Exception e) { Func<string> f = () => e.Message; return f(); }")]
    [InlineData("catch (Exception e) { return Local(); string Local() => e.Message; }")]
    [InlineData("catch (Exception e) when (e.Message.Length > 0) { return \"\"; }")]
    [InlineData("catch (Exception e) { e = null; return \"\"; }")]
    public void AnyOtherUseLeavesTheExceptionOpaque(string clause) =>
        Assert.Contains(
            Opaques(Method(N + "static void Log(object o) { } static string M() { try { N(); return \"\"; } " + clause + " }")),
            static o => o.Reason is "CaughtException" && !o.WholeBody);

    /// <summary>A variable nothing reads, and one an inner clause's handler reads, name a value as well.</summary>
    [Theory]
    [InlineData("catch (Exception e) { return \"\"; }")]
    [InlineData("catch (Exception e) { try { N(); return \"\"; } catch (InvalidOperationException) { return e.Message; } }")]
    public void AVariableOnlyReadForItsMessageNamesAValue(string clause) =>
        Assert.Empty(Opaques(Method(N + "static string M() { try { N(); return \"\"; } " + clause + " }")));

    private static IrMapValue Empty() => new(new IrMap(new IrSort("System.Exception"), new IrSort("System.Exception")), Token, []);

    /// <summary>
    /// The callee named <paramref name="throws"/> throws and leaves <see cref="Thrown"/> in the cell; every other call
    /// completes and clears the cell, and <c>get_Message</c> records what it is handed.
    /// </summary>
    private sealed class Messages(string throws) : ICallOracle
    {
        public static readonly IrSortValue Text = Reference(3);

        public List<IrValue> Read { get; } = [];

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            bool threw = callee.Value.Contains($"::{throws}(", StringComparison.Ordinal);
            if (resultType is not null)
            {
                Read.Add(arguments[0]);
            }

            return new(resultType is null ? null : Text, threw) { Heap = [threw ? Empty().Write(Token, Thrown) : Empty()] };
        }
    }
}
