using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Floating-point, <c>decimal</c> and user-defined operators and conversions lower to <see cref="IrPure"/> applications of
/// the functions in <see cref="PureCatalogue"/> (ADR 0025; ticket M4-002 criteria 3 and 4), each exception flag branching to
/// a throw of its exact type.
/// </summary>
public sealed class PureLoweringTests
{
    private const string Decimal = "System.Decimal";

    [Fact]
    public void DecimalAdditionIsASharedPureFunction()
    {
        IrProcedure procedure = Lowered.Method("static decimal M(decimal a, decimal b) => a + b;");

        IrPure pure = Assert.Single(Pures(procedure));
        Assert.Equal("dec.add", pure.Function);
        Assert.Equal<IrVar>([procedure.Parameters[0].Var, procedure.Parameters[1].Var], pure.Args);
        Assert.Equal(new IrSort(Decimal), pure.Target.Type);
        Assert.Equal(PureCatalogue.Overflow, Assert.Single(pure.Throws).ExceptionType);
        Assert.False(pure.RuntimeSensitive);
        Assert.Empty(Lowered.Calls(procedure));
        Assert.Empty(Lowered.Opaques(procedure));
        Assert.Equal(new IrThrew(PureCatalogue.Overflow), Run(procedure, new Answers(Element(3), true)));
        Assert.Equal(new IrReturned(Element(3)), Run(procedure, new Answers(Element(3), false)));
    }

    [Fact]
    public void DecimalDivisionThrowsDivideByZeroExactly()
    {
        IrProcedure procedure = Lowered.Method("""
            static decimal M(decimal a, decimal b)
            {
                try { return a / b; }
                catch (DivideByZeroException) { return -1m; }
            }
            """);

        IrPure pure = Assert.Single(Pures(procedure));
        Assert.Equal("dec.div", pure.Function);
        Assert.Equal([PureCatalogue.DivideByZero, PureCatalogue.Overflow], pure.Throws.Select(static t => t.ExceptionType), StringComparer.Ordinal);
        Assert.Equal(new IrReturned(TypeMapper.Constant(Compilation().GetSpecialType(SpecialType.System_Decimal), -1m)), Run(procedure, new Answers(Element(3), true, false)));
        Assert.Equal(new IrThrew(PureCatalogue.Overflow), Run(procedure, new Answers(Element(3), false, true)));
        Assert.Equal(new IrReturned(Element(3)), Run(procedure, new Answers(Element(3), false, false)));
    }

    [Theory]
    [InlineData("-", "dec.sub", 1)]
    [InlineData("*", "dec.mul", 1)]
    [InlineData("%", "dec.rem", 2)]
    [InlineData("<", "dec.lt", 0)]
    [InlineData("==", "dec.eq", 0)]
    public void EveryDecimalOperatorIsItsCataloguedFunction(string op, string function, int throws)
    {
        IrPure pure = Assert.Single(Pures(Lowered.Method($"static object M(decimal a, decimal b) => a {op} b;")));

        Assert.Equal(function, pure.Function);
        Assert.Equal(throws, pure.Throws.Length);
    }

    [Theory]
    [InlineData("double", "<", "f64.lt")]
    [InlineData("double", "==", "f64.eq")]
    [InlineData("double", "!=", "f64.ne")]
    [InlineData("float", ">=", "f32.ge")]
    public void DoubleComparisonIsPure(string type, string op, string function)
    {
        IrProcedure procedure = Lowered.Method($"static bool M({type} a, {type} b) => a {op} b;");

        IrPure pure = Assert.Single(Pures(procedure));
        Assert.Equal(function, pure.Function);
        Assert.Equal(new IrBool(), pure.Target.Type);
        Assert.Empty(pure.Throws);
        Assert.False(pure.RuntimeSensitive);
        Assert.DoesNotContain(procedure.Blocks.SelectMany(static b => b.Instructions), static i => i is IrBinary);
    }

