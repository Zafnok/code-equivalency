using System.Collections.Immutable;

using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// The API-equivalence catalogue applied while lowering the legacy side (ticket M3-009; ADR 0020). The test compilation
/// references .NET 10, where <c>s.Split(',')</c> already binds the modern overload and Web API 2 does not exist, so the
/// legacy members are stubs with the legacy identities, and the catalogue's own adapters are applied to them.
/// </summary>
public sealed class ApiEquivalenceLoweringTests
{
    /// <summary>Web API 2's helpers and result types, as stubs with their real names.</summary>
    private const string WebApi = """
        namespace System.Web.Http
        {
            public interface IHttpActionResult { }
            public abstract class ApiController
            {
                protected internal virtual Results.OkResult Ok() => new Results.OkResult();
                protected internal virtual Results.OkNegotiatedContentResult<T> Ok<T>(T content) => new Results.OkNegotiatedContentResult<T>();
                protected internal virtual Results.NotFoundResult NotFound() => new Results.NotFoundResult();
                protected internal virtual Results.BadRequestResult BadRequest() => new Results.BadRequestResult();
            }
        }
        namespace System.Web.Http.Results
        {
            public class OkResult : IHttpActionResult { }
            public class OkNegotiatedContentResult<T> : IHttpActionResult { }
            public class NotFoundResult : IHttpActionResult { }
            public class BadRequestResult : IHttpActionResult { }
        }
        """;

    /// <summary>A stand-in for .NET Framework's <c>String.Split(params char[])</c>, which .NET 10 no longer binds for one char.</summary>
    private const string Splitter = "class S { public string[] Split(params char[] separator) => null!; }\n";

    private static ImmutableArray<ApiEquivalence> Table => ApiEquivalenceTable.Load().Entries;

    private static ApiEquivalence Entry(string id) => Table.Single(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary>The catalogue's Split entry, with its legacy member the stand-in's.</summary>
    private static ImmutableArray<ApiEquivalence> Split => [Entry("bcl.string-split-one-char") with { Legacy = "S::Split(char[])" }];

    [Fact]
    public void LegacySplit_IsRewrittenToTheModernOverload()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Legacy(Splitter + "class C { static string[] M(S s) => s.Split(','); }", Split);

        IrCall call = Assert.Single(Calls(body));
        Assert.Equal("System.String::Split(char,System.StringSplitOptions)", call.Callee.Value);
        Assert.Equal(3, call.Args.Length);
        Assert.Equal("s", call.Args[0].SourceName);
        Assert.Equal(["bcl.string-split-one-char"], applied);
        IrConst options = body.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>().Single(c => c.Target == call.Args[2]);
        Assert.Equal(TypeMapper.Constant("System.StringSplitOptions", "0"), options.Value);
        Assert.Equal(new IrSort("System.StringSplitOptions"), options.Value.Type);
    }

    /// <summary>The rewritten instance call keeps the legacy call's receiver null check.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void LegacySplit_KeepsTheReceiverNullCheck(bool isNull, bool thrown)
    {
        IrProcedure body = Legacy(Splitter + "class C { static string[] M(S s) => s.Split(','); }", Split).Body;

        IrOutcome outcome = Run(body, Reference(0, "S"), Nulls("S", 0, isNull));

        Assert.Equal(thrown, outcome is IrThrew { ExceptionType: "System.NullReferenceException" });
    }

    [Fact]
    public void SplitWithTwoSeparators_IsNotRewritten()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Legacy(Splitter + "class C { static string[] M(S s) => s.Split(',', ';'); }", Split);

