using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Canonical JSON export/parse (RQ-19): a <see cref="Company"/> serialises to a versioned envelope
/// (<c>formatVersion</c> + <c>schemaVersion</c> + <c>company</c> + <c>payload</c>) whose money is integer
/// paisa and whose ordering is deterministic, so the bytes are stable and the round-trip is lossless (PR-4).
/// <para>
/// <b>Parse never throws on bad data.</b> <see cref="Parse"/> returns <c>(model, errors)</c>: a malformed
/// document (not JSON, wrong root, an unknown/absent version, a non-integer money value, a missing required
/// field) yields a <c>null</c> model and a list of per-problem messages — the engine stage rejects the batch
/// (RQ-21) without an exception escaping the IO layer.
/// </para>
/// Pure and framework-agnostic: <see cref="System.Text.Json"/> only, no clock/RNG/Avalonia/NuGet.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        // camelCase envelope keys (formatVersion, schemaVersion, company, payload, …) — matches RQ-19 & is
        // case-insensitive on read so a PascalCase document still parses.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Deterministic: we already order every list in the mapper; keep property order = declaration order.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        // Escape as little as possible so the bytes are stable & legible; still safe (no HTML context).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serialises <paramref name="company"/> to canonical JSON bytes (UTF-8, no BOM).</summary>
    public static byte[] Export(Company company)
    {
        var model = CanonicalMapper.ToModel(company);
        string json = JsonSerializer.Serialize(model, ExportOptions);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    }

    /// <summary>Serialises an already-built <see cref="CanonicalModel"/> (used to prove byte-stability of a parsed model).</summary>
    public static byte[] Export(CanonicalModel model)
    {
        string json = JsonSerializer.Serialize(model, ExportOptions);
        return new UTF8Encoding(false).GetBytes(json);
    }

    /// <summary>
    /// Parses canonical JSON bytes back to a <see cref="CanonicalModel"/>. On any structural problem it returns
    /// <c>(null, [messages])</c>; on success it returns <c>(model, [])</c>. Never throws on bad input.
    /// </summary>
    public static (CanonicalModel? Model, IReadOnlyList<string> Errors) Parse(byte[] bytes)
    {
        var errors = new List<string>();
        if (bytes is null || bytes.Length == 0)
        {
            errors.Add("Import document is empty.");
            return (null, errors);
        }

        CanonicalModel? model;
        try
        {
            model = JsonSerializer.Deserialize<CanonicalModel>(bytes, ExportOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"Malformed JSON: {ex.Message}");
            return (null, errors);
        }

        if (model is null)
        {
            errors.Add("Import document deserialised to null (not a canonical envelope).");
            return (null, errors);
        }

        CanonicalValidation.Validate(model, errors);
        return errors.Count == 0 ? (model, errors) : (null, errors);
    }
}
