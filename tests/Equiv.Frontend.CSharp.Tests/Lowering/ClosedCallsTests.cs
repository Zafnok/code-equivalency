using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// ADR 0041 (ticket P2-060): a call whose callee's containing type, parameter types and type arguments are all inert is
/// closed and gets no heap pairs, in both lowerings; every other call pairs each heap map its body touches.
/// </summary>
public sealed class ClosedCallsTests
{
    public static TheoryData<string, bool> Calls => new()
    {
        { "static string M(string s) { f++; return string.Concat(s, \"t\"); }", true },
        { "static string M(string s) { f++; return s + \"t\"; }", true },
        { "static string M(string s) { f++; return s.Trim(); }", true },
        { "static int M(int? x) { f++; return x.GetValueOrDefault(); }", true },
        { "static bool M(DayOfWeek? d) { f++; return d.HasValue; }", true },
        { "static bool M(string s) { f++; return int.TryParse(s, out _); }", true },
        { "static long M(long x) { f++; return int.CreateChecked(x); }", true },
        { "static bool M(string s) { f++; return bool.Parse(s); }", true },
        { "static bool M(char c) { f++; return char.IsDigit(c); }", true },
        { "static sbyte M(string s) { f++; return sbyte.Parse(s); }", true },
        { "static byte M(string s) { f++; return byte.Parse(s); }", true },
        { "static short M(string s) { f++; return short.Parse(s); }", true },
        { "static ushort M(string s) { f++; return ushort.Parse(s); }", true },
        { "static uint M(string s) { f++; return uint.Parse(s); }", true },
        { "static ulong M(string s) { f++; return ulong.Parse(s); }", true },
        { "static float M(string s) { f++; return float.Parse(s); }", true },
        { "static double M(string s) { f++; return double.Parse(s); }", true },
        { "static decimal M(string s) { f++; return decimal.Parse(s); }", true },
        { "static string M(object o) { f++; return string.Concat(o, o); }", false },
        { "static string M(string[] a) { f++; return string.Concat(a); }", false },
        { "static bool M(DateTime? t) { f++; return t.HasValue; }", false },
        { "static void M(string s) { f++; Console.WriteLine(s); }", false },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public void ACallIsClosedWhenEverythingItIsHandedIsInert(string method, bool closed)
    {
        Compilation compilation = RoslynTestCompilations.Compile($"using System;\nclass C\n{{\nstatic int f;\n{method}\n}}\n");
        IMethodSymbol symbol = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        foreach (IrProcedure procedure in new[] { IrLowerer.Lower(symbol, compilation, RenameMap.Empty, [], Runtimes.Migration), IlLowerer.Lower(symbol, compilation, Runtimes.Migration) })
        {
            Assert.Empty(IrValidator.Validate(procedure));
            IrCall call = Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>());
            Assert.Equal(closed, call.Closed);
            Assert.Equal(closed, call.Heap.IsEmpty);
        }
    }
}
