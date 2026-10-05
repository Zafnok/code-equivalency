using System.Collections.Immutable;

using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// A model Z3 found in the context <paramref name="solving"/>, read from <paramref name="home"/>, the context of the
/// terms a caller has (ticket P2-100): a term is translated in, and a value out. The two are one context for a model of
/// a solver the caller made itself.
/// </summary>
internal sealed class SolverModel(Model model, Context solving, Context home)
{
    /// <summary>The value of <paramref name="term"/>; with <paramref name="completion"/>, of every constant and function in it.</summary>
    public Expr Eval(Expr term, bool completion) => model.Eval(term.Translate(solving), completion).Translate(home);

    /// <summary>
    /// The model's interpretation of <paramref name="function"/>, which takes one argument, as a map: its entries and
    /// the value everywhere else. Null when the model does not interpret it.
    /// </summary>
    public Interpretation? Map(FuncDecl function) =>
        model.FuncInterp(function.Translate(solving)) is { } interpretation
            ? new Interpretation(
                interpretation.Else.Translate(home),
                [.. interpretation.Entries.Select(e => new Entry(e.Args[0].Translate(home), e.Value.Translate(home)))])
            : null;

    public override string ToString() => model.ToString();

    /// <summary>A function of one argument as a model gives it.</summary>
    internal sealed record Interpretation(Expr Else, ImmutableArray<Entry> Entries);

    /// <summary>One point of an <see cref="Interpretation"/>.</summary>
    internal sealed record Entry(Expr Key, Expr Value);
}
