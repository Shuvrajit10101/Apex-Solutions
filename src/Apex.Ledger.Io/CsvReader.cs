namespace Apex.Ledger.Io;

/// <summary>
/// A minimal, allocation-light RFC-4180 CSV tokenizer (the read counterpart to <see cref="CsvWriter"/>). It
/// splits a document into records of fields, honouring double-quoted fields (with doubled <c>""</c> for a
/// literal quote), embedded commas/CR/LF inside quotes, and both CRLF and bare-LF record separators. It does
/// not interpret headers or types — that is <see cref="CsvImport"/>'s job. Pure and deterministic.
/// </summary>
internal static class CsvReader
{
    public static List<List<string>> ReadAll(string text)
    {
        var records = new List<List<string>>();
        var field = new System.Text.StringBuilder();
        var current = new List<string>();
        bool inQuotes = false;
        int i = 0;
        int n = text.Length;

        void EndField() { current.Add(field.ToString()); field.Clear(); }
        void EndRecord() { EndField(); records.Add(current); current = new List<string>(); }

        while (i < n)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < n && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; i++; break;
                case ',': EndField(); i++; break;
                case '\r':
                    if (i + 1 < n && text[i + 1] == '\n') i++;
                    EndRecord(); i++; break;
                case '\n': EndRecord(); i++; break;
                default: field.Append(c); i++; break;
            }
        }

        // Flush the trailing field/record if the document did not end with a newline.
        if (field.Length > 0 || current.Count > 0)
            EndRecord();

        return records;
    }
}