    [Theory]
    [InlineData("static double M(double a, double b) => a / b;", "f64.div")]
    [InlineData("static float M(float a, float b) => a % b;", "f32.rem")]
    [InlineData("static double M(double a) => -a;", "f64.neg")]
    [InlineData("static decimal M(decimal a) => -a;", "dec.neg")]
    public void FloatingPointArithmeticNeverThrows(string members, string function)
    {
        IrPure pure = Assert.Single(Pures(Lowered.Method(members)));

        Assert.Equal(function, pure.Function);
        Assert.Empty(pure.Throws);
    }

    [Theory]
    [InlineData("double", "System.Double")]
    [InlineData("decimal", Decimal)]
    public void UnaryPlusIsItsOperand(string type, string sort)
    {
        IrProcedure procedure = Lowered.Method($"static {type} M({type} a) => +a;");

        Assert.Empty(Pures(procedure));
        Assert.Equal(new IrReturned(Element(7, sort)), Run(procedure, new Answers(Element(1)), Element(7, sort)));
    }

    [Theory]
    [InlineData("static double M(int a) => a;", "conv.i32.f64", 0)]
    [InlineData("static decimal M(long a) => a;", "conv.i64.dec", 0)]
    [InlineData("static double M(float a) => a;", "conv.f32.f64", 0)]
    [InlineData("static float M(double a) => (float)a;", "conv.f64.f32", 0)]
    [InlineData("static decimal M(double a) => (decimal)a;", "conv.f64.dec", 1)]
    [InlineData("static double M(decimal a) => (double)a;", "conv.dec.f64", 0)]
    [InlineData("static int M(decimal a) => (int)a;", "conv.dec.i32", 1)]
    [InlineData("static int M(decimal a) => unchecked((int)a);", "conv.dec.i32", 1)]
    [InlineData("static char M(decimal a) => (char)a;", "conv.dec.char", 1)]
    [InlineData("static decimal M(char a) => a;", "conv.char.dec", 0)]
    public void ConversionsToAndFromFloatingPointAndDecimalAreCatalogued(string members, string function, int throws)
    {
        IrPure pure = Assert.Single(Pures(Lowered.Method(members)));

        Assert.Equal(function, pure.Function);
        Assert.Equal(throws, pure.Throws.Length);
        Assert.All(pure.Throws, static t => Assert.Equal(PureCatalogue.Overflow, t.ExceptionType));
        Assert.False(pure.RuntimeSensitive);
    }

    [Theory]
    [InlineData("static int M(double a) => (int)a;", "conv.f64.i32", 0)]
    [InlineData("static ulong M(float a) => (ulong)a;", "conv.f32.u64", 0)]
    [InlineData("static int M(double a) => checked((int)a);", "conv.f64.i32", 1)]
    public void AFloatToIntConversionIsMarkedRuntimeSensitive(string members, string function, int throws)
    {
        IrPure pure = Assert.Single(Pures(Lowered.Method(members)));

        Assert.Equal(function, pure.Function);
        Assert.True(pure.RuntimeSensitive);
        Assert.Equal(throws, pure.Throws.Length);
    }

    [Theory]
    [InlineData("==", "op:System.String::op_Equality(string,string)")]
    [InlineData("!=", "op:System.String::op_Inequality(string,string)")]
    public void StringEqualityIsTheUserDefinedOperator(string op, string function)
    {
        IrProcedure procedure = Lowered.Method($"static bool M(string a, string b) => a {op} b;");

        IrPure pure = Assert.Single(Pures(procedure));
        Assert.Equal(function, pure.Function);
        Assert.Equal(new IrBool(), pure.Target.Type);
        Assert.Equal(PureCatalogue.AnyException, Assert.Single(pure.Throws).ExceptionType);
        Assert.Empty(Lowered.Calls(procedure));
    }

    [Fact]
    public void UserDefinedOperatorsAndConversionsAreTheirOpFunctions()
    {
        const string Money = """
            struct Money
            {
                public static Money operator +(Money a, Money b) => a;
                public static Money operator -(Money a) => a;
                public static explicit operator decimal(Money m) => 0m;
                public static implicit operator Money(int n) => default;
            }
            """;

        Assert.Equal(
            ["op:Money::op_Addition(Money,Money)", "op:Money::op_UnaryNegation(Money)", "op:Money::op_Implicit(int)", "op:Money::op_Explicit(Money)", "op:Money::op_Explicit(Money)", "dec.add"],
            Functions(Money + "class C { static decimal M(Money a, Money b, int n) { Money c = -(a + b); Money d = n; return (decimal)c + (decimal)d; } }"));
    }

