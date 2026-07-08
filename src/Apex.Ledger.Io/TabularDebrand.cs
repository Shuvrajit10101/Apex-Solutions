namespace Apex.Ledger.Io;

/// <summary>
/// De-branding for tabular cell text (ER-11) that is safe for multi-line data. <see cref="Debrand.Text"/>
/// collapses runs of whitespace (including CR/LF) to a single space, which is right for a one-line PDF
/// title/footer but would destroy a legitimate embedded newline in a CSV/XLSX cell (a multi-line narration,
/// an address). This helper de-brands each physical line independently and rejoins with the original
/// newlines, so the forbidden brand is still scrubbed while the cell's line structure survives.
/// </summary>
internal static class TabularDebrand
{
    /// <summary>De-brands <paramref name="text"/> without collapsing its newlines (each line scrubbed,
    /// original CR/LF sequences preserved). Null/empty is passed through as an empty string.</summary>
    public static string Cell(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
            return Debrand.Text(text);

        // Split on CR, LF and CRLF while keeping the separators so we can reassemble the cell exactly.
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        int lineStart = 0;
        while (i <= text.Length)
        {
            bool atEnd = i == text.Length;
            bool crlf = !atEnd && text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n';
            bool nl = !atEnd && (text[i] == '\r' || text[i] == '\n');
            if (atEnd || nl)
            {
                string line = text.Substring(lineStart, i - lineStart);
                sb.Append(Debrand.Text(line));
                if (atEnd) break;
                sb.Append(crlf ? "\r\n" : text[i]);
                i += crlf ? 2 : 1;
                lineStart = i;
            }
            else
            {
                i++;
            }
        }
        return sb.ToString();
    }
}
