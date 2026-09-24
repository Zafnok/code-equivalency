using System.Collections.Immutable;

using CsCheck;

using Equiv.Core.Matching;

using Xunit;

namespace Equiv.Core.Tests.Matching;

public sealed class StableIdentityMatcherTests
{
    private static readonly StableIdentityMatcher Matcher = new();

    private static readonly Gen<ProcedureIdentity> Identity =
        Gen.OneOfConst("A", "B", "C", "D", "E").Select(static s => new ProcedureIdentity(s));

    private static readonly Gen<ImmutableArray<ProcedureIdentity>> IdentitySet =
        Identity.Array[0, 6].Select(static a => a.ToImmutableArray());

    private static ProcedureIdentity Id(string value) => new(value);

    [Fact]
    public void IdentityOnBothSidesOnceEachIsPaired()
    {
        MatchResult result = Matcher.Match([Id("A")], [Id("A")]);
        Assert.Equal([new ProcedurePair(Id("A"), Id("A"))], result.Pairs);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Ambiguous);
    }

    [Fact]
    public void IdentityOnlyOnTheNewSideIsAdded()
    {
        MatchResult result = Matcher.Match([], [Id("A")]);
        Assert.Equal([Id("A")], result.Added);
        Assert.Empty(result.Pairs);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Ambiguous);
    }

    [Fact]
    public void IdentityOnlyOnTheOldSideIsRemoved()
    {
        MatchResult result = Matcher.Match([Id("A")], []);
        Assert.Equal([Id("A")], result.Removed);
        Assert.Empty(result.Pairs);
        Assert.Empty(result.Added);
        Assert.Empty(result.Ambiguous);
    }

    [Fact]
    public void DuplicateIdentityOnOneSideWithAMatchOnTheOtherIsAmbiguous()
    {
        MatchResult result = Matcher.Match([Id("A"), Id("A")], [Id("A")]);
        Assert.Equal([Id("A")], result.Ambiguous);
        Assert.Empty(result.Pairs);
    }

    [Fact]
    public void DuplicateIdentityOnBothSidesIsAmbiguous()
    {
        MatchResult result = Matcher.Match([Id("A"), Id("A")], [Id("A"), Id("A"), Id("A")]);
        Assert.Equal([Id("A")], result.Ambiguous);
    }

    [Fact]
    public void OrderFollowsFirstAppearanceAcrossOldThenNew()
    {
        MatchResult result = Matcher.Match([Id("B"), Id("A")], [Id("A"), Id("C")]);
        Assert.Equal([Id("B")], result.Removed);
        Assert.Equal([Id("C")], result.Added);
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => Matcher.Match(null!, []));
        Assert.Throws<ArgumentNullException>(() => Matcher.Match([], null!));
    }

    [Fact]
    public void EqualMatchResultsAreEqual()
    {
        MatchResult a = Matcher.Match([Id("A")], [Id("A"), Id("B")]);
        MatchResult b = Matcher.Match([Id("A")], [Id("A"), Id("B")]);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void MatchResultsDifferingInSkippedProjectsAreUnequal()
    {
        MatchResult plain = Matcher.Match([Id("A")], [Id("A")]);
        MatchResult legacySkipped = plain with { LegacySkipped = [Skipped("P")] };
        MatchResult modernSkipped = plain with { ModernSkipped = [Skipped("P")] };

        Assert.Empty(plain.LegacySkipped);
        Assert.Empty(plain.ModernSkipped);
        Assert.NotEqual(plain, legacySkipped);
        Assert.NotEqual(plain, modernSkipped);
        Assert.NotEqual(legacySkipped, modernSkipped);
        Assert.Equal(legacySkipped, plain with { LegacySkipped = [Skipped("P")] });
        Assert.Equal(legacySkipped.GetHashCode(), (plain with { LegacySkipped = [Skipped("P")] }).GetHashCode());
    }

    [Fact]
    public void MatchResultsDifferingInLoweringFailuresAreUnequal()
    {
        MatchResult plain = Matcher.Match([Id("A")], [Id("A")]);
        InvalidOperationException exception = new("boom");
        MatchResult failed = plain with { LoweringFailures = [new LoweringFailure(Id("A"), Id("A"), exception)] };

        Assert.Empty(plain.LoweringFailures);
        Assert.NotEqual(plain, failed);
        Assert.Equal(failed, plain with { LoweringFailures = [new LoweringFailure(Id("A"), Id("A"), exception)] });
        Assert.Equal(failed.GetHashCode(), (plain with { LoweringFailures = [new LoweringFailure(Id("A"), Id("A"), exception)] }).GetHashCode());
    }

    [Fact]
    public void UnverifiedProjectsCompareByValue()
    {
        UnverifiedProject project = Skipped("P");

        Assert.Equal(project, Skipped("P"));
        Assert.Equal(project.GetHashCode(), Skipped("P").GetHashCode());
        Assert.False(project.Equals(other: null));
        Assert.NotEqual(project, project with { Name = "Q" });
        Assert.NotEqual(project, project with { AssemblyName = "Q" });
        Assert.NotEqual(project, project with { IsCSharp = false });
        Assert.NotEqual(project, project with { Diagnostics = ["CS0012: other"] });
        Assert.NotEqual(project, project with { Procedures = [] });
    }

    [Fact]
    public void DifferentMatchResultsAreUnequal()
    {
        MatchResult a = Matcher.Match([Id("A")], [Id("A")]);
        MatchResult b = Matcher.Match([Id("A")], [Id("A"), Id("B")]);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void MatchResultIsNotEqualToNull()
    {
        Assert.False(Matcher.Match([], []).Equals(Null.Of<MatchResult>()));
    }

    [Fact]
    public void MatchingIsTotalOverGeneratedIdentitySets()
    {
        Gen.Select(IdentitySet, IdentitySet).Sample(static t =>
        {
            (ImmutableArray<ProcedureIdentity> oldIds, ImmutableArray<ProcedureIdentity> newIds) = t;
            MatchResult result = Matcher.Match(oldIds, newIds);

            HashSet<ProcedureIdentity> expected = [.. oldIds, .. newIds];
            HashSet<ProcedureIdentity> paired = [.. result.Pairs.Select(static p => p.Old)];
            HashSet<ProcedureIdentity> accounted = [.. paired, .. result.Added, .. result.Removed, .. result.Ambiguous];

            Assert.True(expected.SetEquals(accounted));
            Assert.Equal(expected.Count, paired.Count + result.Added.Length + result.Removed.Length + result.Ambiguous.Length);
        }, iter: 500);
    }

    [Fact]
    public void MatchingIsSymmetricUnderSwappingOldAndNew()
    {
        Gen.Select(IdentitySet, IdentitySet).Sample(static t =>
        {
            (ImmutableArray<ProcedureIdentity> oldIds, ImmutableArray<ProcedureIdentity> newIds) = t;
            MatchResult forward = Matcher.Match(oldIds, newIds);
            MatchResult swapped = Matcher.Match(newIds, oldIds);

            HashSet<ProcedureIdentity> forwardPaired = [.. forward.Pairs.Select(static p => p.Old)];
            HashSet<ProcedureIdentity> swappedPaired = [.. swapped.Pairs.Select(static p => p.Old)];
            Assert.True(forwardPaired.SetEquals(swappedPaired));

            Assert.True(new HashSet<ProcedureIdentity>(forward.Added).SetEquals(swapped.Removed));
            Assert.True(new HashSet<ProcedureIdentity>(forward.Removed).SetEquals(swapped.Added));
            Assert.True(new HashSet<ProcedureIdentity>(forward.Ambiguous).SetEquals(swapped.Ambiguous));
        }, iter: 500);
    }

    private static UnverifiedProject Skipped(string name) =>
        new(name, name + ".dll", IsCSharp: true, ["CS0246: The type or namespace name 'Missing' could not be found"], [new ProcedureIdentity("A")]);
}
