using System.Globalization;

namespace Equiv.Core.Ir;

/// <summary>Malformed IR text. <see cref="Line"/> and <see cref="Column"/> are 1-based; 0 when unknown.</summary>
public sealed class IrParseException : Exception
{
    public IrParseException()
    {
    }

    public IrParseException(string message)
        : base(message)
    {
    }

    public IrParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public IrParseException(string message, int line, int column)
        : base(string.Create(CultureInfo.InvariantCulture, $"{line}:{column}: {message}"))
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }
}
