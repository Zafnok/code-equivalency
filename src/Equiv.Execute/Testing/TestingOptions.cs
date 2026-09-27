using System.Globalization;

namespace Equiv.Execute.Testing;

/// <summary>
/// When testing an Unknown pair stops (ticket P1-008): once the estimated discovery probability is below
/// <see cref="Target"/> after at least <see cref="DifferentialTester.MinimumInputs"/> inputs, or when
/// <see cref="Inputs"/> inputs have run or <see cref="Time"/> has passed, whichever comes first. <c>--test-target</c> and
/// <c>--test-budget</c> set them; <see cref="TryParse"/> is their validation.
/// </summary>
public sealed record TestingOptions(double Target, int Inputs, TimeSpan Time)
{
    public const string TargetUsage = "--test-target must be a number greater than 0 and less than 1, as in 0.001";

    public const string BudgetUsage = "--test-budget must be a positive input count, optionally with a positive number of seconds, as in 10000 or 10000,60";

    public static TestingOptions Default { get; } = new(0.001, 10_000, TimeSpan.FromSeconds(60));

    /// <summary>
    /// The options <paramref name="target"/> and <paramref name="budget"/> spell, each defaulted when null, or null with
    /// the reason in <paramref name="error"/>. A budget is <c>&lt;inputs&gt;</c> or <c>&lt;inputs&gt;,&lt;seconds&gt;</c>.
    /// </summary>
    public static TestingOptions? TryParse(string? target, string? budget, out string error)
    {
        TestingOptions options = Default;
        error = string.Empty;
        if (target is not null)
        {
            if (!double.TryParse(target, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double parsed) || parsed is <= 0 or >= 1)
            {
                error = TargetUsage;
                return null;
            }

            options = options with { Target = parsed };
        }

        if (budget is null)
        {
            return options;
        }

        string[] parts = budget.Split(',');
        if (parts.Length > 2 || !Positive(parts[0], out int inputs) || (parts.Length == 2 && !Positive(parts[1], out _)))
        {
            error = BudgetUsage;
            return null;
        }

        return options with
        {
            Inputs = inputs,
            Time = parts.Length == 2 ? TimeSpan.FromSeconds(int.Parse(parts[1], CultureInfo.InvariantCulture)) : options.Time,
        };
    }

    private static bool Positive(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
}
