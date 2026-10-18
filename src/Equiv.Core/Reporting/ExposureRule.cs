using System.Collections.Immutable;
using System.Text.Json;

using Equiv.Core.Matching;
using Equiv.Core.RuntimeChanges;

namespace Equiv.Core.Reporting;

/// <summary>
/// One runtime rule a runtime exposure names (ADR 0060 decision 3; ticket P2-238): <see cref="Rule"/> is the
/// <c>runtime-changes.json</c> row's member, <c>x87</c>, <c>float-to-integer</c>, <c>extern</c> or <c>extern:</c> and a
/// <c>marshalling</c> entry's id; <see cref="ChangedIn"/> its change point and <see cref="Url"/> the page that documents
/// it, each null when there is none; and <see cref="Precondition"/> the row's precondition, when it has one. SARIF
/// <c>properties.runtimeExposure</c> lists one per rule.
/// </summary>
internal sealed record ExposureRule(string Rule, TargetRuntime? ChangedIn, Uri? Url, RuntimeChangePrecondition? Precondition)
{
    /// <summary>
    /// The rule that floating point on the 32-bit .NET Framework JIT runs on x87 (ADR 0040 decision 2). It turns on the
    /// platform as well as the runtime, so it has no change point.
    /// </summary>
    internal const string X87 = "x87";

    /// <summary>The rule that floating-point to integer conversions saturate from .NET 9 (ADR 0040 decision 2).</summary>
    internal const string FloatToInteger = "float-to-integer";

    /// <summary>The rule that the runtime marshals an <c>extern</c> function's call where no <c>marshalling</c> list covers the interval (ADR 0054 decision 4).</summary>
    internal const string Extern = "extern";

    private const string ExternPrefix = Extern + ":";

    /// <summary>The C# specification's floating-point types, which may be computed at a higher precision than their type: x87 does.</summary>
    private static readonly Uri X87Url = new("https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/types");

    private static readonly Uri FloatToIntegerUrl = new("https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/fp-to-integer");

    private static readonly TargetRuntime Saturation = TargetRuntime.Parse("net9.0")!;

    /// <summary>
    /// The rules <paramref name="notCongruent"/> names, legacy side first, when its component is
    /// <see cref="FingerprintDifference.RuntimeRule"/>: the pair's texts are equal and only these keep it out of
    /// congruence (ADR 0060 decision 1). Empty for any other pair. A row is looked up inside <paramref name="runtimes"/>, as
    /// the frontend flagged the call; a rule nothing in the table names is listed by its name alone.
    /// </summary>
    public static ImmutableArray<ExposureRule> Of(FingerprintDifference? notCongruent, RuntimeInterval runtimes) =>
        notCongruent is { Component: FingerprintDifference.RuntimeRule, Identity: var identity }
            ? [.. identity.Split(", ").Select(rule => Resolve(rule, runtimes))]
            : [];

    /// <summary>The precondition's name as <c>runtime-changes.json</c> spells it: <c>invalidPath</c>, <c>cultureSensitiveText</c>, ...</summary>
    public static string Name(RuntimeChangePrecondition precondition) => JsonNamingPolicy.CamelCase.ConvertName(precondition.ToString());

    private static ExposureRule Resolve(string rule, RuntimeInterval runtimes)
    {
        RuntimeChangeTable table = RuntimeChangeTable.Load();
        return rule switch
        {
            X87 => new ExposureRule(rule, ChangedIn: null, X87Url, Precondition: null),
            FloatToInteger => new ExposureRule(rule, Saturation, FloatToIntegerUrl, Precondition: null),
            Extern => new ExposureRule(rule, ChangedIn: null, Url: null, Precondition: null),
            _ when rule.StartsWith(ExternPrefix, StringComparison.Ordinal)
                && table.Marshalling.FirstOrDefault(m => string.Equals(ExternPrefix + m.Id, rule, StringComparison.Ordinal)) is { } change =>
                new ExposureRule(rule, change.ChangedIn, change.Url, Precondition: null),
            _ when table.TryMatch(new CallIdentity(rule), runtimes, out RuntimeChange row) => new ExposureRule(row.Member, row.ChangedIn, row.Url, row.Precondition),
            _ => new ExposureRule(rule, ChangedIn: null, Url: null, Precondition: null),
        };
    }
}
