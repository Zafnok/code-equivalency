using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>One valid and at least one invalid case per rule id.</summary>
public sealed class IrValidatorTests
{
    private const string Diamond = """
        proc "T::M" (%a: bv32, ref %r: bv32) -> bv32 entry B0
        B0:
          %c: bool = slt %a, %r
          br %c, B1, B2
        B1:
          %x: bv32 = add %a, %a
          goto B3
        B2:
          %y: bv32 = sub %a, %a
          goto B3
        B3:
          %z: bv32 = phi [B1: %x, B2: %y]
          ret %z outs(%r = %z)
        """;

    [Fact]
    public void AWellFormedDiamondIsClean()
    {
        Assert.Empty(IrValidator.Validate(IrText.Parse(Diamond)));
    }

    [Fact]
    public void IR001DuplicateBlockId()
    {
        IrProcedure p = IrText.Parse(Diamond);
        Assert.Equal([IrDiagnosticIds.DuplicateBlockId], Ids(p with { Blocks = p.Blocks.Add(new IrBlock(new IrBlockId(3), [], new IrUnreachable())) }));
    }

    [Fact]
    public void IR002MissingEntry()
    {
        Assert.Equal([IrDiagnosticIds.MissingEntry], Ids(IrText.Parse(Diamond) with { Entry = new IrBlockId(9) }));
    }

    [Fact]
    public void IR003MultipleAssignment()
    {
        Assert.Equal([IrDiagnosticIds.MultipleAssignment], Ids("""
            proc "T::M" (%a: bv32) entry B0
            B0:
              %a: bv32 = const bv32 1
              ret
            """));
    }

    [Fact]
    public void IR004UseBeforeDefinitionInTheSameBlock()
    {
        Assert.Equal([IrDiagnosticIds.UseNotDominated], Ids("""
            proc "T::M" () entry B0
            B0:
              %x: bv32 = neg %y
              %y: bv32 = const bv32 1
              ret
            """));
    }

    [Fact]
    public void IR004UseInASiblingBranch()
    {
        Assert.Equal([IrDiagnosticIds.UseNotDominated], Ids(Diamond.Replace("%y: bv32 = sub %a, %a", "%y: bv32 = sub %x, %a", StringComparison.Ordinal)));
    }

    [Fact]
    public void IR004PhiOperandMustDominateTheEndOfItsPredecessor()
    {
        Assert.Equal([IrDiagnosticIds.UseNotDominated], Ids(Diamond.Replace("[B1: %x, B2: %y]", "[B1: %y, B2: %y]", StringComparison.Ordinal)));
    }

    [Fact]
    public void IR004DefinitionInUnreachableCodeDoesNotDominate()
    {
        Assert.Equal([IrDiagnosticIds.UseNotDominated], Ids("""
            proc "T::M" () -> bv32 entry B0
            B0:
              %x: bv32 = const bv32 1
              goto B2
            B1:
              %y: bv32 = const bv32 2
              goto B2
            B2:
              %z: bv32 = add %y, %x
              ret %z
            """));
    }

    [Fact]
    public void IR004UnreachableCodeOnlyNeedsDefinitions()
    {
        Assert.Empty(IrValidator.Validate(IrText.Parse("""
            proc "T::M" () entry B0
            B0:
              ret
            B1:
              %x: bv32 = add %y, %y
              %y: bv32 = const bv32 1
              ret
            """)));
    }

    [Fact]
    public void IR004UndefinedOrMismatchedUse()
    {
        IrBitVec bv32 = new(32);
        IrVar x = new("x", bv32);
        IrProcedure undefined = new(
            new ProcedureIdentity("T::M"),
            [],
            ReturnType: null,
            [new IrBlock(new IrBlockId(0), [new IrUnary(new IrVar("y", bv32), IrUnaryOp.Neg, x)], new IrReturn(Value: null, []))],
            new IrBlockId(0));
        IrProcedure mismatched = undefined with
        {
            Parameters = [new IrParameter(x with { SourceName = "x" }, IrParameterKind.In)],
        };

        Assert.Equal([IrDiagnosticIds.UseNotDominated], Ids(undefined));
        Assert.Equal([IrDiagnosticIds.UseNotDominated], Ids(mismatched));
    }