    [Theory]
    [InlineData("static double? M(double? a, double? b) => a + b;", "Binary")]
    [InlineData("static double? M(double? a) => -a;", "Unary")]
    [InlineData("static Money? M(Money? a, Money? b) => a + b;", "Binary")]
    [InlineData("static int? M(Money? a) => (int?)a;", "Conversion")]
    public void LiftedOrWidenedOperatorsStayOpaque(string members, string reason)
    {
        IrProcedure procedure = Lowered.Source($"using System;\nstruct Money {{ public static Money operator +(Money a, Money b) => a; public static explicit operator int(Money m) => 0; }}\nclass C\n{{\n{members}\n}}\n");

        Assert.Contains(Lowered.Opaques(procedure), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));
    }

    public static TheoryData<string, string, bool> CompoundOperators()
    {
        TheoryData<string, string, bool> data = [];
        foreach (string type in (string[])["float", "double", "decimal"])
        {
            foreach (string op in (string[])["+", "-", "*", "/", "%"])
            {
                foreach (bool isChecked in (bool[])[false, true])
                {
                    data.Add(type, op, isChecked);
                }
            }
        }

        return data;
    }

    /// <summary>Ticket P2-022 criteria 1 and 2: <c>x op= y</c> applies the function <c>x = x op y</c> does, on an x87 legacy side too.</summary>
    [Theory]
    [MemberData(nameof(CompoundOperators))]
    public void CompoundAssignmentMatchesItsBinaryOperator(string type, string op, bool isChecked)
    {
        string context = isChecked ? "checked" : "unchecked";
        IrProcedure compound = X87Legacy($"class C {{ static {type} M({type} x, {type} y) {{ {context} {{ x {op}= y; }} return x; }} }}");
        IrProcedure expanded = X87Legacy($"class C {{ static {type} M({type} x, {type} y) {{ {context} {{ x = x {op} y; }} return x; }} }}");

        IrPure pure = Assert.Single(Pures(compound));
        IrPure binary = Assert.Single(Pures(expanded));
        Assert.Equal(binary.Function, pure.Function);
        Assert.Equal(binary.Throws.Select(static t => t.ExceptionType), pure.Throws.Select(static t => t.ExceptionType), StringComparer.Ordinal);
        Assert.Equal(binary.RuntimeSensitive, pure.RuntimeSensitive);
        Assert.Equal(!string.Equals(type, "decimal", StringComparison.Ordinal), pure.RuntimeSensitive);
        Assert.Equal<IrVar>([compound.Parameters[0].Var, compound.Parameters[1].Var], pure.Args);
        Assert.Empty(Lowered.Opaques(compound));
    }

    /// <summary>Ticket P2-022 criterion 3: a flag branches to its throw before the target is written.</summary>
    [Theory]
    [InlineData("+=", "OverflowException", true)]
    [InlineData("/=", "DivideByZeroException", true, false)]
    [InlineData("/=", "OverflowException", false, true)]
    public void ACompoundOverflowLeavesTheTargetUnwritten(string op, string caught, params bool[] flags)
    {
        IrProcedure local = Lowered.Method($"static decimal M(decimal a, decimal b) {{ try {{ a {op} b; }} catch ({caught}) {{ }} return a; }}");
        Assert.Equal(new IrReturned(Element(1)), Run(local, new Answers(Element(3), flags)));
        Assert.Equal(new IrReturned(Element(3)), Run(local, new Answers(Element(3), [.. flags.Select(static _ => false)])));

        IrProcedure property = Lowered.Method($"static decimal F; static decimal P {{ get {{ return F; }} set {{ F = value; }} }} static void M(decimal b) {{ P {op} b; }}");
        Accessors accessors = new();
        IrRun thrown = IrInterpreter.Run(property, new IrInputs([Element(2)]), accessors, 100, pure: new Answers(Element(3), flags));
        Assert.Equal(new IrThrew($"System.{caught}"), thrown.Outcome);
        Assert.Equal(["C::get_P()"], thrown.Trace.Select(static c => c.Callee.Value), StringComparer.Ordinal);
        IrRun written = IrInterpreter.Run(property, new IrInputs([Element(2)]), accessors, 100, pure: new Answers(Element(3), [.. flags.Select(static _ => false)]));
        Assert.Equal(["C::get_P()", "C::set_P(decimal)"], written.Trace.Select(static c => c.Callee.Value), StringComparer.Ordinal);
        Assert.Equal(Element(3), written.Trace[1].Arguments[0]);
    }

    /// <summary>Ticket P2-022 criterion 4: <c>++</c> and <c>--</c> add or subtract the literal <c>1</c>; postfix yields the value read.</summary>
    [Theory]
    [InlineData("float", "System.Single", "1f", "f32")]
    [InlineData("double", "System.Double", "1d", "f64")]
    [InlineData("decimal", Decimal, "1m", "dec")]
    public void IncrementIsAddingTheLiteralOne(string type, string sort, string one, string code)
    {
        foreach ((string body, string op, string symbol, bool postfix) in (ReadOnlySpan<(string, string, string, bool)>)[("x++", "add", "+", true), ("++x", "add", "+", false), ("x--", "sub", "-", true), ("--x", "sub", "-", false)])
        {
            IrProcedure step = Lowered.Method($"static {type} M({type} x) => {body};");
            IrProcedure expanded = Lowered.Method($"static {type} M({type} x) => x {symbol} {one};");

            IrPure pure = Assert.Single(Pures(step));
            IrPure binary = Assert.Single(Pures(expanded));
            Assert.Equal($"{code}.{op}", pure.Function);
            Assert.Equal(binary.Function, pure.Function);
            Assert.Equal(binary.Throws.Select(static t => t.ExceptionType), pure.Throws.Select(static t => t.ExceptionType), StringComparer.Ordinal);
            Assert.Equal(ConstantOf(expanded, binary.Args[1]), ConstantOf(step, pure.Args[1]));
            Assert.Equal(step.Parameters[0].Var, pure.Args[0]);
            Assert.Equal(new IrReturned(postfix ? Element(1, sort) : Element(9, sort)), Run(step, new Answers(Element(9, sort), false, false), Element(1, sort)));
        }
    }

    [Fact]
    public void DecimalIncrementHasTheOverflowFlag()
    {
        IrPure pure = Assert.Single(Pures(Lowered.Method("static decimal M(decimal m) => ++m;")));

        Assert.Equal("dec.add", pure.Function);
        Assert.Equal(PureCatalogue.Overflow, Assert.Single(pure.Throws).ExceptionType);
    }

    /// <summary>Ticket P2-022 criterion 5: a static user-defined <c>+</c>, <c>++</c> or <c>--</c> is its <c>op:</c> function.</summary>
    [Fact]
    public void UserDefinedCompoundAndIncrementAreTheirOpFunctions()
    {
        IrProcedure procedure = Lowered.Source("""
            struct Money
            {
                public static Money operator +(Money a, Money b) => a;
                public static Money operator ++(Money a) => a;
                public static Money operator --(Money a) => a;
            }
            class C { static Money M(Money a, Money b) { a += b; a++; return --a; } }
            """);

        ImmutableArray<IrPure> pures = Pures(procedure);
        Assert.Equal(["op:Money::op_Addition(Money,Money)", "op:Money::op_Increment(Money)", "op:Money::op_Decrement(Money)"], pures.Select(static p => p.Function), StringComparer.Ordinal);
        Assert.All(pures, static p => Assert.Equal(PureCatalogue.AnyException, Assert.Single(p.Throws).ExceptionType));
        Assert.All(pures, static p => Assert.False(p.RuntimeSensitive));
        Assert.Equal<IrVar>([procedure.Parameters[0].Var, procedure.Parameters[1].Var], pures[0].Args);
        Assert.Equal<IrVar>([pures[0].Target], pures[1].Args);
        Assert.Equal<IrVar>([pures[1].Target], pures[2].Args);
        Assert.Empty(Lowered.Opaques(procedure));
        Assert.Empty(Lowered.Calls(procedure));
        // A `decimal` target whose operator is user-defined is not the catalogue's `dec.add`.
        Assert.Equal(
            ["op:Money::op_Addition(decimal,Money)"],
            Functions("struct Money { public static decimal operator +(decimal a, Money b) => a; } class C { static decimal M(decimal m, Money b) { m += b; return m; } }"));
    }

    /// <summary>Ticket P2-022 criterion 6.</summary>
    [Theory]
    [InlineData("static decimal? M(decimal? m) { m += 1; return m; }", "CompoundAssignment")]
    [InlineData("static decimal? M(decimal? m) { m++; return m; }", "Increment")]
    [InlineData("static double? M(double? d) { d--; return d; }", "Decrement")]
    [InlineData("static Money? M(Money? a, Money b) { a += b; return a; }", "CompoundAssignment")]
    [InlineData("static Money? M(Money? a) { a++; return a; }", "Increment")]
    [InlineData("static Wrapper M(Wrapper w, Money b) { w += b; return w; }", "CompoundAssignment")]
    [InlineData("static Counter M(Counter c, Counter d) { c += d; return c; }", "CompoundAssignment")]
    [InlineData("static Counter M(Counter c) { c++; return c; }", "Increment")]
    [InlineData("static Counter M(Counter c) { --c; return c; }", "Decrement")]
    [InlineData("static decimal M(decimal[] a) { a[0] += 1m; return a[0]; }", "ArrayElementReference")]
    public void LiftedConvertedOrInstanceCompoundOperatorsStayOpaque(string members, string reason)
    {
        IrProcedure procedure = Lowered.Source($$"""
            using System;
            struct Money
            {
                public static Money operator +(Money a, Money b) => a;
                public static Money operator ++(Money a) => a;
            }
            struct Wrapper
            {
                public static implicit operator Money(Wrapper w) => default;
                public static implicit operator Wrapper(Money m) => default;
            }
            class Counter
            {
                public static Counter operator +(Counter a, Counter b) => a;
                public void operator +=(Counter other) { }
                public void operator ++() { }
                public void operator --() { }
            }
            class C
            {
            {{members}}
            }
            """);

        Assert.Contains(Lowered.Opaques(procedure), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));
        Assert.DoesNotContain(Pures(procedure), static p => p.Function.StartsWith(PureCatalogue.OperatorPrefix, StringComparison.Ordinal) || p.Function.Contains(".add", StringComparison.Ordinal) || p.Function.Contains(".sub", StringComparison.Ordinal));
    }

    [Fact]
    public void X87LegacyFloatIsSideSpecific()
    {
        const string Source = "class C { static int M(double a, double b, decimal m) => a < b ? (int)(a + b) : (int)m; }";
        Compilation x86 = Compilation(Source).WithOptions(Compilation(Source).Options.WithPlatform(Platform.X86));
        Compilation preferred = Compilation(Source).WithOptions(Compilation(Source).Options.WithPlatform(Platform.AnyCpu32BitPreferred));

        Assert.Equal(["f64.lt:True", "f64.add:True", "conv.f64.i32:True", "conv.dec.i32:False"], Sensitivity(Lower(x86, legacy: true)));
        Assert.Equal(["f64.lt:True", "f64.add:True", "conv.f64.i32:True", "conv.dec.i32:False"], Sensitivity(Lower(preferred, legacy: true)));
        Assert.Equal(["f64.lt:False", "f64.add:False", "conv.f64.i32:True", "conv.dec.i32:False"], Sensitivity(Lower(x86, legacy: false)));
        Assert.Equal(["f64.lt:False", "f64.add:False", "conv.f64.i32:True", "conv.dec.i32:False"], Sensitivity(Lower(Compilation(Source), legacy: true)));
    }

    [Fact]
    public void PureAddsNoTraceEvent()
    {
        IrProcedure procedure = Lowered.Method("static decimal M(decimal a, int n) => a * n - a;");

        Assert.Equal(["conv.i32.dec", "dec.mul", "dec.sub"], Pures(procedure).Select(static p => p.Function), StringComparer.Ordinal);
        Assert.Empty(Lowered.Calls(procedure));
        IrRun run = IrInterpreter.Run(procedure, new IrInputs([Element(1), IrBitVecValue.FromSigned(32, 2)]), new NoCalls(), 100, pure: new Answers(Element(5), false));
        Assert.Empty(run.Trace);
    }

    [Fact]
    public void TheCatalogueListsEachFunctionOnceWithItsTypesAndExceptions()
    {
        Assert.Equal(96, PureCatalogue.Entries.Count);
        PureCatalogue.Entry divide = PureCatalogue.Entries["dec.div"];
        Assert.Equal([SpecialType.System_Decimal, SpecialType.System_Decimal], divide.Arguments);
        Assert.Equal(SpecialType.System_Decimal, divide.Result);
        Assert.Equal([PureCatalogue.DivideByZero, PureCatalogue.Overflow], divide.Raises(isChecked: false), StringComparer.Ordinal);
        Assert.Equal([PureCatalogue.DivideByZero, PureCatalogue.Overflow], divide.Raises(isChecked: true), StringComparer.Ordinal);
        Assert.Equal(SpecialType.System_Boolean, PureCatalogue.Entries["f32.le"].Result);
        Assert.Empty(PureCatalogue.Entries["conv.f64.i64"].Raises(isChecked: false));
        Assert.Equal([PureCatalogue.Overflow], PureCatalogue.Entries["conv.f64.i64"].Raises(isChecked: true), StringComparer.Ordinal);
        Assert.Equal([PureCatalogue.Overflow], PureCatalogue.Entries["conv.dec.u8"].Raises(isChecked: false), StringComparer.Ordinal);
        Assert.All(PureCatalogue.Entries, static e => Assert.Equal(e.Key, e.Value.Function));
    }

    [Fact]
    public void AnOperatorOutsideTheCatalogueKeepsItsReason()
    {
        IrProcedure procedure = Lowered.Method("static bool M(object a, object b) => a == b;");

        Assert.Empty(Pures(procedure));
        Assert.Equal("Binary", Assert.Single(Lowered.Opaques(procedure)).Reason);
    }

    private static Compilation Compilation(string source = "class C { }") => RoslynTestCompilations.Compile(source);

    private static IrProcedure Lower(Compilation compilation, bool legacy)
    {
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        return IrLowerer.Lower(method, compilation, RenameMap.Empty, [], [], legacy).Body;
    }

    private static IrProcedure X87Legacy(string source)
    {
        Compilation compilation = Compilation(source);
        return Lower(compilation.WithOptions(compilation.Options.WithPlatform(Platform.X86)), legacy: true);
    }

    private static IrValue ConstantOf(IrProcedure procedure, IrVar var) =>
        procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>().Single(c => c.Target == var).Value;

    private static ImmutableArray<string> Sensitivity(IrProcedure procedure) =>
        [.. Pures(procedure).Select(static p => $"{p.Function}:{p.RuntimeSensitive}")];

    private static ImmutableArray<string> Functions(string source) => [.. Pures(Lowered.Source(source)).Select(static p => p.Function)];

    private static ImmutableArray<IrPure> Pures(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>()];

    private static IrSortValue Element(int id, string sort = Decimal) => new(sort, id);

    private static IrOutcome Run(IrProcedure procedure, IPureOracle pure, params IrValue[] arguments) =>
        IrInterpreter.Run(procedure, new IrInputs(arguments.Length > 0 ? [.. arguments] : [Element(1), Element(2)]), new NoCalls(), 100, pure: pure).Outcome;

    /// <summary>Answers every pure application with one value and fixed flags.</summary>
    private sealed class Answers(IrValue value, params bool[] flags) : IPureOracle
    {
        public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments) => new(value, [.. flags.Take(pure.Throws.Length)]);
    }

    /// <summary>A static property's getter yields element 1; its setter yields nothing.</summary>
    private sealed class Accessors : ICallOracle
    {
        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap) =>
            new(resultType is null ? null : Element(1), Threw: false);
    }

    private sealed class NoCalls : ICallOracle
    {
        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap) =>
            throw new InvalidOperationException($"Unexpected call {callee.Value}.");
    }
}
