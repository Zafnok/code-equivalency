namespace Equiv.Core;

/// <summary>
/// Thrown by <see cref="ILanguageFrontend.Analyze"/> when a path cannot be loaded enough to attempt
/// lowering (e.g. a workspace diagnostic, a missing project reference) — as opposed to an unsupported
/// construct once loading succeeded, which lowers to an opaque IR node instead (CLAUDE.md).
/// </summary>
public sealed class FrontendLoadException : Exception
{
    public FrontendLoadException()
    {
    }

    public FrontendLoadException(string message)
        : base(message)
    {
    }

    public FrontendLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FrontendLoadException(string path, string detail)
        : base($"failed to load '{path}': {detail}")
    {
        Path = path;
        Detail = detail;
    }

    public string? Path { get; }

    public string? Detail { get; }
}
