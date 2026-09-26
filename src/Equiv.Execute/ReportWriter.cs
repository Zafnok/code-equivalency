using System.Text.Json;

using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>
/// Writes <c>runtime-diff</c>'s report (ticket M3-032; the format is in <c>tools/runtime-diff/README.md</c>). Inputs and
/// canonical outcomes are the drivers' own JSON text, written as raw values.
/// </summary>
internal static class ReportWriter
{
    public static void Write(Stream stream, RuntimeDiffOptions options, IReadOnlyList<string> cultures, IReadOnlyList<OverloadReport> overloads)
    {
        using Utf8JsonWriter json = new(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("member", options.Member);
        json.WriteNumber("seed", options.Seed);
        json.WriteNumber("cases", options.Cases);
        json.WriteStartArray("cultures");
        foreach (string culture in cultures)
        {
            json.WriteStringValue(culture);
        }

        json.WriteEndArray();
        json.WriteStartArray("overloads");
        foreach (OverloadReport overload in overloads)
        {
            Overload(json, overload);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Overload(Utf8JsonWriter json, OverloadReport overload)
    {
        json.WriteStartObject();
        json.WriteString("member", overload.Member);
        json.WriteNumber("casesRun", overload.CasesRun);
        json.WriteNumber("divergent", overload.Divergent);
        json.WriteStartArray("witnesses");
        foreach (OverloadReport.Witness witness in overload.Witnesses)
        {
            json.WriteStartObject();
            json.WriteStartArray("input");
            foreach (string argument in witness.Legacy.Input.Arguments)
            {
                json.WriteRawValue(argument, skipInputValidation: true);
            }

            json.WriteEndArray();
            json.WriteString("culture", witness.Legacy.Culture);
            Outcome(json, "legacy", witness.Legacy);
            Outcome(json, "modern", witness.Modern);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartObject("nondeterministic");
        json.WriteNumber("legacy", overload.LegacyNondeterministic);
        json.WriteNumber("modern", overload.ModernNondeterministic);
        json.WriteNumber("both", overload.BothNondeterministic);
        json.WriteEndObject();
        json.WriteNumber("notComparable", overload.NotComparable);
        json.WriteStartArray("notConstructible");
        foreach (string reason in overload.NotConstructible)
        {
            json.WriteStringValue(reason);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void Outcome(Utf8JsonWriter json, string name, ExecutionOutcome outcome)
    {
        json.WriteStartObject(name);
        json.WriteString("kind", OutcomeLine.Name(outcome.Kind));
        json.WritePropertyName("value");
        json.WriteRawValue(outcome.Canonical, skipInputValidation: true);
        json.WriteEndObject();
    }
}
