using System.Globalization;

using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;
using Equiv.Execute.Testing;

using Xunit;

using static Equiv.Execute.Tests.FakeFactory;

namespace Equiv.Execute.Tests.Testing;

public sealed class DifferentialTesterTests
{
    private static readonly ExecutionDrivers Drivers = new("legacy.exe", "modern.dll");

    private static readonly IrProcedure Body = IrText.Parse("""
        proc "T::M" (%a: bv32) -> bv32 entry B0
        B0:
          ret %a
        """);

    private static readonly TestingPlan IntPlan = TestingPlan.Runnable(Drivers, [Parameter(ExecutionTypeKind.Signed32)], []);

    private static TestingOutcome Test(FakeHost host, TestingPlan plan, TestingOptions? options = null, TimeProvider? time = null, IrProcedure? body = null) =>
        new DifferentialTester(host, options ?? TestingOptions.Default, time ?? new SteppingClock(TimeSpan.Zero)).Test(plan, body ?? Body, body ?? Body);

    /// <summary>Both sides return the argument, so the species follow the values' hash buckets.</summary>
    private static string Echo(string line) => $"[\"Returned\",{line[(line.IndexOf(',', StringComparison.Ordinal) + 1)..^1]}]";

    [Fact]
    public void StopsAtTarget()
    {
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");

        TestingOutcome outcome = Test(host, IntPlan);

        Assert.Null(outcome.Observed);
        Assert.Equal(DifferentialTesting.Tested(DifferentialTester.MinimumInputs, 1, 0, 0, TestingStop.Target), outcome.Testing);
        Assert.Equal(["legacy.exe", "modern.dll"], host.Starts);
        Assert.Equal(2 * DifferentialTester.MinimumInputs, host.Exchanges.Count);
    }

    [Fact]
    public void StopsAtBudget()
    {
        FakeHost host = new((_, _, line) => Echo(line));

        TestingOutcome byInputs = Test(host, IntPlan, TestingOptions.Default with { Inputs = 300 });
        TestingOutcome byTime = Test(host, IntPlan, time: new SteppingClock(TimeSpan.FromSeconds(1)));

        Assert.Equal((300, TestingStop.Budget), (byInputs.Testing!.Inputs, byInputs.Testing.StoppedBy));
        Assert.Equal(byInputs.Testing.Singletons / 300d, byInputs.Testing.DiscoveryProbability);

        // The clock moves a second per reading: the start, then one reading per input, so 60 s pass on input 60.
        Assert.Equal((60, TestingStop.Budget), (byTime.Testing!.Inputs, byTime.Testing.StoppedBy));
    }

