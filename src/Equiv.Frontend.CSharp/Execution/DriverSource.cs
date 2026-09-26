using System.Globalization;

using Equiv.Core.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// The C# source of a driver for one member (ADR 0035, ticket M3-032). Its <c>Main</c> reads one JSON line per case on
/// stdin (<c>[culture,arg0,...]</c>), sets the current culture and UI culture, calls the member, and writes one line
/// (<c>["Kind",canonical]</c>) on stdout. The canonical form is written by the driver's own code, never by a runtime's
/// <c>ToString</c>: integers in decimal, <c>float</c> and <c>double</c> as their IEEE bits in hex, <c>decimal</c> as its
/// four <c>GetBits</c> integers, strings as ASCII JSON, <c>char</c> as its code point, an enum as its underlying integer,
/// arrays and <c>List&lt;T&gt;</c> of those element-wise, and an exception as its type's full name. The expression that
/// canonicalises the result is chosen from the member's static return type, so there is no reflection. The source is
/// C# 7.3, the newest the .NET Framework 4.8 side compiles by default.
/// </summary>
internal static class DriverSource
{
    private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat;

    public static string Generate(IMethodSymbol method)
    {
        List<string> declarations = [];
        List<string> arguments = [];
        string? receiver = null;
        if (DriverFactory.HasReceiver(method))
        {
            receiver = Declare(method.ContainingType, declarations);
        }

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            arguments.Add(Declare(parameter.Type, declarations));
        }

