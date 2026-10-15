using System.Text;

namespace Equiv.Samples.AppendInterpolatedString
{
    public static class Text
    {
        // .NET 5 builds the string and calls Append(string); .NET 6 calls Append(ref AppendInterpolatedStringHandler).
        public static void Header(StringBuilder sb, string id) { sb.Append($"index {id}\n"); }

        // An int hole: formatted with the current culture by both bindings.
        public static void Count(StringBuilder sb, int n) { sb.Append($"count {n}\n"); }

        // AppendLine(string) against AppendLine(ref AppendInterpolatedStringHandler).
        public static void Line(StringBuilder sb, string id) { sb.AppendLine($"id {id}"); }
    }
}
