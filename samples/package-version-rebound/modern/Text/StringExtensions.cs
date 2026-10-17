namespace Equiv.Samples.PackageVersionRebound.Text;

public static class StringExtensions
{
    public static string Shout(this string s) => s.ToUpperInvariant() + "!";

    public static string Tidy(this string s) => s.Trim() + ".";
}
