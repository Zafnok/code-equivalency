using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Execute.Tests;

public sealed class ReplayerTests
{
    private const string TurkishUpper = "%r: bv32 = call \"System.String::ToUpper()\"!(%a)";

    private static readonly ReplayPlan Plan = ReplayPlan.Runnable(
        new ExecutionDrivers("legacy.exe", "modern.dll"), new ExecutionInput(["null"]), new ExecutionInput(["null", "1"]));

    private static readonly IrProcedure Body = Procedure(string.Empty);

    /// <summary>A solver counterexample: both sides return, different values, and neither calls a runtime-changed member.</summary>
    private static readonly Counterexample Model = new(
        new IrInputs([]), new IrRun(new IrReturned(new IrBitVecValue(32, 1)), [], []), new IrRun(new IrReturned(new IrBitVecValue(32, 2)), [], []));

    /// <summary>An EQ006 counterexample: the legacy side's trace holds a call to a runtime-changed member.</summary>
    private static readonly Counterexample RuntimeChangedModel = Model with
    {
        Old = Model.Old with { Trace = [new IrCallRecord(new CallIdentity("System.String::IndexOf(System.String)", RuntimeChanged: true), [])] },
    };

    private static IrProcedure Procedure(string instruction) => IrText.Parse($"""
        proc "T::M" (%a: bv32) -> bv32 entry B0
        B0:
          {instruction}
          ret %a
        """);

    private static ReplayResult Replay(string legacy, string modern, FakeHost? observed = null, Counterexample? model = null, IrProcedure? old = null, IrProcedure? @new = null)
    {
        FakeHost host = observed ?? new FakeHost((driver, _, _) => driver.EndsWith(".exe", StringComparison.Ordinal) ? legacy : modern);
        return new Replayer(host).Replay(Plan, model ?? Model, old ?? Body, @new ?? Body);
    }

