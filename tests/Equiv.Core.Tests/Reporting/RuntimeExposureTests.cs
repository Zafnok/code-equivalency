using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Reporting;
using Equiv.Core.RuntimeChanges;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Core.Tests.Reporting;

/// <summary>
/// Ticket P2-238 (ADR 0060 decisions 1 to 5): a pair whose equal texts only a runtime rule keeps out of congruence is a
/// runtime exposure. Its EQ006 and EQ003 are EQ007, with and without a model; EQ001 and EQ002 are unchanged; the real
/// runtimes showing the difference make it EQ006.
/// </summary>
public sealed class RuntimeExposureTests
{
    private const string Combine = "System.IO.Path::Combine(string,string)";
    private const string CombineUrl = "https://learn.microsoft.com/en-us/dotnet/api/system.io.path.combine";
    private const string Identity = "N.Paths::Join(string)";

    private static readonly RuntimeInterval Migration = new(TargetRuntime.Parse("net48")!, TargetRuntime.Parse("net5.0")!);

    /// <summary>The snapshot of an unedited body calling <c>Path.Combine</c> across net48 and net5.0, with a solver model.</summary>
    [Fact]
    public Task WitnessedExposure() => VerifyJson(Serialize(Exposed(Witnessed())));

    /// <summary>The same body without one: the solver did not decide.</summary>
    [Fact]
    public Task UnwitnessedExposure() => VerifyJson(Serialize(Exposed(new Unknown(UnknownReason.Timeout, "solver gave up after 5000ms"))));

    /// <summary>Criterion 1: what is EQ006 today is EQ007 with <c>properties.model</c>, a warning that fails.</summary>
    [Fact]
    public void AWitnessedExposureIsEq007WithItsModel()
    {
        Divergent divergent = Witnessed();

        Result result = Write(Exposed(divergent));

        Assert.Equal("EQ007", result.RuleId);
        Assert.Equal(FailureLevel.Warning, result.Level);
        Assert.Equal(ResultKind.Fail, result.Kind);
        Assert.Equal(CounterexampleText.Dump(divergent.Counterexample), result.GetProperty<string>("model"));
        Assert.Equal(CombineUrl, result.GetProperty<string>("helpUri"));
        Assert.Equal($"runtime-change:{Combine}", result.GetProperty<string>("reviewGroup"));
        Assert.Equal(
            $"{Identity}: unedited body; the runtime change between net48 and net5.0 reaches it through {Combine} ({CombineUrl}). Witness: {CounterexampleText.Dump(divergent.Counterexample)}",
            result.Message.Text);
    }

    /// <summary>Criterion 1: what is EQ003 today, whatever its reason, is EQ007 keeping <c>properties.unknownReason</c>, with no model.</summary>
    [Theory]
    [InlineData(UnknownReason.Timeout, "timeout")]
    [InlineData(UnknownReason.Abstraction, "abstraction")]
    [InlineData(UnknownReason.Opaque, "opaque")]
    [InlineData(UnknownReason.UnalignedLoop, "unaligned-loop")]
    [InlineData(UnknownReason.Unbound, "unbound")]
    public void AnUnwitnessedExposureIsEq007WithItsUnknownReason(UnknownReason reason, string name)
    {
        Result result = Write(Exposed(new Unknown(reason, "why")));

        Assert.Equal("EQ007", result.RuleId);
        Assert.Equal(FailureLevel.Warning, result.Level);
        Assert.Equal(name, result.GetProperty<string>("unknownReason"));
        Assert.False(result.TryGetProperty("model", out string? _));
        Assert.Equal(
            $"{Identity}: unedited body; the runtime change between net48 and net5.0 reaches it through {Combine} ({CombineUrl}). Whether it reaches the outcome is unknown ({reason}): why",
            result.Message.Text);
    }

