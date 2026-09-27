using Equiv.Core.Execution;

namespace Equiv.Execute.Testing;

/// <summary>
/// An outcome's class for the species definition (ticket P1-008): the exception type, or <c>returned</c> plus one of
/// <see cref="Buckets"/> buckets of a fixed hash of the canonical value, or <c>not-comparable</c>. The hash is FNV-1a, so
/// a class is the same on every machine and every .NET version.
/// </summary>
internal static class OutcomeClass
{
    public const int Buckets = 16;

    public static string Of(ExecutionOutcome outcome) => outcome.Kind switch
    {
        OutcomeKind.Threw => "threw " + outcome.Canonical,
        OutcomeKind.Returned => $"returned #{Bucket(outcome.Canonical)}",
        _ => "not-comparable",
    };

    public static uint Bucket(string canonical)
    {
        uint hash = 2166136261;
        foreach (char c in canonical)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash % Buckets;
    }
}
