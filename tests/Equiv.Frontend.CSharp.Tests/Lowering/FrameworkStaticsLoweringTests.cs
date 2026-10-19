using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Tests.Lowering.Il;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-250: a framework <c>static readonly</c> field of a value or string type has one value for the whole run. A read
/// of one whose value <see cref="FrameworkStatics"/> knows is that constant; a read of any other is its <c>field.*</c> map,
/// an <see cref="IrParameterKind.In"/> input no call writes.
/// </summary>
public sealed class FrameworkStaticsLoweringTests
{
    /// <summary>A known field is its constant, in the IOperation and the IL lowering alike, and the body has no input for it.</summary>
    [Theory]
    [InlineData("string", "string.Empty", "")]
    [InlineData("IntPtr", "IntPtr.Zero", null)]
    [InlineData("UIntPtr", "UIntPtr.Zero", null)]
    [InlineData("Guid", "Guid.Empty", null)]
    [InlineData("TimeSpan", "TimeSpan.Zero", null)]
    [InlineData("DateTime", "DateTime.MinValue", null)]
    [InlineData("System.Drawing.Color", "System.Drawing.Color.Empty", null)]
    [InlineData("System.Drawing.Point", "System.Drawing.Point.Empty", null)]
    [InlineData("System.Drawing.PointF", "System.Drawing.PointF.Empty", null)]
    [InlineData("System.Drawing.Size", "System.Drawing.Size.Empty", null)]
    [InlineData("System.Drawing.SizeF", "System.Drawing.SizeF.Empty", null)]
    [InlineData("System.Drawing.Rectangle", "System.Drawing.Rectangle.Empty", null)]
    [InlineData("System.Drawing.RectangleF", "System.Drawing.RectangleF.Empty", null)]
    public void AKnownFieldIsItsConstant(string type, string field, string? value)
    {
        string members = $"static {type} M() => {field};";
        foreach (IrProcedure procedure in new[] { Method(members), IlLowererTests.Lower(members) })
        {
            Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("field.", StringComparison.Ordinal));
            IrReturn exit = Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrReturn>());
            IrConst read = Assert.Single(Instructions(procedure).OfType<IrConst>(), c => c.Target == exit.Value);
            Assert.Equal(value is null ? new IrSortValue(((IrSort)read.Target.Type).Name, 0) : Assert.Single(Constants(Method($"static string M() => \"{value}\";"))), read.Value);
        }
    }

    /// <summary><c>string.Empty</c> and <c>""</c> lower alike: the same constants over the same inputs.</summary>
    [Fact]
    public void StringEmptyIsTheEmptyLiteral()
    {
        IrProcedure empty = Method("static bool M(string s) => s == string.Empty;");
        IrProcedure literal = Method("static bool M(string s) => s == \"\";");

        Assert.Equal(Constants(literal), Constants(empty));
        Assert.Equal(literal.Parameters.Select(static p => p.Var.Name), empty.Parameters.Select(static p => p.Var.Name), StringComparer.Ordinal);
    }

    /// <summary>
    /// A framework <c>static readonly</c> value or string field the catalogue does not know is its map, which is
    /// <see cref="IrParameterKind.In"/> and in no call's heap pairs, so a read after a call is the read before it.
    /// </summary>
    [Theory]
    [InlineData("static TimeSpan M(string s) { Console.WriteLine(s); return TimeSpan.MaxValue; }", "field.System.TimeSpan.MaxValue")]
    [InlineData("static bool M(string s) { Console.WriteLine(s); return BitConverter.IsLittleEndian; }", "field.System.BitConverter.IsLittleEndian")]
    [InlineData("static string M(string s) { Console.WriteLine(s); return Uri.SchemeDelimiter; }", "field.System.Uri.SchemeDelimiter")]
    public void AnUnknownFieldIsAnInputNoCallWrites(string members, string map)
    {
        foreach (IrProcedure procedure in new[] { Method(members), IlLowererTests.Lower(members) })
        {
            Assert.Equal(IrParameterKind.In, Assert.Single(procedure.Parameters, p => string.Equals(p.Var.Name, map, StringComparison.Ordinal)).Kind);
            Assert.DoesNotContain(Calls(procedure).SelectMany(static c => c.Heap), p => string.Equals(p.Map, map, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A field the source declares, a framework field of a reference type and a framework field that is not
    /// <c>readonly</c> stay maps that are <see cref="IrParameterKind.Ref"/> and that every call reads and writes.
    /// </summary>
    [Theory]
    [InlineData("static readonly int K = 3; static int M(string s) { Console.WriteLine(s); return K; }", "field.C.K")]
    [InlineData("static readonly string K = \"k\"; static string M(string s) { Console.WriteLine(s); return K; }", "field.C.K")]
    [InlineData("static EventArgs M(string s) { Console.WriteLine(s); return EventArgs.Empty; }", "field.System.EventArgs.Empty")]
    public void AnyOtherStaticFieldIsWrittenByCalls(string members, string map)
    {
        IrProcedure procedure = Method(members);

        Assert.Equal(IrParameterKind.Ref, Assert.Single(procedure.Parameters, p => string.Equals(p.Var.Name, map, StringComparison.Ordinal)).Kind);
        Assert.Contains(Assert.Single(Calls(procedure)).Heap, p => string.Equals(p.Map, map, StringComparison.Ordinal));
    }

    private static ImmutableArray<IrInstruction> Instructions(IrProcedure procedure) => [.. procedure.Blocks.SelectMany(static b => b.Instructions)];

    private static ImmutableArray<IrValue> Constants(IrProcedure procedure) => [.. Instructions(procedure).OfType<IrConst>().Select(static c => c.Value)];
}
