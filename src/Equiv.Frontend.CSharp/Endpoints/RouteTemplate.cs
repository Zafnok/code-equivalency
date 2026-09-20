using System.Text.RegularExpressions;

namespace Equiv.Frontend.CSharp.Endpoints;

/// <summary>
/// Pure route-template normalisation (ticket M2-005 acceptance criterion 2): join the type-level
/// prefix and the method-level route with one <c>/</c>, add a leading <c>/</c>, drop a trailing
/// <c>/</c>, replace the <c>[controller]</c>/<c>[action]</c> tokens Web API 2/MVC 5/ASP.NET Core
/// attribute routing all support, strip parameter constraints (<c>{id:int}</c> -&gt; <c>{id}</c>),
/// and lower-case the result. Parameter names are left as-is; there is no rule that changes them.
/// <see cref="Join"/> trims every leading/trailing <c>/</c> off <paramref name="prefix"/> and route
/// before joining with exactly one, so the joined text itself never starts or ends with <c>/</c>;
/// prepending a single leading <c>/</c> here therefore always both adds the leading slash and leaves
/// no trailing one (the root case, both empty, becomes <c>/</c>), with no conditional needed for either.
/// </summary>
internal static partial class RouteTemplate
{
    public static string Normalize(string? prefix, string? route, string typeName, string methodName)
    {
        string joined = "/" + Join(prefix, route);
        string tokensReplaced = ReplaceTokens(joined, ControllerName(typeName), methodName);
        string constraintsStripped = ConstraintPattern().Replace(tokensReplaced, "{${name}}");

        return constraintsStripped.ToLowerInvariant();
    }

    private static string Join(string? prefix, string? route)
    {
        string left = (prefix ?? string.Empty).Trim('/');
        string right = (route ?? string.Empty).Trim('/');
        return left.Length == 0 ? right : right.Length == 0 ? left : $"{left}/{right}";
    }

    private static string ControllerName(string typeName) =>
        typeName.EndsWith("Controller", StringComparison.Ordinal) ? typeName[..^"Controller".Length] : typeName;

    private static string ReplaceTokens(string template, string controllerName, string actionName) =>
        template
            .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\{(?<name>\w+):[^{}]+\}", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ConstraintPattern();
}
