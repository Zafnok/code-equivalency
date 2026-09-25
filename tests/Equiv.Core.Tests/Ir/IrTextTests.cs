using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

public sealed class IrTextTests
{
    /// <summary>Every construct of the format once, in dump spelling.</summary>
    private const string EveryConstruct = """
        proc "Ns.T::M(\"q\\\n)" (%a "a": bv32, ref %r "r": bv64, out %o: bool, %s: sort "System.String", %m: map<bv8, map<bool, bv16>>) -> sort "System.String" entry B0
        B0:
          %t0: bv32 = const bv32 4294967295
          %t1: bool = const bool true
          %t2: bool = const bool false
          %t3: sort "System.String" = const sort "System.String" 7
          %t4: map<bv8, bool> = const map<bv8, bool> [bv8 1 -> bool true, bv8 2 -> bool false] default bool false
          %t5: map<bv8, bool> = const map<bv8, bool> [] default bool true
          %t6: bv32 = sub %a, %t0
          %t7: bool = overflows usub %a, %t0
          %t8: bv8 = trunc %a
          %t9: bv32 = call "Svc::F"(%a, %s) threw %t10: bool
          call "Svc::Log"()
          %t11: map<bool, bv16> = mapread %m, %t8
          %t12: map<bv8, map<bool, bv16>> = mapwrite %m, %t8, %t11
          %t13: bv64 = opaque "dynamic" at "src/a.cs" 1:2-3:4
          opaque body "lock" at "src/a.cs" 5:6-7:8
          %t15: bv32 = pure "f64.add"(%a, %t0)
          %t16: sort "System.Decimal" = pure "dec.div"!(%s, %s) throws(%t17: bool "System.DivideByZeroException", %t18 "q": bool "System.OverflowException")
          switch %a [bv32 1 -> B1, bv32 2 -> B2] default B3
        B1:
          goto B3
        B2:
          throw "System.Exception" outs(%r = %t13, %o = %t1)
        B3:
          %t14: bv32 = phi [B0: %t6, B1: %a]
          br %t7, B4, B5
        B4:
          ret %t3 outs(%r = %r, %o = %t2)
        B5:
          unreachable

        """;

    [Fact]
    public void DumpWritesEveryConstructInTheDocumentedSpelling()
    {
        Assert.Equal(EveryConstruct.ReplaceLineEndings("\n"), IrText.Dump(IrText.Parse(EveryConstruct)));
    }

    [Fact]
    public void ParseReadsEscapesSourceNamesAndNestedTypes()
    {
        IrProcedure p = IrText.Parse(EveryConstruct);

        Assert.Equal("Ns.T::M(\"q\\\n)", p.Identity.Value);
        Assert.Equal(new IrParameter(new IrVar("r", new IrBitVec(64), "r"), IrParameterKind.Ref), p.Parameters[1]);
        Assert.Equal(IrParameterKind.Out, p.Parameters[2].Kind);
        Assert.Equal(new IrMap(new IrBitVec(8), new IrMap(new IrBool(), new IrBitVec(16))), p.Parameters[4].Var.Type);
        Assert.Equal(new IrSort("System.String"), p.ReturnType);
    }

    [Fact]
    public void IrText_RoundTripsRuntimeChangedFlag()
    {
        CallIdentity flagged = new("System.String::IndexOf(char)", RuntimeChanged: true);
        IrProcedure p = new(
            new ProcedureIdentity("P"),
            [],
            ReturnType: null,
            [new IrBlock(new IrBlockId(0), [new IrCall(Target: null, Threw: null, flagged, [])], new IrReturn(Value: null, []))],
            new IrBlockId(0));

        string dumped = IrText.Dump(p);
        Assert.Contains("\"System.String::IndexOf(char)\"!", dumped, StringComparison.Ordinal);

        IrCall parsed = Assert.IsType<IrCall>(IrText.Parse(dumped).Blocks[0].Instructions[0]);
        Assert.Equal(flagged, parsed.Callee);
        Assert.True(parsed.Callee.RuntimeChanged);
    }

    [Fact]
    public void WholeBodyFlagRoundTripsInIrText()
    {
        SourceSpan span = new("a.cs", 1, 2, 3, 4);
        IrOpaque wholeBody = new(Target: null, "lock", span) { WholeBody = true };
        IrOpaque expression = new(Target: null, "dynamic", span);
        IrProcedure p = new(
            new ProcedureIdentity("P"),
            [],
            ReturnType: null,
            [new IrBlock(new IrBlockId(0), [wholeBody, expression], new IrReturn(Value: null, []))],
            new IrBlockId(0));

        string dumped = IrText.Dump(p);
        Assert.Contains("opaque body \"lock\" at", dumped, StringComparison.Ordinal);
        Assert.Contains("opaque \"dynamic\" at", dumped, StringComparison.Ordinal);

        IrProcedure parsed = IrText.Parse(dumped);
        Assert.Equal<IrInstruction>([wholeBody, expression], parsed.Blocks[0].Instructions);
        Assert.NotEqual(wholeBody, expression with { Reason = "lock" });
    }

