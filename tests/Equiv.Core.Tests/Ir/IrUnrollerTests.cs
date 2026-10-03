using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;

using Equiv.Core;
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

    /// <summary>Sums an array from index <c>%i</c> to its end: reads <c>array.*</c> and <c>length.*</c>.</summary>
    private const string ArraySum = """
        proc "T::Sum(int[],int)" (%a: sort "int[]", %i: bv32, ref %array.int__: map<sort "int[]", map<bv32, bv32>>, %length.int__: map<sort "int[]", bv32>) -> bv32 entry B0
        B0:
          %elems: map<bv32, bv32> = mapread %array.int__, %a
          %len: bv32 = mapread %length.int__, %a
          %one: bv32 = const bv32 1
          %j: bv32 = add %i, %one
          %c: bool = uge %j, %len
          br %c, B1, B2
        B1:
          %x: bv32 = mapread %elems, %i
          ret %x outs(%array.int__ = %array.int__)
        B2:
          %y: bv32 = mapread %elems, %i
          %r: bv32 = call "T::Sum(int[],int)"(%a, %j) threw %t: bool heap("array.int__" %array.int__ -> %array.int__.1: map<sort "int[]", map<bv32, bv32>>)
          br %t, B3, B4
        B3:
          throw "System.Exception" outs(%array.int__ = %array.int__.1)
        B4:
          %s: bv32 = add %y, %r
          ret %s outs(%array.int__ = %array.int__.1)
        """;

    /// <summary>Adds the counts down from <c>%n</c> to a field it reads at the base case.</summary>
    private const string FieldRead = """
        proc "T::F(int)" (%n: bv32, %this: sort "T", ref %field.T.x: map<sort "T", bv32>) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %c: bool = eq %n, %z
          br %c, B1, B2
        B1:
          %v: bv32 = mapread %field.T.x, %this
          ret %v outs(%field.T.x = %field.T.x)
        B2:
          %one: bv32 = const bv32 1
          %m: bv32 = sub %n, %one
          %r: bv32 = call "T::F(int)"(%this, %m) threw %t: bool heap("field.T.x" %field.T.x -> %field.T.x.1: map<sort "T", bv32>)
          br %t, B3, B4
        B3:
          throw "System.Exception" outs(%field.T.x = %field.T.x.1)
        B4:
          %s: bv32 = add %r, %n
          ret %s outs(%field.T.x = %field.T.x.1)
        """;

    /// <summary>Writes a field before its self-call and reads it after.</summary>
    private const string FieldWrittenBeforeTheCall = """
        proc "T::F(int)" (%n: bv32, %this: sort "T", ref %field.T.x: map<sort "T", bv32>) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %c: bool = eq %n, %z
          br %c, B1, B2
        B1:
          ret %n outs(%field.T.x = %field.T.x)
        B2:
          %w: map<sort "T", bv32> = mapwrite %field.T.x, %this, %n
          %one: bv32 = const bv32 1
          %m: bv32 = sub %n, %one
          %r: bv32 = call "T::F(int)"(%this, %m) threw %t: bool heap("field.T.x" %w -> %field.T.x.1: map<sort "T", bv32>)
          br %t, B3, B4
        B3:
          throw "System.Exception" outs(%field.T.x = %field.T.x.1)
        B4:
          %v: bv32 = mapread %field.T.x.1, %this
          %s: bv32 = add %r, %v
          ret %s outs(%field.T.x = %field.T.x.1)
        """;

    /// <summary>Writes a field at the base case, which each caller reads after its self-call.</summary>
    private const string FieldWrittenByTheCallee = """
        proc "T::F(int)" (%n: bv32, %this: sort "T", ref %field.T.x: map<sort "T", bv32>) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %c: bool = eq %n, %z
          br %c, B1, B2
        B1:
          %seven: bv32 = const bv32 7
          %w: map<sort "T", bv32> = mapwrite %field.T.x, %this, %seven
          ret %z outs(%field.T.x = %w)
        B2:
          %one: bv32 = const bv32 1
          %m: bv32 = sub %n, %one
          %r: bv32 = call "T::F(int)"(%this, %m) threw %t: bool heap("field.T.x" %field.T.x -> %field.T.x.1: map<sort "T", bv32>)
          br %t, B3, B4
        B3:
          throw "System.Exception" outs(%field.T.x = %field.T.x.1)
        B4:
          %v: bv32 = mapread %field.T.x.1, %this
          %s: bv32 = add %r, %v
          ret %s outs(%field.T.x = %field.T.x.1)
        """;

    public static TheoryData<string, int> Fixtures => new()
    {
        { nameof(LoopFixtures.Single), 1 },
        { nameof(LoopFixtures.Single), 3 },
        { nameof(LoopFixtures.Nested), 1 },
        { nameof(LoopFixtures.Nested), 3 },
        { nameof(LoopFixtures.EarlyReturn), 2 },
        { nameof(LoopFixtures.Throws), 3 },
        { nameof(LoopFixtures.ExitPhi), 3 },
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
    [InlineData(nameof(LoopFixtures.ExitPhi), 2)]
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
    [InlineData(nameof(LoopFixtures.ExitPhi), 2)]
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
    public Task InlinedCopiesGetFreshNamesPerInstance() => Verify(IrText.Dump(IrUnroller.Unroll(IrText.Parse(Recursive), 2)));

    [Fact]
    public void InliningBindsTheReceiverToThisAndEachArgumentToItsParameter()
    {
        IrProcedure unrolled = IrUnroller.Unroll(IrText.Parse("""
            proc "T::G(int,int,T)" (%a: bv32, %b: bv32, %other: sort "T", %field.T.x: map<sort "T", bv32>, %this: sort "T") -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = eq %a, %z
              br %c, B1, B2
            B1:
              %v: bv32 = mapread %field.T.x, %this
              %r0: bv32 = sub %v, %b
              ret %r0
            B2:
              %one: bv32 = const bv32 1
              %m: bv32 = sub %a, %one
              %r: bv32 = call "T::G(int,int,T)"(%other, %m, %b, %this) threw %t: bool
              br %t, B3, B4
            B3:
              throw "System.Exception"
            B4:
              ret %r
            """), 2);
        IrMap fields = new(new IrSort("T"), new IrBitVec(32));
        IrMapValue x = new(fields, Bits(0), ImmutableDictionary<IrValue, IrValue>.Empty.Add(new IrSortValue("T", 0), Bits(10)).Add(new IrSortValue("T", 1), Bits(20)));

        IrRun run = IrGen.Run(unrolled, new IrInputs([Bits(1), Bits(5), new IrSortValue("T", 1), x, new IrSortValue("T", 0)]));

        Assert.Empty(IrValidator.Validate(unrolled));
        Assert.Equal(new IrReturned(Bits(15)), run.Outcome);
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

    /// <summary>Self-recursive procedures over the heap (ticket P1-007 criterion 5), each by what it does with the heap, and its result.</summary>
    public static TheoryData<string, string, uint> HeapRecursion => new()
    {
        { "reads array.* and length.*", ArraySum, 39u },
        { "reads field.*", FieldRead, 8u },
        { "writes field.* before the self-call and reads it after", FieldWrittenBeforeTheCall, 2u },
        { "the callee writes field.* and the caller reads it after the call", FieldWrittenByTheCallee, 14u },
    };

    [Theory]
    [MemberData(nameof(HeapRecursion))]
    public void SelfRecursionOverTheHeapIsInlined(string heap, string text, uint result)
    {
        IrProcedure original = IrText.Parse(text);
        IrInputs input = new([.. original.Parameters.Select(static p => HeapInput(p.Var.Type))]);

        IrProcedure unrolled = IrUnroller.Unroll(original, 3);

        Assert.Null(IrUnroller.InliningObstacle(original));
        Assert.Empty(IrValidator.Validate(unrolled));
        IrRun run = IrGen.Run(unrolled, input);
        IrRun expected = RunRecursively(original, input);
        Assert.Equal(new IrReturned(Bits(result)), run.Outcome);
        Assert.True(expected == run, $"{heap}: expected {expected.Outcome} {string.Join(", ", expected.Outs)}, got {run.Outcome} {string.Join(", ", run.Outs)}");
    }

    [Theory]
    [InlineData("ref %n: bv32", "%n", "", " outs(%n = %n)", "it has a by-ref parameter")]
    [InlineData("%n: bv32, %this: sort \"T\"", "%n", "", "", "a self-call has no threw flag or its arguments do not match the parameters")]
    [InlineData("%n: bv32", "%n, %n, %n", "", "", "a self-call has no threw flag or its arguments do not match the parameters")]
    [InlineData("%n: bv32, ref %m: bv32", "%n, %m", "", " outs(%m = %m)", "it has a by-ref parameter")]
    [InlineData("%n: bv32, ref %field.T.x: map<bv32, bv32>", "%n", "", " outs(%field.T.x = %field.T.x)", "a self-call's heap pairs do not match the heap parameters")]
    [InlineData(
        "%n: bv32, ref %field.T.x: map<bv32, bv32>",
        "%n",
        " heap(\"field.T.x\" %field.T.x -> %x1: map<bv32, bv32>, \"field.T.x\" %field.T.x -> %x2: map<bv32, bv32>)",
        " outs(%field.T.x = %field.T.x)",
        "a self-call's heap pairs do not match the heap parameters")]
    [InlineData(
        "%n: bv32, ref %field.T.b: map<bv32, bv32>, ref %field.T.a: map<bv32, bv32>",
        "%n, %n, %n",
        " heap(\"field.T.b\" %field.T.b -> %b1: map<bv32, bv32>, \"field.T.a\" %field.T.a -> %a1: map<bv32, bv32>)",
        " outs(%field.T.b = %field.T.b, %field.T.a = %field.T.a)",
        "a self-call has no threw flag or its arguments do not match the parameters")]
    public void SomeSelfRecursionCannotBeInlined(string parameters, string arguments, string heap, string outs, string obstacle)
    {
        IrProcedure procedure = IrText.Parse($$"""
            proc "T::F(int)" ({{parameters}}) entry B0
            B0:
              call "T::F(int)"({{arguments}}) threw %t: bool{{heap}}
              ret{{outs}}
            """);

        Assert.Equal(obstacle, IrUnroller.InliningObstacle(procedure));
        ArgumentException error = Assert.Throws<ArgumentException>(() => IrUnroller.Unroll(procedure, 2));
        Assert.StartsWith($"T::F(int) cannot be inlined into itself: {obstacle}.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("call \"T::F(int)\"(%n)", "a self-call has no threw flag or its arguments do not match the parameters")]
    [InlineData("call \"T::F(int)\"(%n) threw %t: bool\n  call \"T::F(int)\"(%n)", "a self-call has no threw flag or its arguments do not match the parameters")]
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

    /// <summary>
    /// Ticket P2-109: the cost of unrolling grows with the size of what is copied, not with its cube. The procedure is
    /// the shape that took an OpenRA pair over an hour: a loop that calls its own procedure, so inlining nests the loop
    /// in itself, with a block per call in its body, and a second loop beside it. Four times the body took 45 times as
    /// long before the fix and takes about four times as long after it; the bound of ten sits between the two.
    /// </summary>
    [Fact]
    public void UnrollingALoopThatCallsItsOwnProcedureIsLinearInItsSize()
    {
        IrProcedure small = IrText.Parse(LoopAroundASelfCall(calls: 80));
        IrProcedure large = IrText.Parse(LoopAroundASelfCall(calls: 320));
        Assert.Empty(IrValidator.Validate(large));
        IrProcedure unrolled = IrUnroller.Unroll(large, 3);

        Assert.Empty(IrLoopAnalysis.Of(unrolled).Loops);
        Assert.True(unrolled.Blocks.Length > 20_000);
        Assert.True(Fastest(large) < 10 * Fastest(small), "unrolling four times the loop took more than ten times as long");

        static TimeSpan Fastest(IrProcedure procedure) =>
            Enumerable.Range(0, 3).Min(_ =>
            {
                Stopwatch watch = Stopwatch.StartNew();
                IrUnroller.Unroll(procedure, 3);
                return watch.Elapsed;
            });
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

    /// <summary>A counting loop whose body makes <paramref name="calls"/> calls, each ending its block, then calls the procedure itself; a second loop follows.</summary>
    private static string LoopAroundASelfCall(int calls)
    {
        StringBuilder text = new("""
            proc "T::W(int)" (%n: bv32) entry B0
            B0:
              %z: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              goto B1
            B1:
              %i: bv32 = phi [B0: %z, B3: %i2]
              %c: bool = sge %i, %n
              br %c, B5, B2
            B2:

            """);
        for (int k = 0; k < calls; k++)
        {
            string id = k.ToString(CultureInfo.InvariantCulture);
            string next = (100 + k).ToString(CultureInfo.InvariantCulture);
            text.Append("  %f" + id + ": bv32 = call \"X::F(int)\"(%i) threw %g" + id + ": bool\n  br %g" + id + ", B4, B" + next + "\nB" + next + ":\n");
        }

        return text.Append("""
              %m: bv32 = sub %n, %one
              call "T::W(int)"(%m) threw %t: bool
              br %t, B4, B3
            B3:
              %i2: bv32 = add %i, %one
              goto B1
            B4:
              throw "System.Exception"
            B5:
              goto B6
            B6:
              %j: bv32 = phi [B5: %z, B7: %j2]
              %d: bool = sge %j, %n
              br %d, B8, B7
            B7:
              %j2: bv32 = add %j, %one
              goto B6
            B8:
              ret
            """).ToString();
    }

    private static IrProcedure Load(string name) => IrText.Parse(name switch
    {
        nameof(LoopFixtures.Single) => LoopFixtures.Single,
        nameof(LoopFixtures.Nested) => LoopFixtures.Nested,
        nameof(LoopFixtures.EarlyReturn) => LoopFixtures.EarlyReturn,
        nameof(LoopFixtures.ExitPhi) => LoopFixtures.ExitPhi,
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

    /// <summary>
    /// An input that keeps every <see cref="HeapRecursion"/> row two self-calls deep: every bitvector is 2 (a count down
    /// from 2, or index 2 of a length-5 array), every field and length is 5, and every array holds 10 + i at index i.
    /// </summary>
    private static IrValue HeapInput(IrType type) => type switch
    {
        IrBitVec => Bits(2),
        IrSort sort => new IrSortValue(sort.Name, 0),
        IrMap { Key: IrBitVec } elements => new IrMapValue(elements, Bits(0), ImmutableDictionary<IrValue, IrValue>.Empty.Add(Bits(2), Bits(12)).Add(Bits(3), Bits(13)).Add(Bits(4), Bits(14))),
        IrMap { Value: IrBitVec } values => new IrMapValue(values, Bits(5), []),
        _ => new IrMapValue((IrMap)type, HeapInput(((IrMap)type).Value), []),
    };

    /// <summary>
    /// <paramref name="procedure"/>'s run with each self-call answered by running <paramref name="procedure"/> itself
    /// (every other call by <see cref="IrGenOracle"/>), and each self-call's trace record replaced by that run's trace:
    /// what an exact inlining must reproduce.
    /// </summary>
    private static IrRun RunRecursively(IrProcedure procedure, IrInputs inputs)
    {
        SelfOracle oracle = new(procedure, inputs);
        IrRun run = IrInterpreter.Run(procedure, inputs, oracle, IrGen.StepBudget, pure: IrGenOracle.Instance);
        int next = 0;
        return run with { Trace = [.. run.Trace.SelectMany(r => SelfCall(procedure, r.Callee) ? oracle.Traces[next++] : [r])] };
    }

    private static bool SelfCall(IrProcedure procedure, CallIdentity callee) => string.Equals(callee.Value, procedure.Identity.Value, StringComparison.Ordinal);

    /// <summary>Answers a self-call by running the procedure on its arguments, the heap it was given and the caller's other inputs.</summary>
    private sealed class SelfOracle(IrProcedure procedure, IrInputs caller) : ICallOracle
    {
        public List<ImmutableArray<IrCallRecord>> Traces { get; } = [];

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            if (!SelfCall(procedure, callee))
            {
                return IrGenOracle.Instance.Answer(callee, arguments, resultType, position, heap, refOuts);
            }

            IrParameter[] parameters = [.. procedure.Parameters];
            Queue<IrValue> source = new(arguments.Skip(arguments.Length - parameters.Count(static p => !IrParameterNames.IsSynthesised(p.Var.Name))));
            IrValue[] values = new IrValue[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                string name = parameters[i].Var.Name;
                values[i] = IrParameterNames.IsSynthesised(name) switch
                {
                    false => source.Dequeue(),
                    true when string.Equals(name, IrParameterNames.Receiver, StringComparison.Ordinal) => arguments[0],
                    true => heap.FirstOrDefault(h => string.Equals(h.Map, name, StringComparison.Ordinal))?.Value ?? caller.Arguments[i],
                };
            }

            IrRun run = RunRecursively(procedure, new IrInputs([.. values]));
            Traces.Add(run.Trace);
            string[] byRef = [.. parameters.Where(static p => p.Kind != IrParameterKind.In).Select(static p => p.Var.Name)];
            IrValue? value = run.Outcome is IrReturned returned ? returned.Value : resultType is null ? null : IrUnroller.Default(resultType);
            return new IrCallResult(value, run.Outcome is IrThrew) { Heap = [.. heap.Select(h => run.Outs[Array.IndexOf(byRef, h.Map)])] };
        }
    }
}
