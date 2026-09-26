using System.Globalization;

using Equiv.Core.Execution;

namespace Equiv.Execute.Inputs;

/// <summary>
/// Type-directed inputs for a member's parameters (ticket M3-032), deterministic from a seed. The first cases walk every
/// combination of the parameters' edge values, the first parameter varying fastest; the rest draw random values. A
/// member whose every parameter has finitely many values (<c>bool</c>, an enum, <c>null</c>) stops once every
/// combination is listed.
/// </summary>
internal static class InputGenerator
{
    // The value lists below are properties, not static readonly fields: a field initialiser runs once per test process,
    // so a mutation tester could never switch its mutants on.

    /// <summary>ASCII, Latin-1, Turkish dotted and dotless i, combining marks and surrogate halves.</summary>
    private static char[] Chars =>
        ['a', 'I', 'i', '0', ' ', '\u00DF', '\u00E6', '\u00E9', '\u0130', '\u0131', '\u0301', '\u0308', '\uD800', '\uDC00'];

    /// <summary>The culture-sensitive string corpus (<c>\u00AD</c> is a soft hyphen), then empty and <c>null</c>.</summary>
    private static string[] Strings =>
        [.. new[] { "i", "I", "\u00DF", "ss", "\u0000", "\u00AD", "\u00E6", "ae", string.Empty }.Select(JsonText.String), "null"];

    private static float[] Singles =>
        [0f, -0f, 1f, -1f, 0.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, float.Epsilon];

    private static double[] Doubles =>
        [0d, -0d, 1d, -1d, 0.1d, 0.1d + 0.2d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue, double.Epsilon];

    private static decimal[] Decimals => [0m, 1m, -1m, 0.1m, 1.0m, decimal.MaxValue, decimal.MinValue];

    private const int MaxRandomStringLength = 8;

    public static IReadOnlyList<ExecutionInput> Generate(IReadOnlyList<ExecutionParameter> parameters, ulong seed, int cases)
    {
        IReadOnlyList<IReadOnlyList<string>> edges = [.. parameters.Select(Edges)];
        long combinations = edges.Aggregate(1L, static (product, values) => Math.Min(product * values.Count, int.MaxValue));
        List<ExecutionInput> inputs = [];
        for (int i = 0; i < cases && i < combinations; i++)
        {
            inputs.Add(new ExecutionInput(Combination(edges, i)));
        }

        if (parameters.All(static p => IsFinite(p.Kind)))
        {
            return inputs;
        }

        SplitMix random = new(seed);
        while (inputs.Count < cases)
        {
            inputs.Add(new ExecutionInput([.. parameters.Select((p, k) => Random(p.Kind, edges[k], random))]));
        }

        return inputs;
    }

    /// <summary>Case <paramref name="index"/> of the edge-value product, read as a mixed-radix number.</summary>
    private static List<string> Combination(IReadOnlyList<IReadOnlyList<string>> edges, int index)
    {
        List<string> arguments = [];
        foreach (IReadOnlyList<string> values in edges)
        {
            arguments.Add(values[index % values.Count]);
            index /= values.Count;
        }

        return arguments;
    }

    private static bool IsFinite(ExecutionTypeKind kind) => kind is ExecutionTypeKind.Boolean or ExecutionTypeKind.Enum or ExecutionTypeKind.NullOnly;

