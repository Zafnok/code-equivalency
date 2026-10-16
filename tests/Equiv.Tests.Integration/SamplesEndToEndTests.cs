using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

using Equiv.Cli;
using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-003: <see cref="Program.Main"/>, wired to the real <c>Equiv.Frontend.CSharp</c>/<c>Equiv.Verify.Z3</c>
/// backend, end to end on every directory under <c>samples/</c>. Criterion 3's snapshot and criterion 4's explicit
/// per-verdict assertions both read the one cached <see cref="RunSample"/> per sample, so each sample runs once no
/// matter how many facts examine it.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed partial class SamplesEndToEndTests
{
    private static readonly ConcurrentDictionary<string, Lazy<SampleRun>> Cache = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Lazy<SampleRun>> ThoroughCache = new(StringComparer.Ordinal);

    /// <summary>
    /// The first pass's resource limit on every sample run here, pinned on purpose (ticket P1-032; P2-050's
    /// <c>loop-fusion</c> margin): the samples' expected verdicts were chosen at 5,000,000, and they are about what
    /// <c>equiv</c> decides, not about where the default budget sits.
    /// </summary>
    private static readonly string[] Pinned = ["--resource-limit", "5000000"];

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples =>
        [.. Directory.GetDirectories(SamplesRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task MatchesTheCheckedInSarifSnapshotAndItsReadmesExitCode(string sample)
    {
        SampleRun run = RunSample(sample);

        Assert.Equal(ExpectedExitCode(sample), run.ExitCode);

        string expectedPath = Path.Combine(SamplesRoot, sample, "expected.sarif.json");
        if (!File.Exists(expectedPath))
        {
            // First run for a new sample (or a deleted snapshot): write today's output, but still fail, as Verify does,
            // so CI never passes a sample nobody has reviewed. Commit the file and it is reviewed like code in the PR diff.
            await File.WriteAllTextAsync(expectedPath, run.NormalizedSarif, TestContext.Current.CancellationToken);
            Assert.Fail($"{sample}/expected.sarif.json was missing; wrote today's output. Review it, commit it, and re-run.");
        }

        string expected = await File.ReadAllTextAsync(expectedPath, TestContext.Current.CancellationToken);
        Assert.Equal(expected, run.NormalizedSarif);
    }

    /// <summary>Criterion 5: a second run with the first SARIF as <c>--baseline</c> exits 0 and carries every result as unchanged.</summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void BaselineRoundTripIsAllUnchanged(string sample)
    {
        SampleRun first = RunSample(sample);
        string baselinePath = Path.Combine(Path.GetTempPath(), $"equiv-M3-003-baseline-{Guid.NewGuid():N}.sarif");
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M3-003-rerun-{Guid.NewGuid():N}.sarif");
        try
        {
            File.WriteAllText(baselinePath, first.Json);
            (string legacy, string modern) = Solutions(sample);

            int exitCode = RunProgramSilently(() => Program.Main(
                ["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, "--baseline", baselinePath, .. Pinned, .. ConfigOptions(sample)]));

            Assert.Equal(ExitCodes.Success, exitCode);
            SarifLog log = SarifLog.Load(outPath);
            Assert.NotEmpty(log.Runs[0].Results);
            Assert.All(log.Runs[0].Results, static r => Assert.Equal(BaselineState.Unchanged, r.BaselineState));
        }
        finally
        {
            File.Delete(baselinePath);
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Identical_EveryProcedureIsEquivalentWithAProofMethod()
    {
        Result[] results = [.. RunSample("identical").Log.Runs[0].Results];
        Assert.NotEmpty(results);
        Assert.All(results, static r =>
        {
            Assert.Equal("EQ001", r.RuleId);
            Assert.True(r.TryGetProperty("proofMethod", out string? proofMethod));
            Assert.False(string.IsNullOrEmpty(proofMethod));
        });
    }

    /// <summary>
    /// Ticket P1-029, criterion 3: with the same <c>await using</c> or <c>await foreach</c> on both sides, each pair is
    /// compared on the statement inside it, one Equivalent and one Divergent per construct.
    /// </summary>
    [Theory]
    [InlineData("::FlushAsync(", "EQ001")]
    [InlineData("::CloseAsync(", "EQ002")]
    [InlineData("::LastAsync(", "EQ001")]
    [InlineData("::FirstAsync(", "EQ002")]
    public void AsyncDisposal_EachConstructHasAnEquivalentAndADivergentPair(string procedure, string rule) =>
        Assert.Equal(rule, Single("async-disposal", procedure).RuleId);

    [Fact]
    public void RenamedLocals_EveryProcedureIsEquivalent()
    {
        Result[] results = [.. RunSample("renamed-locals").Log.Runs[0].Results];
        Assert.NotEmpty(results);
        Assert.All(results, static r => Assert.Equal("EQ001", r.RuleId));
    }

    [Fact]
    public void AddedBranch_IsDivergentWithACounterexample()
    {
        Result result = Single("added-branch", "::Double(");
        Assert.Equal("EQ002", result.RuleId);
        Assert.Contains("inputs(bv32 0)", result.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedNullCheck_IsDivergentOnDifferingExceptionTypes()
    {
        Result result = Single("removed-null-check", "::Greet(");
        Assert.Equal("EQ002", result.RuleId);
        Assert.Contains("ArgumentNullException", result.Message.Text, StringComparison.Ordinal);
        Assert.Contains("NullReferenceException", result.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P1-022 criterion 2 (ADR 0048): the pair diverges only on a null argument, so its EQ002 says it is Equivalent
    /// when <c>name</c> is not null, in the property and at the end of the message.
    /// </summary>
    [Fact]
    public void RemovedNullCheck_AgreesWhenTheNameIsNotNull()
    {
        Result result = Single("removed-null-check", "::Greet(");

        Dictionary<string, string> agreesWhen = result.GetProperty<Dictionary<string, string>>("agreesWhen");
        Assert.Equal("EQ002", result.RuleId);
        Assert.Equal("name != null", agreesWhen["text"]);
        Assert.Equal("(not (select in.null.System.String in.name))", agreesWhen["smt"]);
        Assert.Equal("harvested-predicates", agreesWhen["proposedBy"]);
        Assert.Equal("bounded", agreesWhen["proofMethod"]);
        Assert.EndsWith(" Equivalent when name != null.", result.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P1-022 criterion 3: a Divergent with no admitted candidate carries no <c>agreesWhen</c>, and its message
    /// ends with its counterexample as before. <c>callee-changed</c>'s <c>Tax</c> computes no predicate of its argument,
    /// so its pair is searched and nothing is admitted.
    /// </summary>
    [Fact]
    public void CalleeChanged_HasNoAgreesWhenAndItsMessageIsUnchanged()
    {
        Result result = Single("callee-changed", "::Tax(int) diverges");

        Assert.Equal("EQ002", result.RuleId);
        Assert.False(result.TryGetProperty("agreesWhen", out Dictionary<string, string>? _));
        Assert.EndsWith(": " + result.GetProperty<string>("model"), result.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void LoopBoundChange_IsDivergentOnRung1()
    {
        Result result = Single("loop-bound-change", "::SumUpTo(");
        Assert.Equal("EQ002", result.RuleId);
        List<Dictionary<string, string>> ladder = result.GetProperty<List<Dictionary<string, string>>>("ladderTrace");
        Assert.Contains(ladder, static step => string.Equals(step["rung"], "bounded", StringComparison.Ordinal) && string.Equals(step["outcome"], "refuted", StringComparison.Ordinal));
    }

    /// <summary>Ticket P1-001 criterion 1: rung 4 proves both unaligned-loop samples, with Spacer's invariant and the arithmetic it holds in.</summary>
    [Theory]
    [InlineData("loop-to-linq", "::CountPositive(", "bitvector")]
    [InlineData("loop-fusion", "::CountOutside(", "int")]
    public void UnalignedLoopSamplesAreEquivalentByChcWithTheirInvariant(string sample, string member, string chcMode)
    {
        Result result = Single(sample, member);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("chc", result.GetProperty<string>("proofMethod"));
        Assert.Equal(chcMode, result.GetProperty<string>("chcMode"));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty<string>("invariant")));
    }

    [Fact]
    public void WebApiBasic_MatchesByEndpointAndFindCarriesTheEquivalencesApplied()
    {
        SarifLog log = RunSample("webapi-basic").Log;
        Result get = log.Runs[0].Results.Single(static r => string.Equals(r.Message.Text.Split(' ')[0], "GET", StringComparison.Ordinal) && r.Message.Text.Contains("/api/orders/{id}", StringComparison.Ordinal) && !r.Message.Text.Contains("find", StringComparison.Ordinal));
        Result find = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("/api/orders/find/{id}", StringComparison.Ordinal));

        Assert.Equal("EQ001", get.RuleId);
        Assert.Equal("endpoint", Assert.Single(get.Locations).LogicalLocations.Single().Kind);
        Assert.Equal("EQ001", find.RuleId);
        Assert.Equal("endpoint", Assert.Single(find.Locations).LogicalLocations.Single().Kind);
        Assert.Equal(
            ["webapi.not-found", "webapi.ok-of-int", "webapi.type.action-result", "webapi.type.not-found-result", "webapi.type.ok-content-result"],
            find.GetProperty<List<string>>("equivalencesApplied"),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-184: both sides' <c>Contains</c> calls are closed (ADR 0041), so <c>HasX</c>'s difference on a null
    /// <c>s</c> rests on what the solver chose for the legacy call, which the replay taints (ADR 0056). It is
    /// Unknown(Abstraction), naming that callee, and equivalent when <c>s</c> is not null.
    /// </summary>
    [Fact]
    public void ApiDrift_PartsIsEquivalentAndHasXIsAnAbstractionOnANullString()
    {
        Result parts = Single("api-drift", "::Parts(");
        Result hasX = Single("api-drift", "::HasX(");

        Assert.Equal("EQ001", parts.RuleId);
        Assert.Equal("EQ003", hasX.RuleId);
        Assert.Equal("abstraction", hasX.GetProperty<string>("unknownReason"));
        Assert.Equal("System.String::Contains(char)", Assert.Single(hasX.GetProperty<List<Dictionary<string, string>>>("abstractions"))["identity"]);
        Assert.Contains("Equivalent when s != null.", hasX.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-184 criterion 2: the unedited <c>Count</c> reads a field and calls a member after a rewritten one-char
    /// <c>Split</c>, which is closed on both sides as a direct call of its modern member is, so neither side's call writes
    /// the heap and it is Equivalent.
    /// </summary>
    [Fact]
    public void ApiDrift_AFieldReadAndACallAfterARewrittenSplitAreEquivalent()
    {
        Result count = Single("api-drift", "::Count(");

        Assert.Equal("EQ001", count.RuleId);
        Assert.Equal(["bcl.string-split-one-char"], count.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-184 criterion 2: <c>Total</c>'s modern body adds one more after the same call, so it is not Equivalent.
    /// The difference shows only past a closed call that can throw, so it is Unknown(Abstraction) naming <c>Split</c>
    /// (ADR 0056).
    /// </summary>
    [Fact]
    public void ApiDrift_BodiesThatDifferAfterARewrittenSplitAreNotEquivalent()
    {
        Result total = Single("api-drift", "::Total(");

        Assert.Equal("EQ003", total.RuleId);
        Assert.Equal("abstraction", total.GetProperty<string>("unknownReason"));
        Assert.All(
            total.GetProperty<List<Dictionary<string, string>>>("abstractions"),
            static a => Assert.Equal("System.String::Split(char,System.StringSplitOptions)", a["identity"]));
        Assert.Equal(["bcl.string-split-one-char"], total.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-070 criterion 1: identical source that .NET 10 binds to an added overload or a moved member is Equivalent,
    /// with the catalogue entry that rewrote the legacy call listed and no rebound call left.
    /// </summary>
    [Theory]
    [InlineData("::Strip(", "bcl.string-trim-end-one-char")]
    [InlineData("::Indent(", "bcl.string-trim-start-no-chars")]
    [InlineData("::Full(", "bcl.directory-info-full-name")]
    public void BclOverloadRebinding_IdenticalSourceIsEquivalentWithItsEntryApplied(string member, string entry)
    {
        Result result = Single("bcl-overload-rebinding", member);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal([entry], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
        Assert.False(result.TryGetProperty("reboundCalls", out string? _));
    }

    /// <summary>
    /// Ticket P2-070 criterion 2: trimming a different character on the modern side is not Equivalent. Since ticket
    /// P2-184 the rewritten call is closed as the modern one is, so the two differ only in what the solver chose for a
    /// closed member, which the replay taints (ADR 0056): Unknown(Abstraction), naming <c>TrimEnd(char)</c>.
    /// </summary>
    [Fact]
    public void BclOverloadRebinding_TrimmingAnotherCharacterIsNotEquivalent()
    {
        Result result = Single("bcl-overload-rebinding", "::StripOther(");

        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("abstraction", result.GetProperty<string>("unknownReason"));
        Assert.All(result.GetProperty<List<Dictionary<string, string>>>("abstractions"), static a => Assert.Equal("System.String::TrimEnd(char)", a["identity"]));
        Assert.Equal(["bcl.string-trim-end-one-char"], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-137 criterion 3: <c>s.TrimEnd()</c>, which .NET 10 binds to the parameterless overload, is Equivalent with
    /// its entry applied and no rebound call left, although the legacy member has an earlier entry for one element.
    /// </summary>
    [Fact]
    public void BclReboundOverloads_TrimEndOfNoCharsIsEquivalentWithItsEntryApplied()
    {
        Result result = Single("bcl-rebound-overloads", "::TrimTail(");

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal(["bcl.string-trim-end-no-chars"], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
        Assert.False(result.TryGetProperty("reboundCalls", out string? _));
    }

    /// <summary>
    /// Ticket P2-137 criterion 3: the entry does not hide an edit. Trimming a character on the modern side is not
    /// Equivalent. Since ticket P2-184 both calls are closed, so the two differ only in what the solver chose for closed
    /// members, which the replay taints (ADR 0056): Unknown(Abstraction), naming both.
    /// </summary>
    [Fact]
    public void BclReboundOverloads_TrimmingACharacterOnTheModernSideIsNotEquivalent()
    {
        Result result = Single("bcl-rebound-overloads", "::TrimTailOther(");

        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("abstraction", result.GetProperty<string>("unknownReason"));
        Assert.Equal(
            ["System.String::TrimEnd()", "System.String::TrimEnd(char)"],
            result.GetProperty<List<Dictionary<string, string>>>("abstractions").Select(static a => a["identity"]),
            StringComparer.Ordinal);
        Assert.Equal(["bcl.string-trim-end-no-chars"], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-137 criterion 3: <c>TimeSpan.FromHours(double)</c> and <c>TimeSpan.FromHours(int)</c> throw different
    /// exception types for a value out of range, so the catalogue's entry for them (ticket P2-142) does not apply to an
    /// <c>int</c> that is not constant: the call stays a rebound call (ADR 0042) and the pair Unknown, with the two callees named.
    /// </summary>
    [Fact]
    public void BclReboundOverloads_OverloadsThatDifferOutOfRangeStayARebindingAndUnknown()
    {
        Result result = Single("bcl-rebound-overloads", "::Hours(");

        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("opaque", result.GetProperty<string>("unknownReason"));
        Assert.False(result.TryGetProperty("equivalencesApplied", out string? _));
        Dictionary<string, string> rebound = Assert.Single(result.GetProperty<List<Dictionary<string, string>>>("reboundCalls"));
        Assert.Equal(("System.TimeSpan::FromHours(double)", "System.TimeSpan::FromHours(int)"), (rebound["legacy"], rebound["modern"]));
    }

    /// <summary>
    /// Ticket P2-142 criterion 3: a call of each of the five <c>TimeSpan</c> factories whose argument is known to be in the
    /// range on which the <c>double</c> and the integer overload agree, a constant in it or an <c>int</c> of seconds, is not a
    /// rebound call, and the pair is Equivalent with the entry applied.
    /// </summary>
    [Theory]
    [InlineData("::Week(", "bcl.timespan-from-days-integer")]
    [InlineData("::CacheLifetime(", "bcl.timespan-from-hours-integer")]
    [InlineData("::Retry(", "bcl.timespan-from-minutes-integer")]
    [InlineData("::Poll(", "bcl.timespan-from-seconds-integer")]
    [InlineData("::Never(", "bcl.timespan-from-milliseconds-integer")]
    [InlineData("::Wait(", "bcl.timespan-from-seconds-integer")]
    public void TimeSpanIntegerOverloads_AnArgumentKnownToBeInRangeIsEquivalentWithItsEntryApplied(string member, string entry)
    {
        Result result = Single("timespan-integer-overloads", member);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal([entry], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
        Assert.False(result.TryGetProperty("reboundCalls", out string? _));
    }

    /// <summary>
    /// Ticket P2-142 criterion 4: a constant out of range, an <c>int</c> of hours and a <c>long</c> of milliseconds are not
    /// known to be in range, so no entry applies: the call stays a rebound call (ADR 0042) and the pair Unknown, with the
    /// two callees named.
    /// </summary>
    [Theory]
    [InlineData("::TooLong(", "System.TimeSpan::FromHours(double)", "System.TimeSpan::FromHours(int)")]
    [InlineData("::Hours(", "System.TimeSpan::FromHours(double)", "System.TimeSpan::FromHours(int)")]
    [InlineData("::Delay(", "System.TimeSpan::FromMilliseconds(double)", "System.TimeSpan::FromMilliseconds(long)")]
    public void TimeSpanIntegerOverloads_AnArgumentThatMayBeOutOfRangeStaysARebindingAndUnknown(string member, string legacy, string modern)
    {
        Result result = Single("timespan-integer-overloads", member);

        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("opaque", result.GetProperty<string>("unknownReason"));
        Assert.False(result.TryGetProperty("equivalencesApplied", out string? _));
        Dictionary<string, string> rebound = Assert.Single(result.GetProperty<List<Dictionary<string, string>>>("reboundCalls"));
        Assert.Equal((legacy, modern), (rebound["legacy"], rebound["modern"]));
    }

    /// <summary>Ticket P2-142 criterion 4: the entry does not hide an edit. Another constant on the modern side stays Divergent.</summary>
    [Fact]
    public void TimeSpanIntegerOverloads_AnotherConstantOnTheModernSideIsDivergent()
    {
        Result result = Single("timespan-integer-overloads", "::Backoff(");

        Assert.Equal("EQ002", result.RuleId);
        Assert.Equal(["bcl.timespan-from-minutes-integer"], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-144 criterion 3: the same <c>switch</c> expression with no discard arm on .NET Framework 4.8 and .NET 10
    /// throws another exception type where no arm matches, so the pair is Divergent by the runtime rule (EQ006) and not
    /// Equivalent by congruence; the compiler's two constructors are not a rebound call.
    /// </summary>
    [Fact]
    public void SwitchExpressionNoMatch_AnExpressionThatMayMatchNoArmIsDivergentByItsRuntimeRow()
    {
        Result result = Single("switch-expression-no-match", "::Weight(");

        Assert.Equal("EQ006", result.RuleId);
        Assert.Contains("between net48 and net10.0", result.Message.Text, StringComparison.Ordinal);
        Assert.Contains("SwitchExpressionException", result.Message.Text, StringComparison.Ordinal);
        Assert.False(result.TryGetProperty("reboundCalls", out string? _));
    }

    /// <summary>
    /// Ticket P2-144 criterion 4: a <c>switch</c> expression whose arms always match (a discard arm, or <c>true</c> and
    /// <c>false</c>) has no such throw and stays Equivalent by congruence.
    /// </summary>
    [Theory]
    [InlineData("::WeightOrZero(")]
    [InlineData("::Sign(")]
    public void SwitchExpressionNoMatch_AnExpressionWhoseArmsAlwaysMatchIsEquivalentByCongruence(string method)
    {
        Result result = Single("switch-expression-no-match", method);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    /// <summary>
    /// Ticket P2-073 criterion 2: a call whose compile-time constant arguments cannot reach the change its
    /// <c>runtime-changes.json</c> row documents (a regex pattern that does not ignore case, a plain path literal) is not
    /// flagged, so the byte-identical method is Equivalent by congruence from .NET Framework 4.8 to .NET 10.
    /// </summary>
    [Theory]
    [InlineData("Calls::Words(")]
    [InlineData("Calls::Cache(")]
    public void RuntimeRowConstantArgs_AConstantThatCannotReachTheChangeIsEquivalent(string method)
    {
        Result result = Single("runtime-row-constant-args", method);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    /// <summary>
    /// Ticket P2-073 criteria 2 and 3: the same calls with a constant that can reach the change (the pattern
    /// <c>(?i)[a-z]</c>, the path literal <c>a|b</c>), or with an argument that is no constant, stay EQ006.
    /// </summary>
    [Theory]
    [InlineData("Calls::Letters(", "regex")]
    [InlineData("Calls::Piped(", "invalid path characters")]
    [InlineData("Calls::SplitBy(", "regex")]
    [InlineData("Calls::Under(", "invalid path characters")]
    public void RuntimeRowConstantArgs_AConstantThatCanReachTheChangeOrAVariableStaysDivergent(string method, string reason)
    {
        Result result = Single("runtime-row-constant-args", method);

        Assert.Equal("EQ006", result.RuleId);
        Assert.Contains("between net48 and net10.0", result.Message.Text, StringComparison.Ordinal);
        Assert.Contains(reason, result.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-074 criterion 2: a path-validation row does not fire on the directory of <c>Assembly.Location</c>, a
    /// valid-path source, nor on a <c>StreamReader</c> opened only where <c>File.Exists</c> returned true for the path.
    /// Neither method has an EQ006; both are congruent.
    /// </summary>
    /// <remarks>
    /// Ticket P2-188: nor on the result of the sample's own <c>Full</c>, whose every return is the start of a
    /// <c>Path.GetFullPath</c> result, which .NET Framework has validated.
    /// </remarks>
    [Theory]
    [InlineData("Paths::BaseDir(")]
    [InlineData("Paths::Load(")]
    [InlineData("Paths::Create(")]
    public void RuntimeRowValidPathSource_AValidPathDoesNotFireThePathRows(string method)
    {
        Result result = Single("runtime-row-valid-path-source", method);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    /// <summary>Ticket P2-074 criterion 2: a parameter passed straight to <c>Path.Combine</c> stays EQ006, the only one in the sample.</summary>
    [Fact]
    public void RuntimeRowValidPathSource_AParameterPassedStraightThroughStaysDivergent()
    {
        Result result = Assert.Single(RunSample("runtime-row-valid-path-source").Log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ006", StringComparison.Ordinal));

        Assert.Contains("Paths::Under(", result.Message.Text, StringComparison.Ordinal);
        Assert.Contains("invalid path characters", result.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-188: the parameter <c>Full</c> passes to <c>Path.GetFullPath</c> still reaches that row, so <c>Full</c> is
    /// not Equivalent, and <c>Create</c>'s verdict names it as an assumption no proof covers.
    /// </summary>
    [Fact]
    public void RuntimeRowValidPathSource_TheValidatorsOwnCallIsNotEquivalent()
    {
        Result full = Single("runtime-row-valid-path-source", "Paths::Full(string) is ");
        Result create = Single("runtime-row-valid-path-source", "Paths::Create(");

        Assert.NotEqual("EQ001", full.RuleId, StringComparer.Ordinal);
        Assert.Equal(["Equiv.Samples.RuntimeRowValidPathSource.Paths::Full(string)"], create.GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-217 criterion 1: a path row does not fire on a field read again where a <c>File.Exists</c> guard on the same
    /// field passed, with nothing between that runs code or writes it: a <c>readonly</c> field (Duplicati's
    /// <c>BorderTests.TearDown</c>) or a mutable one written only after the call (Duplicati's <c>TempFile.Dispose</c>).
    /// </summary>
    [Theory]
    [InlineData("Scratch::TearDown(")]
    [InlineData("Scratch::Dispose(")]
    public void RuntimeRowGuardedMember_AGuardedFieldDoesNotFireThePathRow(string method)
    {
        Result result = Single("runtime-row-guarded-member", method);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    /// <summary>Ticket P2-217: a call between the guard and the use that writes the field leaves the row firing, the only EQ006 in the sample.</summary>
    [Fact]
    public void RuntimeRowGuardedMember_ACallBetweenThatWritesTheFieldStaysDivergent()
    {
        Result result = Assert.Single(RunSample("runtime-row-guarded-member").Log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ006", StringComparison.Ordinal));

        Assert.Contains("Scratch::Move(string)", result.Message.Text, StringComparison.Ordinal);
        Assert.Contains("invalid path characters", result.Message.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-181 criterion 3: a body nobody edited whose runtime-sensitive calls cannot reach their rows' change by
    /// their arguments (a path literal, a search for a character, split options .NET Framework accepts) is Equivalent by
    /// congruence from .NET Framework 4.8 to .NET 10, and its runtime-rule identity is gone with it.
    /// </summary>
    [Theory]
    [InlineData("Calls::Cache(")]
    [InlineData("Calls::Slash(")]
    [InlineData("Calls::Fields(")]
    public void RuntimeRowUnreachableChange_ACallThatCannotReachItsRowsChangeIsCongruent(string method)
    {
        Result result = Single("runtime-row-unreachable-change", method);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    /// <summary>
    /// Ticket P2-181's soundness test: the same three calls with a parameter in place of the constant can reach their rows'
    /// change, so none of the three bodies is Equivalent. Each is EQ006, and its <c>notCongruent</c> names the callee the
    /// runtime rule applies to.
    /// </summary>
    [Theory]
    [InlineData("Calls::Under(", "System.IO.Path::Combine(string,string)")]
    [InlineData("Calls::Find(", "System.String::IndexOf(string)")]
    [InlineData("Calls::FieldsBy(", "System.String::Split(char[],System.StringSplitOptions)")]
    public void RuntimeRowUnreachableChange_ACallThatCanReachItsRowsChangeIsNotEquivalent(string method, string rule)
    {
        Result result = Single("runtime-row-unreachable-change", method);

        Assert.Equal("EQ006", result.RuleId);
        Assert.Contains("between net48 and net10.0", result.Message.Text, StringComparison.Ordinal);
        FingerprintDifference notCongruent = result.GetProperty<FingerprintDifference>("notCongruent");
        Assert.Equal(new FingerprintDifference(FingerprintDifference.RuntimeRule, rule), notCongruent);
    }

    /// <summary>
    /// Ticket P2-216 criterion 2: a body nobody edited whose call cannot reach its row's change (<c>StartInfo</c> of a new
    /// process, constant well-formed UTF-8, an image index of <c>Count - 1</c>, <c>Version.Major &gt;= 6</c>) is Equivalent by
    /// congruence from .NET Framework 4.8 to .NET 10. Each was EQ006 before the ticket.
    /// </summary>
    [Theory]
    [InlineData("Calls::NewProcessFile(")]
    [InlineData("Calls::Katakana(")]
    [InlineData("Calls::ShowLastImage(")]
    [InlineData("Calls::IsVistaOrNewer(")]
    public void RuntimeRowCallCannotReach_ACallThatCannotReachItsRowsChangeIsCongruent(string method)
    {
        Result result = Single("runtime-row-call-cannot-reach", method);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    /// <summary>
    /// Ticket P2-216's soundness test: the same four calls where the change is reachable (a process passed in, bytes that end
    /// mid-sequence, an index passed in, a comparison with 10) stay EQ006, and <c>notCongruent</c> names the row's callee.
    /// </summary>
    [Theory]
    [InlineData("Calls::ProcessFile(", "System.Diagnostics.Process::get_StartInfo()")]
    [InlineData("Calls::Truncated(", "System.Text.Encoding::GetString(byte[])")]
    [InlineData("Calls::ShowImage(", "System.Windows.Forms.TreeNode::set_ImageIndex(int)")]
    [InlineData("Calls::IsWindows10OrNewer(", "System.Environment::get_OSVersion()")]
    public void RuntimeRowCallCannotReach_ACallThatCanReachItsRowsChangeIsNotEquivalent(string method, string rule)
    {
        Result result = Single("runtime-row-call-cannot-reach", method);

        Assert.Equal("EQ006", result.RuleId);
        Assert.Contains("between net48 and net10.0", result.Message.Text, StringComparison.Ordinal);
        FingerprintDifference notCongruent = result.GetProperty<FingerprintDifference>("notCongruent");
        Assert.Equal(new FingerprintDifference(FingerprintDifference.RuntimeRule, rule), notCongruent);
    }

    /// <summary>
    /// Ticket P2-055 criterion 2 (ADR 0040 decision 2): both sides run on .NET 10, so no runtime rule applies. The
    /// byte-identical method that calls <c>double.ToString()</c> and <c>string.StartsWith(string)</c> and casts a
    /// <c>double</c> to <c>int</c> is Equivalent by congruence, and the <c>if</c> chain made a <c>switch</c> expression is
    /// Equivalent by the solver. No call counts as a runtime-change call. The partial method, the same on both sides, is
    /// Equivalent by congruence too, by the fingerprint of its implementing part (ADR 0024 as clarified by ticket P2-107).
    /// </summary>
    /// <summary>
    /// Ticket P2-145 criterion 4 (ADR 0054): an <c>extern</c> method is a procedure. <c>F</c>, whose library differs between
    /// the sides, has a result, and it is Unknown, never Equivalent and never Divergent; before, the run reported its
    /// caller alone. The caller, the same on both sides, is congruent and names <c>F</c> as an assumption not proved (ADR
    /// 0019). An unedited import is congruent.
    /// </summary>
    [Fact]
    public void ExternImport_AChangedImportIsUnknownAndAnUneditedOneIsCongruent()
    {
        const string Changed = "Equiv.Samples.ExternImport.Native::F()";
        Run run = RunSample("extern-import").Log.Runs[0];
        Result Of(string member) => run.Results.Single(r => string.Equals(r.PartialFingerprints["procedureIdentity/v1"], $"Equiv.Samples.ExternImport.Native::{member}()", StringComparison.Ordinal));
        Result changed = Of("F");
        Result unedited = Of("Ticks");
        Result caller = Of("M");

        Assert.Equal(3, run.Results.Count);
        Assert.Equal("EQ003", changed.RuleId);
        Assert.Equal("opaque", changed.GetProperty<string>("unknownReason"));
        Assert.Contains("no-body", changed.Message.Text, StringComparison.Ordinal);
        Assert.Equal("EQ001", unedited.RuleId);
        Assert.Equal("congruence", unedited.GetProperty<string>("proofMethod"));
        Assert.Equal("EQ001", caller.RuleId);
        Assert.Equal("congruence", caller.GetProperty<string>("proofMethod"));
        Assert.Equal([Changed], caller.GetProperty<List<string>>("assumedCallees"), StringComparer.Ordinal);
        Assert.Equal([Changed], caller.GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket P2-165 criteria 2 and 4 (ADR 0058): .NET 5 against .NET 6, where no interop change reaches an import. The
    /// two unedited imports are Equivalent by congruence, and the one whose library differs is Unknown, <c>no-body</c>.
    /// </summary>
    [Fact]
    public void ExternImportRuntime_UneditedImportsAreCongruentAcrossNet5AndNet6AndAnEditedOneIsNot()
    {
        Run run = RunSample("extern-import-runtime").Log.Runs[0];

        Assert.Equal(3, run.Results.Count);
        AssertCongruent(ExternImportRuntime(run.Results, "Native::Ticks()"));
        AssertCongruent(ExternImportRuntime(run.Results, "Native::MessageBox(System.IntPtr,string,string,uint)"));
        AssertNoBody(ExternImportRuntime(run.Results, "Changed::F()"));
    }

    /// <summary>
    /// Ticket P2-165 criterion 2 (ADR 0058 decision 3): the same files from .NET Core 3.1 cross .NET 5, whose suffix
    /// probing reaches the import that is not spelled exactly, which stays Unknown; the one spelled exactly is congruent.
    /// </summary>
    [Fact]
    public void ExternImportRuntime_FromNetCore31TheSuffixProbingReachesOnlyTheInexactImport()
    {
        IList<Result> results = RunExternImportRuntimeFrom("netcoreapp3.1");

        Assert.Equal(3, results.Count);
        AssertCongruent(ExternImportRuntime(results, "Native::Ticks()"));
        AssertNoBody(ExternImportRuntime(results, "Native::MessageBox(System.IntPtr,string,string,uint)"));
        AssertNoBody(ExternImportRuntime(results, "Changed::F()"));
    }

    /// <summary>
    /// Ticket P2-165 criterion 3 (ADR 0058 decision 2): from .NET Framework 4.8 no list of interop changes is complete, so
    /// ADR 0054 decision 4 stands for every import, the one the list reaches and the one it does not.
    /// </summary>
    [Fact]
    public void ExternImportRuntime_FromNet48EveryImportStaysUnknown()
    {
        IList<Result> results = RunExternImportRuntimeFrom("net48");

        Assert.Equal(3, results.Count);
        Assert.All(results, AssertNoBody);
    }

    private static Result ExternImportRuntime(IList<Result> results, string member) =>
        results.Single(r => string.Equals(r.PartialFingerprints["procedureIdentity/v1"], $"Equiv.Samples.ExternImportRuntime.{member}", StringComparison.Ordinal));

    private static void AssertCongruent(Result result)
    {
        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    private static void AssertNoBody(Result result)
    {
        Assert.Equal("EQ003", result.RuleId);
        Assert.Equal("opaque", result.GetProperty<string>("unknownReason"));
        Assert.Contains("no-body", result.Message.Text, StringComparison.Ordinal);
    }

    /// <summary><c>extern-import-runtime</c>'s files compiled from <paramref name="legacy"/>'s project, against its .NET 6 side.</summary>
    private static IList<Result> RunExternImportRuntimeFrom(string legacy)
    {
        string sampleDir = Path.Combine(SamplesRoot, "extern-import-runtime");
        string legacySolution = Directory.GetFiles(Path.Combine(sampleDir, legacy, "legacy"), "*.sln").Single();
        string modernSolution = Directory.GetFiles(Path.Combine(sampleDir, "modern"), "*.slnx").Single();
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-165-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => Program.Main(["compare", "--legacy", legacySolution, "--modern", modernSolution, "--out", outPath, .. Pinned]));

            Assert.Equal(ExitCodes.Success, exitCode);
            return SarifLog.Load(outPath).Runs[0].Results;
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    /// <summary>
    /// Ticket P2-118 criterion 2 (ADR 0055): .NET 8 against .NET 9 with one <c>[GeneratedRegex]</c> method nobody edited.
    /// The method is congruent by its declaration, its caller is congruent, and no result names the matcher the two
    /// generators emit differently: it is no procedure, and the run counts what each side left out.
    /// </summary>
    [Fact]
    public void GeneratedRegex_AnUneditedDeclarationIsCongruentAndTheMatcherIsNoPair()
    {
        Run run = RunSample("generated-regex-unedited").Log.Runs[0];

        Assert.Equal(
            ["Equiv.Samples.GeneratedRegexUnedited.Words::Collapse(string)", "Equiv.Samples.GeneratedRegexUnedited.Words::WhiteSpace()"],
            run.Results.Select(static r => r.PartialFingerprints["procedureIdentity/v1"]).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.All(run.Results, static r => Assert.Equal("EQ001", r.RuleId));
        Assert.All(run.Results, static r => Assert.Equal("congruence", r.GetProperty<string>("proofMethod")));
        Dictionary<string, long> generated = run.GetProperty<Dictionary<string, long>>("generatedProcedures");
        Assert.True(generated["legacy"] > 0);
        Assert.True(generated["modern"] > 0);
    }

    /// <summary>
    /// Ticket P2-118 criterion 3 (ADR 0055 decision 2): the same sample with the pattern edited on the modern side. The
    /// method is Unknown, never Equivalent and never Divergent, and its unedited caller names it as an assumption not proved.
    /// </summary>
    [Fact]
    public void GeneratedRegexEdited_AnEditedPatternIsUnknownOnTheDeclaration()
    {
        const string Edited = "Equiv.Samples.GeneratedRegexEdited.Words::WhiteSpace()";
        Run run = RunSample("generated-regex-edited").Log.Runs[0];
        Result declaration = run.Results.Single(static r => string.Equals(r.PartialFingerprints["procedureIdentity/v1"], Edited, StringComparison.Ordinal));
        Result caller = run.Results.Single(r => r != declaration);

        Assert.Equal("EQ003", declaration.RuleId);
        Assert.Equal("opaque", declaration.GetProperty<string>("unknownReason"));
        Assert.Contains("no-body", declaration.Message.Text, StringComparison.Ordinal);
        Assert.Equal("EQ001", caller.RuleId);
        Assert.Equal("congruence", caller.GetProperty<string>("proofMethod"));
        Assert.Equal([Edited], caller.GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
    }

    [Fact]
    public void SameRuntimeCleanup_NoRuntimeRuleApplies()
    {
        Run run = RunSample("same-runtime-cleanup").Log.Runs[0];
        Result describe = Single("same-runtime-cleanup", "::Describe(");
        Result rank = Single("same-runtime-cleanup", "::Rank(");
        Result beep = Single("same-runtime-cleanup", "::Beep(");

        Assert.Equal("EQ001", beep.RuleId);
        Assert.Equal("congruence", beep.GetProperty<string>("proofMethod"));
        Assert.Equal("EQ001", describe.RuleId);
        Assert.Equal("congruence", describe.GetProperty<string>("proofMethod"));
        Assert.Equal("EQ001", rank.RuleId);
        Assert.Equal("bounded", rank.GetProperty<string>("proofMethod"));
        Assert.Equal(3, run.Results.Count);
        Assert.Null(run.Invocations);
        Assert.Contains("\"callSites\":{\"legacy\":0,\"modern\":0}", Newtonsoft.Json.JsonConvert.SerializeObject(run.GetProperty<Dictionary<string, object>>("loweringCensus")["runtimeChangeCalls"]), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-119 criterion 2: four controllers take their route from one base class's <c>[Route("[controller]")]</c>,
    /// two of them share <c>[HttpGet("Configuration")]</c> and two share <c>[HttpGet]</c> plus <c>[HttpHead]</c> on
    /// <c>{itemId}/stream</c>. Each action is its own endpoint, matched by its route and Equivalent by congruence; none
    /// is an <c>unmatched-overload</c> Unknown.
    /// </summary>
    [Fact]
    public void WebApiInheritedRoute_EveryActionIsMatchedByItsOwnRouteAndCongruent()
    {
        Result[] results = [.. RunSample("webapi-inherited-route").Log.Runs[0].Results];

        Assert.Equal(
            ["GET /audio/{itemid}/stream", "GET /branding/configuration", "GET /startup/configuration", "GET /videos/{itemid}/stream"],
            results.Select(static r => Assert.Single(r.Locations).LogicalLocations.Single().FullyQualifiedName).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.All(results, static r =>
        {
            Assert.Equal("EQ001", r.RuleId);
            Assert.Equal("congruence", r.GetProperty<string>("proofMethod"));
        });
    }

    /// <summary>
    /// Ticket P2-055 criterion 3: .NET 8 against .NET 10 crosses only the rows changed in .NET 9 or .NET 10. The
    /// byte-identical method that calls <c>BinaryReader.ReadString</c> (changed in .NET 9) is not congruent and is EQ006,
    /// its message naming both runtimes; the one that calls <c>double.ToString()</c> (changed in .NET Core 3.0) is
    /// Equivalent by congruence.
    /// </summary>
    [Fact]
    public void VersionBump_OnlyRowsInsideTheIntervalApply()
    {
        Result name = Single("version-bump", "::Name(");
        Result format = Single("version-bump", "::Format(");

        Assert.Equal("EQ006", name.RuleId);
        Assert.Contains("diverges via a runtime-changed API between net8.0 and net10.0 (", name.Message.Text, StringComparison.Ordinal);
        Assert.Contains("/compatibility/core-libraries/9.0/", name.GetProperty<string>("helpUri"), StringComparison.Ordinal);
        Assert.Equal("EQ001", format.RuleId);
        Assert.Equal("congruence", format.GetProperty<string>("proofMethod"));
    }

    /// <summary>
    /// Ticket P2-113 criterion 3: .NET 8 against .NET 9 crosses neither the <c>String.Equals</c> row (changed in .NET 5)
    /// nor the <c>Encoding.Default</c> row (changed at the .NET Framework to .NET boundary), so the byte-identical
    /// methods that call them are Equivalent by congruence and nothing is EQ006.
    /// </summary>
    [Fact]
    public void RuntimeRowFrameworkOnlyChange_NoRowAppliesBetweenTwoNetVersions()
    {
        Result[] results = [.. RunSample("runtime-row-framework-only-change").Log.Runs[0].Results];

        Assert.Equal(2, results.Length);
        Assert.All(results, static r => Assert.Equal("EQ001", r.RuleId));
        Assert.All(results, static r => Assert.Equal("congruence", r.GetProperty<string>("proofMethod")));
    }

    /// <summary>
    /// Ticket P2-113 criterion 3: the same file with the legacy project on .NET Framework 4.8 crosses both change
    /// points, so both methods stay EQ006.
    /// </summary>
    [Fact]
    public void RuntimeRowFrameworkOnlyChange_FromNet48KeepsBothRows()
    {
        string sampleDir = Path.Combine(SamplesRoot, "runtime-row-framework-only-change");
        string legacy = Directory.GetFiles(Path.Combine(sampleDir, "net48", "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(sampleDir, "modern"), "*.slnx").Single();
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-113-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => Program.Main(["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, .. Pinned]));

            Assert.Equal(ExitCodes.Divergent, exitCode);
            Result[] results = [.. SarifLog.Load(outPath).Runs[0].Results];
            Assert.Equal(2, results.Length);
            Assert.All(results, static r => Assert.Equal("EQ006", r.RuleId));
            Assert.All(results, static r => Assert.Contains("between net48 and net9.0 (", r.Message.Text, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    /// <summary>
    /// Ticket P2-114 criterion 1: .NET 5 against .NET 6 crosses the <c>FileStream.Position</c> row, but the method reads
    /// <c>Position</c> after a synchronous <c>Read</c> only, so the row does not fire and the byte-identical pair is
    /// Equivalent by congruence.
    /// </summary>
    [Fact]
    public void RuntimeRowSyncPosition_ASynchronousReadDoesNotFireThePositionRow()
    {
        Result result = Assert.Single(RunSample("runtime-row-sync-position").Log.Runs[0].Results);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    /// <summary>
    /// Ticket P2-164 criterion 1: an interpolated string appended to a <c>StringBuilder</c>, which binds <c>Append(string)</c>
    /// on .NET 5 and the interpolated string handler on .NET 6, is no EQ002: each method is Equivalent, proved and not
    /// congruent, since the two bound trees differ.
    /// </summary>
    [Fact]
    public void AppendInterpolatedString_EachMethodIsEquivalentAcrossNet5AndNet6()
    {
        IList<Result> results = RunSample("append-interpolated-string").Log.Runs[0].Results;

        Assert.Equal(3, results.Count);
        Assert.All(results, static r =>
        {
            Assert.Equal("EQ001", r.RuleId);
            Assert.NotEqual("congruence", r.GetProperty<string>("proofMethod"), StringComparer.Ordinal);
        });
    }

    /// <summary>Ticket P2-114 criterion 2: the same method with <c>ReadAsync</c> in place of <c>Read</c> stays EQ006.</summary>
    [Fact]
    public void RuntimeRowSyncPosition_AnAsyncReadKeepsTheRow()
    {
        string variantDir = Path.Combine(SamplesRoot, "runtime-row-sync-position", "async-variant");
        string legacy = Directory.GetFiles(Path.Combine(variantDir, "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(variantDir, "modern"), "*.slnx").Single();
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-114-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => Program.Main(["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, .. Pinned]));

            Assert.Equal(ExitCodes.Divergent, exitCode);
            Result result = Assert.Single(SarifLog.Load(outPath).Runs[0].Results);
            Assert.Equal("EQ006", result.RuleId);
            Assert.Contains("between net5.0 and net6.0 (", result.Message.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    /// <summary>
    /// Ticket P2-163 criterion 1: a read of <c>TreeNodeCollection</c>'s indexer on a .NET 5 to .NET 6 pair cannot reach
    /// the null-argument change, which only the setter has, so the pair is congruent.
    /// </summary>
    [Fact]
    public void RuntimeRowIndexerGetter_AReadOfTheIndexerIsCongruent()
    {
        Result result = Assert.Single(RunSample("runtime-row-indexer-getter").Log.Runs[0].Results);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
    }

    /// <summary>Ticket P2-163 criterion 2: the same method assigning <c>null</c> through the indexer is EQ006 under the null-argument change.</summary>
    [Fact]
    public void RuntimeRowIndexerGetter_ANullAssignmentThroughTheIndexerIsEQ006()
    {
        string variantDir = Path.Combine(SamplesRoot, "runtime-row-indexer-getter", "null-assignment");
        string legacy = Directory.GetFiles(Path.Combine(variantDir, "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(variantDir, "modern"), "*.slnx").Single();
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-163-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => Program.Main(["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, .. Pinned]));

            Assert.Equal(ExitCodes.Divergent, exitCode);
            Result result = Assert.Single(SarifLog.Load(outPath).Runs[0].Results);
            Assert.Equal("EQ006", result.RuleId);
            Assert.Contains("between net5.0 and net6.0 (Assigning a null node throws ArgumentNullException", result.Message.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    /// <summary>
    /// Ticket P2-075 criterion 1: <c>s.IndexOf("x.exe", StringComparison.OrdinalIgnoreCase)</c> on a .NET Framework 4.8 to .NET 10
    /// pair is Equivalent, because the ICU row does not apply to an ordinal comparison; the same call with
    /// <c>CurrentCultureIgnoreCase</c> stays EQ006.
    /// </summary>
    [Fact]
    public void RuntimeRowUnaffectedOverload_AnOrdinalComparisonIsEquivalentAndACultureOneStaysEQ006()
    {
        Result ordinal = Single("runtime-row-unaffected-overload", "::Has(");
        Result culture = Single("runtime-row-unaffected-overload", "::HasCulture(");

        Assert.Equal("EQ001", ordinal.RuleId);
        Assert.Equal("congruence", ordinal.GetProperty<string>("proofMethod"));
        Assert.Equal("EQ006", culture.RuleId);
        Assert.Equal(2, RunSample("runtime-row-unaffected-overload").Log.Runs[0].Results.Count);
    }

    [Fact]
    public void AddedRemoved_HasAnEQ004AndAnEQ005()
    {
        Result[] results = [.. RunSample("added-removed").Log.Runs[0].Results];
        Assert.Contains(results, static r => string.Equals(r.RuleId, "EQ004", StringComparison.Ordinal));
        Assert.Contains(results, static r => string.Equals(r.RuleId, "EQ005", StringComparison.Ordinal));
    }

    [Fact]
    public void CalleeChanged_TaxIsDivergentAndTotalIsEquivalentWithAnUnprovenAssumption()
    {
        SarifLog log = RunSample("callee-changed").Log;
        Result tax = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Tax(int) diverges", StringComparison.Ordinal));
        Result total = log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Total(int) is equivalent", StringComparison.Ordinal));

        Assert.Equal("EQ002", tax.RuleId);
        Assert.Equal("EQ001", total.RuleId);
        Assert.Contains(total.GetProperty<List<string>>("unprovenAssumptions"), static a => a.Contains("::Tax(", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ticket P1-010 criterion 5 (ADR 0036 decision 2): the caller sees only <c>Score(a) &gt; 0</c>, so it is Equivalent
    /// under an admitted contract for the Divergent <c>Score</c>, which leaves no unproven assumption. The contracts pass
    /// is thorough mode's (ADR 0049's table), so that is the run read here. The checked-in snapshot is the default, quick
    /// run, where <c>Classify</c> is Equivalent with <c>Score</c> still an unproven assumption (ADR 0052; ticket P1-032).
    /// </summary>
    [Fact]
    public async Task CalleeChangedInvisible_EquivalentPlusContract()
    {
        SampleRun run = RunSample("callee-changed-invisible");
        IList<Result> thorough = ThoroughRun("callee-changed-invisible").Log.Runs[0].Results;
        Result score = thorough.Single(static r => r.Message.Text.Contains("::Score(int) diverges", StringComparison.Ordinal));
        Result classify = thorough.Single(static r => r.Message.Text.Contains("::Classify(int) is equivalent", StringComparison.Ordinal));
        Result assumed = Single("callee-changed-invisible", "::Classify(int) is equivalent");

        Assert.Equal(await Snapshot("callee-changed-invisible"), run.NormalizedSarif);
        Assert.Equal("EQ001", assumed.RuleId);
        Assert.EndsWith("::Score(int)", Assert.Single(assumed.GetProperty<List<string>>("unprovenAssumptions")), StringComparison.Ordinal);
        Assert.False(assumed.TryGetProperty("contractsUsed", out List<Dictionary<string, string>>? _));
        Assert.Equal("EQ002", score.RuleId);
        Assert.Equal("EQ001", classify.RuleId);
        Assert.Equal("bounded+contract", classify.GetProperty<string>("proofMethod"));
        Dictionary<string, string> contract = Assert.Single(classify.GetProperty<List<Dictionary<string, string>>>("contractsUsed"));
        Assert.EndsWith("::Score(int)", contract["callee"], StringComparison.Ordinal);
        Assert.Equal("observed-predicates", contract["proposedBy"]);
        Assert.False(classify.TryGetProperty("unprovenAssumptions", out List<string>? _));
    }

    /// <summary>
    /// Ticket P1-010 criterion 6: <c>Total</c> returns what <c>Tax</c> returned, so it observes the change, no contract is
    /// used, and both verdicts and the snapshot stay as M3-015 left them.
    /// </summary>
    [Fact]
    public async Task CalleeChanged_Unaffected()
    {
        SampleRun run = RunSample("callee-changed");
        Result total = Single("callee-changed", "::Total(int) is equivalent");

        Assert.Equal(await Snapshot("callee-changed"), run.NormalizedSarif);
        Assert.Equal("congruence", total.GetProperty<string>("proofMethod"));
        Assert.False(total.TryGetProperty("contractsUsed", out List<Dictionary<string, string>>? _));
        Assert.Contains(total.GetProperty<List<string>>("unprovenAssumptions"), static a => a.Contains("::Tax(", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ticket P1-013 criterion 5 (ADR 0037), with the ticket's recorded deviation: the modern side computes an unshared
    /// opaque value and then adds a guard that throws, so the pair is Unknown; the new throw lies past the opaque node, so
    /// <c>newFailures</c> is <c>unknown</c>, and the legacy side never throws, so <c>removedFailures</c> is <c>none-proved</c>.
    /// That is the default, quick run and the checked-in snapshot. With <c>--mode thorough</c> the IL pass reads the pair
    /// from IL, where the interpolated string is not opaque, and finds the new throw: Divergent (ADR 0049; ticket P1-032).
    /// </summary>
    [Fact]
    public async Task UnknownNewThrow_CarriesItsFailureRefinement()
    {
        SampleRun run = RunSample("unknown-new-throw");
        Result width = Single("unknown-new-throw", "::Width(int) is unknown");
        Result decided = ThoroughRun("unknown-new-throw").Log.Runs[0].Results.Single(static r => r.Message.Text.Contains("::Width(int)", StringComparison.Ordinal));

        Assert.Equal(await Snapshot("unknown-new-throw"), run.NormalizedSarif);
        Assert.Equal(("EQ002", "il-pass"), (decided.RuleId, decided.GetProperty<string>("decidedBy")));
        Assert.Equal("EQ003", width.RuleId);
        Dictionary<string, Dictionary<string, string>> refinement = width.GetProperty<Dictionary<string, Dictionary<string, string>>>("failureRefinement");
        Assert.Equal("unknown", refinement["newFailures"]["outcome"]);
        Assert.Equal("none-proved", refinement["removedFailures"]["outcome"]);
    }

    /// <summary>
    /// Ticket P2-085 criterion 2 (ADR 0029 as clarified): the modern project does not compile, and it is still loaded. The
    /// method that does not bind is Unknown(unbound) and points at its first error, the other three are verified, the one
    /// that calls it with that callee as an unproven assumption, the method of the file the tool did not carry over is
    /// Removed, and no project is skipped. The checked-in snapshot is the whole run.
    /// </summary>
    [Fact]
    public async Task PartlyCompilingModern_TheMethodThatDoesNotBindIsUnknownAndTheRestAreVerified()
    {
        SampleRun run = RunSample("partly-compiling-modern");
        Result currency = Single("partly-compiling-modern", "Pricing::Currency() is unknown");

        Assert.Equal(await Snapshot("partly-compiling-modern"), run.NormalizedSarif);
        Assert.Equal(ExitCodes.Success, run.ExitCode);
        Assert.Equal("EQ003", currency.RuleId);
        Assert.Equal("unbound", currency.GetProperty<string>("unknownReason"));
        Assert.Equal("method", currency.GetProperty<string>("scope"));
        Assert.EndsWith("Pricing::Currency() is unknown (Unbound): the modern body does not bind", currency.Message.Text, StringComparison.Ordinal);
        PhysicalLocation error = Assert.Single(currency.RelatedLocations).PhysicalLocation;
        Assert.EndsWith("modern/Pricing.cs", error.ArtifactLocation.Uri.OriginalString, StringComparison.Ordinal);
        Assert.Equal((16, 20), (error.Region.StartLine, error.Region.StartColumn));
        Assert.Equal((16, 20), (currency.Locations[0].PhysicalLocation.Region.StartLine, currency.Locations[0].PhysicalLocation.Region.StartColumn));

        Assert.Equal("congruence", Single("partly-compiling-modern", "::Net(int) is equivalent").GetProperty<string>("proofMethod"));
        Assert.Equal("bounded", Single("partly-compiling-modern", "::Discount(int) is equivalent").GetProperty<string>("proofMethod"));
        Result caller = Single("partly-compiling-modern", "::HasCurrency() is equivalent");
        Assert.Equal("EQ001", caller.RuleId);
        Assert.Equal(["Equiv.Samples.PartlyCompilingModern.Pricing::Currency()"], caller.GetProperty<List<string>>("unprovenAssumptions"), StringComparer.Ordinal);
        Assert.Equal("EQ005", Single("partly-compiling-modern", "Regional::Currency()").RuleId);

        Run sarif = run.Log.Runs[0];
        Assert.Equal(5, sarif.Results.Count);
        Assert.Null(sarif.Invocations);
        Assert.True(sarif.TryGetSerializedPropertyValue("loweringCensus", out string? census));
        Assert.Contains("\"projectsSkipped\":{\"legacy\":0,\"modern\":0}", census, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-069 criteria 2 and 3 (ADR 0042): <c>Has</c> is the same source on both sides, and the library upgrade rebinds
    /// its call from <c>FileBase::Exists</c> to <c>IFile::Exists</c>. It is Unknown, not Divergent: line-scoped, pointing at the
    /// call site on each side, and naming the two callees it treated as possibly the same. <c>Clear</c> calls <c>Delete</c>
    /// on the modern side, another member, and stays Divergent with no rebound pair.
    /// </summary>
    [Fact]
    public async Task DependencyRebinding_TheReboundCallIsUnknownAndNamedAndAnotherMemberStaysDivergent()
    {
        SampleRun run = RunSample("dependency-rebinding");
        Result has = Single("dependency-rebinding", "::Has(");
        Result clear = Single("dependency-rebinding", "::Clear(");

        Assert.Equal(await Snapshot("dependency-rebinding"), run.NormalizedSarif);
        Assert.Equal("EQ003", has.RuleId);
        Assert.Equal("opaque", has.GetProperty<string>("unknownReason"));
        Assert.Equal("line", has.GetProperty<string>("scope"));
        Dictionary<string, string> rebound = Assert.Single(has.GetProperty<List<Dictionary<string, string>>>("reboundCalls"));
        Assert.Equal("Equiv.Samples.DependencyRebinding.Files.FileBase::Exists(string)", rebound["legacy"]);
        Assert.Equal("Equiv.Samples.DependencyRebinding.Files.IFile::Exists(string)", rebound["modern"]);
        Assert.Equal(["rebound-call", "rebound-call"], has.RelatedLocations.Select(static l => l.Message.Text), StringComparer.Ordinal);
        Assert.Equal("EQ002", clear.RuleId);
        Assert.False(clear.TryGetProperty("reboundCalls", out List<Dictionary<string, string>>? _));
    }

    /// <summary>
    /// Ticket P2-068 criteria 2 and 3 (ADR 0047): the legacy <c>Name</c> calls <c>Text.Blank</c>, whose whole body forwards
    /// to <c>string.IsNullOrWhiteSpace</c>, and the modern one calls that directly. It is Equivalent, and names the
    /// forwarder it resolved. <c>Trimmed</c>'s helper passes <c>s?.Trim()</c>, so it is no forwarder: the pair stays
    /// Divergent and names none.
    /// </summary>
    [Fact]
    public async Task ForwarderToBcl_TheForwarderIsItsTargetAndAHelperThatChangesItsArgumentIsNot()
    {
        SampleRun run = RunSample("forwarder-to-bcl");
        Result name = Single("forwarder-to-bcl", "::Name(");
        Result trimmed = Single("forwarder-to-bcl", "::Trimmed(");

        Assert.Equal(await Snapshot("forwarder-to-bcl"), run.NormalizedSarif);
        Assert.Equal("EQ001", name.RuleId);
        Dictionary<string, string> resolved = Assert.Single(name.GetProperty<List<Dictionary<string, string>>>("forwardersResolved"));
        Assert.Equal("Equiv.Samples.ForwarderToBcl.Text::Blank(string)", resolved["forwarder"]);
        Assert.Equal("System.String::IsNullOrWhiteSpace(string)", resolved["target"]);
        Assert.Equal("EQ002", trimmed.RuleId);
        Assert.False(trimmed.TryGetProperty("forwardersResolved", out List<Dictionary<string, string>>? _));
        Assert.Equal(1, run.ExitCode);
    }

    /// <summary>
    /// Ticket P2-192 criterion 2 (ADR 0059): with the sample's config, which maps the remoting <c>CallContext</c> members to
    /// the solution's shim, the two callers that make the same calls are Equivalent, and each lists the remoting member as an
    /// unproven assumption with the shim member it was declared to be. The caller that passes another value stays Divergent.
    /// The checked-in snapshot is the whole run.
    /// </summary>
    [Fact]
    public async Task CalleeReplacement_AMappedCallIsAnUnprovenAssumptionAndADifferentArgumentStaysDivergent()
    {
        const string GetData = "System.Runtime.Remoting.Messaging.CallContext::LogicalGetData(string)";
        const string SetData = "System.Runtime.Remoting.Messaging.CallContext::LogicalSetData(string,object)";
        SampleRun run = RunSample("callee-replacement");
        Result current = Single("callee-replacement", "::Current(");
        Result enter = Single("callee-replacement", "::Enter(");
        Result leave = Single("callee-replacement", "::Leave(");

        Assert.Equal(await Snapshot("callee-replacement"), run.NormalizedSarif);
        Assert.Equal("EQ001", current.RuleId);
        Assert.Equal([GetData], current.GetProperty<List<string>>("unprovenAssumptions"));
        Dictionary<string, string> mapped = Assert.Single(current.GetProperty<List<Dictionary<string, string>>>("callIdentitiesMapped"));
        Assert.Equal(GetData, mapped["legacy"]);
        Assert.Equal("Equiv.Samples.CalleeReplacement.Slots::GetData(string)", mapped["modern"]);
        Assert.Contains($"Assumes callees equivalent; not proved for: {GetData}.", current.Message.Text, StringComparison.Ordinal);
        Assert.Equal("EQ001", enter.RuleId);
        Assert.Equal([SetData], enter.GetProperty<List<string>>("unprovenAssumptions"));
        Assert.Equal("EQ002", leave.RuleId);
        Assert.Equal([SetData], leave.GetProperty<List<string>>("unprovenAssumptions"));
        Assert.Equal(ExitCodes.Divergent, run.ExitCode);
    }

    /// <summary>
    /// Ticket P2-192 (ADR 0059): without the declaration the remoting member and the shim are two unrelated functions, so
    /// every caller is Divergent and none names a mapped call.
    /// </summary>
    [Fact]
    public void CalleeReplacement_WithoutTheDeclarationEveryCallerIsDivergent()
    {
        (string legacy, string modern) = Solutions("callee-replacement");
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-192-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => Program.Main(["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, .. Pinned]));

            Result[] callers = [.. SarifLog.Load(outPath).Runs[0].Results.Where(static r => r.Message.Text.Contains("Culture::", StringComparison.Ordinal))];
            Assert.Equal(ExitCodes.Divergent, exitCode);
            Assert.Equal(3, callers.Length);
            Assert.All(callers, static r => Assert.Equal("EQ002", r.RuleId));
            Assert.All(callers, static r => Assert.False(r.TryGetProperty("callIdentitiesMapped", out List<Dictionary<string, string>>? _)));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    /// <summary>
    /// Ticket P2-071 criteria 2 and 3 (ADR 0043): a discarded extra read of <c>String.Length</c> and a new
    /// <c>Collection&lt;int&gt;</c> stored where the legacy side stored a new <c>List&lt;int&gt;</c> are no calls, so both
    /// pairs are Equivalent by the solver. A modern side that calls <c>list.Clear()</c>, a member with an effect, stays
    /// Divergent on that call. The checked-in snapshot is the whole run.
    /// </summary>
    [Fact]
    public async Task EffectFreeBclCall_AnEffectFreeMemberIsNoCallAndAMemberWithAnEffectStaysDivergent()
    {
        SampleRun run = RunSample("effect-free-bcl-call");
        Result first = Single("effect-free-bcl-call", "::First(");
        Result basket = Single("effect-free-bcl-call", "Basket::.ctor(");
        Result reset = Single("effect-free-bcl-call", "::Reset(");

        Assert.Equal(await Snapshot("effect-free-bcl-call"), run.NormalizedSarif);
        Assert.Equal("EQ001", first.RuleId);
        Assert.Equal("bounded", first.GetProperty<string>("proofMethod"));
        Assert.Equal("EQ001", basket.RuleId);
        Assert.Equal("bounded", basket.GetProperty<string>("proofMethod"));
        Assert.Equal("EQ002", reset.RuleId);
        Assert.Equal(4, run.Log.Runs[0].Results.Count);
    }

    /// <summary>
    /// Ticket P1-030 criteria 2 and 3 (ADR 0053): <c>a * 2.0</c> against <c>a + a</c> is Equivalent once the two
    /// functions have their IEEE meaning, <c>a + b</c> against <c>a - b</c> and the two groupings of a three-way sum are
    /// Divergent with numbers for a model, and each such result says it was refined and with what. A pair whose candidate
    /// also depends on a user-defined conversion is not refined, and names the conversion. The checked-in snapshot is the
    /// whole run.
    /// </summary>
    [Fact]
    public async Task FloatArithmetic_AnInterpretableCandidateIsRefinedAndAnyOtherIsLeftUnknown()
    {
        SampleRun run = RunSample("float-arithmetic");
        Result twice = Single("float-arithmetic", "::Twice(");
        Result ordered = Single("float-arithmetic", "::IsOrdered(");
        Result combine = Single("float-arithmetic", "::Combine(");
        Result sum = Single("float-arithmetic", "::Sum(");
        Result mean = Single("float-arithmetic", "::Mean(");

        Assert.Equal(await Snapshot("float-arithmetic"), run.NormalizedSarif);
        Assert.Equal(("EQ001", "bounded+refined"), (twice.RuleId, twice.GetProperty<string>("proofMethod")));
        Assert.Equal(["f64.add", "f64.mul"], twice.GetProperty<List<string>>("refined"));
        Assert.Equal(("EQ001", "bounded+refined"), (ordered.RuleId, ordered.GetProperty<string>("proofMethod")));
        Assert.Equal(["f32.ge", "f32.le"], ordered.GetProperty<List<string>>("refined"));
        Assert.Equal(("EQ002", "bounded+refined"), (combine.RuleId, combine.GetProperty<string>("proofMethod")));
        Assert.Equal(["f64.add", "f64.sub"], combine.GetProperty<List<string>>("refined"));
        Assert.StartsWith("inputs(f64 ", combine.GetProperty<string>("model"), StringComparison.Ordinal);
        Assert.Equal(("EQ002", "bounded+refined"), (sum.RuleId, sum.GetProperty<string>("proofMethod")));
        Assert.Equal(["f64.add"], sum.GetProperty<List<string>>("refined"));
        Assert.Equal("EQ003", mean.RuleId);
        Assert.Equal("abstraction", mean.GetProperty<string>("unknownReason"));
        Assert.False(mean.TryGetProperty("refined", out List<string>? _));
        Assert.False(mean.TryGetProperty("proofMethod", out string? _));
        Assert.Contains("op:Equiv.Samples.FloatArithmetic.Celsius::op_Implicit(double)", mean.Message.Text, StringComparison.Ordinal);
        Assert.Equal(7, run.Log.Runs[0].Results.Count);
    }

    /// <summary>
    /// Ticket P2-116 criterion 1: a method whose string constant holds two lone surrogates, the same file on both sides,
    /// lowers with no notification and is Equivalent by congruence.
    /// </summary>
    [Fact]
    public void LoneSurrogateConstant_LowersWithNoNotificationAndIsCongruent()
    {
        SampleRun run = RunSample("lone-surrogate-constant");
        Result find = Assert.Single(run.Log.Runs[0].Results);

        Assert.Equal("EQ001", find.RuleId);
        Assert.Equal("congruence", find.GetProperty<string>("proofMethod"));
        Assert.Empty(run.Log.Runs[0].Invocations?.SelectMany(static i => i.ToolExecutionNotifications ?? []) ?? []);
        Assert.Equal(ExitCodes.Success, run.ExitCode);
    }

    /// <summary>
    /// Ticket P2-064 criterion 5: the three methods the same API swap was made in share one review group, which ranks above
    /// the group of the method that changed on its own. The checked-in snapshot is the whole run.
    /// </summary>
    [Fact]
    public async Task RepeatedEdit_TheThreeSwapsAreOneGroupRankedAboveTheOther()
    {
        const string Swap = "calls:System.Convert::ToInt32(string)|System.Int32::Parse(string)";
        SampleRun run = RunSample("repeated-edit");
        Result[] results = [.. run.Log.Runs[0].Results];
        Result discount = Single("repeated-edit", "::Discount(int) diverges");

        Assert.Equal(await Snapshot("repeated-edit"), run.NormalizedSarif);
        Assert.Equal(4, results.Length);
        Assert.All(results, static r => Assert.Equal("EQ002", r.RuleId));
        Assert.Equal("proofMethod:none", discount.GetProperty<string>("reviewGroup"));
        Assert.Equal(60.002, discount.Rank);
        Assert.All(results.Except([discount]), static r =>
        {
            Assert.Equal(Swap, r.GetProperty<string>("reviewGroup"));
            Assert.Equal(60.006, r.Rank);
        });
        List<Dictionary<string, object>> list = run.Log.Runs[0].GetProperty<List<Dictionary<string, object>>>("reviewList");
        Assert.Equal([Swap, "proofMethod:none"], list.Select(static e => (string)e["group"]), StringComparer.Ordinal);
        Assert.Equal([3L, 1L], list.Select(static e => (long)e["count"]));
    }

    /// <summary>
    /// Ticket P1-013 criterion 4 (ADR 0037): on every sample, a run whose backend drops the failure refinement has the same
    /// exit code, and every result the same rule id and result fingerprint, as the real run.
    /// </summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void Refinement_NeverChangesVerdictOrFingerprint(string sample)
    {
        SampleRun refined = RunSample(sample);
        (string legacy, string modern) = Solutions(sample);
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P1-013-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => CompareCommand.Run(
                new Equiv.Cli.CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath(sample), FailOn: null, DryRun: false) { ResourceLimit = 5_000_000 },
                [new CSharpFrontend()],
                new WithoutRefinement(new Z3Backend()),
                new FileReportSink(outPath),
                NullRunLog.Instance));
            SarifLog plain = SarifLog.Load(outPath);

            Assert.Equal(refined.ExitCode, exitCode);
            Assert.Equal(Verdicts(refined.Log), Verdicts(plain));
            Assert.DoesNotContain(plain.Runs[0].Results, static r => r.TryGetProperty("failureRefinement", out Dictionary<string, object>? _));
        }
        finally
        {
            File.Delete(outPath);
        }

        static string[] Verdicts(SarifLog log) =>
            [.. log.Runs[0].Results.Select(static r => $"{r.PartialFingerprints["procedureIdentity/v1"]} {r.RuleId} {r.PartialFingerprints["resultFingerprint/v1"]}").Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Ticket P1-020 criterion 7: <c>added-null-guard</c>'s <c>Greet</c> gained <c>?? string.Empty</c>, a removed failure and
    /// nothing else, and says so in its message; its <c>Shout</c> lost the branch that returned for <c>null</c>, a new failure
    /// and nothing else. The census counts both pairs. <c>removed-null-check</c> throws on the same input on both sides and
    /// differs only in the exception type, which none of the three queries asks about: undetermined.
    /// </summary>
    [Fact]
    public async Task AddedNullGuard_SaysWhichSideGainedTheFailure()
    {
        SampleRun run = RunSample("added-null-guard");
        Result greet = Single("added-null-guard", "::Greet(");
        Result shout = Single("added-null-guard", "::Shout(");
        Result typeOnly = Single("removed-null-check", "::Greet(");

        Assert.Equal(await Snapshot("added-null-guard"), run.NormalizedSarif);
        Assert.Equal(["EQ002", "EQ002"], [greet.RuleId, shout.RuleId]);
        Assert.Equal("removed-failures-only", greet.GetProperty<string>("divergenceKind"));
        Assert.Equal(["none-proved", "found", "none-proved"], Outcomes(greet));
        Assert.EndsWith(" The modern side differs only where the legacy side throws.", greet.Message.Text, StringComparison.Ordinal);
        Assert.Equal("new-failures-only", shout.GetProperty<string>("divergenceKind"));
        Assert.Equal(["found", "none-proved", "none-proved"], Outcomes(shout));
        Assert.DoesNotContain("differs only where", shout.Message.Text, StringComparison.Ordinal);
        Assert.Equal(2L, (long)run.Log.Runs[0].GetProperty<Newtonsoft.Json.Linq.JObject>("loweringCensus")["divergentRefinement"]!["pairs"]!);
        Assert.Equal("undetermined", typeOnly.GetProperty<string>("divergenceKind"));
        Assert.Equal(["none-proved", "none-proved", "none-proved"], Outcomes(typeOnly));

        static string[] Outcomes(Result result)
        {
            Dictionary<string, Dictionary<string, string>> refinement = result.GetProperty<Dictionary<string, Dictionary<string, string>>>("failureRefinement");
            return [refinement["newFailures"]["outcome"], refinement["removedFailures"]["outcome"], refinement["resultChanges"]["outcome"]];
        }
    }

    /// <summary>
    /// Ticket P1-020 criterion 5, pinned on <c>added-null-guard</c>: a run whose backend drops the three answers exits with
    /// the same code, and each result has the same rule id, level, kind, fingerprints, review group and rank. Only the two
    /// properties and the one sentence differ.
    /// </summary>
    [Fact]
    public void DivergenceKind_ChangesNoFingerprintOrExitCode()
    {
        SampleRun refined = RunSample("added-null-guard");
        (string legacy, string modern) = Solutions("added-null-guard");
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P1-020-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => CompareCommand.Run(
                new Equiv.Cli.CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false) { ResourceLimit = 5_000_000 },
                [new CSharpFrontend()],
                new WithoutRefinement(new Z3Backend()),
                new FileReportSink(outPath),
                NullRunLog.Instance));
            SarifLog plain = SarifLog.Load(outPath);

            Assert.Equal(ExitCodes.Divergent, refined.ExitCode);
            Assert.Equal(refined.ExitCode, exitCode);
            Assert.Equal(Facts(refined.Log), Facts(plain));
            Assert.All(refined.Log.Runs[0].Results, static r => Assert.True(r.TryGetProperty("divergenceKind", out string? _)));
            Assert.All(plain.Runs[0].Results, static r => Assert.False(r.TryGetProperty("divergenceKind", out string? _)));
        }
        finally
        {
            File.Delete(outPath);
        }

        static string[] Facts(SarifLog log) =>
        [
            .. log.Runs[0].Results
                .Select(static r => string.Join(' ', r.RuleId, r.Level, r.Kind, r.Rank.ToString(CultureInfo.InvariantCulture), r.GetProperty<string>("reviewGroup"), r.PartialFingerprints["procedureIdentity/v1"], r.PartialFingerprints["resultFingerprint/v1"]))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Ticket P2-050 criterion 5: with a tiny <c>--resource-limit</c> and a ten-minute <c>timeoutMs</c>, the pair of
    /// <c>added-branch</c> that needs the solver is Unknown(timeout) on the resource limit, long before the wall-clock
    /// backstop, and a second run writes the same bytes: nothing in the result depends on how fast the machine was. The one
    /// measurement in the file, the census's failure-refinement time, is masked: a timeout Unknown is queried (ticket P1-035).
    /// The run is a thorough one, since quick asks no refinement on a timeout, and the config's <c>escalation</c> keeps the
    /// budget pass at the same tiny limit (ticket P1-032): then the run has none, and the first pass asks the refinement
    /// itself. With ADR 0049's 30,000,000 the pass would decide the pair.
    /// </summary>
    [Fact]
    public void RepeatedRunsAreByteIdentical()
    {
        (string legacy, string modern) = Solutions("added-branch");
        string configPath = Path.Combine(Path.GetTempPath(), $"equiv-P2-050-{Guid.NewGuid():N}.json");
        string[] outPaths = [.. Enumerable.Range(0, 2).Select(static _ => Path.Combine(Path.GetTempPath(), $"equiv-P2-050-{Guid.NewGuid():N}.sarif"))];
        try
        {
            File.WriteAllText(configPath, """{ "timeoutMs": 600000, "escalation": { "bound": 3, "resourceLimit": 1 } }""");

            int[] exitCodes = [.. outPaths.Select(outPath => RunProgramSilently(() => Program.Main(
                ["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, "--config", configPath, "--resource-limit", "1", "--mode", "thorough"])))];

            Assert.Equal([ExitCodes.Success, ExitCodes.Success], exitCodes);
            Result unknown = SarifLog.Load(outPaths[0]).Runs[0].Results.Single(static r => r.Message.Text.Contains("::Double(", StringComparison.Ordinal));
            Assert.Equal("EQ003", unknown.RuleId);
            Assert.Equal("timeout", unknown.GetProperty<string>("unknownReason"));
            Assert.Contains("solver returned unknown (canceled): resource limit 1 hit", unknown.Message.Text, StringComparison.Ordinal);
            Assert.True(unknown.TryGetProperty("failureRefinement", out Dictionary<string, object>? _));
            string[] texts = [.. outPaths.Select(static outPath => RefinementMilliseconds.Replace(File.ReadAllText(outPath), "${key}0"))];
            Assert.Matches(RefinementMilliseconds, File.ReadAllText(outPaths[0]));
            Assert.Equal(texts[0], texts[1]);
        }
        finally
        {
            File.Delete(configPath);
            Array.ForEach(outPaths, File.Delete);
        }
    }

    private static Task<string> Snapshot(string sample) =>
        File.ReadAllTextAsync(Path.Combine(SamplesRoot, sample, "expected.sarif.json"), TestContext.Current.CancellationToken);

    private static Result Single(string sample, string messageContains) =>
        RunSample(sample).Log.Runs[0].Results.Single(r => r.Message.Text.Contains(messageContains, StringComparison.Ordinal));

    private static int ExpectedExitCode(string sample)
    {
        string readme = File.ReadAllText(Path.Combine(SamplesRoot, sample, "README.md"));
        Match match = ExitCodePattern.Match(readme);
        Assert.True(match.Success, $"{sample}/README.md does not document its exit code");
        return int.Parse(match.Groups["code"].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The sample's own <c>equiv.config.json</c>, which only a sample that declares something in it has (ticket P2-192's
    /// <c>callee-replacement</c>); null otherwise. Every run of a sample takes it, so two runs of one sample differ only in
    /// what the test varies.
    /// </summary>
    private static string? ConfigPath(string sample)
    {
        string path = Path.Combine(SamplesRoot, sample, "equiv.config.json");
        return File.Exists(path) ? path : null;
    }

    private static string[] ConfigOptions(string sample) => ConfigPath(sample) is { } path ? ["--config", path] : [];

    private static (string Legacy, string Modern) Solutions(string sample) => (
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, "legacy"), "*.sln").Single(),
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, "modern"), "*.slnx").Single());

    /// <summary>
    /// Ticket P1-032 criterion 5 (ADR 0049 decision 5): on every sample, each result that is Equivalent or Divergent in
    /// quick mode, the default the other facts here run in (ADR 0052), has the same rule id with <c>--mode thorough</c>.
    /// Thorough only ever adds to what quick decides.
    /// </summary>
    [Theory]
    [MemberData(nameof(Samples))]
    public void QuickDecisionsHoldInThorough(string sample)
    {
        Run thoroughRun = ThoroughRun(sample).Log.Runs[0];
        Dictionary<string, string> thorough = thoroughRun.Results.ToDictionary(static r => r.PartialFingerprints["procedureIdentity/v1"], static r => r.RuleId, StringComparer.Ordinal);
        Run quick = RunSample(sample).Log.Runs[0];

        Assert.Equal("thorough", (string)thoroughRun.GetProperty<Newtonsoft.Json.Linq.JObject>("mode")["name"]!);
        Assert.Equal("quick", (string)quick.GetProperty<Newtonsoft.Json.Linq.JObject>("mode")["name"]!);
        Assert.Equal(thorough.Count, quick.Results.Count);
        Assert.All(
            quick.Results.Where(static r => r.RuleId is "EQ001" or "EQ002" or "EQ006"),
            r => Assert.Equal(r.RuleId, thorough[r.PartialFingerprints["procedureIdentity/v1"]]));
        Assert.All(quick.Results, static r => Assert.False(r.TryGetProperty("decidedBy", out string? _)));
    }

    private static SampleRun RunSample(string sample) =>
        Cache.GetOrAdd(sample, static s => new Lazy<SampleRun>(() => Execute(s))).Value;

    /// <summary>The sample's one <c>--mode thorough</c> run, cached as <see cref="RunSample"/>'s default, quick one is.</summary>
    private static SampleRun ThoroughRun(string sample) =>
        ThoroughCache.GetOrAdd(sample, static s => new Lazy<SampleRun>(() => Execute(s, "--mode", "thorough"))).Value;

    private static SampleRun Execute(string sample, params string[] options)
    {
        string sampleDir = Path.Combine(SamplesRoot, sample);
        (string legacy, string modern) = Solutions(sample);
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M3-003-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = RunProgramSilently(() => Program.Main(["compare", "--legacy", legacy, "--modern", modern, "--out", outPath, .. Pinned, .. ConfigOptions(sample), .. options]));
            string json = File.ReadAllText(outPath);
            return new SampleRun(exitCode, json, SarifNormalizer.Normalize(json, sampleDir), SarifLog.Load(outPath));
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    private static int RunProgramSilently(Func<int> action)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter output = new();
        using StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            return action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [GeneratedRegex(@"Exit code:\s*(?<code>\d+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExitCodePattern { get; }

    /// <summary>The census's <c>failureRefinement.milliseconds</c>: wall-clock time, the one value that differs between two runs.</summary>
    [GeneratedRegex(@"(?<key>""pairs"":\s*\d+,\s*""milliseconds"":\s*)\d+", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RefinementMilliseconds { get; }

    private sealed record SampleRun(int ExitCode, string Json, string NormalizedSarif, SarifLog Log);

    /// <summary><paramref name="backend"/> with every Unknown's and Divergent's failure refinement dropped, as the backend was before tickets P1-013 and P1-020.</summary>
    private sealed class WithoutRefinement(IVerificationBackend backend) : IVerificationBackend
    {
        public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options)
        {
            Verdict verdict = backend.Verify(oldBody, newBody, options);
            return verdict switch
            {
                Unknown unknown => unknown with { FailureRefinement = null },
                Divergent divergent => divergent with { FailureRefinement = null },
                _ => verdict,
            };
        }

        public Equivalent? VerifyUnderContracts(IrProcedure oldBody, IrProcedure newBody, ImmutableArray<CalleePair> callees, VerificationOptions options) =>
            backend.VerifyUnderContracts(oldBody, newBody, callees, options);
    }
}
