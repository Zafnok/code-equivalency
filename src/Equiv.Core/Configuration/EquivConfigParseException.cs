namespace Equiv.Core.Configuration;

/// <summary>The config text is not valid JSON at all (as opposed to valid JSON with the wrong shape, which <see cref="EquivConfigLoader.Load"/> reports as diagnostics instead).</summary>
public sealed class EquivConfigParseException : Exception
{
    public EquivConfigParseException()
    {
    }

    public EquivConfigParseException(string message)
        : base(message)
    {
    }

    public EquivConfigParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
