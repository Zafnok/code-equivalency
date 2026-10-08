using System;
using System.IO;
using System.Text;

namespace Equiv.Samples.ParamsSpanOverloads;

public static class Texts
{
    public static string Describe(IFormatProvider provider, string format, object a, object b, object c, object d)
    {
        return string.Format(provider, format, a, b, c, d);
    }

    public static string Row(char separator, string a, string b)
    {
        return string.Join(separator, a, b);
    }

    public static StringBuilder Append(StringBuilder text, IFormatProvider provider, string format, object a, object b, object c, object d)
    {
        return text.AppendFormat(provider, format, a, b, c, d);
    }

    public static string Locate(string a, string b, string c, string d, string e)
    {
        return Path.Combine(a, b, c, d, e);
    }

    public static string RowSwapped(char separator, string a, string b)
    {
        return string.Join(separator, a, b);
    }

    public static string RowOfArray(char separator, string[] parts)
    {
        return string.Join(separator, parts);
    }
}
