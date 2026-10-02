namespace Equiv.Cli;

/// <summary>
/// The two text streams <see cref="CompareCommand.Run"/> writes its own lines to: <see cref="Out"/> for the route, the analysed
/// line counts and the review list, <see cref="Error"/> for every message and warning. <see cref="Current"/> is the console's, read at call time so
/// a test that redirects the console before a run still sees the output.
/// </summary>
internal sealed record Streams(TextWriter Out, TextWriter Error)
{
    public static Streams Current => new(Console.Out, Console.Error);
}
