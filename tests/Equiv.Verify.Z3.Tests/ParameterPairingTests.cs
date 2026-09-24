using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Xunit;

using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// How <see cref="ProductEncoder.Pair"/> shares inputs between the sides (ADR 0021): source-language
/// parameters by position, synthesised inputs by name, and never two parameters of different types.
/// </summary>
public sealed class ParameterPairingTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Fact]
    public void SourceParametersPairByPositionAndSynthesisedOnesByName()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "C::M(int, int)" (%a: bv32, %b: bv32, %field.C.x: map<sort "C", bv32>, %this: sort "C") entry B0
            B0:
              ret
            ---
            proc "C::M(int, int)" (%b: bv32, %c: bv32, %this: sort "C", %field.C.y: map<sort "C", bv32>) entry B0
            B0:
              ret
            """);

        ImmutableArray<SharedParameter> shared = ProductEncoder.Pair(old, @new);

        Assert.Equal(
            [("a", "b", "in.a"), ("b", "c", "in.b"), ("field.C.x", null, "in.field.C.x"), ("this", "this", "in.this"), (null, "field.C.y", "in.new.field.C.y")],
            shared.Select(static s => (s.Old?.Var.Name, s.New?.Var.Name, s.InputName)));
    }

    [Fact]
    public void AParameterWhoseTypeChangedIsAnInputOfEachSide()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M(int)" (%a: bv32, ref %r: bv32) entry B0
            B0:
              ret outs(%r = %r)
            ---
            proc "T::M(int)" (%a: bv32, ref %r: bv64) entry B0
            B0:
              ret outs(%r = %r)
            """);

        ImmutableArray<SharedParameter> shared = ProductEncoder.Pair(old, @new);

        Assert.Equal(
            [("a", "a", "in.a", false), ("r", null, "in.r", true), (null, "r", "in.new.r", true)],
            shared.Select(static s => (s.Old?.Var.Name, s.New?.Var.Name, s.InputName, s.ByRef)));
        Assert.IsType<Equivalent>(new Z3Backend().Verify(old, @new, Options));
    }

    [Fact]
    public void AnExtraSourceParameterIsAnInputOfItsSideOnly()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              ret %a
            ---
            proc "T::M(int)" (%b: bv32, %a: bv32) -> bv32 entry B0
            B0:
              ret %a
            """);

        Assert.Equal(
            [("a", "b"), (null, "a")],
            ProductEncoder.Pair(old, @new).Select(static s => (s.Old?.Var.Name, s.New?.Var.Name)));
        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(old, @new, Options));
        Assert.Equal(2, divergent.Counterexample.Inputs.Arguments.Length);
    }
}
