using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// <see cref="LoweringCensus"/> (ADR 0027; ticket M3-014 acceptance criteria 1 and 3): counts over the lowered bodies of
/// matched pairs, computed without a solver.
/// </summary>
public sealed class LoweringCensusTests
{
    private const string Clean = """
        proc "T::M" (%a: bv32) -> bv32 entry B0
        B0:
          ret %a
        """;

    [Fact]
    public void CensusCountsOpaqueReasonsPerSide()
    {
        // Legacy has "PropertyReference" twice in one body (one count) and "Binary" once; modern has only "Binary".
        IrProcedure legacy = Body("""
            proc "T::M" (%a: bv32, %c: bool) -> bv32 entry B0
            B0:
              %x: bv32 = opaque "PropertyReference" at "f.cs" 1:1-1:2
              br %c, B1, B2
            B1:
              %y: bv32 = opaque "PropertyReference" at "f.cs" 2:1-2:2
              %z: bv32 = opaque "Binary" at "f.cs" 3:1-3:2
              ret %a
            B2:
              ret %a
            """);
        IrProcedure modern = Body("""
            proc "T::M" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = opaque "Binary" at "f.cs" 3:1-3:2
              goto B1
            B1:
              ret %a
            """);

        LoweringCensus census = LoweringCensus.Compute([(legacy, modern), (Body(Clean), legacy)], removed: 0, added: 0);

        Assert.Equal(["Binary", "PropertyReference"], census.OpaqueByReason.Keys, StringComparer.Ordinal);
        Assert.Equal(new SideCounts(Legacy: 1, Modern: 2), census.OpaqueByReason["Binary"]);
        Assert.Equal(new SideCounts(Legacy: 1, Modern: 1), census.OpaqueByReason["PropertyReference"]);
        Assert.Equal(0, census.PairsWithoutOpaque);
        Assert.Equal(0, census.PairsWholeBodyOpaque);
    }

    [Fact]
    public void WholeBodyOpaqueCountsOnce()
    {
        IrProcedure wholeBody = Body("""
            proc "T::M" (%a: bv32) -> bv32 entry B0
            B0:
              %$0: bv32 = opaque "foreach-enumerator" at "f.cs" 1:1-9:2
              ret %$0
            """);

        LoweringCensus census = LoweringCensus.Compute([(wholeBody, wholeBody), (Body(Clean), wholeBody), (wholeBody, Body(Clean))], removed: 0, added: 0);

        Assert.Equal(3, census.PairsWholeBodyOpaque);
        Assert.Equal(new SideCounts(Legacy: 2, Modern: 2), Assert.Single(census.OpaqueByReason).Value);
    }

    [Fact]
    public void ABodyWithMoreThanTheOneOpaqueIsNotWholeBodyOpaque()
    {
        IrProcedure twoOpaques = Body("""
            proc "T::M" (%a: bv32) -> bv32 entry B0
            B0:
              %$0: bv32 = opaque "undefined" at "f.cs" 1:1-9:2
              %$1: bv32 = opaque "Binary" at "f.cs" 2:1-2:9
              ret %$1
            """);
        IrProcedure twoBlocks = Body("""
            proc "T::M" (%a: bv32) -> bv32 entry B0
            B0:
              %$0: bv32 = opaque "Binary" at "f.cs" 1:1-9:2
              goto B1
            B1:
              ret %$0
            """);

        LoweringCensus census = LoweringCensus.Compute([(twoOpaques, twoBlocks)], removed: 0, added: 0);

        Assert.Equal(0, census.PairsWholeBodyOpaque);
        Assert.Equal(0, census.PairsWithoutOpaque);
    }

    [Fact]
    public void ProceduresCountEachSidesMatchedPlusUnmatchedAndCongruentStaysZero()
    {
        LoweringCensus census = LoweringCensus.Compute([(Body(Clean), Body(Clean)), (Body(Clean), Body(Clean))], removed: 3, added: 5);

        Assert.Equal(new SideCounts(Legacy: 5, Modern: 7), census.Procedures);
        Assert.Equal(2, census.MatchedPairs);
        Assert.Equal(2, census.PairsWithoutOpaque);
        Assert.Equal(0, census.PairsCongruent);
        Assert.Equal(new SideCounts(0, 0), census.ProjectsSkipped);
        Assert.Empty(census.OpaqueByReason);
    }

    [Fact]
    public void ProjectsSkippedAreCountedPerSide()
    {
        LoweringCensus census = LoweringCensus.Compute([], removed: 0, added: 0, projectsSkipped: new SideCounts(Legacy: 2, Modern: 1));

        Assert.Equal(new SideCounts(2, 1), census.ProjectsSkipped);
        Assert.Equal(
            """{"legacy":2,"modern":1}""",
            Newtonsoft.Json.JsonConvert.SerializeObject(census.ToProperty()["projectsSkipped"]));
    }

    [Fact]
    public void NullPairsThrow() =>
        Assert.Throws<ArgumentNullException>(() => LoweringCensus.Compute(null!, removed: 0, added: 0));

    [Fact]
    public void ThePropertyHasTheFieldsAdr0027NamesWithReasonsSorted()
    {
        LoweringCensus census = new(
            new SideCounts(4, 5),
            MatchedPairs: 3,
            PairsWithoutOpaque: 2,
            PairsWholeBodyOpaque: 1,
            PairsCongruent: 0,
            ProjectsSkipped: new SideCounts(0, 0),
            new Dictionary<string, SideCounts>(StringComparer.Ordinal) { ["using"] = new(1, 0), ["Binary"] = new(0, 1) }
                .ToImmutableSortedDictionary(StringComparer.Ordinal));

        Dictionary<string, object> property = census.ToProperty();

        Assert.Equal(["procedures", "matchedPairs", "pairsWithoutOpaque", "pairsWholeBodyOpaque", "pairsCongruent", "projectsSkipped", "opaqueByReason"], property.Keys, StringComparer.Ordinal);
        Assert.Equal(
            """{"procedures":{"legacy":4,"modern":5},"matchedPairs":3,"pairsWithoutOpaque":2,"pairsWholeBodyOpaque":1,"pairsCongruent":0,"projectsSkipped":{"legacy":0,"modern":0},"opaqueByReason":{"Binary":{"legacy":0,"modern":1},"using":{"legacy":1,"modern":0}}}""",
            Newtonsoft.Json.JsonConvert.SerializeObject(property));
    }

    private static IrProcedure Body(string text) => IrText.Parse(text);
}
