using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;

namespace Equiv.Cli;

/// <summary>
/// What each pass of one <c>equiv compare</c> run verifies with (ADR 0049 decision 2; ticket P1-032).
/// <see cref="Whole"/> is the run's options with every query on, at the first pass's budgets; the contracts pass uses it.
/// <see cref="First"/> is the first pass's. <see cref="Budget"/> is the budget pass's, null when the run has none: in quick
/// mode, and in thorough mode when the first pass's values already meet the escalation's. The first pass leaves rung 5's
/// local proposer and failure refinement on a <c>timeout</c> Unknown to the budget pass, which every pair they apply to
/// reaches; a thorough run with no budget pass asks them in its first pass, whose budgets are then the budget pass's.
/// </summary>
internal sealed record Passes(CompareMode Mode, VerificationOptions Whole, VerificationOptions First, VerificationOptions? Budget)
{
    /// <summary>The name of <see cref="CompareMode.Thorough"/> on the command line, in the config and in SARIF.</summary>
    internal const string ThoroughName = "thorough";

    /// <summary>The name of <see cref="CompareMode.Quick"/>.</summary>
    internal const string QuickName = "quick";

    /// <summary>The run property <see cref="ToProperty"/> is written as.</summary>
    internal const string Property = "mode";

    /// <summary>The run log's phase for the budget pass; with <c>-pass</c>, a result's <c>decidedBy</c>.</summary>
    internal const string BudgetPhase = "budget";

    /// <summary>The run log's phase for the IL pass; with <c>-pass</c>, a result's <c>decidedBy</c>.</summary>
    internal const string IlPhase = "il";

    public bool Thorough => Mode == CompareMode.Thorough;

    public string Name => Thorough ? ThoroughName : QuickName;

    /// <summary>
    /// The passes <paramref name="config"/>'s mode, budgets and escalation give, over <paramref name="options"/>, the
    /// backend's options at the config's budgets. The budget pass never asks with less than the first (ADR 0049 decision 4).
    /// </summary>
    public static Passes Of(EquivConfig config, VerificationOptions options)
    {
        bool thorough = config.Mode == CompareMode.Thorough;
        Escalation first = new(options.Bound, options.ResourceLimit, options.TimeoutMs);
        Escalation escalation = config.Escalation.AtLeast(first.Bound, first.ResourceLimit, first.TimeoutMs);
        VerificationOptions? budget = thorough && escalation != first
            ? options with { Bound = escalation.Bound, ResourceLimit = escalation.ResourceLimit, TimeoutMs = escalation.TimeoutMs }
            : null;
        bool asksEverything = thorough && budget is null;
        return new Passes(config.Mode, options, options with { LocalProposer = asksEverything, RefineTimeouts = asksEverything }, budget);
    }

    /// <summary>
    /// <c>run.properties.mode</c> (ADR 0049 decision 6): the mode's name, the first pass's values, the budget pass's as
    /// <c>escalation</c> when the run has one, and the names of the settings given explicitly.
    /// </summary>
    public Dictionary<string, object> ToProperty(ImmutableArray<string> explicitSettings)
    {
        Dictionary<string, object> property = new(StringComparer.Ordinal) { ["name"] = Name };
        foreach ((string budget, object value) in Budgets(First))
        {
            property[budget] = value;
        }

        if (Budget is { } escalation)
        {
            property["escalation"] = Budgets(escalation);
        }

        property["explicit"] = (List<string>)[.. explicitSettings];
        return property;
    }

    private static Dictionary<string, object> Budgets(VerificationOptions options) => new(StringComparer.Ordinal)
    {
        ["bound"] = options.Bound,
        ["resourceLimit"] = options.ResourceLimit,
        ["timeoutMs"] = options.TimeoutMs,
    };
}
