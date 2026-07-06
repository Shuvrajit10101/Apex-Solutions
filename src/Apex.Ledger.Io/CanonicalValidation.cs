namespace Apex.Ledger.Io;

/// <summary>
/// Envelope-level, format-agnostic validation of a parsed <see cref="CanonicalModel"/> (shared by the JSON and
/// XML parsers). It confirms the versioned-envelope invariants — a supported <c>formatVersion</c>, a company
/// header with a name and parseable dates, and internally-consistent required fields — and appends a
/// per-problem message for each failure (RQ-21) rather than throwing. Deeper referential + accounting checks
/// (a line referencing a missing ledger, an unbalanced voucher) are the engine stage's job (ER-6); this layer
/// guards the wire shape so the engine only ever sees a structurally-sound model.
/// </summary>
public static class CanonicalValidation
{
    /// <summary>The format versions this build can import.</summary>
    private static readonly int[] SupportedFormatVersions = [CanonicalMapper.FormatVersion];

    public static void Validate(CanonicalModel model, List<string> errors)
    {
        if (!SupportedFormatVersions.Contains(model.FormatVersion))
            errors.Add($"Unsupported formatVersion {model.FormatVersion} (supported: {string.Join(", ", SupportedFormatVersions)}).");

        if (model.Company is null)
        {
            errors.Add("Envelope is missing the 'company' header.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(model.Company.Name))
                errors.Add("company.name is required.");
            RequireIsoDate("company.financialYearStart", model.Company.FinancialYearStart, errors);
            RequireIsoDate("company.booksBeginFrom", model.Company.BooksBeginFrom, errors);
        }

        if (model.Payload is null)
        {
            errors.Add("Envelope is missing the 'payload'.");
            return;
        }

        foreach (var g in model.Payload.Groups)
            if (string.IsNullOrWhiteSpace(g.Name)) errors.Add($"Group {g.Id} has an empty name.");
        foreach (var l in model.Payload.Ledgers)
            if (string.IsNullOrWhiteSpace(l.Name)) errors.Add($"Ledger {l.Id} has an empty name.");
        foreach (var v in model.Payload.Vouchers)
            RequireIsoDate($"voucher {v.Id} date", v.Date, errors);
    }

    private static void RequireIsoDate(string field, string? value, List<string> errors)
    {
        // Strict: ONLY yyyy-MM-dd, InvariantCulture — a locale-ambiguous form (e.g. "01/04/2021" or "4-1-2021")
        // is rejected, so the wire date is unambiguous regardless of the machine's culture (RQ-19/RQ-21).
        if (string.IsNullOrWhiteSpace(value) ||
            !DateOnly.TryParseExact(value, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            errors.Add($"{field} '{value}' is not a valid ISO yyyy-MM-dd date.");
    }
}
