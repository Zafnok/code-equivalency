using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// Ticket P2-077 criterion 5: over generated sets of pairs, <c>--jobs 1</c> and <c>--jobs 4</c> give the same exit code,
/// the same lines on stderr and the same SARIF. Each pair is one of the ways the <c>verify</c> and <c>contracts</c>
/// phases can end one, and the backend answers a pair the same whichever thread asks. The SARIF these runs write holds
/// no time, so there is no timing property to remove before comparing.
/// </summary>
[Collection("Console")]
public sealed class ParallelVerifyProperties
{
    private const string Callee = "T::F()";

    /// <summary>How one generated pair ends.</summary>
    private enum Ending
    {
        Equivalent,
        Divergent,
        TimedOut,
        Crashes,
        Congruent,
        Unbound,
        ProvedUnderContract,
        ContractCrashes,
        KeptWithoutContract,
    }

    [Fact]
    public void JobsDoNotChangeResults()
    {
        Gen.Enum<Ending>().Array[1, 12].Sample(
            static endings =>
            {
                ProcedurePair[] pairs = [.. CompareCommandTests.Identities(endings.Length).Select((identity, i) => Pair(identity, endings[i])), CompareCommandTests.Caller(Callee)];

                (int exitCode, SarifLog log, string stderr) = CompareCommandTests.ParallelLog(pairs, Backend(endings), jobs: 1);
                (int parallelExitCode, SarifLog parallelLog, string parallelStderr) = CompareCommandTests.ParallelLog(pairs, Backend(endings), jobs: 4);

                Assert.Equal(exitCode, parallelExitCode);
                Assert.Equal(stderr, parallelStderr);
                Assert.Equal(CompareCommandTests.Serialize(log), CompareCommandTests.Serialize(parallelLog));
            },
            iter: 60,
            threads: 1, // each run captures the console, which is the process's
            print: static endings => string.Join(' ', endings));
    }

    private static ProcedurePair Pair(ProcedureIdentity identity, Ending ending)
    {
        BodyFingerprint same = new("same", RuntimeSensitive: false);
        return ending switch
        {
            Ending.Congruent => CompareCommandTests.Pair(identity) with { OldFingerprint = same, NewFingerprint = same },
            Ending.Unbound => CompareCommandTests.Pair(identity) with { NewBody = CompareCommandTests.UnboundBody(identity) },
            Ending.ProvedUnderContract or Ending.ContractCrashes or Ending.KeptWithoutContract => CompareCommandTests.Caller(identity.Value, Callee),
            _ => CompareCommandTests.Pair(identity),
        };
    }

    /// <summary>A backend that answers each pair by its ending, after giving up its thread so the pairs finish in no fixed order.</summary>
    private static ScriptedBackend Backend(Ending[] endings) => new((identity, _) =>
    {
        Thread.Yield();
        return string.Equals(identity, Callee, StringComparison.Ordinal)
            ? new Divergent(CompareCommandTests.Counterexample())
            : endings[CompareCommandTests.Index(identity)] switch
            {
                Ending.Divergent => new Divergent(CompareCommandTests.Counterexample()),
                Ending.TimedOut => new Unknown(UnknownReason.Timeout, "gave up")
                {
                    Ladder = [new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, $"gave up{QueryEndings.ResourceLimitHit}5000000 hit")],
                },
                Ending.Crashes => throw new InvalidOperationException($"bug in {identity}"),
                _ => new Equivalent(ProofMethod.Bounded),
            };
    })
    {
        Contracts = (identity, _) =>
        {
            Thread.Yield();
            return endings[CompareCommandTests.Index(identity)] switch
            {
                Ending.ProvedUnderContract => new Equivalent(ProofMethod.Bounded) { ContractsUsed = [new ContractUse(Callee, "true", "observed-predicates")] },
                Ending.ContractCrashes => throw new InvalidOperationException($"contract bug in {identity}"),
                _ => null,
            };
        },
    };
}
