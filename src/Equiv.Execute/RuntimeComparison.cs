using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>Compares the canonical outcomes of the two runtimes case by case (ADR 0035, ticket M3-032).</summary>
internal static class RuntimeComparison
{
    public const int MaxWitnesses = 5;

    public static OverloadReport Compare(string member, RunOutcomes runs)
    {
        List<OverloadReport.Witness> witnesses = [];
        int divergent = 0, legacyOnly = 0, modernOnly = 0, both = 0, notComparable = 0;
        for (int i = 0; i < runs.Legacy1.Count; i++)
        {
            ExecutionOutcome legacy = runs.Legacy1[i];
            ExecutionOutcome modern = runs.Modern1[i];
            bool legacyVaries = !Identical(legacy, runs.Legacy2[i]);
            bool modernVaries = !Identical(modern, runs.Modern2[i]);
            if (legacyVaries && modernVaries)
            {
                both++;
            }
            else if (legacyVaries)
            {
                legacyOnly++;
            }
            else if (modernVaries)
            {
                modernOnly++;
            }
            else if (!Comparable(legacy) || !Comparable(modern))
            {
                notComparable++;
            }
            else if (!Same(legacy, modern))
            {
                divergent++;
                if (witnesses.Count < MaxWitnesses)
                {
                    witnesses.Add(new OverloadReport.Witness(legacy, modern));
                }
            }
        }

        return new OverloadReport(member, runs.Legacy1.Count, divergent, witnesses, legacyOnly, modernOnly, both, notComparable, []);
    }

    public static bool Comparable(ExecutionOutcome outcome) => outcome.Kind is OutcomeKind.Returned or OutcomeKind.Threw;

    /// <summary>
    /// Whether two runtimes' outcomes are the same behaviour: both returned or threw, alike. Two outcomes with no canonical
    /// form say nothing about the values, so they are never the same, whatever their text (ticket P2-242).
    /// </summary>
    public static bool Same(ExecutionOutcome a, ExecutionOutcome b) => Comparable(a) && Comparable(b) && Identical(a, b);

    /// <summary>Whether two outcomes are the same answer, comparable or not: what one side giving it again means (a rerun).</summary>
    public static bool Identical(ExecutionOutcome a, ExecutionOutcome b) =>
        a.Kind == b.Kind && string.Equals(a.Canonical, b.Canonical, StringComparison.Ordinal);
}
