using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// The property table of one bare evaluation (M3-029) and MSBuild's <c>$(...)</c> expansion over it. Global properties
/// cannot be overridden by the project. A name the project never set falls back to the environment, as in MSBuild.
/// MSBuild's own tool paths expand to a <see cref="PropertyValue.ToolPath"/> value, and a property function, a
/// registry property or an item reference expands to a value that names that construct as unsupported.
/// </summary>
internal sealed class MsBuildProperties
{
    private static readonly FrozenSet<string> ToolPaths = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "MSBuildBinPath",
        "MSBuildExtensionsPath",
        "MSBuildExtensionsPath32",
        "MSBuildExtensionsPath64",
        "MSBuildFrameworkToolsPath",
        "MSBuildFrameworkToolsPath32",
        "MSBuildFrameworkToolsPath64",
        "MSBuildSDKsPath",
        "MSBuildToolsPath",
        "MSBuildToolsPath32",
        "MSBuildToolsPath64",
        "MSBuildToolsRoot",
        "MSBuildToolsVersion",
        "MSBuildVersion",
        "VSToolsPath");

    private readonly Dictionary<string, PropertyValue> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly FrozenSet<string> _global;
    private readonly Func<string, string?> _environment;

    public MsBuildProperties(IReadOnlyDictionary<string, string> global, Func<string, string?> environment)
    {
        foreach ((string name, string value) in global)
        {
            _values[name] = new PropertyValue(value);
        }

        _global = global.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _environment = environment;
    }

    public PropertyValue this[string name] =>
        _values.TryGetValue(name, out PropertyValue? value) ? value
        : ToolPaths.Contains(name) ? new PropertyValue(string.Empty, $"MSBuild's tool path $({name})", ToolPath: true)
        : _environment(name) is { } variable ? new PropertyValue(variable)
        : PropertyValue.Empty;

    /// <summary>Sets a property the project defines; a global property keeps its value.</summary>
    public void Set(string name, PropertyValue value)
    {
        if (!_global.Contains(name))
        {
            _values[name] = value;
        }
    }

    /// <summary>Sets a property only when it is empty, the way MSBuild's own props and targets default one.</summary>
    public void Default(string name, string value)
    {
        if (this[name] is { Unsupported: null, Text.Length: 0 })
        {
            Set(name, new PropertyValue(value));
        }
    }

    /// <summary>The exact value of a property the loader reads.</summary>
    /// <exception cref="UnsupportedConstructException">The property depends on a construct the bare loader cannot evaluate.</exception>
    public string Read(string name) => this[name].Exact;

    /// <summary>A property the loader reads as a boolean: true only for <c>true</c>, in any case.</summary>
    public bool IsTrue(string name) => string.Equals(Read(name).Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Expands every <c>$(...)</c> in <paramref name="text"/>. Escapes (<c>%3B</c>) are left for the caller.</summary>
    public PropertyValue Expand(string text)
    {
        if (text.Contains("@(", StringComparison.Ordinal) || text.Contains("%(", StringComparison.Ordinal))
        {
            return PropertyValue.Poisoned($"an item reference or metadata in '{text}'");
        }

        StringBuilder expanded = new();
        string? unsupported = null;
        bool toolPath = false;
        int i = 0;
        while (i < text.Length)
        {
            int start = text.IndexOf("$(", i, StringComparison.Ordinal);
            int end = start < 0 ? -1 : Close(text, start + 1);
            if (end < 0)
            {
                expanded.Append(text, i, text.Length - i);
                break;
            }

            expanded.Append(text, i, start - i);
            PropertyValue value = Lookup(text[(start + 2)..end]);
            expanded.Append(value.Text);
            unsupported ??= value.ToolPath ? null : value.Unsupported;
            toolPath |= value.ToolPath;
            i = end + 1;
        }

        return unsupported is not null ? PropertyValue.Poisoned(unsupported)
            : toolPath ? new PropertyValue(string.Empty, $"MSBuild's tool path in '{text}'", ToolPath: true)
            : new PropertyValue(expanded.ToString());
    }

    /// <summary>MSBuild's <c>%XX</c> escapes, decoded.</summary>
    public static string Unescape(string text)
    {
        if (!text.Contains('%', StringComparison.Ordinal))
        {
            return text;
        }

        StringBuilder unescaped = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 2 < text.Length && int.TryParse(text.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
            {
                unescaped.Append((char)code);
                i += 2;
            }
            else
            {
                unescaped.Append(text[i]);
            }
        }

        return unescaped.ToString();
    }

    private PropertyValue Lookup(string inner)
    {
        string name = inner.Trim();
        return name.StartsWith('[') || name.Contains('.', StringComparison.Ordinal) || name.Contains('(', StringComparison.Ordinal)
                ? PropertyValue.Poisoned($"the property function $({inner})")
            : name.Contains(':', StringComparison.Ordinal) ? PropertyValue.Poisoned($"the registry property $({inner})")
            : this[name];
    }

    /// <summary>The index of the parenthesis that closes the one at <paramref name="open"/>, or -1.</summary>
    private static int Close(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            depth += text[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