    [Fact]
    public void IR004LoopBodiesAreDominatedByTheirHeader()
    {
        Assert.Empty(IrValidator.Validate(IrText.Parse("""
            proc "T::M" (%n: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              goto B1
            B1:
              %i: bv32 = phi [B0: %zero, B3: %j]
              %more: bool = ult %i, %n
              br %more, B2, B4
            B2:
              %odd: bool = eq %i, %one
              br %odd, B3, B5
            B5:
              goto B3
            B3:
              %j: bv32 = add %i, %one
              goto B1
            B4:
              ret %i
            """)));
    }

    [Fact]
    public void IR005PhiPredecessors()
    {
        Assert.Equal([IrDiagnosticIds.PhiPredecessors], Ids(Diamond.Replace("[B1: %x, B2: %y]", "[B1: %x]", StringComparison.Ordinal)));
        Assert.Equal([IrDiagnosticIds.PhiPredecessors], Ids(Diamond.Replace("[B1: %x, B2: %y]", "[B1: %x, B1: %x, B2: %y]", StringComparison.Ordinal)));
    }

    [Fact]
    public void IR006PhiAfterAnInstruction()
    {
        Assert.Equal([IrDiagnosticIds.PhiPlacement], Ids(Diamond.Replace(
            "  %z: bv32 = phi [B1: %x, B2: %y]",
            "  %w: bv32 = const bv32 0\n  %z: bv32 = phi [B1: %x, B2: %y]",
            StringComparison.Ordinal)));
    }

    [Fact]
    public void IR006PhiInTheEntryBlock()
    {
        Assert.Equal([IrDiagnosticIds.PhiPlacement], Ids("""
            proc "T::M" () entry B0
            B0:
              %z: bv32 = phi []
              ret
            """));
    }

    [Theory]
    [InlineData("%t: bool = const bv32 1")]
    [InlineData("%t: bv32 = add %a, %b8")]
    [InlineData("%t: bool = add %a, %a")]
    [InlineData("%t: bv32 = slt %a, %a")]
    [InlineData("%t: bool = add %c, %c")]
    [InlineData("%t: bv32 = and %c, %c")]
    [InlineData("%t: bool = slt %s, %s")]
    [InlineData("%t: bool = eq %m, %m")]
    [InlineData("%t: bool = overflows sadd %c, %c")]
    [InlineData("%t: bool = overflows sadd %a, %b8")]
    [InlineData("%t: bv32 = overflows sadd %a, %a")]
    [InlineData("%t: bool = boolnot %a")]
    [InlineData("%t: bv32 = boolnot %c")]
    [InlineData("%t: bv32 = neg %c")]
    [InlineData("%t: bool = neg %a")]
    [InlineData("%t: bv64 = neg %a")]
    [InlineData("%t: bv8 = zext %a")]
    [InlineData("%t: bv32 = sext %a")]
    [InlineData("%t: bv64 = trunc %a")]
    [InlineData("%t: bool = call \"F\"() threw %u: bv32")]
    [InlineData("%t: bool = opaque \"x\" at \"f.cs\" 1:1-1:2 fragment \"f\" reads(%a) threw %u: bv32")]
    public void IR007OperandTypes(string instruction)
    {
        Assert.Equal([IrDiagnosticIds.OperandTypes], Ids(WithInstruction(instruction)));
    }

    [Theory]
    [InlineData("%t: bool = const bool true")]
    [InlineData("%t: bool = slt %a, %a")]
    [InlineData("%t: bv32 = and %a, %a")]
    [InlineData("%t: bool = and %c, %c")]
    [InlineData("%t: bool = ne %s, %s")]
    [InlineData("%t: bool = overflows umul %a, %a")]
    [InlineData("%t: bool = boolnot %c")]
    [InlineData("%t: bv32 = not %a")]
    [InlineData("%t: bv64 = sext %a")]
    [InlineData("%t: bv8 = trunc %a")]
    [InlineData("%t: bv32 = call \"F\"() threw %u: bool")]
    [InlineData("call \"F\"(%a, %m)")]
    [InlineData("%t: bool = mapread %m, %a")]
    [InlineData("%t: map<bv32, bool> = mapwrite %m, %a, %c")]
    [InlineData("opaque \"x\" at \"f.cs\" 1:1-1:2")]
    [InlineData("%t: bool = opaque \"x\" at \"f.cs\" 1:1-1:2 fragment \"f\" reads(%a, %s) threw %u: bool")]
    public void IR007AndIR009AcceptWellTypedInstructions(string instruction)
    {
        Assert.Empty(IrValidator.Validate(WithInstruction(instruction)));
    }

