using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>
/// One overload's result (ticket M3-032). <see cref="Witnesses"/> holds the first divergent cases. A case whose two runs
/// differ on one side only counts on that side and is a finding of its own; one that differs on both sides is excluded
/// and counted in <see cref="BothNondeterministic"/>. <see cref="NotConstructible"/> names why the overload was not run.
/// </summary>
internal sealed record OverloadReport(
    string Member,
    int CasesRun,
    int Divergent,
    IReadOnlyList<OverloadReport.Witness> Witnesses,
    int LegacyNondeterministic,
    int ModernNondeterministic,
    int BothNondeterministic,
    int NotComparable,
    IReadOnlyList<string> NotConstructible)
{
    public static OverloadReport NotRun(string member, IReadOnlyList<string> notConstructible) =>
        new(member, 0, 0, [], 0, 0, 0, 0, notConstructible);

    internal sealed record Witness(ExecutionOutcome Legacy, ExecutionOutcome Modern);
}