        ITypeSymbol? result = method.MethodKind == MethodKind.Constructor ? method.ContainingType : method.ReturnsVoid ? null : method.ReturnType;
        string call = Call(method, receiver, arguments);
        string invoke = result is null ? $"{call};" : $"r = {call};";
        string declareResult = result is null ? string.Empty : $"{result.ToDisplayString(Qualified)} r;";
        string answer = result is null
            ? "Returned(\"null\")"
            : Canonical(result, "r", 0) is { } canonical
                ? $"Returned({canonical})"
                : $"NotComparable({SymbolDisplay.FormatLiteral(result.ToDisplayString(), quote: true)})";
        return Template
            .Replace("/*DECLARATIONS*/", string.Concat(declarations), StringComparison.Ordinal)
            .Replace("/*RESULT*/", declareResult, StringComparison.Ordinal)
            .Replace("/*CALL*/", invoke, StringComparison.Ordinal)
            .Replace("/*ANSWER*/", answer, StringComparison.Ordinal);
    }

    /// <summary>Declares the next argument, decoded from its slot in the case line (slot 0 is the culture); returns its name.</summary>
    private static string Declare(ITypeSymbol type, List<string> declarations)
    {
        int slot = declarations.Count + 1;
        string name = $"p{(slot - 1).ToString(CultureInfo.InvariantCulture)}";
        string value = $"a[{slot.ToString(CultureInfo.InvariantCulture)}]";
        string display = type.ToDisplayString(Qualified);
        string decoded = DriverFactory.Classify(type) switch
        {
            ExecutionTypeKind.Boolean => $"R.B({value})",
            ExecutionTypeKind.Character or ExecutionTypeKind.UnsignedByte or ExecutionTypeKind.Unsigned16 or ExecutionTypeKind.Unsigned32 or ExecutionTypeKind.Unsigned64
                => $"checked(({display})R.U({value}))",
            ExecutionTypeKind.SignedByte or ExecutionTypeKind.Signed16 or ExecutionTypeKind.Signed32 or ExecutionTypeKind.Signed64
                => $"checked(({display})R.I({value}))",
            ExecutionTypeKind.Binary32 => $"R.F({value})",
            ExecutionTypeKind.Binary64 => $"R.D({value})",
            ExecutionTypeKind.DecimalNumber => $"R.M({value})",
            ExecutionTypeKind.Text => $"R.S({value})",
            ExecutionTypeKind.Enum => IsUnsigned(((INamedTypeSymbol)type).EnumUnderlyingType!) ? $"({display})R.U({value})" : $"({display})R.I({value})",
            ExecutionTypeKind.NullOnly => $"({display}){value}",
            _ => throw new InvalidOperationException($"no input can be built for {type.ToDisplayString()}"),
        };
        declarations.Add($"{display} {name} = {decoded};");
        return name;
    }

    private static string Call(IMethodSymbol method, string? receiver, List<string> arguments)
    {
        string target = receiver ?? method.ContainingType.ToDisplayString(Qualified);
        string list = string.Join(", ", arguments);
        return method switch
        {
            { MethodKind: MethodKind.Constructor } => $"new {target}({list})",
            { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol { IsIndexer: true } } => $"{target}[{list}]",
            { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol property } => $"{target}.{property.Name}",
            _ => $"{target}.{method.Name}({list})",
        };
    }

    /// <summary>A C# expression giving <paramref name="expression"/>'s canonical JSON text, or null when its type has none.</summary>
    private static string? Canonical(ITypeSymbol type, string expression, int depth) => type.SpecialType switch
    {
        SpecialType.System_Boolean => $"W.B({expression})",
        SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 => $"W.I({expression})",
        SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_Char => $"W.U({expression})",
        SpecialType.System_Single => $"W.F({expression})",
        SpecialType.System_Double => $"W.D({expression})",
        SpecialType.System_Decimal => $"W.M({expression})",
        SpecialType.System_String => $"W.Str({expression})",
        _ => Composite(type, expression, depth),
    };

    /// <summary>An enum as its underlying integer; a one-dimensional array or a <c>List&lt;T&gt;</c> element-wise.</summary>
    private static string? Composite(ITypeSymbol type, string expression, int depth) => type switch
    {
        INamedTypeSymbol { EnumUnderlyingType: { } underlying } => Canonical(underlying, $"(({underlying.ToDisplayString(Qualified)}){expression})", depth),
        IArrayTypeSymbol array => array.IsSZArray ? Sequence(array.ElementType, expression, depth) : null,
        _ => IsList(type) ? Sequence(((INamedTypeSymbol)type).TypeArguments[0], expression, depth) : null,
    };

    private static string? Sequence(ITypeSymbol element, string expression, int depth)
    {
        string item = $"x{depth.ToString(CultureInfo.InvariantCulture)}";
        return Canonical(element, item, depth + 1) is { } canonical ? $"W.Seq({expression}, {item} => {canonical})" : null;
    }

    private static bool IsList(ITypeSymbol type) =>
        string.Equals(type.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.List<T>", StringComparison.Ordinal);

    private static bool IsUnsigned(INamedTypeSymbol underlying) =>
        underlying.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;

    private const string Template = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;
        using System.Text;
        using System.Threading;

        internal static class Program
        {
            private static void Main()
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    string answer;
                    try
                    {
                        List<object> a = J.Parse(line);
                        string culture = (string)a[0];
                        CultureInfo info = culture == "invariant" ? CultureInfo.InvariantCulture : new CultureInfo(culture);
                        Thread.CurrentThread.CurrentCulture = info;
                        Thread.CurrentThread.CurrentUICulture = info;
                        answer = Case(a);
                    }
                    catch (Exception e)
                    {
                        answer = "[\"NotConstructible\"," + W.Str(e.GetType().FullName) + "]";
                    }

                    Console.Out.WriteLine(answer);
                    Console.Out.Flush();
                }
            }

            private static string Case(List<object> a)
            {
                /*DECLARATIONS*/
                /*RESULT*/
                try
                {
                    /*CALL*/
                }
                catch (Exception e)
                {
                    return "[\"Threw\"," + W.Str(e.GetType().FullName) + "]";
                }

                return /*ANSWER*/;
            }

            private static string Returned(string canonical)
            {
                return "[\"Returned\"," + canonical + "]";
            }

            private static string NotComparable(string type)
            {
                return "[\"NotComparable\"," + W.Str(type) + "]";
            }
        }

        internal static class R
        {
            public static bool B(object v) { return (bool)v; }
            public static long I(object v) { return long.Parse((string)v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); }
            public static ulong U(object v) { return ulong.Parse((string)v, NumberStyles.None, CultureInfo.InvariantCulture); }
            public static float F(object v) { return BitConverter.ToSingle(BitConverter.GetBytes(uint.Parse(((string)v).Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)), 0); }
            public static double D(object v) { return BitConverter.Int64BitsToDouble(long.Parse(((string)v).Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)); }
            public static decimal M(object v) { List<object> b = (List<object>)v; return new decimal(new[] { (int)I(b[0]), (int)I(b[1]), (int)I(b[2]), (int)I(b[3]) }); }
            public static string S(object v) { return (string)v; }
        }

        internal static class W
        {
            private const string Hex = "0123456789ABCDEF";

            public static string B(bool v) { return v ? "true" : "false"; }

            public static string I(long v) { return v < 0 ? "-" + U((ulong)(-(v + 1)) + 1UL) : U((ulong)v); }

            public static string U(ulong v)
            {
                char[] c = new char[20];
                int i = 20;
                do { c[--i] = (char)('0' + (int)(v % 10)); v /= 10; } while (v != 0);
                return new string(c, i, 20 - i);
            }

            public static string F(float v) { return X(BitConverter.ToUInt32(BitConverter.GetBytes(v), 0), 8); }

            public static string D(double v) { return X((ulong)BitConverter.DoubleToInt64Bits(v), 16); }

            public static string M(decimal v)
            {
                int[] b = decimal.GetBits(v);
                return "[" + I(b[0]) + "," + I(b[1]) + "," + I(b[2]) + "," + I(b[3]) + "]";
            }

            public static string Str(string v)
            {
                if (v == null) { return "null"; }
                StringBuilder s = new StringBuilder("\"");
                foreach (char ch in v)
                {
                    if (ch == '"' || ch == '\\') { s.Append('\\').Append(ch); }
                    else if (ch < ' ' || ch > '~') { s.Append("\\u").Append(Hex[(ch >> 12) & 15]).Append(Hex[(ch >> 8) & 15]).Append(Hex[(ch >> 4) & 15]).Append(Hex[ch & 15]); }
                    else { s.Append(ch); }
                }

                return s.Append('"').ToString();
            }

            public static string Seq<T>(IEnumerable<T> xs, Func<T, string> f)
            {
                if (xs == null) { return "null"; }
                StringBuilder s = new StringBuilder("[");
                bool first = true;
                foreach (T x in xs)
                {
                    if (!first) { s.Append(','); }
                    first = false;
                    s.Append(f(x));
                }

                return s.Append(']').ToString();
            }

            private static string X(ulong bits, int digits)
            {
                char[] c = new char[digits];
                for (int i = digits - 1; i >= 0; i--) { c[i] = Hex[(int)(bits & 15)]; bits >>= 4; }
                return "\"0x" + new string(c) + "\"";
            }
        }

        internal static class J
        {
            public static List<object> Parse(string s)
            {
                int i = 0;
                return (List<object>)Value(s, ref i);
            }

            private static object Value(string s, ref int i)
            {
                if (s[i] == '[')
                {
                    List<object> list = new List<object>();
                    i++;
                    if (s[i] == ']') { i++; return list; }
                    while (true)
                    {
                        list.Add(Value(s, ref i));
                        if (s[i++] == ']') { return list; }
                    }
                }

                if (s[i] == '"')
                {
                    StringBuilder b = new StringBuilder();
                    i++;
                    while (s[i] != '"')
                    {
                        char c = s[i++];
                        if (c != '\\') { b.Append(c); continue; }
                        char e = s[i++];
                        if (e == 'u') { b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)); i += 4; }
                        else { b.Append(e); }
                    }

                    i++;
                    return b.ToString();
                }

                int start = i;
                while (i < s.Length && s[i] != ',' && s[i] != ']') { i++; }
                string token = s.Substring(start, i - start);
                if (token == "null") { return null; }
                if (token == "true") { return true; }
                if (token == "false") { return false; }
                return token;
            }
        }
        """;
}
