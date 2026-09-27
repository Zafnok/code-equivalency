namespace Equiv.Core.Execution;

/// <summary>Why testing an Unknown pair stopped (ticket P1-008). SARIF <c>differentialTesting.stoppedBy</c>.</summary>
public enum TestingStop
{
    /// <summary>The estimated discovery probability fell below <c>--test-target</c> after at least the minimum number of inputs.</summary>
    Target,

    /// <summary><c>--test-budget</c>'s input count or time ran out first.</summary>
    Budget,
}
