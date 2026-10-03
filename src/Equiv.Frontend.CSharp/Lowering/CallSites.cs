using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Matching;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// What one body's lowering is told and tells about its call sites (ADR 0042; ticket P2-069). It is told the callee
/// identities of the pair's rebound calls on its side, and lowers each call to one as an <c>IrOpaque</c> with reason
/// <see cref="ReboundCall.OpaqueReason"/>. It records every call it lowers at a syntax node, keyed by the node's tokens and
/// the member's name, and <see cref="Rebound(CallSites, CallSites, ImmutableDictionary{string, string})"/> finds the
/// rebound pairs of two bodies from those. It also records every forwarder a call was resolved through (ADR 0043; ticket
/// P2-068); such a call's site keeps the member name written there, and its callee is the forwarder's target.
/// </summary>
/// <param name="rebound">The callee identities whose calls the body lowers as opaque.</param>
internal sealed class CallSites(IEnumerable<string> rebound)
{
    private readonly ImmutableHashSet<string> rebound = rebound.ToImmutableHashSet(StringComparer.Ordinal);
    private readonly List<Site> seen = [];
    private readonly HashSet<ResolvedForwarder> forwarders = [];

    /// <summary>The call sites of a body none of whose calls is known to be rebound: a pair's first lowering.</summary>
    public CallSites()
        : this([])
    {
    }

    /// <summary>
    /// The identities of the forwarders the body does not resolve, because the pair's two sides do not agree on what each
    /// forwards to (ADR 0043): a call to one stays a call to it.
    /// </summary>
    public ImmutableHashSet<string> KeptForwarders { get; init; } = [];

    public bool IsRebound(CallIdentity callee) => rebound.Contains(callee.Value);

    /// <summary>Records a call to <paramref name="callee"/>, a member named <paramref name="member"/>, lowered at <paramref name="syntax"/>.</summary>
    public void Add(SyntaxNode syntax, string member, CallIdentity callee) =>
        seen.Add(new Site(string.Join(' ', syntax.DescendantTokens().Select(static t => t.Text)), member, callee));

    /// <summary>Records that a call to <paramref name="forwarder"/> was lowered as a call to <paramref name="target"/>.</summary>
    public void Forwarded(CallIdentity forwarder, CallIdentity target) => forwarders.Add(new ResolvedForwarder(forwarder.Value, target.Value));

    /// <summary>The forwarders either of a matched pair's bodies resolved, sorted by forwarder and then target.</summary>
    public static ImmutableArray<ResolvedForwarder> Forwarders(CallSites legacy, CallSites modern) =>
    [
        .. legacy.forwarders.Union(modern.forwarders)
            .OrderBy(static f => f.Forwarder, StringComparer.Ordinal)
            .ThenBy(static f => f.Target, StringComparer.Ordinal),
    ];

    /// <summary>
    /// The rebound pairs of a matched pair's bodies, sorted by legacy and then modern identity. A key (text and member)
    /// that occurs on both sides pairs each identity only the <paramref name="legacy"/> side binds it to with each one only
    /// the <paramref name="modern"/> side does. A legacy identity <paramref name="callIdentityRenames"/> maps to the modern
    /// one is the same call, and a callee in the runtime-changes table is in no pair, so it keeps its EQ006.
    /// </summary>
    public static ImmutableArray<ReboundCall> Rebound(CallSites legacy, CallSites modern, ImmutableDictionary<string, string> callIdentityRenames)
    {
        ILookup<(string Text, string Member), string> modernByKey = modern.seen
            .Where(static s => !s.Callee.RuntimeChanged)
            .ToLookup(static s => (s.Text, s.Member), static s => s.Callee.Value);
        return
        [
            .. legacy.seen
                .Where(static s => !s.Callee.RuntimeChanged)
                .GroupBy(static s => (s.Text, s.Member), static s => s.Callee.Value)
                .SelectMany(site =>
                {
                    HashSet<string> bound = new(modernByKey[site.Key], StringComparer.Ordinal);
                    HashSet<string> renamed = new(site.Select(Renamed), StringComparer.Ordinal);
                    return site
                        .Where(l => !bound.Contains(Renamed(l)))
                        .SelectMany(l => bound.Where(m => !renamed.Contains(m)).Select(m => new ReboundCall(l, m)));
                })
                .Distinct()
                .OrderBy(static r => r.Legacy, StringComparer.Ordinal)
                .ThenBy(static r => r.Modern, StringComparer.Ordinal),
        ];

        string Renamed(string identity) => callIdentityRenames.GetValueOrDefault(identity, identity);
    }

    /// <summary>One lowered call: its site's tokens, the member's name and the callee it binds to.</summary>
    private sealed record Site(string Text, string Member, CallIdentity Callee);
}
