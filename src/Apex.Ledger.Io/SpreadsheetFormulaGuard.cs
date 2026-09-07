namespace Apex.Ledger.Io;

/// <summary>
/// The <b>single</b> spreadsheet-formula-injection guard (OWASP "CSV injection" / "formula injection") for every
/// delimited file this product writes for a human to open in a spreadsheet.
///
/// <para>🔴 <b>Why this is public and lives here.</b> It used to be a <c>private</c> step inside
/// <see cref="DelimitedText"/>, which is <c>internal</c> — so the two Desktop view models that hand-roll their own
/// CSV (<c>KeralaFloodCessReturnViewModel</c>, <c>ProfessionalTaxRegisterViewModel</c>) could not reach it and
/// shipped user-typed text (a company name, an employee name, an enrolment number) unguarded. A clerk opening
/// such a file with a name like <c>=cmd|'/c calc'!A1</c> in it executes it. Widening exactly this one rule — and
/// nothing else — is what lets those exporters call the SAME guard instead of growing a third and fourth private
/// copy that drifts. If you are writing a new delimited export, call this; do not re-derive it.</para>
///
/// <para><b>Ordering matters at every call site: NEUTRALISE FIRST, THEN QUOTE.</b> The prefix must land INSIDE the
/// RFC-4180 quotes (<c>"'=cmd…"</c>), never outside (<c>'"=cmd…"</c>, which is not a valid field). An exporter
/// that quotes only conditionally must still keep the prefix on the unquoted branch.</para>
/// </summary>
public static class SpreadsheetFormulaGuard
{
    /// <summary>
    /// Neutralizes a text field: one whose first value-carrying character is one a spreadsheet may interpret as the
    /// start of a formula (<c>= + - @</c>) or a leading control (tab <c>0x09</c>, CR <c>0x0D</c>) is prefixed with a
    /// single quote <c>'</c> so the spreadsheet renders it as literal text rather than evaluating it.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The leading-space skip is load-bearing and is not an edge case.</b> The trigger is looked for at the
    /// first character that CARRIES the value, not at <c>field[0]</c>. Leading SPACES do not stop the attack: an
    /// importer that trims on the way in (LibreOffice's "Trim spaces", Google Sheets, downstream tooling) evaluates
    /// what follows them. This matters because we indent our own output —
    /// <c>MasterListTabularProjector.ProjectChartOfAccounts</c> prefixes two spaces per level onto every non-root
    /// Name — so testing <c>field[0]</c> alone would leave every nested group and ledger row unguarded. Only
    /// <c>' '</c> is skipped; <c>'\t'</c> and <c>'\r'</c> are themselves TRIGGERS and must not be skipped past.
    ///
    /// <para>🔴 The guard <b>PREFIXES and never rewrites</b>: the field's own bytes (indentation included) survive
    /// verbatim after the quote, so ruling 18's verbatim-book-data property is untouched by the guard.</para>
    /// </remarks>
    public static string Neutralize(string? field)
    {
        if (string.IsNullOrEmpty(field)) return field ?? string.Empty;

        int i = 0;
        while (i < field.Length && field[i] == ' ') i++;
        char c = i < field.Length ? field[i] : field[0];

        return c is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + field : field;
    }
}