    [Fact]
    public void IrText_DoesNotSuffixAnUnflaggedCallee()
    {
        IrProcedure p = new(
            new ProcedureIdentity("P"),
            [],
            ReturnType: null,
            [new IrBlock(new IrBlockId(0), [new IrCall(Target: null, Threw: null, new CallIdentity("F"), [])], new IrReturn(Value: null, []))],
            new IrBlockId(0));

        Assert.Contains("call \"F\"()", IrText.Dump(p), StringComparison.Ordinal);
    }

    [Fact]
    public void MapEntriesAreDumpedInTextOrder()
    {
        IrMap type = new(new IrBitVec(8), new IrBitVec(8));
        IrMapValue map = new(
            type,
            new IrBitVecValue(8, 0),
            ImmutableDictionary<IrValue, IrValue>.Empty
                .Add(new IrBitVecValue(8, 20), new IrBitVecValue(8, 1))
                .Add(new IrBitVecValue(8, 3), new IrBitVecValue(8, 1)));
        IrProcedure p = new(
            new ProcedureIdentity("P"),
            [],
            ReturnType: null,
            [new IrBlock(new IrBlockId(0), [new IrConst(new IrVar("m", type), map)], new IrReturn(Value: null, []))],
            new IrBlockId(0));

        Assert.Contains("[bv8 20 -> bv8 1, bv8 3 -> bv8 1]", IrText.Dump(p), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAcceptsHandWrittenLayoutAndNegativeLiterals()
    {
        IrProcedure p = IrText.Parse("proc \"P\" () -> bv8 entry B0 B0: %_x.1$: bv8 = const bv8 -128 %y: bv64 = const bv64 -1 ret %_x.1$");

        IrConst first = Assert.IsType<IrConst>(p.Blocks[0].Instructions[0]);
        IrConst second = Assert.IsType<IrConst>(p.Blocks[0].Instructions[1]);
        Assert.Equal("_x.1$", first.Target.Name);
        Assert.Equal(new IrBitVecValue(8, 0x80), first.Value);
        Assert.Equal(new IrBitVecValue(64, ulong.MaxValue), second.Value);
    }

    [Theory]
    [InlineData("proc \"P\" () entry B0\nB0:\n  %: bv8 = const bv8 1", 3, 3, "variable name")]
    [InlineData("proc \"P", 1, 6, "unterminated string")]
    [InlineData("proc \"P\n\"", 1, 6, "unterminated string")]
    [InlineData("proc \"P\\t\"", 1, 6, "unknown escape")]
    [InlineData("proc \"P\\", 1, 6, "unknown escape")]
    [InlineData("proc @", 1, 6, "unexpected character")]
    [InlineData("func", 1, 1, "expected 'proc' but found 'func'")]
    [InlineData("proc \"P\" (", 1, 11, "but found end of input")]
    [InlineData("proc \"P\" () -> bv12 entry B0", 1, 16, "unknown type 'bv12'")]
    [InlineData("proc \"P\" () -> map<bool bool> entry B0", 1, 25, "expected ','")]
    [InlineData("proc \"P\" () entry C0", 1, 19, "block id")]
    [InlineData("proc \"P\" () entry B", 1, 19, "block id")]
    [InlineData("proc \"P\" () entry B0 B0: %x: bool = const bool yes ret", 1, 48, "true or false")]
    [InlineData("proc \"P\" () entry B0 B0: %x: bv8 = const bv8 256 ret", 1, 46, "does not fit in 8 bits")]
    [InlineData("proc \"P\" () entry B0 B0: %x: bv8 = const bv8 -129 ret", 1, 47, "does not fit in 8 bits")]
    [InlineData("proc \"P\" () entry B0 B0: %x: bv64 = const bv64 18446744073709551616 ret", 1, 48, "does not fit in 64 bits")]
    [InlineData("proc \"P\" () entry B0 B0: %x: sort \"S\" = const sort \"S\" 2147483648 ret", 1, 56, "out of range")]
    [InlineData("proc \"P\" () entry B0 B0: %x: bv8 = frob %x ret", 1, 36, "unknown instruction 'frob'")]
    [InlineData("proc \"P\" () entry B0 B0: %x: bool = overflows sneg %x, %x ret", 1, 47, "unknown overflow operation 'sneg'")]
    [InlineData("proc \"P\" () entry B0 B0: %x: bv8 = neg %y ret", 1, 40, "undefined variable %y")]
    [InlineData("proc \"P\" () entry B0 B0: jump B0", 1, 26, "expected an instruction or terminator but found 'jump'")]
    [InlineData("proc \"P\" () entry B0 B0: ret 1", 1, 30, "expected a block id but found '1'")]
    [InlineData("proc \"P\" () entry B0 B0: 5", 1, 26, "expected an instruction or terminator but found '5'")]
    public void ParseErrorsCarryLineAndColumn(string text, int line, int column, string message)
    {
        IrParseException error = Assert.Throws<IrParseException>(() => IrText.Parse(text));

        Assert.Equal((line, column), (error.Line, error.Column));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseExceptionHasTheStandardConstructors()
    {
        InvalidOperationException inner = new("inner");

        Assert.Equal(0, new IrParseException().Line);
        Assert.Equal("m", new IrParseException("m").Message);
        Assert.Same(inner, new IrParseException("m", inner).InnerException);
        Assert.Equal("2:3: m", new IrParseException("m", 2, 3).Message);
    }
}