    [Theory]
    [InlineData("%t: bool = mapread %a, %a")]
    [InlineData("%t: bool = mapread %m, %c")]
    [InlineData("%t: bv32 = mapread %m, %a")]
    [InlineData("%t: map<bv32, bool> = mapwrite %a, %a, %c")]
    [InlineData("%t: map<bv32, bool> = mapwrite %m, %c, %c")]
    [InlineData("%t: map<bv32, bool> = mapwrite %m, %a, %a")]
    [InlineData("%t: map<bv8, bool> = mapwrite %m, %a, %c")]
    public void IR009MapTypes(string instruction)
    {
        Assert.Equal([IrDiagnosticIds.MapTypes], Ids(WithInstructionText(instruction)));
    }

    [Fact]
    public void IR007PhiIncomingTypes()
    {
        Assert.Equal([IrDiagnosticIds.OperandTypes], Ids(Diamond.Replace("%y: bv32 = sub %a, %a", "%y: bool = slt %a, %a", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("br %a, B1, B1", IrDiagnosticIds.OperandTypes)]
    [InlineData("switch %a [bool true -> B1] default B1", IrDiagnosticIds.OperandTypes)]
    [InlineData("ret", IrDiagnosticIds.OperandTypes)]
    [InlineData("ret %c", IrDiagnosticIds.OperandTypes)]
    [InlineData("goto B7", IrDiagnosticIds.MissingTarget)]
    [InlineData("br %c, B7, B1", IrDiagnosticIds.MissingTarget)]
    public void TerminatorRules(string terminator, string expected)
    {
        Assert.Equal([expected], Ids(WithTerminator(terminator)));
    }

    [Theory]
    [InlineData("br %c, B1, B1")]
    [InlineData("switch %a [bv32 1 -> B1] default B1")]
    [InlineData("throw \"E\"")]
    [InlineData("unreachable")]
    public void WellTypedTerminators(string terminator)
    {
        Assert.Empty(IrValidator.Validate(IrText.Parse(WithTerminatorText(terminator))));
    }

    [Theory]
    [InlineData("ret %z")]
    [InlineData("ret %z outs(%r = %z, %r = %z)")]
    [InlineData("ret %z outs(%a = %z)")]
    [InlineData("throw \"E\"")]
    public void IR010ExitOuts(string exit)
    {
        Assert.Equal([IrDiagnosticIds.ExitOuts], Ids(Diamond.Replace("ret %z outs(%r = %z)", exit, StringComparison.Ordinal)));
    }

    [Fact]
    public void IR010FinalValueTypeMustMatchTheParameter()
    {
        Assert.Equal([IrDiagnosticIds.ExitOuts], Ids(Diamond.Replace("ret %z outs(%r = %z)", "ret %z outs(%r = %c)", StringComparison.Ordinal)));
    }

    [Fact]
    public void DiagnosticsNameTheBlock()
    {
        IrDiagnostic diagnostic = Assert.Single(IrValidator.Validate(IrText.Parse(WithTerminatorText("goto B7"))));
        Assert.Equal(new IrBlockId(0), diagnostic.Block);
        Assert.Contains("B7", diagnostic.Message, StringComparison.Ordinal);
    }

    private static IrProcedure WithInstruction(string instruction) => IrText.Parse(WithInstructionText(instruction));

    private static string WithInstructionText(string instruction) => $$"""
        proc "T::M" (%a: bv32, %b8: bv8, %c: bool, %s: sort "S", %m: map<bv32, bool>) entry B0
        B0:
          {{instruction}}
          ret
        """;

    private static IrProcedure WithTerminator(string terminator) => IrText.Parse(WithTerminatorText(terminator));

    private static string WithTerminatorText(string terminator) => $$"""
        proc "T::M" (%a: bv32, %c: bool) -> bv32 entry B0
        B0:
          {{terminator}}
        B1:
          ret %a
        """;

    private const string HeapCall = """
        proc "T::M" (%a: bv32, ref %field.C.x: map<bv32, bv32>, ref %field.C.y: map<bv32, bv32>, ref %r: bv32, %m: map<bv32, bv32>) entry B0
        B0:
          call "F"(%a) heap({{pairs}})
          ret outs(%field.C.x = %field.C.x, %field.C.y = %field.C.y, %r = %r)
        """;

    [Fact]
    public void IrCallValidator_AcceptsOnePairPerByRefMap()
    {
        Assert.Empty(Ids(HeapCall.Replace("{{pairs}}", "\"field.C.x\" %field.C.x -> %x1: map<bv32, bv32>, \"field.C.y\" %field.C.y -> %y1: map<bv32, bv32>", StringComparison.Ordinal)));
    }

    [Fact]
    public void IrCallValidator_RejectsARepeatedMap()
    {
        Assert.Equal(
            [IrDiagnosticIds.HeapPairRepeated],
            Ids(HeapCall.Replace("{{pairs}}", "\"field.C.x\" %field.C.x -> %x1: map<bv32, bv32>, \"field.C.x\" %field.C.x -> %x2: map<bv32, bv32>", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("\"field.C.z\" %field.C.x -> %x1: map<bv32, bv32>")]
    [InlineData("\"r\" %r -> %r1: bv32")]
    [InlineData("\"m\" %m -> %m1: map<bv32, bv32>")]
    [InlineData("\"field.C.x\" %a -> %x1: map<bv32, bv32>")]
    [InlineData("\"field.C.x\" %field.C.x -> %x1: map<bv32, bool>")]
    public void IrCallValidator_RejectsAPairThatIsNotAByRefMapOfItsType(string pair)
    {
        Assert.Equal([IrDiagnosticIds.HeapPairMap], Ids(HeapCall.Replace("{{pairs}}", pair, StringComparison.Ordinal)));
    }

    [Fact]
    public void IrCallValidator_RejectsAnAfterThatReusesAnSsaName()
    {
        Assert.Equal(
            [IrDiagnosticIds.MultipleAssignment],
            Ids(HeapCall.Replace("{{pairs}}", "\"field.C.x\" %field.C.x -> %field.C.y: map<bv32, bv32>", StringComparison.Ordinal)));
    }

    [Fact]
    public void IrCallValidator_RejectsABeforeThatIsNotDefined()
    {
        IrProcedure p = IrText.Parse(HeapCall.Replace("{{pairs}}", "\"field.C.x\" %field.C.x -> %x1: map<bv32, bv32>", StringComparison.Ordinal));
        IrCall call = (IrCall)p.Blocks[0].Instructions[0];
        IrVar stranger = new("stranger", call.Heap[0].Before.Type);
        IrProcedure changed = p with
        {
            Blocks = [p.Blocks[0] with { Instructions = [call with { Heap = [call.Heap[0] with { Before = stranger }] }] }],
        };

        Assert.Equal([IrDiagnosticIds.UseNotDominated], Ids(changed));
    }

    /// <summary>Ticket M4-004: a fragment's heap pairs follow a call's rules.</summary>
    [Theory]
    [InlineData("\"field.C.x\" %field.C.x -> %x1: map<bv32, bv32>, \"field.C.x\" %field.C.x -> %x2: map<bv32, bv32>", IrDiagnosticIds.HeapPairRepeated)]
    [InlineData("\"m\" %m -> %m1: map<bv32, bv32>", IrDiagnosticIds.HeapPairMap)]
    public void AFragmentsHeapPairsFollowTheCallRules(string pairs, string expected)
    {
        string fragment = HeapCall.Replace("call \"F\"(%a)", "opaque \"x\" at \"f.cs\" 1:1-1:2 fragment \"f\" reads(%a)", StringComparison.Ordinal);

        Assert.Equal([expected], Ids(fragment.Replace("{{pairs}}", pairs, StringComparison.Ordinal)));
    }

    [Fact]
    public void AFragmentsReadMustBeDefined()
    {
        IrProcedure p = WithInstruction("opaque \"x\" at \"f.cs\" 1:1-1:2 fragment \"f\" reads(%a)");
        IrOpaque fragment = (IrOpaque)p.Blocks[0].Instructions[0];
        IrProcedure changed = p with { Blocks = [p.Blocks[0] with { Instructions = [fragment with { Reads = [new IrVar("stranger", fragment.Reads[0].Type)] }] }] };

        Assert.Equal([IrDiagnosticIds.UseNotDominated], Ids(changed));
    }

    private static string[] Ids(string text) => Ids(IrText.Parse(text));

    private static string[] Ids(IrProcedure procedure) => [.. IrValidator.Validate(procedure).Select(static d => d.Id)];
}
