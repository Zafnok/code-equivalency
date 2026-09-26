using System.Globalization;
using System.Text;

namespace Equiv.Execute;

/// <summary>
/// JSON text in the driver's wire form (ticket M3-032). A string is ASCII only: every character outside printable ASCII,
/// a lone surrogate included, is a <c>\uXXXX</c> escape, so the text survives any console code page unchanged.
/// </summary>
internal static class JsonText
{
    public static string String(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        StringBuilder text = new("\"");
        foreach (char c in value)
        {
            if (c is '"' or '\\')
            {
                text.Append('\\').Append(c);
            }
            else if (c is < ' ' or > '~')
            {
                text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.Append('"').ToString();
    }
}
