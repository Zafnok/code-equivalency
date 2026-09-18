using System.Collections.Immutable;

namespace Equiv.Core.Configuration;

/// <summary>
/// Structural, order-independent equality for the <see cref="ImmutableDictionary{TKey,TValue}"/>
/// members of config records (it compares by reference otherwise). Mirrors <c>Equiv.Core.Ir.IrEquality</c>.
/// </summary>
internal static class ConfigEquality
{
    public static bool DictionaryEqual(ImmutableDictionary<string, string> left, ImmutableDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach ((string key, string value) in left)
        {
            if (!right.TryGetValue(key, out string? otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public static int Hash(ImmutableDictionary<string, string> dictionary)
    {
        int hash = 0;
        foreach ((string key, string value) in dictionary)
        {
            hash ^= HashCode.Combine(key, value);
        }

        return hash;
    }
}
