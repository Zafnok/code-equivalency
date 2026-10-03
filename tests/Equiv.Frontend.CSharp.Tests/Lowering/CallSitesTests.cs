using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// ADR 0042 (ticket P2-069): which callee pairs two bodies' call sites make rebound. A site's key is its tokens and the
/// member's name; a key on both sides that binds to different identities pairs them.
/// </summary>
public sealed class CallSitesTests
{
    private const string Text = "fs.File.Exists(p)";
    private const string Legacy = "Lib.FileBase::Exists(string)";
    private const string Modern = "Lib.IFile::Exists(string)";

    private static readonly ImmutableDictionary<string, string> NoRenames = [];

    [Fact]
    public void ASiteWithTheSameTextBoundToAnotherCalleeIsAReboundPair()
    {
        ImmutableArray<ReboundCall> rebound = CallSites.Rebound(Sites((Text, "Exists", Legacy)), Sites((Text, "Exists", Modern)), NoRenames);

        Assert.Equal([new ReboundCall(Legacy, Modern)], rebound);
    }

    [Fact]
    public void ASiteBoundToTheSameCalleeIsNotRebound() =>
        Assert.Empty(CallSites.Rebound(Sites((Text, "Exists", Legacy)), Sites((Text, "Exists", Legacy)), NoRenames));

    /// <summary>An edit the developer made stays a difference: other text, or another member at the same text.</summary>
    [Theory]
    [InlineData("fs.File.Delete(p)", "Exists")]
    [InlineData(Text, "Delete")]
    public void ASiteWithOtherTextOrAnotherMemberIsNotRebound(string modernText, string modernMember) =>
        Assert.Empty(CallSites.Rebound(Sites((Text, "Exists", Legacy)), Sites((modernText, modernMember, Modern)), NoRenames));

    [Fact]
    public void LayoutAndCommentsAreNotPartOfTheKey()
    {
        ImmutableArray<ReboundCall> rebound = CallSites.Rebound(
            Sites(("fs.File.Exists( p )", "Exists", Legacy)),
            Sites(("fs.File\n    .Exists(/* the path */ p)", "Exists", Modern)),
            NoRenames);

        Assert.Equal([new ReboundCall(Legacy, Modern)], rebound);
    }

    /// <summary>A legacy identity the config's call-identity map renames to the modern one is the same call.</summary>
    [Fact]
    public void ACallIdentityRenameMakesTheTwoOneCall()
    {
        ImmutableDictionary<string, string> renames = NoRenames.Add(Legacy, Modern);

        Assert.Empty(CallSites.Rebound(Sites((Text, "Exists", Legacy)), Sites((Text, "Exists", Modern)), renames));
        Assert.Equal(
            [new ReboundCall(Legacy, "Lib.Other::Exists(string)")],
            CallSites.Rebound(Sites((Text, "Exists", Legacy)), Sites((Text, "Exists", "Lib.Other::Exists(string)")), renames));
    }

    /// <summary>A callee in the runtime-changes table keeps its EQ006, whichever side calls it.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ARuntimeChangedCalleeIsInNoPair(bool legacyChanged, bool modernChanged)
    {
        CallSites legacy = new();
        legacy.Add(SyntaxFactory.ParseExpression(Text), "Exists", new CallIdentity(Legacy, RuntimeChanged: legacyChanged));
        CallSites modern = new();
        modern.Add(SyntaxFactory.ParseExpression(Text), "Exists", new CallIdentity(Modern, RuntimeChanged: modernChanged));

        Assert.Empty(CallSites.Rebound(legacy, modern, NoRenames));
    }

    /// <summary>
    /// A key bound to several identities on a side pairs each legacy-only identity with each modern-only one, once, sorted
    /// by legacy and then modern identity; an identity both sides bind there is in no pair.
    /// </summary>
    [Fact]
    public void AKeyBoundToSeveralIdentitiesPairsEachLegacyOnlyWithEachModernOnly()
    {
        CallSites legacy = Sites(("x.F()", "F", "B::F()"), ("x.F()", "F", "S::F()"), ("x.F()", "F", "A::F()"), ("x.F()", "F", "A::F()"));
        CallSites modern = Sites(("x.F()", "F", "D::F()"), ("x.F()", "F", "S::F()"), ("x.F()", "F", "C::F()"), ("y.G()", "G", "E::G()"));

        ImmutableArray<ReboundCall> rebound = CallSites.Rebound(legacy, modern, NoRenames);

        Assert.Equal(
            [new ReboundCall("A::F()", "C::F()"), new ReboundCall("A::F()", "D::F()"), new ReboundCall("B::F()", "C::F()"), new ReboundCall("B::F()", "D::F()")],
            rebound);
    }

    [Fact]
    public void ABodyHoldsTheIdentitiesItWasToldAreRebound()
    {
        CallSites told = new([Legacy]);

        Assert.True(told.IsRebound(new CallIdentity(Legacy)));
        Assert.False(told.IsRebound(new CallIdentity(Modern)));
        Assert.False(new CallSites().IsRebound(new CallIdentity(Legacy)));
    }

    /// <summary>ADR 0047 (ticket P2-068): the forwarders of both bodies, each once, sorted by forwarder and then target.</summary>
    [Fact]
    public void TheForwardersOfBothBodiesAreListedOnceAndSorted()
    {
        CallSites legacy = new();
        legacy.Forwarded(new CallIdentity("B::F()"), new CallIdentity("T::Z()"));
        legacy.Forwarded(new CallIdentity("B::F()"), new CallIdentity("T::Z()"));
        legacy.Forwarded(new CallIdentity("A::F()"), new CallIdentity("T::Y()"));
        CallSites modern = new();
        modern.Forwarded(new CallIdentity("B::F()"), new CallIdentity("T::Z()"));
        modern.Forwarded(new CallIdentity("A::F()"), new CallIdentity("T::X()"));

        Assert.Equal(
            [new ResolvedForwarder("A::F()", "T::X()"), new ResolvedForwarder("A::F()", "T::Y()"), new ResolvedForwarder("B::F()", "T::Z()")],
            CallSites.Forwarders(legacy, modern));
        Assert.Empty(CallSites.Forwarders(new CallSites(), new CallSites()));
    }

    private static CallSites Sites(params (string Text, string Member, string Callee)[] calls)
    {
        CallSites sites = new();
        foreach ((string text, string member, string callee) in calls)
        {
            sites.Add(SyntaxFactory.ParseExpression(text), member, new CallIdentity(callee));
        }

        return sites;
    }
}