    private static IReadOnlyList<string> Edges(ExecutionParameter parameter) => parameter.Kind switch
    {
        ExecutionTypeKind.Boolean => ["false", "true"],
        ExecutionTypeKind.Character => [.. Chars.Select(static c => Integer((ulong)c))],
        ExecutionTypeKind.SignedByte => Signed(sbyte.MinValue, sbyte.MaxValue),
        ExecutionTypeKind.UnsignedByte => Unsigned(byte.MaxValue),
        ExecutionTypeKind.Signed16 => Signed(short.MinValue, short.MaxValue),
        ExecutionTypeKind.Unsigned16 => Unsigned(ushort.MaxValue),
        ExecutionTypeKind.Signed32 => Signed(int.MinValue, int.MaxValue),
        ExecutionTypeKind.Unsigned32 => Unsigned(uint.MaxValue),
        ExecutionTypeKind.Signed64 => Signed(long.MinValue, long.MaxValue),
        ExecutionTypeKind.Unsigned64 => Unsigned(ulong.MaxValue),
        ExecutionTypeKind.Binary32 => [.. Singles.Select(static f => Bits(BitConverter.SingleToUInt32Bits(f), 8))],
        ExecutionTypeKind.Binary64 => [.. Doubles.Select(static d => Bits(BitConverter.DoubleToUInt64Bits(d), 16))],
        ExecutionTypeKind.DecimalNumber => [.. Decimals.Select(static m => Decimal(decimal.GetBits(m)))],
        ExecutionTypeKind.Text => Strings,
        ExecutionTypeKind.Enum => [.. parameter.EnumValues, Undefined(parameter.EnumValues)],
        ExecutionTypeKind.NullOnly => ["null"],
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter.TypeName, "No input can be built for this parameter type."),
    };

    private static string Random(ExecutionTypeKind kind, IReadOnlyList<string> edges, SplitMix random) => kind switch
    {
        ExecutionTypeKind.Character => Integer(random.Next() & 0xFFFF),
        ExecutionTypeKind.SignedByte => Integer((sbyte)random.Next()),
        ExecutionTypeKind.UnsignedByte => Integer((byte)random.Next()),
        ExecutionTypeKind.Signed16 => Integer((short)random.Next()),
        ExecutionTypeKind.Unsigned16 => Integer((ushort)random.Next()),
        ExecutionTypeKind.Signed32 => Integer((int)random.Next()),
        ExecutionTypeKind.Unsigned32 => Integer((uint)random.Next()),
        ExecutionTypeKind.Signed64 => Integer((long)random.Next()),
        ExecutionTypeKind.Unsigned64 => Integer(random.Next()),
        ExecutionTypeKind.Binary32 => Bits(random.Next() & uint.MaxValue, 8),
        ExecutionTypeKind.Binary64 => Bits(random.Next(), 16),
        ExecutionTypeKind.DecimalNumber => Decimal([(int)random.Next(), (int)random.Next(), (int)random.Next(), RandomDecimalFlags(random)]),
        ExecutionTypeKind.Text => RandomString(random),
        _ => edges[(int)(random.Next() % (ulong)edges.Count)],
    };

    private static IReadOnlyList<string> Signed(long min, long max) => ["0", "1", "-1", Integer(min), Integer(max)];

    private static IReadOnlyList<string> Unsigned(ulong max) => ["0", "1", Integer(max)];

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Integer(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bits(ulong bits, int digits) => $"\"0x{bits.ToString("X" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)}\"";

    private static string Decimal(int[] bits) => $"[{string.Join(',', bits.Select(static b => Integer(b)))}]";

    /// <summary>A sign bit and a scale from 0 to 28, the only flags <c>new decimal(int[])</c> accepts.</summary>
    private static int RandomDecimalFlags(SplitMix random)
    {
        int scale = (int)(random.Next() % 29);
        int sign = (random.Next() & 1) == 0 ? 0 : int.MinValue;
        return sign | (scale << 16);
    }

    /// <summary>Up to eight characters, each printable ASCII or one of the edge characters, half and half.</summary>
    private static string RandomString(SplitMix random)
    {
        int length = (int)(random.Next() % (MaxRandomStringLength + 1));
        char[] characters = new char[length];
        for (int i = 0; i < length; i++)
        {
            ulong draw = random.Next();
            characters[i] = (draw & 1) == 0 ? (char)(' ' + (int)((draw >> 1) % 95)) : Chars[(int)((draw >> 1) % (ulong)Chars.Length)];
        }

        return JsonText.String(new string(characters));
    }

    /// <summary>The smallest non-negative integer that no defined value spells.</summary>
    private static string Undefined(IReadOnlyList<string> defined)
    {
        long candidate = 0;
        while (defined.Contains(Integer(candidate), StringComparer.Ordinal))
        {
            candidate++;
        }

        return Integer(candidate);
    }

    /// <summary>SplitMix64: a fixed generator, so a seed means the same inputs on every .NET version.</summary>
    private sealed class SplitMix(ulong seed)
    {
        private ulong state = seed;

        public ulong Next()
        {
            ulong z = state += 0x9E3779B97F4A7C15;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }
}
