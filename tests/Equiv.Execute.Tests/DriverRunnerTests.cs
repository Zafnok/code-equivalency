using System.Globalization;

using Equiv.Core;
using Equiv.Core.Execution;

using Xunit;

namespace Equiv.Execute.Tests;

public sealed class DriverRunnerTests
{
    private static readonly ExecutionDrivers Drivers = new("legacy.exe", "modern.dll");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private static ExecutionRequest Request(int inputs, params string[] cultures) =>
        new(new CallIdentity("System.String::ToUpper()"), [.. Enumerable.Range(0, inputs).Select(static i => new ExecutionInput([string.Create(CultureInfo.InvariantCulture, $"\"s{i}\"")]))], cultures);

    [Fact]
    public void Runner_RunsEachInputTwicePerSide()
    {
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");

        RunOutcomes runs = new DriverRunner(host, Timeout).Run(Drivers, Request(3, "invariant", "tr-TR"));

        Assert.Equal(["legacy.exe", "legacy.exe", "modern.dll", "modern.dll"], host.Starts);
        Assert.Equal(4 * 3 * 2, host.Exchanges.Count);
        foreach (int session in Enumerable.Range(1, 4))
        {
            Assert.Equal(
                ["[\"invariant\",\"s0\"]", "[\"tr-TR\",\"s0\"]", "[\"invariant\",\"s1\"]", "[\"tr-TR\",\"s1\"]", "[\"invariant\",\"s2\"]", "[\"tr-TR\",\"s2\"]"],
                host.Exchanges.Where(e => e.Session == session).Select(static e => e.Line),
                StringComparer.Ordinal);
        }

        Assert.All([runs.Legacy1, runs.Legacy2, runs.Modern1, runs.Modern2], static run => Assert.Equal(6, run.Count));
        Assert.Equal("tr-TR", runs.Modern2[5].Culture);
        Assert.Equal("\"s2\"", runs.Modern2[5].Input.Arguments[0]);
        Assert.Equal(4, host.Disposed);
    }

    [Fact]
    public void Runner_TimesOutACase()
    {
        FakeHost host = new(static (_, _, line) => line.Contains("s1", StringComparison.Ordinal) ? null : "[\"Returned\",true]");

        RunOutcomes runs = new DriverRunner(host, Timeout).Run(Drivers, Request(3, "invariant"));

        ExecutionOutcome timedOut = runs.Legacy1[1];
        Assert.Equal(OutcomeKind.NotComparable, timedOut.Kind);
        Assert.Equal(OutcomeLine.NoAnswer, timedOut.Canonical);
        Assert.Equal(OutcomeKind.Returned, runs.Legacy1[2].Kind);

        // The dead process is dropped and the next case starts a new one: two processes per run.
        Assert.Equal(8, host.Starts.Count);
        Assert.Equal(8, host.Disposed);
    }

    [Fact]
    public void Runner_StartsNoProcessForNoCases()
    {
        FakeHost host = new(static (_, _, _) => "[\"Returned\",1]");

        RunOutcomes runs = new DriverRunner(host, Timeout).Run(Drivers, Request(0, "invariant"));

        Assert.Empty(host.Starts);
        Assert.Empty(runs.Legacy1);
    }

    [Fact]
    public void Canonical_FormatsEveryOutcomeKind()
    {
        string[] answers =
        [
            "[\"Returned\",[\"0x3FB999999999999A\",null,\"\\u0130\"]]",
            "[\"Threw\",\"System.ArgumentNullException\"]",
            "[\"NotComparable\",\"System.DateTime\"]",
            "[\"NotConstructible\",\"System.Globalization.CultureNotFoundException\"]",
            "garbage",
        ];
        FakeHost host = new((_, _, line) => answers[int.Parse(line[^2..^1], System.Globalization.CultureInfo.InvariantCulture)]);
        ExecutionRequest request = new(new CallIdentity("M()"), [.. Enumerable.Range(0, answers.Length).Select(static i => new ExecutionInput([i.ToString(CultureInfo.InvariantCulture)]))], ["invariant"]);

        RunOutcomes runs = new DriverRunner(host, Timeout).Run(Drivers, request);

        Assert.Equal(
            [
                (OutcomeKind.Returned, "[\"0x3FB999999999999A\",null,\"\\u0130\"]"),
                (OutcomeKind.Threw, "\"System.ArgumentNullException\""),
                (OutcomeKind.NotComparable, "\"System.DateTime\""),
                (OutcomeKind.NotConstructible, "\"System.Globalization.CultureNotFoundException\""),
                (OutcomeKind.NotComparable, "\"malformed: garbage\""),
            ],
            runs.Modern1.Select(static o => (o.Kind, o.Canonical)));
        Assert.Equal(["Returned", "Threw", "NotComparable", "NotConstructible"], new[] { OutcomeKind.Returned, OutcomeKind.Threw, OutcomeKind.NotComparable, OutcomeKind.NotConstructible }.Select(OutcomeLine.Name), StringComparer.Ordinal);
    }
}
