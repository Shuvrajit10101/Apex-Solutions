namespace Apex.Ledger.Io;

/// <summary>
/// The spreadsheet-formula-injection guard (OWASP "CSV injection" / "formula injection") for every delimited file
/// this product writes for a human to open in a spreadsheet. It is the home of that rule for everything that can
/// see <c>Apex.Ledger.Io</c> — which is everything except <c>Apex.Ledger</c> itself. Read the two-copy note below
/// before you edit the rule.
///
/// <para>🔴 <b>Why this is public and lives here.</b> It used to be a <c>private</c> step inside
/// <see cref="DelimitedText"/>, which is <c>internal</c> — so the two Desktop view models that hand-roll their own
/// CSV (<c>KeralaFloodCessReturnViewModel</c>, <c>ProfessionalTaxRegisterViewModel</c>) could not reach it and
/// shipped user-typed text (a company name, an employee name, an enrolment number) unguarded. A clerk opening
/// such a file with a name like <c>=cmd|'/c calc'!A1</c> in it executes it. Widening exactly this one rule — and
/// nothing else — is what lets those exporters call the SAME guard instead of growing private copies that drift.
/// If you are writing a new delimited export, call this; do not re-derive it.</para>
///
/// <para>🔴 <b>THERE ARE TWO COPIES OF THIS RULE IN THE TREE, AND THE OTHER ONE IS NOT A MISTAKE.</b> The second
/// is <c>Apex.Ledger.Reports.EPayments.Csv</c> (<c>src/Apex.Ledger/Reports/EPayments.cs</c>), which quotes and
/// neutralises the bank payment-instruction file. It <b>cannot</b> call this helper and must not be "cleaned up"
/// into one: <c>Apex.Ledger.Io.csproj</c> references <c>Apex.Ledger</c>, so the dependency points
/// <c>Io → Ledger</c>, and <c>Apex.Ledger</c> therefore cannot see this type at all. Reversing that arrow to share
/// forty characters of guard would be a far worse trade than the duplication.
/// <b>So: if you change the trigger set, the space-skip or the prefix here, change
/// <c>EPayments.Csv</c> in the same commit</b> — otherwise every payment-instruction file silently keeps the old
/// rule while a grep says the rule lives in one place. <c>SpreadsheetFormulaGuardDriftLockTests</c> reads the
/// shipped source and FAILS when the two trigger sets diverge, so the cross-reference is enforced rather than
/// merely written down. These delimited writers call this one:
/// <see cref="DelimitedText"/>, <see cref="EsiContributionWriter"/>, <see cref="EcrWriter"/>,
/// <see cref="FvuWriter"/>, and the two hand-rolled Desktop exporters named above.</para>
///
/// <para>🔴 <b>That list is NOT "every delimited writer in the product", and saying so would be false.</b>
/// <c>Form24QViewModel.BuildFlatFile</c> (<c>src/Apex.Desktop/ViewModels/Form24QViewModel.cs</c>) hand-rolls a
/// pipe-delimited <c>.txt</c> return of its own and reaches NEITHER this guard NOR <see cref="FvuWriter"/> —
/// unlike its siblings <c>Form26QViewModel</c> and <c>Form27EQViewModel</c>, which do delegate to
/// <see cref="FvuWriter"/>. Its field encoder is
/// <c>(s ?? string.Empty).Replace('|', ' ').Trim()</c>: it strips the delimiter but NOT <c>'\r'</c> and NOT
/// <c>'\n'</c>, and applies no guard at all. Compare <see cref="FvuWriter.Text"/>, which strips the delimiter,
/// CR and LF and then neutralises. So a user-typed employee name carrying an embedded newline still breaks the
/// record structure of a statutory return there, and one beginning <c>= + - @</c> is still unguarded — the same
/// two defects this slice closed elsewhere. It is PRE-EXISTING, is byte-identical to <c>origin/main</c>, and was
/// deliberately left out of this slice rather than fixed unreviewed; it is recorded here so the next reader is
/// not misled by the list above into thinking the sweep was complete.</para>
///
/// <para><b>Ordering matters at every call site, and it is NOT the same order everywhere.</b> Where the exporter
/// QUOTES, neutralise FIRST: the prefix must land INSIDE the RFC-4180 quotes (<c>"'=cmd…"</c>), never outside
/// (<c>'"=cmd…"</c>, which is not a valid field), and an exporter that quotes only conditionally must still keep
/// the prefix on the unquoted branch. Where the exporter has no quoting and instead SANITISES the framing
/// characters away (the flat-file writers: <see cref="EsiContributionWriter"/>, <see cref="EcrWriter"/>,
/// <see cref="FvuWriter"/> replace the delimiter and CR/LF with a space), neutralise LAST — because that
/// replacement can itself expose a trigger that was not first before: <c>",=cmd…"</c> sanitises to
/// <c>" =cmd…"</c>, which a trimming importer evaluates. Guarding before the replacement would miss it.</para>
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
