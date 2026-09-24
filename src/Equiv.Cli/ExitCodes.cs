namespace Equiv.Cli;

/// <summary>
/// Process exit codes for <c>equiv compare</c> (ARCHITECTURE.md's "Equiv.Cli" exit-codes list). When several apply,
/// a tool fault outranks a verdict, because it leaves the result set incomplete (ADRs 0023 and 0029): 5 (internal
/// error, M3-013) outranks 4, and both outrank 1 and 2. <see cref="LoadFailure"/> after a written SARIF log means some
/// C# project was skipped; without one, it means a side had no C# project that loaded.
/// </summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int Divergent = 1;
    public const int UnknownPresent = 2;
    public const int UsageError = 3;
    public const int LoadFailure = 4;
    public const int InternalError = 5;
}