        Assert.Equal("S::Split(char[])", Assert.Single(Calls(body)).Callee.Value);
        Assert.Empty(applied);
    }

    [Theory]
    [InlineData("s.Split()")]
    [InlineData("s.Split(new[] { ',' })")]
    public void SplitWithNoSeparatorOrAnArray_IsNotRewritten(string call)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Legacy(Splitter + $"class C {{ static string[] M(S s) => {call}; }}", Split);

        Assert.Equal("S::Split(char[])", Assert.Single(Calls(body)).Callee.Value);
        Assert.Empty(applied);
    }

    [Fact]
    public void LegacyLinqContains_UnwrapsTheStringArgument()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Legacy(
            "using System.Linq;\nclass C { static bool M(string s, char c) => Enumerable.Contains(s, c); }",
            [Entry("bcl.string-contains-char")]);

        IrCall call = Assert.Single(Calls(body));
        Assert.Equal("System.String::Contains(char)", call.Callee.Value);
        Assert.Equal(["s", "c"], call.Args.Select(static a => a.SourceName), StringComparer.Ordinal);
        Assert.Equal(new IrSort("System.String"), call.Args[0].Type);
        Assert.Equal(["bcl.string-contains-char"], applied);
    }

    /// <summary>ADR 0020: the legacy call is static, so a null string reaches the modern member without a receiver check.</summary>
    [Fact]
    public void LegacyLinqContains_EmitsNoReceiverNullCheck()
    {
        IrProcedure body = Legacy(
            "using System.Linq;\nclass C { static bool M(string s, char c) => Enumerable.Contains(s, c); }",
            [Entry("bcl.string-contains-char")]).Body;

        Assert.DoesNotContain(body.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
        IrOutcome outcome = Run(body, Reference(0), Bits(16, 'x'), Nulls("System.String", 0, isNull: true));
        Assert.False(outcome is IrThrew { ExceptionType: "System.NullReferenceException" });
    }

    [Fact]
    public void OkOfInt_ConvertsItsArgumentToObject()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Legacy(
            WebApi + "class C : System.Web.Http.ApiController { System.Web.Http.IHttpActionResult M(int id) => Ok(id); }",
            Table);

        IrCall call = Assert.Single(Calls(body));
        Assert.Equal("Microsoft.AspNetCore.Mvc.ControllerBase::Ok(object)", call.Callee.Value);
        Assert.Equal(new IrSort("System.Object"), call.Args[1].Type);
        IrMapRead boxed = body.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>().Single(r => r.Target == call.Args[1]);
        Assert.Equal("cast.System.Int32.System.Object", boxed.Map.Name);
        Assert.Equal(new IrSort("Microsoft.AspNetCore.Mvc.OkObjectResult"), call.Target!.Type);
        Assert.Equal(["webapi.ok-of-int", "webapi.type.action-result", "webapi.type.ok-content-result"], applied);
    }

    [Fact]
    public void LegacyResultSort_MapsToTheModernSort()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Legacy(
            WebApi + """
                class C : System.Web.Http.ApiController
                {
                    System.Web.Http.IHttpActionResult M(int id)
                    {
                        if (id < 0) return NotFound();
                        if (id == 0) return BadRequest();
                        return Ok();
                    }
                }
                """,
            Table);

        Assert.Equal(new IrSort("Microsoft.AspNetCore.Mvc.IActionResult"), body.ReturnType);
        Assert.Contains(body.Parameters, static p => string.Equals(p.Var.Name, "cast.Microsoft.AspNetCore.Mvc.NotFoundResult.Microsoft.AspNetCore.Mvc.IActionResult", StringComparison.Ordinal));
        Assert.Equal(
            ["Microsoft.AspNetCore.Mvc.ControllerBase::NotFound()", "Microsoft.AspNetCore.Mvc.ControllerBase::BadRequest()", "Microsoft.AspNetCore.Mvc.ControllerBase::Ok()"],
            Calls(body).Select(static c => c.Callee.Value),
            StringComparer.Ordinal);
        Assert.Equal(
            ["webapi.bad-request", "webapi.not-found", "webapi.ok", "webapi.type.action-result", "webapi.type.bad-request-result", "webapi.type.not-found-result", "webapi.type.ok-result"],
            applied);
    }

    /// <summary>A whole-body opaque still has the modern signature, so the pair's signatures agree.</summary>
    [Fact]
    public void AnOpaqueLegacyBodyHasTheModernReturnSort()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Legacy(
            WebApi + "class C : System.Web.Http.ApiController { async System.Threading.Tasks.Task<System.Web.Http.IHttpActionResult> M() => NotFound(); }",
            Table);

        Assert.Single(Opaques(body));
        Assert.Empty(applied);
    }

    [Fact]
    public void ModernSide_IsNeverRewritten()
    {
        Compilation compilation = RoslynTestCompilations.Compile(Splitter + "class C { static string[] M(S s) => s.Split(','); }");
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        EquivConfig config = EquivConfig.Default;

        // The shipped table names the real legacy member, so the stand-in only matches through a crafted entry; the
        // production entry point gives the modern side no entries at all, whichever table is shipped.
        (IrProcedure modern, ImmutableArray<string> modernApplied) = CSharpFrontend.LowerWithIrLowerer(method, compilation, config, legacy: false);
        (IrProcedure legacy, ImmutableArray<string> legacyApplied) = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Split);

        Assert.Equal("S::Split(char[])", Assert.Single(Calls(modern)).Callee.Value);
        Assert.Empty(modernApplied);
        Assert.Equal("System.String::Split(char,System.StringSplitOptions)", Assert.Single(Calls(legacy)).Callee.Value);
        Assert.NotEmpty(legacyApplied);
    }

    [Fact]
    public void ModernSide_KeepsWebApiSortNames()
    {
        Compilation compilation = RoslynTestCompilations.Compile(WebApi + "class C : System.Web.Http.ApiController { System.Web.Http.IHttpActionResult M() => NotFound(); }");
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        (IrProcedure modern, ImmutableArray<string> modernApplied) = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, legacy: false);
        (IrProcedure legacy, ImmutableArray<string> legacyApplied) = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, legacy: true);
        (_, ImmutableArray<string> suppressedApplied) = CSharpFrontend.LowerWithIrLowerer(
            method, compilation, EquivConfig.Default with { SuppressApiEquivalences = ["webapi."] }, legacy: true);

        Assert.Equal(new IrSort("System.Web.Http.IHttpActionResult"), modern.ReturnType);
        Assert.Equal("System.Web.Http.ApiController::NotFound()", Assert.Single(Calls(modern)).Callee.Value);
        Assert.Empty(modernApplied);
        Assert.Equal(new IrSort("Microsoft.AspNetCore.Mvc.IActionResult"), legacy.ReturnType);
        Assert.NotEmpty(legacyApplied);
        Assert.Empty(suppressedApplied);
    }

    /// <summary>Adapters the call's arguments do not fit leave it as it is, with nothing emitted for the attempt.</summary>
    [Theory]
    [MemberData(nameof(UnaddressableAdapters), DisableDiscoveryEnumeration = true)]
    public void AnAdapterThatCannotAddressTheCall_LeavesItAsItIs(string call, ApiArgument[] arguments)
    {
        ApiEquivalence entry = new("t", IsType: false, "H::F(object,int)", "H::G()", [.. arguments], "r", new Uri("https://learn.microsoft.com/x"));
        const string Helper = "static class H { public static int F(object o, int i) => i; public static int G() => 0; }\n";

        (IrProcedure body, ImmutableArray<string> applied) = Legacy(Helper + $"class C {{ static int M(string s, int i) => {call}; }}", [entry]);

        Assert.Equal("H::F(object,int)", Assert.Single(Calls(body)).Callee.Value);
        Assert.Empty(applied);
    }

    public static TheoryData<string, ApiArgument[]> UnaddressableAdapters() => new()
    {
        { "H.F(s, i)", [new ApiArgument(0)] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(2)] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(-1)] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(1), new ApiArgument(0, Unwrap: true)] },
        { "H.F(s, i)", [new ApiArgument(0, Unwrap: true), new ApiArgument(1, Unwrap: true)] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(1, ConvertTo: "No.Such.Type")] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(1, ConvertTo: "System.String")] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(1), new ApiArgument(Source: null, ConstantType: "bool", Constant: "maybe")] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(1), new ApiArgument(Source: null, ConstantType: "bvx", Constant: "1")] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(1), new ApiArgument(Source: null, ConstantType: "bv8", Constant: "one")] },
        { "H.F(s, i)", [new ApiArgument(0), new ApiArgument(1, ConvertTo: "System.Int64")] },
        { "H.F(null, i)", [new ApiArgument(0, Unwrap: true, ConvertTo: "System.Object"), new ApiArgument(1)] },
    };

    /// <summary>Every adapter form that addresses a call, and the arguments each produces.</summary>
    [Fact]
    public void AnAdapterAddressesPositionsConversionsAndConstants()
    {
        ApiEquivalence entry = new(
            "t",
            IsType: false,
            "H::F(object,int)",
            "H::G(string,object,int,bool,bv8)",
            [
                new ApiArgument(0, Unwrap: true),
                new ApiArgument(0, Unwrap: true, ConvertTo: "System.Object"),
                new ApiArgument(1, ConvertTo: "System.Int32"),
                new ApiArgument(Source: null, ConstantType: "bool", Constant: "true"),
                new ApiArgument(Source: null, ConstantType: "bv8", Constant: "-1"),
            ],
            "r",
            new Uri("https://learn.microsoft.com/x"));
        const string Helper = "static class H { public static int F(object o, int i) => i; }\n";

        (IrProcedure body, ImmutableArray<string> applied) = Legacy(Helper + "class C { static int M(string s, int i) => H.F(s, i); }", [entry]);

        IrCall call = Assert.Single(Calls(body));
        Assert.Equal("H::G(string,object,int,bool,bv8)", call.Callee.Value);
        Assert.Equal([new IrSort("System.String"), new IrSort("System.Object"), new IrBitVec(32), new IrBool(), new IrBitVec(8)], call.Args.Select(static a => a.Type));
        Assert.Equal("s", call.Args[0].SourceName);
        Assert.Equal("i", call.Args[2].SourceName);
        Assert.Equal(["t"], applied);
    }

    /// <summary>Named arguments out of parameter order are addressed by parameter position.</summary>
    [Fact]
    public void AnAdapterAddressesNamedArgumentsByParameterPosition()
    {
        ApiEquivalence entry = new("t", IsType: false, "H::F(int,int)", "H::G(int,int)", [new ApiArgument(1), new ApiArgument(0)], "r", new Uri("https://learn.microsoft.com/x"));
        const string Helper = "static class H { public static int F(int a, int b) => a; }\n";

        IrProcedure body = Legacy(Helper + "class C { static int M(int x, int y) => H.F(b: y, a: x); }", [entry]).Body;

        Assert.Equal(["y", "x"], Assert.Single(Calls(body)).Args.Select(static a => a.SourceName), StringComparer.Ordinal);
    }

    /// <summary>A value-type receiver is not null-checked, as the legacy call's is not.</summary>
    [Fact]
    public void AnAdapterPassesAValueTypeReceiverWithoutANullCheck()
    {
        ApiEquivalence entry = new("t", IsType: false, "System.Int32::GetHashCode()", "H::G(int)", [new ApiArgument(0)], "r", new Uri("https://learn.microsoft.com/x"));

        IrProcedure body = Legacy("class C { static int M(int i) => i.GetHashCode(); }", [entry]).Body;

        Assert.Equal(["i"], Assert.Single(Calls(body)).Args.Select(static a => a.SourceName), StringComparer.Ordinal);
        Assert.DoesNotContain(body.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
    }

    [Fact]
    public void AnAdapterConstantParsesItsIrType()
    {
        Assert.Equal(new IrBoolValue(Value: true), TypeMapper.Constant("bool", "true"));
        Assert.Equal(Bits(16, -2), TypeMapper.Constant("bv16", "-2"));
        Assert.Equal(TypeMapper.Constant(RoslynTestCompilations.Compile("").GetTypeByMetadataName("System.StringSplitOptions")!, 0), TypeMapper.Constant("System.StringSplitOptions", "0"));
    }

    [Theory]
    [InlineData("bool", "1")]
    [InlineData("bv", "1")]
    [InlineData("bv8", "x")]
    public void AnAdapterConstantThatDoesNotParseIsNull(string type, string text) => Assert.Null(TypeMapper.Constant(type, text));
}
