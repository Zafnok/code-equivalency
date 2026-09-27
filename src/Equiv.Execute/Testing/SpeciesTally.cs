namespace Equiv.Execute.Testing;

/// <summary>
/// The species seen so far and how often (ticket P1-008). <see cref="DiscoveryProbability"/> is the Good-Turing estimate
/// of Böhme, Liyanage and Wüstholz (FSE 2021): after n inputs with f1 species seen exactly once, the probability that the
/// next input shows a species not seen yet is f1 / n. The generators are not adaptive, so the plain estimator applies.
/// </summary>
internal sealed class SpeciesTally
{
    private readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);

    public int Inputs { get; private set; }

    public int Singletons { get; private set; }

    public int Species => counts.Count;

    public double DiscoveryProbability => Estimate(Inputs, Singletons);

    /// <summary>f1 / n, and 1 before any input: nothing has been seen, so the next input is surely new.</summary>
    public static double Estimate(int inputs, int singletons) => inputs == 0 ? 1 : (double)singletons / inputs;

    public void Add(string species)
    {
        int count = counts.GetValueOrDefault(species) + 1;
        counts[species] = count;
        Inputs++;
        Singletons += count switch
        {
            1 => 1,
            2 => -1,
            _ => 0,
        };
    }
}
