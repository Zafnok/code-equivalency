using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// The causes <see cref="Z3Backend"/> gives an Unknown (ADR 0027 decision 4; ticket M3-016 criteria 7 and 9): every
/// opaque node some input reaches, each line once, legacy side first and then in source order; never one no input
/// reaches. The detail names the reasons only, so moving an opaque node keeps the baseline unchanged.
/// </summary>
public sealed class UnknownCauseTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Fact]
    public void UnknownListsEveryReachedOpaqueSpan()
    {
        Unknown unknown = Assert.IsType<Unknown>(Verify(
            """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %negative: bool = slt %a, %zero
              br %negative, B1, B2
            B1:
              opaque "Lambda" at "Old.cs" 5:13-5:30
              ret %zero
            B2:
              %never: bool = const bool false
              br %never, B3, B4
            B3:
              opaque "Dead" at "Old.cs" 9:13-9:30
              ret %a
            B4:
              ret %a
            """,
            """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %negative: bool = slt %a, %zero
              br %negative, B1, B2
            B1:
              ret %zero
            B2:
              opaque "Await" at "New.cs" 7:9-7:20
              ret %a
            """));

        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Equal(
            [
                new UnknownCause(Codebase.Legacy, "Lambda", new SourceSpan("Old.cs", 5, 13, 5, 30)),
                new UnknownCause(Codebase.Modern, "Await", new SourceSpan("New.cs", 7, 9, 7, 20)),
            ],
            unknown.Causes);
        Assert.Equal("old: Lambda; new: Await", unknown.Detail);
    }

    [Fact]
    public void CausesAreEachLineOnceLegacyFirstInSourceOrder()
    {
        Unknown unknown = Assert.IsType<Unknown>(Verify(
            """
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              opaque "Late" at "Old.cs" 8:1-8:9
              ret
            """,
            """
            proc "T::M(int)" (%a: bv32) entry B0
            B0:
              opaque "Other" at "B.cs" 1:1-1:9
              goto B1
            B1:
              opaque "Late" at "A.cs" 9:1-9:9
              opaque "Wide" at "A.cs" 4:20-4:29
              opaque "Near" at "A.cs" 4:5-4:9
              opaque "Near" at "A.cs" 4:5-4:9
              ret
            """));

        Assert.Equal(
            [
                new UnknownCause(Codebase.Legacy, "Late", new SourceSpan("Old.cs", 8, 1, 8, 9)),
                new UnknownCause(Codebase.Modern, "Near", new SourceSpan("A.cs", 4, 5, 4, 9)),
                new UnknownCause(Codebase.Modern, "Wide", new SourceSpan("A.cs", 4, 20, 4, 29)),
                new UnknownCause(Codebase.Modern, "Late", new SourceSpan("A.cs", 9, 1, 9, 9)),
                new UnknownCause(Codebase.Modern, "Other", new SourceSpan("B.cs", 1, 1, 1, 9)),
            ],
            unknown.Causes);
        Assert.Equal("old: Late; new: Near; new: Wide; new: Late; new: Other", unknown.Detail);
    }

    [Fact]
    public void AnInductionRungsOpaqueIsACause()
    {
        Fixture fixture = Fixture.Load("loops/loop-opaque");

        Unknown unknown = Assert.IsType<Unknown>(new Z3Backend().Verify(fixture.Old, fixture.New, Options));

        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.NotEmpty(unknown.Causes);
        Assert.All(unknown.Causes, static c => Assert.Equal(new SourceSpan("T.cs", 7, 13, 7, 40), c.Span));
    }

    /// <summary>The backend end of criterion 9: the detail does not carry the line, so a moved opaque node baselines as unchanged.</summary>
    [Fact]
    public void MovingAnOpaqueNodeKeepsTheBaselineUnchanged()
    {
        ProcedureIdentity identity = new("T::M(int)", new SourceSpan("New.cs", 2, 5, 2, 6));
        SarifLog baseline = SarifReportWriter.Write([new VerificationResult(identity, OpaqueAtLine("7"))]);

        Result moved = SarifReportWriter.Write([new VerificationResult(identity, OpaqueAtLine("12"))], baseline).Runs[0].Results[0];

        Assert.Equal(BaselineState.Unchanged, moved.BaselineState);
        Assert.Equal(baseline.Runs[0].Results[0].PartialFingerprints, moved.PartialFingerprints);
        Assert.Equal(7, baseline.Runs[0].Results[0].Locations[0].PhysicalLocation.Region.StartLine);
        Assert.Equal(12, moved.Locations[0].PhysicalLocation.Region.StartLine);
    }

    private static Verdict OpaqueAtLine(string line) => Verify(
        """
        proc "T::M(int)" (%a: bv32) entry B0
        B0:
          ret
        """,
        $$"""
        proc "T::M(int)" (%a: bv32) entry B0
        B0:
          opaque "Await" at "New.cs" {{line}}:9-{{line}}:20
          ret
        """);

    private static Verdict Verify(string old, string @new) => new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options);
}
