using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.TestSupport;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Core.Tests.Ir;

/// <summary>
/// <see cref="IrUnroller"/> on the <see cref="LoopFixtures"/> (ticket M3-002 deliverable 1 and criterion 6): every result
/// validates, rung 1's unrolling is acyclic and runs exactly as the original on inputs within the bound, and the
/// in-place and peeled copies keep the semantics on every input.
/// </summary>
public sealed class IrUnrollerTests
{
    private const string Recursive = """
        proc "T::F(int)" (%n: bv32) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %c: bool = sle %n, %z
          br %c, B1, B2
        B1:
          ret %z
        B2:
          %one: bv32 = const bv32 1
          %m: bv32 = sub %n, %one
          %r: bv32 = call "T::F(int)"(%m) threw %t: bool
          br %t, B3, B4
        B3:
          throw "System.Exception"
        B4:
          %s: bv32 = add %n, %r
          ret %s
        """;

    public static TheoryData<string, int> Fixtures => new()
    {
        { nameof(LoopFixtures.Single), 1 },
        { nameof(LoopFixtures.Single), 3 },
        { nameof(LoopFixtures.Nested), 1 },
        { nameof(LoopFixtures.Nested), 3 },
        { nameof(LoopFixtures.EarlyReturn), 2 },
        { nameof(LoopFixtures.Throws), 3 },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void UnrollingIsAcyclicValidAndExactWithinTheBound(string name, int bound)
    {
        IrProcedure original = Load(name);

        IrProcedure unrolled = IrUnroller.Unroll(original, bound);

        Assert.Empty(IrValidator.Validate(unrolled));
        Assert.Empty(IrLoopAnalysis.Of(unrolled).Loops);
        int within = 0;
        foreach (IrInputs input in Inputs(original))
        {
            IrRun run = IrGen.Run(unrolled, input);
            if (run.Outcome is not IrInfeasible)
            {
                within++;
                Assert.Equal(IrGen.Run(original, input), run);
            }
        }

        Assert.NotEqual(0, within);
    }

    [Fact]
    public void AnInputBeyondTheBoundIsInfeasible()
    {
        IrProcedure unrolled = IrUnroller.Unroll(IrText.Parse(LoopFixtures.Single), 3);

        Assert.Equal(new IrReturned(Bits(1)), IrGen.Run(unrolled, new IrInputs([Bits(2)])).Outcome);
        Assert.IsType<IrInfeasible>(IrGen.Run(unrolled, new IrInputs([Bits(3)])).Outcome);
    }

    [Fact]
    public Task NestedLoopsUnrollInsideOut() => Verify(IrText.Dump(IrUnroller.Unroll(IrText.Parse(LoopFixtures.Nested), 2)));

    [Theory]
    [InlineData(nameof(LoopFixtures.Single), 1)]
    [InlineData(nameof(LoopFixtures.Single), 3)]
    [InlineData(nameof(LoopFixtures.EarlyReturn), 2)]
    [InlineData(nameof(LoopFixtures.Throws), 3)]
    public void UnrollingInPlaceKeepsTheLoopAndTheSemantics(string name, int copies)
    {
        IrProcedure original = Load(name);
        IrBlockId header = IrLoopAnalysis.Of(original).Loops[0].Header;

        (IrProcedure unrolled, ImmutableArray<IrBlockId> headers) = IrUnroller.UnrollInPlace(original, header, copies);

        Assert.Empty(IrValidator.Validate(unrolled));
        Assert.Equal(copies, headers.Length);
        Assert.Equal(header, headers[0]);
        Assert.Equal([header], IrLoopAnalysis.Of(unrolled).Loops.Select(static l => l.Header));
        Assert.All(Inputs(original), input => Assert.Equal(IrGen.Run(original, input), IrGen.Run(unrolled, input)));
    }

    [Theory]
    [InlineData(nameof(LoopFixtures.Single), 1)]
    [InlineData(nameof(LoopFixtures.Single), 3)]
    [InlineData(nameof(LoopFixtures.EarlyReturn), 2)]
    [InlineData(nameof(LoopFixtures.Throws), 3)]
    public void PeelingKeepsTheSemanticsAndTheLastCopyLoops(string name, int copies)
    {
        IrProcedure original = Load(name);
        IrBlockId header = IrLoopAnalysis.Of(original).Loops[0].Header;

        (IrProcedure peeled, ImmutableArray<IrBlockId> headers) = IrUnroller.Peel(original, header, copies);

        Assert.Empty(IrValidator.Validate(peeled));
        Assert.Equal(copies, headers.Length);
        Assert.Equal([headers[^1]], IrLoopAnalysis.Of(peeled).Loops.Select(static l => l.Header));
        Assert.All(Inputs(original), input => Assert.Equal(IrGen.Run(original, input), IrGen.Run(peeled, input)));
    }

    [Fact]
    public void InPlaceUnrollingOfAnOuterLoopCopiesItsInnerLoop()
    {
        IrProcedure original = IrText.Parse(LoopFixtures.Nested);

        (IrProcedure unrolled, _) = IrUnroller.UnrollInPlace(original, new IrBlockId(1), 2);

        Assert.Empty(IrValidator.Validate(unrolled));
        Assert.Equal(3, IrLoopAnalysis.Of(unrolled).Loops.Length);
        Assert.All(Inputs(original), input => Assert.Equal(IrGen.Run(original, input), IrGen.Run(unrolled, input)));
    }

    [Fact]
    public void ABlockThatIsNotAHeaderIsRejected()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(static () => IrUnroller.Peel(IrText.Parse(LoopFixtures.Single), new IrBlockId(2), 2));

        Assert.StartsWith("B2 is not a loop header of T::Sum(int).", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IrreducibleControlFlowIsNeverUnrolled()
    {
        IrProcedure tangle = IrText.Parse("""
            proc "T::Tangle(bool)" (%c: bool) entry B0
            B0:
              br %c, B1, B2
            B1:
              br %c, B2, B3
            B2:
              goto B1
            B3:
              ret
            """);

        ArgumentException unroll = Assert.Throws<ArgumentException>(() => IrUnroller.Unroll(tangle, 2));
        Assert.StartsWith("T::Tangle(bool) has irreducible control flow, which is never unrolled.", unroll.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => IrUnroller.Peel(tangle, new IrBlockId(1), 2));
    }

    [Fact]
    public void SelfRecursionIsInlinedToTheBound()
    {
        IrProcedure unrolled = IrUnroller.Unroll(IrText.Parse(Recursive), 3);

        Assert.Empty(IrValidator.Validate(unrolled));
        Assert.False(IrLoopAnalysis.Of(unrolled).IsSelfRecursive);
        Assert.Equal([0u, 1, 3, 6], new uint[] { 0, 1, 2, 3 }.Select(n => (uint)((IrBitVecValue)((IrReturned)IrGen.Run(unrolled, new IrInputs([Bits(n)])).Outcome).Value!).Bits));
        Assert.IsType<IrInfeasible>(IrGen.Run(unrolled, new IrInputs([Bits(4)])).Outcome);
    }

    [Fact]
    public void AnInlinedThrowSetsTheThrewFlagAndAReceiverBindsThis()
    {
        IrProcedure unrolled = IrUnroller.Unroll(IrText.Parse("""
            proc "T::G(int)" (%n: bv32, %this: sort "T") -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = eq %n, %z
              br %c, B1, B2
            B1:
              throw "System.InvalidOperationException"
            B2:
              %r: bv32 = call "T::G(int)"(%this, %z) threw %t: bool
              br %t, B3, B4
            B3:
              ret %n
            B4:
              ret %r
            """), 1);

        Assert.Empty(IrValidator.Validate(unrolled));
        IrInputs input = new([Bits(5), new IrSortValue("T", 0)]);
        Assert.Equal(new IrReturned(Bits(5)), IrGen.Run(unrolled, input).Outcome);
    }

    [Fact]
    public void AVoidSelfCallInlinesWithoutAResult()
    {
        IrProcedure unrolled = IrUnroller.Unroll(IrText.Parse("""
            proc "T::H(int)" (%n: bv32) entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = eq %n, %z
              br %c, B1, B2
            B1:
              ret
            B2:
              %one: bv32 = const bv32 1
              %m: bv32 = sub %n, %one
              call "System.Console::WriteLine(int)"(%n)
              call "T::H(int)"(%m) threw %t: bool
              ret
            """), 2);

        Assert.Empty(IrValidator.Validate(unrolled));
        Assert.Equal(2, IrGen.Run(unrolled, new IrInputs([Bits(2)])).Trace.Length);
    }

    [Theory]
    [InlineData("ref %n: bv32", "%n", " outs(%n = %n)", "it has a by-ref parameter")]
    [InlineData("%n: bv32, %length.a: bv32", "%n", "", "an input is keyed by an array variable")]
    [InlineData("%n: bv32, %array.a: map<bv32, bv32>", "%n", "", "an input is keyed by an array variable")]
    [InlineData("%n: bv32, %this: sort \"T\"", "%n", "", "a self-call has no threw flag or its arguments do not match the parameters")]
    [InlineData("%n: bv32", "%n, %n, %n", "", "a self-call has no threw flag or its arguments do not match the parameters")]
    public void SomeSelfRecursionCannotBeInlined(string parameters, string arguments, string outs, string obstacle)
    {
        IrProcedure procedure = IrText.Parse($$"""
            proc "T::F(int)" ({{parameters}}) entry B0
            B0:
              call "T::F(int)"({{arguments}}) threw %t: bool
              ret{{outs}}
            """);

        Assert.Equal(obstacle, IrUnroller.InliningObstacle(procedure));
        ArgumentException error = Assert.Throws<ArgumentException>(() => IrUnroller.Unroll(procedure, 2));
        Assert.StartsWith($"T::F(int) cannot be inlined into itself: {obstacle}.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("call \"T::F(int)\"(%n)", "a self-call has no threw flag or its arguments do not match the parameters")]
    [InlineData("%w: map<bv32, bv32> = mapwrite %field.T.x, %n, %n\n  call \"T::F(int)\"(%n) threw %t: bool", "it writes the heap")]
    [InlineData("call \"T::G(int)\"(%n) threw %t: bool", null)]
    public void InliningObstaclesLookAtTheBody(string body, string? obstacle)
    {
        IrProcedure procedure = IrText.Parse($$"""
            proc "T::F(int)" (%n: bv32, %field.T.x: map<bv32, bv32>) entry B0
            B0:
              {{body}}
              ret
            """);

        Assert.Equal(obstacle, IrUnroller.InliningObstacle(procedure));
    }

    [Fact]
    public void DefaultValuesExistForEveryType()
    {
        Assert.Equal(new IrBoolValue(Value: false), IrUnroller.Default(new IrBool()));
        Assert.Equal(new IrSortValue("S", 0), IrUnroller.Default(new IrSort("S")));
        Assert.Equal(
            new IrMapValue(new IrMap(new IrBitVec(8), new IrBool()), new IrBoolValue(Value: false), []),
            IrUnroller.Default(new IrMap(new IrBitVec(8), new IrBool())));
    }

    [Fact]
    public void EveryInstructionAndTerminatorKindIsRenamed()
    {
        IrProcedure procedure = IrText.Parse("""
            proc "T::M(int)" (%a: bv32, %m: map<bv32, bv32>) -> bv32 entry B0
            B0:
              %b: bv32 = add %a, %a
              %o: bool = overflows sadd %a, %b
              %n: bv32 = neg %b
              %r: bv32 = mapread %m, %n
              %w: map<bv32, bv32> = mapwrite %m, %r, %n
              %x: bv32 = opaque "why" at "T.cs" 1:1-1:2
              switch %a [bv32 0 -> B1] default B2
            B1:
              unreachable
            B2:
              br %o, B3, B4
            B3:
              throw "System.OverflowException"
            B4:
              ret %x
            """);
        static IrVar Rename(IrVar var) => var with { Name = var.Name + "_" };

        IrInstruction[] instructions = [.. procedure.Blocks.SelectMany(static b => b.Instructions).Select(static i => IrUnroller.Rewrite(i, Rename))];
        IrTerminator[] terminators = [.. procedure.Blocks.Select(static b => IrUnroller.Rewrite(b.Terminator, Rename, static b => b))];

        Assert.All(
            instructions.SelectMany(static i => i.Definitions().Concat(i.Uses())).Concat(terminators.SelectMany(static t => t.Uses())),
            static v => Assert.EndsWith("_", v.Name, StringComparison.Ordinal));
        Assert.Equal(6, instructions.SelectMany(static i => i.Definitions()).Count());
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        IrProcedure single = IrText.Parse(LoopFixtures.Single);

        Assert.Throws<ArgumentNullException>(static () => IrUnroller.Unroll(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => IrUnroller.Unroll(single, 0));
        Assert.Throws<ArgumentNullException>(static () => IrUnroller.InliningObstacle(null!));
        Assert.Throws<ArgumentNullException>(static () => IrUnroller.Peel(null!, new IrBlockId(1), 1));
        Assert.Throws<ArgumentNullException>(() => IrUnroller.Peel(single, null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => IrUnroller.UnrollInPlace(single, new IrBlockId(1), 0));
    }

    private static IrProcedure Load(string name) => IrText.Parse(name switch
    {
        nameof(LoopFixtures.Single) => LoopFixtures.Single,
        nameof(LoopFixtures.Nested) => LoopFixtures.Nested,
        nameof(LoopFixtures.EarlyReturn) => LoopFixtures.EarlyReturn,
        _ => LoopFixtures.Throws,
    });

    /// <summary>Every combination of small and edge values for the procedure's bitvector parameters.</summary>
    private static IEnumerable<IrInputs> Inputs(IrProcedure procedure)
    {
        uint[] values = [0, 1, 2, 3, 4, 0xFFFF_FFFF];
        return procedure.Parameters
            .Aggregate(
                (IEnumerable<ImmutableArray<IrValue>>)[[]],
                (combinations, _) => [.. combinations.SelectMany(c => values.Select(v => c.Add(Bits(v))))])
            .Select(static c => new IrInputs(c));
    }

    private static IrBitVecValue Bits(uint value) => new(32, value);
}