    [Fact]
    public void Replay_RunsEachSideOnceUnderTheInvariantCulture()
    {
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");

        _ = Replay(string.Empty, string.Empty, host);

        Assert.Equal(["legacy.exe", "modern.dll"], host.Starts);
        Assert.Equal(
            [("legacy.exe", "[\"invariant\",null]"), ("modern.dll", "[\"invariant\",null,1]")],
            host.Exchanges.Select(static e => (e.Driver, e.Line)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARuntimeChangedCallOnEitherSide_AlsoReplaysUnderTheTurkishCulture(bool legacy)
    {
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");

        _ = Replay(string.Empty, string.Empty, host, old: legacy ? Procedure(TurkishUpper) : null, @new: legacy ? null : Procedure(TurkishUpper));

        Assert.Equal(["legacy.exe", "modern.dll"], host.Starts);
        Assert.Equal(
            [
                ("legacy.exe", "[\"invariant\",null]"), ("legacy.exe", "[\"tr-TR\",null]"),
                ("modern.dll", "[\"invariant\",null,1]"), ("modern.dll", "[\"tr-TR\",null,1]"),
            ],
            host.Exchanges.Select(static e => (e.Driver, e.Line)));
    }

    /// <summary>The ticket's repro: <c>s.IndexOf(t)</c> agrees under the invariant culture and differs under <c>tr-TR</c>.</summary>
    [Fact]
    public void ADivergenceOnlyUnderTheTurkishCulture_Reproduces()
    {
        FakeHost host = new(static (driver, _, line) =>
            line.StartsWith("[\"tr-TR\"", StringComparison.Ordinal) && driver.EndsWith(".dll", StringComparison.Ordinal) ? "[\"Returned\",-1]" : "[\"Returned\",0]");

        Assert.Equal(ReplayResult.Reproduced, Replay(string.Empty, string.Empty, host, RuntimeChangedModel, Procedure(TurkishUpper), Procedure(TurkishUpper)));
    }

    [Fact]
    public void DifferingOutcomes_Reproduce() =>
        Assert.Equal(
            ReplayResult.Reproduced,
            Replay("[\"Threw\",\"System.ArgumentNullException\"]", "[\"Threw\",\"System.NullReferenceException\"]"));

    [Fact]
    public void EqualOutcomes_DoNotReproduceAndCarryBoth()
    {
        ReplayResult result = Replay("[\"Returned\",\"a\"]", "[\"Returned\",\"a\"]");

        Assert.Equal(ReplayStatus.NotReproduced, result.Status);
        Assert.Equal((OutcomeKind.Returned, "\"a\""), (result.Legacy!.Kind, result.Legacy.Canonical));
        Assert.Equal((OutcomeKind.Returned, "\"a\""), (result.Modern!.Kind, result.Modern.Canonical));
        Assert.Equal("invariant", result.Legacy.Culture);
    }

    /// <summary>EQ006 claims the member differs between runtimes, not that the model's input shows it (ticket P2-038).</summary>
    [Fact]
    public void EqualOutcomesOfARuntimeChangedDivergence_AreNotApplicableAndCarryBoth()
    {
        ReplayResult result = Replay("[\"Returned\",\"a\"]", "[\"Returned\",\"a\"]", model: RuntimeChangedModel, old: Procedure(TurkishUpper));

        Assert.Equal(ReplayStatus.NotApplicable, result.Status);
        Assert.Equal((OutcomeKind.Returned, "\"a\"", "invariant"), (result.Legacy!.Kind, result.Legacy.Canonical, result.Legacy.Culture));
        Assert.Equal((OutcomeKind.Returned, "\"a\"", "invariant"), (result.Modern!.Kind, result.Modern.Canonical, result.Modern.Culture));
    }

    /// <summary>
    /// Both sides throwing the same exception where the model's runs do not both throw means the driver's <c>new T()</c> or
    /// an argument is not the model's (ticket P2-038: <c>GetHashCode()</c>, <c>CreateFromFile(string)</c>).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothSidesThrowingWhereTheModelDoesNot_IsNotConstructible(bool runtimeChanged)
    {
        ReplayResult result = Replay(
            "[\"Threw\",\"System.NullReferenceException\"]", "[\"Threw\",\"System.NullReferenceException\"]", model: runtimeChanged ? RuntimeChangedModel : Model);

        Assert.Equal(
            ReplayResult.NotConstructible("both sides threw \"System.NullReferenceException\", which the model's runs do not: the receiver or an argument is not the model's"),
            result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void BothSidesThrowingWhereOneModelRunReturns_IsNotConstructible(bool oldThrew, bool newThrew)
    {
        IrRun threw = new(new IrThrew("System.NullReferenceException"), [], []);
        Counterexample model = new(Model.Inputs, oldThrew ? threw : Model.Old, newThrew ? threw : Model.New);

        Assert.Equal(ReplayStatus.NotConstructible, Replay("[\"Threw\",\"System.IO.IOException\"]", "[\"Threw\",\"System.IO.IOException\"]", model: model).Status);
    }

    [Fact]
    public void BothSidesThrowingAsTheModelsRunsDo_DoesNotReproduce()
    {
        Counterexample model = new(
            Model.Inputs, new IrRun(new IrThrew("System.ArgumentException"), [], []), new IrRun(new IrThrew("System.ArgumentNullException"), [], []));

        ReplayResult result = Replay("[\"Threw\",\"System.ArgumentException\"]", "[\"Threw\",\"System.ArgumentException\"]", model: model);

        Assert.Equal(ReplayStatus.NotReproduced, result.Status);
    }

    [Theory]
    [InlineData("[\"NotComparable\",\"Odd.Holder\"]", "[\"Returned\",1]", "the legacy side gave NotComparable \"Odd.Holder\"")]
    [InlineData("[\"Returned\",1]", "[\"NotConstructible\",\"System.FormatException\"]", "the modern side gave NotConstructible \"System.FormatException\"")]
    public void ASideWithNoComparableOutcome_IsNotConstructible(string legacy, string modern, string reason) =>
        Assert.Equal(ReplayResult.NotConstructible(reason), Replay(legacy, modern));

    /// <summary>A culture a side cannot build stops the replay even when the invariant run agreed.</summary>
    [Fact]
    public void ATurkishRunWithNoComparableOutcome_IsNotConstructible()
    {
        FakeHost host = new(static (_, _, line) =>
            line.StartsWith("[\"tr-TR\"", StringComparison.Ordinal) ? "[\"NotConstructible\",\"System.Globalization.CultureNotFoundException\"]" : "[\"Returned\",0]");

        Assert.Equal(
            ReplayResult.NotConstructible("the legacy side gave NotConstructible \"System.Globalization.CultureNotFoundException\""),
            Replay(string.Empty, string.Empty, host, old: Procedure(TurkishUpper)));
    }

    [Fact]
    public void APlanWithoutDrivers_IsNotConstructibleWithItsReasonAndRunsNothing()
    {
        FakeHost host = new(static (_, _, _) => null);

        ReplayResult result = new Replayer(host).Replay(ReplayPlan.NotConstructible("emit-failed"), Model, Body, Body);

        Assert.Equal(ReplayResult.NotConstructible("emit-failed"), result);
        Assert.Empty(host.Starts);
    }

    [Fact]
    public void Replay_RejectsNullArguments()
    {
        Replayer replayer = new(new FakeHost(static (_, _, _) => null));

        Assert.Throws<ArgumentNullException>(() => replayer.Replay(null!, Model, Body, Body));
        Assert.Throws<ArgumentNullException>(() => replayer.Replay(Plan, null!, Body, Body));
        Assert.Throws<ArgumentNullException>(() => replayer.Replay(Plan, Model, null!, Body));
        Assert.Throws<ArgumentNullException>(() => replayer.Replay(Plan, Model, Body, null!));
    }
}