    [Fact]
    public void SeedsWithCandidateCounterexamples()
    {
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");
        TestingPlan plan = TestingPlan.Runnable(Drivers, [Parameter(ExecutionTypeKind.Signed32)], [new ExecutionInput(["42"])]);

        _ = Test(host, plan, TestingOptions.Default with { Inputs = 3 });

        Assert.Equal(
            ["[\"invariant\",42]", "[\"invariant\",42]", "[\"invariant\",0]", "[\"invariant\",0]", "[\"invariant\",1]", "[\"invariant\",1]"],
            host.Exchanges.Select(static e => e.Line),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ObservedDivergence_IsEq002Observed()
    {
        FakeHost host = new(static (driver, _, line) =>
            line.EndsWith(",-1]", StringComparison.Ordinal) && driver.EndsWith(".dll", StringComparison.Ordinal) ? "[\"Threw\",\"System.OverflowException\"]" : "[\"Returned\",0]");

        TestingOutcome outcome = Test(host, IntPlan);

        Assert.Null(outcome.Testing);
        ObservedDivergence observed = Assert.IsType<ObservedDivergence>(outcome.Observed);
        Assert.Equal(("-1", "invariant", OutcomeKind.Returned, "0"), (observed.Legacy.Input.Arguments[0], observed.Legacy.Culture, observed.Legacy.Kind, observed.Legacy.Canonical));
        Assert.Equal((OutcomeKind.Threw, "\"System.OverflowException\""), (observed.Modern.Kind, observed.Modern.Canonical));

        // Inputs 0, 1 and -1 on the streams, then -1 again on one fresh process per side.
        Assert.Equal(["legacy.exe", "modern.dll", "legacy.exe", "modern.dll"], host.Starts);
        Assert.Equal(8, host.Exchanges.Count);
        Assert.Equal(4, host.Disposed);

        VerificationResult unknown = new(new ProcedureIdentity("T::M"), new Unknown(UnknownReason.Opaque, "old: lambda")) { AssumedCallees = ["T::F"] };
        VerificationResult divergent = outcome.Apply(unknown);
        Assert.Equal(Divergent.Observation(observed), divergent.Verdict);
        Assert.Equal(["T::F"], divergent.AssumedCallees);
        Assert.Null(divergent.Testing);
    }

    [Fact]
    public void ADivergenceThatDoesNotRepeatIsANewSpeciesNotAnObservation()
    {
        FakeHost host = new(static (driver, session, line) =>
            line.EndsWith(",-1]", StringComparison.Ordinal) && session == 2 ? "[\"Returned\",2]" : "[\"Returned\",0]");

        TestingOutcome outcome = Test(host, IntPlan);

        Assert.Null(outcome.Observed);
        Assert.Equal(TestingStop.Target, outcome.Testing!.StoppedBy);
        Assert.Equal(2, outcome.Testing.Species);
    }

    [Theory]
    [InlineData("modern.dll", "[\"Returned\",0]", "[\"Returned\",1]")]
    [InlineData("legacy.exe", "[\"Returned\",1]", "[\"Returned\",0]")]
    public void ARepeatThatChangesEitherSideIsNoise(string noisy, string first, string again)
    {
        FakeHost host = new((driver, session, _) => !string.Equals(driver, noisy, StringComparison.Ordinal) ? "[\"Returned\",5]" : session <= 2 ? first : again);

        Assert.Null(Test(host, IntPlan, TestingOptions.Default with { Inputs = 1 }).Observed);
    }

    [Fact]
    public void AnUnanswerableCaseIsASpeciesNotADivergence()
    {
        FakeHost host = new(static (driver, _, _) => driver.EndsWith(".exe", StringComparison.Ordinal) ? null : "[\"Returned\",0]");

        TestingOutcome outcome = Test(host, IntPlan, TestingOptions.Default with { Inputs = 2 });

        Assert.Equal((2, 1, 0), (outcome.Testing!.Inputs, outcome.Testing.Species, outcome.Testing.Singletons));
    }

    [Theory]
    [InlineData("legacy.exe", "the legacy side gave NotConstructible \"System.FormatException\"")]
    [InlineData("modern.dll", "the modern side gave NotConstructible \"System.FormatException\"")]
    public void ASideThatCannotBuildTheArgumentsIsNotConstructible(string broken, string reason)
    {
        FakeHost host = new((driver, _, _) => string.Equals(driver, broken, StringComparison.Ordinal) ? "[\"NotConstructible\",\"System.FormatException\"]" : "[\"Returned\",0]");

        Assert.Equal(new TestingOutcome(DifferentialTesting.Unconstructible(reason), Observed: null), Test(host, IntPlan));
    }

    [Fact]
    public void APlanWithoutDriversIsNotConstructibleAndRunsNothing()
    {
        FakeHost host = new(static (_, _, _) => null);

        TestingOutcome outcome = Test(host, TestingPlan.NotConstructible("generic"));

        Assert.Equal("generic", outcome.Testing!.NotConstructible);
        Assert.Empty(host.Starts);
        VerificationResult unknown = new(new ProcedureIdentity("T::M"), new Unknown(UnknownReason.Opaque, "old: lambda"));
        Assert.Equal(unknown with { Testing = outcome.Testing }, outcome.Apply(unknown));
    }

    [Theory]
    [InlineData("%r: bv32 = call \"System.String::ToUpper()\"!(%a)")]
    [InlineData("%r: sort \"System.Double\" = pure \"f64.parse\"!(%a)")]
    public void ARuntimeChangedCallAddsTheTurkishCulture(string instruction)
    {
        IrProcedure body = IrText.Parse($"""
            proc "T::M" (%a: bv32) -> bv32 entry B0
            B0:
              {instruction}
              ret %a
            """);
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");

        _ = Test(host, IntPlan, TestingOptions.Default with { Inputs = 1 }, body: body);

        Assert.Equal(["[\"invariant\",0]", "[\"invariant\",0]", "[\"tr-TR\",0]", "[\"tr-TR\",0]"], host.Exchanges.Select(static e => e.Line), StringComparer.Ordinal);
    }

    [Fact]
    public void TheIrPathTellsSpeciesApart()
    {
        IrProcedure body = IrText.Parse("""
            proc "T::M" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              br %c, B1, B2
            B1:
              ret %a
            B2:
              ret %a
            """);
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");

        TestingOutcome outcome = Test(host, IntPlan, TestingOptions.Default with { Inputs = 5 }, body: body);

        // 0, 1, -1, int.MinValue, int.MaxValue: two paths, each seen more than once.
        Assert.Equal((5, 2, 0), (outcome.Testing!.Inputs, outcome.Testing.Species, outcome.Testing.Singletons));
    }

    [Fact]
    public void Tester_RejectsNullArguments()
    {
        DifferentialTester tester = new(new FakeHost(static (_, _, _) => null), TestingOptions.Default, TimeProvider.System);

        Assert.Throws<ArgumentNullException>(() => tester.Test(null!, Body, Body));
        Assert.Throws<ArgumentNullException>(() => tester.Test(IntPlan, null!, Body));
        Assert.Throws<ArgumentNullException>(() => tester.Test(IntPlan, Body, null!));
        Assert.Throws<ArgumentNullException>(() => new TestingOutcome(Testing: null, Observed: null).Apply(null!));
    }

    [Theory]
    [InlineData(null, null, 0.001, 10_000, 60)]
    [InlineData("0.01", null, 0.01, 10_000, 60)]
    [InlineData("1e-4", "500", 0.0001, 500, 60)]
    [InlineData(null, "2000,5", 0.001, 2_000, 5)]
    public void Options_ParseTargetAndBudget(string? target, string? budget, double expectedTarget, int inputs, int seconds)
    {
        Assert.Equal(new TestingOptions(expectedTarget, inputs, TimeSpan.FromSeconds(seconds)), TestingOptions.TryParse(target, budget, out string error));
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("0", null, TestingOptions.TargetUsage)]
    [InlineData("1", null, TestingOptions.TargetUsage)]
    [InlineData("-0.1", null, TestingOptions.TargetUsage)]
    [InlineData("often", null, TestingOptions.TargetUsage)]
    [InlineData(null, "0", TestingOptions.BudgetUsage)]
    [InlineData(null, "many", TestingOptions.BudgetUsage)]
    [InlineData(null, "10,0", TestingOptions.BudgetUsage)]
    [InlineData(null, "10,5,5", TestingOptions.BudgetUsage)]
    [InlineData(null, "10,5s", TestingOptions.BudgetUsage)]
    public void Options_RejectNonsense(string? target, string? budget, string usage)
    {
        Assert.Null(TestingOptions.TryParse(target, budget, out string error));
        Assert.Equal(usage, error);
    }

    /// <summary>A clock that moves <paramref name="step"/> every time it is read.</summary>
    private sealed class SteppingClock(TimeSpan step) : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            long now = ticks;
            ticks += step.Ticks;
            return now;
        }
    }
}