    [Fact]
    public void ALineScopedExposureKeepsItsClaim()
    {
        Result result = Write(Exposed(new Unknown(UnknownReason.Opaque, "Lambda") { Scope = UnknownScope.Line }));

        Assert.Equal("EQ007", result.RuleId);
        Assert.EndsWith("Whether it reaches the outcome is unknown (Opaque): Lambda. It is equivalent on every input that reaches none of the related locations.", result.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>Criterion 1: the one Equivalent stays EQ001, and an EQ002 on such a pair stays EQ002 (a modelling finding).</summary>
    [Fact]
    public void AnEquivalentOrAnEq002ExposureKeepsItsRule()
    {
        Result equivalent = Write(Exposed(new Equivalent(ProofMethod.Bounded)));
        Result divergent = Write(Exposed(new Divergent(Fixtures.Counterexample())));

        Assert.Equal("EQ001", equivalent.RuleId);
        Assert.Equal($"{Identity} is equivalent.", equivalent.Message.Text);
        Assert.Equal("EQ002", divergent.RuleId);
        Assert.StartsWith($"{Identity} diverges: ", divergent.Message.Text, StringComparison.Ordinal);
        Assert.All([equivalent, divergent], static r => Assert.False(r.TryGetProperty("runtimeExposure", out List<Dictionary<string, string>>? _)));
    }

    /// <summary>ADR 0060 decision 1: a pair whose text differs keeps EQ006 and EQ003, because the edit is in play.</summary>
    [Fact]
    public void AnEditedBodyIsNoExposure()
    {
        VerificationResult edited = Fixtures.Result(Witnessed(), Identity) with
        {
            Runtimes = Migration,
            NotCongruent = new FingerprintDifference(FingerprintDifference.Callee, Combine),
        };

        Assert.Equal("EQ006", Write(edited).RuleId);
        Assert.Equal("EQ003", Write(edited with { Verdict = new Unknown(UnknownReason.Timeout, "slow") }).RuleId);
    }

    /// <summary>
    /// Criterion 2: one entry per rule, legacy side first, with its change point, page and the row's precondition when
    /// each is known, and the message names every rule and the interval.
    /// </summary>
    [Fact]
    public void EachRuleIsAnEntryInTheOrderNotCongruentNamesIt()
    {
        Result result = Write(Exposed(new Unknown(UnknownReason.Timeout, "slow"), $"x87, {Combine}, float-to-integer, extern, N.Unlisted::Field") with
        {
            Runtimes = new RuntimeInterval(TargetRuntime.Parse("net48")!, TargetRuntime.Parse("net9.0")!),
        });

        Assert.Equal(
            [
                Entry("x87", changedIn: null, "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/types"),
                Entry(Combine, "netcoreapp1.0", CombineUrl, "invalidPath"),
                Entry("float-to-integer", "net9.0", "https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/fp-to-integer"),
                Entry("extern"),
                Entry("N.Unlisted::Field"),
            ],
            result.GetProperty<List<Dictionary<string, string>>>("runtimeExposure"));
        Assert.StartsWith(
            $"{Identity}: unedited body; the runtime change between net48 and net9.0 reaches it through x87 (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/types), {Combine} ({CombineUrl}), float-to-integer (https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/fp-to-integer), extern, N.Unlisted::Field. ",
            result.Message.Text,
            StringComparison.Ordinal);
        Assert.Equal("runtime-change:x87", result.GetProperty<string>("reviewGroup"));
    }

    /// <summary>
    /// Criterion 1 and ADR 0058's <c>everyImport</c> rows: an unedited <c>extern</c> function a <c>marshalling</c> entry
    /// reaches is EQ007, not EQ003 <c>no-body</c>, and names the entry.
    /// </summary>
    [Fact]
    public void AnExternAnEveryImportEntryReachesIsEq007()
    {
        VerificationResult import = Exposed(new Unknown(UnknownReason.Opaque, "old: no-body; new: no-body"), "extern:netcoredeps") with
        {
            Identity = new ProcedureIdentity("N.Native::Ticks()"),
            Runtimes = new RuntimeInterval(TargetRuntime.Parse("net7.0")!, TargetRuntime.Parse("net8.0")!),
        };

        Result result = Write(import);

        Assert.Equal("EQ007", result.RuleId);
        Assert.Equal("opaque", result.GetProperty<string>("unknownReason"));
        Assert.Equal("runtime-change:extern:netcoredeps", result.GetProperty<string>("reviewGroup"));
        Assert.Equal(
            [Entry("extern:netcoredeps", "net8.0", "https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/linux-netcoredeps")],
            result.GetProperty<List<Dictionary<string, string>>>("runtimeExposure"));
        Assert.Equal(
            "N.Native::Ticks(): unedited body; the runtime change between net7.0 and net8.0 reaches it through extern:netcoredeps (https://learn.microsoft.com/en-us/dotnet/core/compatibility/interop/8.0/linux-netcoredeps). Whether it reaches the outcome is unknown (Opaque): old: no-body; new: no-body",
            result.Message.Text);
    }

    /// <summary>An exposure whose rules have no page carries no <c>helpUri</c>.</summary>
    [Fact]
    public void AnExposureWithNoPageHasNoHelpUri()
    {
        Result result = Write(Exposed(new Unknown(UnknownReason.Opaque, "old: no-body; new: no-body"), "extern"));

        Assert.Equal("EQ007", result.RuleId);
        Assert.False(result.TryGetProperty("helpUri", out string? _));
        Assert.Equal([Entry("extern")], result.GetProperty<List<Dictionary<string, string>>>("runtimeExposure"));
    }

    /// <summary>Criterion 3 (ADR 0060 decision 4): a replay that reproduces the witness makes the result EQ006, observed.</summary>
    [Fact]
    public void AReproducedWitnessIsEq006Observed()
    {
        Divergent divergent = Witnessed();

        Result result = Write(Exposed(divergent) with { Replay = ReplayResult.Reproduced });

        Assert.Equal("EQ006", result.RuleId);
        Assert.Equal(FailureLevel.Error, result.Level);
        Assert.Equal("observed", result.GetProperty<string>("proofMethod"));
        Assert.Equal("reproduced", result.GetProperty<string>("replay"));
        Assert.Equal($"runtime-change:{Combine}", result.GetProperty<string>("reviewGroup"));
        Assert.Equal([Entry(Combine, "netcoreapp1.0", CombineUrl, "invalidPath")], result.GetProperty<List<Dictionary<string, string>>>("runtimeExposure"));
        Assert.Equal(
            $"{Identity}: unedited body; the runtime change between net48 and net5.0 reaches it through {Combine} ({CombineUrl}), and the real runtimes show it: {CounterexampleText.Dump(divergent.Counterexample)}",
            result.Message.Text);
    }

    /// <summary>Criterion 3: equal outcomes on the real runtimes leave a witnessed exposure EQ007.</summary>
    [Fact]
    public void EqualOutcomesLeaveTheExposureEq007()
    {
        ExecutionOutcome outcome = new(new ExecutionInput(["\"a\"", "\"b\""]), "invariant", OutcomeKind.Returned, "\"a\\\\b\"");

        Result result = Write(Exposed(Witnessed()) with { Replay = ReplayResult.NotApplicable(outcome, outcome) });

        Assert.Equal("EQ007", result.RuleId);
        Assert.Equal("not-applicable", result.GetProperty<string>("replay"));
        Assert.False(result.TryGetProperty("proofMethod", out string? _));
    }

    /// <summary>Criterion 3: a difference differential testing observed on an unwitnessed exposure makes it EQ006, observed.</summary>
    [Fact]
    public void AnObservedDifferenceIsEq006Observed()
    {
        ExecutionInput input = new(["\"\\u0000\"", "\"i\""]);
        ObservedDivergence observed = new(
            new ExecutionOutcome(input, "invariant", OutcomeKind.Threw, "\"System.ArgumentException\""),
            new ExecutionOutcome(input, "invariant", OutcomeKind.Returned, "\"\\u0000\\\\i\""));

        Result result = Write(Exposed(Divergent.Observation(observed)));

        Assert.Equal("EQ006", result.RuleId);
        Assert.Equal(FailureLevel.Error, result.Level);
        Assert.Equal("observed", result.GetProperty<string>("proofMethod"));
        Assert.Equal(CombineUrl, result.GetProperty<string>("helpUri"));
        Assert.Equal($"runtime-change:{Combine}", result.GetProperty<string>("reviewGroup"));
        Assert.Equal(
            $"{Identity}: unedited body; the runtime change between net48 and net5.0 reaches it through {Combine} ({CombineUrl}), and the real runtimes show it: {ObservationText.Dump(observed)}",
            result.Message.Text);
    }

    /// <summary>ADR 0060 decision 5: a baseline written before this ticket shows each moved result as <c>new</c> once.</summary>
    [Fact]
    public void AMovedResultIsNewAgainstAnOlderBaseline()
    {
        VerificationResult exposure = Exposed(Witnessed());
        SarifLog before = SarifReportWriter.Write([exposure with { NotCongruent = null }]);

        Result result = SarifReportWriter.Write([exposure], before).Runs[0].Results[0];

        Assert.Equal("EQ006", before.Runs[0].Results[0].RuleId);
        Assert.Equal("EQ007", result.RuleId);
        Assert.Equal(before.Runs[0].Results[0].PartialFingerprints, result.PartialFingerprints);
        Assert.Equal(BaselineState.New, result.BaselineState);
    }

    /// <summary>Criterion 1: the EQ007 descriptor, <c>kind: fail</c> at <c>warning</c>, after EQ001 to EQ006.</summary>
    [Fact]
    public void TheDriverHasTheEq007Rule()
    {
        ReportingDescriptor rule = SarifReportWriter.Write([]).Runs[0].Tool.Driver.Rules[^1];

        Assert.Equal("EQ007", rule.Id);
        Assert.Equal("RuntimeExposure", rule.Name);
        Assert.Equal(FailureLevel.Warning, rule.DefaultConfiguration.Level);
    }

    [Theory]
    [InlineData(RuntimeChangePrecondition.InvalidPath, "invalidPath")]
    [InlineData(RuntimeChangePrecondition.CultureSensitiveText, "cultureSensitiveText")]
    [InlineData(RuntimeChangePrecondition.TwoDigitYearFormat, "twoDigitYearFormat")]
    public void APreconditionIsNamedAsTheTableSpellsIt(RuntimeChangePrecondition precondition, string name) =>
        Assert.Equal(name, ExposureRule.Name(precondition));

    [Fact]
    public void DescribeRejectsANullResult() =>
        Assert.Throws<ArgumentNullException>(static () => VerdictRule.Describe((VerificationResult)null!, Migration));

    private static Divergent Witnessed()
    {
        IrCallRecord record = new(new CallIdentity(Combine, RuntimeChanged: true), [new IrSortValue("string", 1), new IrSortValue("string", 2)]);
        return new Divergent(Fixtures.Counterexample() with { Old = Fixtures.Run() with { Trace = [record] } });
    }

    private static VerificationResult Exposed(Verdict verdict, string rules = Combine) =>
        Fixtures.Result(verdict, Identity) with { Runtimes = Migration, NotCongruent = new FingerprintDifference(FingerprintDifference.RuntimeRule, rules) };

    private static Dictionary<string, string> Entry(string rule, string? changedIn = null, string? url = null, string? precondition = null)
    {
        Dictionary<string, string> entry = new(StringComparer.Ordinal) { ["rule"] = rule };
        if (changedIn is not null)
        {
            entry["changedIn"] = changedIn;
        }

        if (url is not null)
        {
            entry["url"] = url;
        }

        if (precondition is not null)
        {
            entry["precondition"] = precondition;
        }

        return entry;
    }

    private static Result Write(VerificationResult result) => SarifReportWriter.Write([result]).Runs[0].Results[0];

    private static string Serialize(VerificationResult result)
    {
        SarifLog log = SarifReportWriter.Write([result]);
        string path = Path.GetTempFileName();
        try
        {
            log.Save(path);
            return File.ReadAllText(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
