using System;
using System.Text;

namespace Equiv.Samples.RuntimeRowFrameworkOnlyChange;

public static class Text
{
    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.CurrentCulture);

    public static Encoding Enc() => Encoding.Default;
}
