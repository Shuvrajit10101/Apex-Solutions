using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// A <b>schema-driven</b> JSON validator over the subset of JSON Schema draft-04 that the published NIC statutory
/// schemas actually use: <c>type</c>, <c>required</c>, <c>properties</c>, <c>items</c>, <c>enum</c>, <c>pattern</c>,
/// <c>maxLength</c>/<c>minLength</c>, <c>maximum</c>/<c>minimum</c> and <c>multipleOf</c>.
///
/// <para><b>🔴 WHY THIS EXISTS RATHER THAN A LIST OF ASSERTIONS.</b> The EWB-01 defect (T1-29) was not that a
/// developer mistyped a key — it was that every test we had asserted on <b>our own shape</b>, so a payload the
/// portal would reject passed a green suite for as long as the payload existed. A test that re-states our key names
/// can only ever confirm that we still emit what we emit. The oracle therefore has to be the <b>schema document
/// itself</b>: <c>Fixtures/ewb01-v1.03.schema.json</c> is a verbatim copy of the <c>JSON Schema</c> block published
/// at <c>https://docs.ewaybillgst.gov.in/apidocs/version1.03/generate-eway-bill.html</c>, and this walker drives the
/// assertions off it. Rename a key in the emitter and the schema — not a hand-maintained list — is what fails.</para>
///
/// <para><b>Deliberately no NuGet schema library.</b> The gate runs on ubuntu, windows and macos; a validator this
/// small and this total is cheaper to keep honest than a dependency, and the subset is fixed by what NIC publishes.
/// Unknown keywords are ignored rather than silently treated as satisfied — see <see cref="Validate"/>.</para>
///
/// <para><b>Draft-04 semantics kept faithfully where it matters:</b> <c>pattern</c> is an <i>unanchored</i> partial
/// match (as the spec requires, not a full match); the tuple form of <c>items</c> — which is what NIC publishes, a
/// one-element array — is applied to <b>every</b> element rather than only the first, because NIC plainly means "each
/// item looks like this" and validating only element 0 would let a malformed second line through; and a member that
/// is <c>null</c> fails its declared <c>type</c>, which is exactly how an unsourced mandatory field must behave.</para>
/// </summary>
internal static class JsonSchemaSubsetValidator
{
    /// <summary>Every way <paramref name="instance"/> departs from <paramref name="schema"/>, each message carrying
    /// the JSON path of the offending member. An empty list is conformance.</summary>
    public static IReadOnlyList<string> Validate(JsonElement instance, JsonElement schema, string path = "$")
    {
        var errors = new List<string>();
        Walk(instance, schema, path, errors);
        return errors;
    }

    private static void Walk(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;

        if (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            CheckType(value, type.GetString()!, path, errors);

        if (schema.TryGetProperty("enum", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            var raw = value.GetRawText();
            if (!choices.EnumerateArray().Any(c => c.GetRawText() == raw))
                errors.Add($"{path}: {raw} is not one of the schema's enum values.");
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckRequired(value, schema, path, errors);
                CheckProperties(value, schema, path, errors);
                break;

            case JsonValueKind.Array:
                CheckItems(value, schema, path, errors);
                break;

            case JsonValueKind.String:
                CheckString(value.GetString()!, schema, path, errors);
                break;

            case JsonValueKind.Number:
                CheckNumber(value, schema, path, errors);
                break;
        }
    }

    private static void CheckType(JsonElement value, string type, string path, List<string> errors)
    {
        var ok = type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            "string" => value.ValueKind == JsonValueKind.String,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            "number" => value.ValueKind == JsonValueKind.Number,
            // draft-04: an integer is a number with a zero fractional part.
            "integer" => value.ValueKind == JsonValueKind.Number
                         && value.TryGetDecimal(out var d) && decimal.Truncate(d) == d,
            _ => true,
        };
        if (!ok) errors.Add($"{path}: expected type '{type}' but found {Describe(value)}.");
    }

    private static void CheckRequired(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array) return;
        foreach (var name in required.EnumerateArray())
        {
            var key = name.GetString();
            if (key is not null && !value.TryGetProperty(key, out _))
                errors.Add($"{path}: mandatory member '{key}' is absent.");
        }
    }

    private static void CheckProperties(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return;
        foreach (var declared in properties.EnumerateObject())
            if (value.TryGetProperty(declared.Name, out var member))
                Walk(member, declared.Value, $"{path}.{declared.Name}", errors);
    }

    private static void CheckItems(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (!schema.TryGetProperty("items", out var items)) return;
        // NIC publishes the TUPLE form with a single element; treat it as the shape of every element (see remarks).
        var element = items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().FirstOrDefault()
            : items;
        if (element.ValueKind != JsonValueKind.Object) return;
        var index = 0;
        foreach (var entry in value.EnumerateArray())
            Walk(entry, element, $"{path}[{index++}]", errors);
    }

    private static void CheckString(string text, JsonElement schema, string path, List<string> errors)
    {
        if (schema.TryGetProperty("maxLength", out var max) && max.TryGetInt32(out var maxLength)
            && text.Length > maxLength)
            errors.Add($"{path}: '{text}' is {text.Length} characters, over the schema's maxLength {maxLength}.");

        if (schema.TryGetProperty("minLength", out var min) && min.TryGetInt32(out var minLength)
            && text.Length < minLength)
            errors.Add($"{path}: '{text}' is {text.Length} characters, under the schema's minLength {minLength}.");

        if (schema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String
            && !Regex.IsMatch(text, pattern.GetString()!))   // draft-04: unanchored partial match
            errors.Add($"{path}: '{text}' does not match the schema's pattern {pattern.GetString()}.");
    }

    private static void CheckNumber(JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (!value.TryGetDecimal(out var number)) return;

        if (schema.TryGetProperty("maximum", out var max) && max.TryGetDecimal(out var maximum) && number > maximum)
            errors.Add($"{path}: {Text(number)} exceeds the schema's maximum {Text(maximum)}.");

        if (schema.TryGetProperty("minimum", out var min) && min.TryGetDecimal(out var minimum) && number < minimum)
            errors.Add($"{path}: {Text(number)} is below the schema's minimum {Text(minimum)}.");

        if (schema.TryGetProperty("multipleOf", out var step) && step.TryGetDecimal(out var multiple)
            && multiple != 0m && decimal.Remainder(number, multiple) != 0m)
            errors.Add($"{path}: {Text(number)} is not a multiple of the schema's {Text(multiple)}.");
    }

    private static string Describe(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? "null" : $"{value.ValueKind} ({value.GetRawText()})";

    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
