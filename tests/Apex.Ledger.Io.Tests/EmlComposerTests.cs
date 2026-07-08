using System.Linq;
using System.Text;
using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for the offline email composer (RQ-25/26): an RFC-5322 / MIME multipart-mixed .eml message
/// carrying a report/invoice PDF as a base64 attachment. The composer is framework-agnostic,
/// deterministic and byte-stable — it has NO clock and NO RNG. The Date header value and the
/// Message-ID are passed IN by the caller; the MIME boundary is fixed. Live SMTP send is DEFERRED:
/// nothing here opens a socket.
/// </summary>
public sealed class EmlComposerTests
{
    private static byte[] SamplePdf()
    {
        // A real PDF produced by the report renderer — the attachment must decode back to these exact bytes.
        var report = new PrintReport
        {
            Title = "Trial Balance",
            Subtitle = "Bright Traders",
            Columns = new[] { new PrintColumn("Particulars", 3.0, CellAlign.Left), new PrintColumn("Debit", 1.5, CellAlign.Right) },
            Rows = new[] { new PrintRow("Cash-in-Hand", "1,05,000.00") },
        };
        return ReportPdf.Render(report, new PageConfig());
    }

    private static EmlMessage SampleMessage(byte[] pdf) => new()
    {
        From = new EmlAddress("accounts@apexco.example", "Apex Accounts"),
        To = new[] { new EmlAddress("client@buyer.example", "A Client") },
        Cc = new[] { new EmlAddress("boss@apexco.example") },
        Subject = "Your Trial Balance",
        Body = "Please find the Trial Balance attached.\r\nRegards,\r\nApex Solutions",
        // Caller supplies both (ER-8): no clock, no RNG in the composer.
        Date = "Mon, 06 Jul 2026 12:00:00 +0530",
        MessageId = "<report-20260706-120000@apexco.example>",
        Attachments = new[] { new EmlAttachment("TrialBalance.pdf", "application/pdf", pdf) },
    };

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    [Fact]
    public void Required_headers_are_present_and_correct()
    {
        var eml = EmlComposer.Compose(SampleMessage(SamplePdf()));
        string s = AsLatin1(eml);

        Assert.Contains("MIME-Version: 1.0\r\n", s);
        Assert.Contains("From: Apex Accounts <accounts@apexco.example>\r\n", s);
        Assert.Contains("To: A Client <client@buyer.example>\r\n", s);
        Assert.Contains("Cc: boss@apexco.example\r\n", s);
        Assert.Contains("Subject: Your Trial Balance\r\n", s);
        Assert.Contains("Date: Mon, 06 Jul 2026 12:00:00 +0530\r\n", s);
        Assert.Contains("Message-ID: <report-20260706-120000@apexco.example>\r\n", s);
        Assert.Contains("Content-Type: multipart/mixed; boundary=", s);
    }

    [Fact]
    public void Body_and_attachment_parts_have_the_right_mime_headers()
    {
        var eml = EmlComposer.Compose(SampleMessage(SamplePdf()));
        string s = AsLatin1(eml);

        // text/plain body part.
        Assert.Contains("Content-Type: text/plain; charset=utf-8\r\n", s);
        // PDF attachment part.
        Assert.Contains("Content-Type: application/pdf\r\n", s);
        Assert.Contains("Content-Transfer-Encoding: base64\r\n", s);
        Assert.Contains("Content-Disposition: attachment; filename=\"TrialBalance.pdf\"\r\n", s);
    }

    [Fact]
    public void Message_is_structurally_wellformed_multipart()
    {
        var eml = EmlComposer.Compose(SampleMessage(SamplePdf()));
        var msg = MiniMime.Parse(eml);

        Assert.StartsWith("multipart/mixed", msg.ContentType);
        Assert.Equal(2, msg.Parts.Count);
        Assert.StartsWith("text/plain", msg.Parts[0].ContentType);
        Assert.StartsWith("application/pdf", msg.Parts[1].ContentType);
        // Boundary opens each part and the closing delimiter (--boundary--) terminates the body.
        Assert.True(msg.ClosingDelimiterPresent);
    }

    [Fact]
    public void Attachment_base64_decodes_to_the_exact_input_bytes()
    {
        var pdf = SamplePdf();
        var eml = EmlComposer.Compose(SampleMessage(pdf));
        var msg = MiniMime.Parse(eml);

        var decoded = msg.Parts[1].DecodeBase64();
        Assert.Equal(pdf, decoded);
    }

    [Fact]
    public void Base64_body_is_wrapped_at_76_columns()
    {
        var eml = EmlComposer.Compose(SampleMessage(SamplePdf()));
        var msg = MiniMime.Parse(eml);

        foreach (string line in msg.Parts[1].RawBodyLines)
            Assert.True(line.Length <= 76, $"base64 line exceeds 76 cols: {line.Length}");
        // And at least one full-width line exists (the PDF is well over 76 base64 chars).
        Assert.Contains(msg.Parts[1].RawBodyLines, l => l.Length == 76);
    }

    [Fact]
    public void Same_message_recomposes_byte_identical()
    {
        var pdf = SamplePdf();
        var a = EmlComposer.Compose(SampleMessage(pdf));
        var b = EmlComposer.Compose(SampleMessage(pdf));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Nonascii_subject_is_rfc2047_encoded_word()
    {
        var msg = SampleMessage(SamplePdf()) with { Subject = "Rechnung — Grüße" };
        var eml = EmlComposer.Compose(msg);
        string s = AsLatin1(eml);

        // The raw subject must NOT leak as raw UTF-8; it is an encoded-word (=?utf-8?B?...?=).
        Assert.Contains("Subject: =?utf-8?B?", s);
        // Decoding the encoded word yields the original.
        string subjLine = FindHeader(s, "Subject");
        Assert.Equal("Rechnung — Grüße", MiniMime.DecodeEncodedWords(subjLine));
    }

    [Fact]
    public void Nonascii_display_name_is_encoded_and_address_stays_ascii()
    {
        var msg = SampleMessage(SamplePdf()) with
        {
            From = new EmlAddress("info@apexco.example", "Müller & Co"),
        };
        var eml = EmlComposer.Compose(msg);
        string s = AsLatin1(eml);

        Assert.Contains("From: =?utf-8?B?", s);
        Assert.Contains("<info@apexco.example>\r\n", s);
    }

    [Fact]
    public void Multiple_recipients_are_comma_separated()
    {
        var msg = SampleMessage(SamplePdf()) with
        {
            To = new[]
            {
                new EmlAddress("one@x.example", "One"),
                new EmlAddress("two@x.example"),
            },
        };
        var eml = EmlComposer.Compose(msg);
        string s = AsLatin1(eml);
        Assert.Contains("To: One <one@x.example>, two@x.example\r\n", s);
    }

    [Fact]
    public void Multiple_attachments_all_present_and_decode()
    {
        var pdf = SamplePdf();
        var csv = Encoding.UTF8.GetBytes("a,b\r\n1,2\r\n");
        var msg = SampleMessage(pdf) with
        {
            Attachments = new[]
            {
                new EmlAttachment("Report.pdf", "application/pdf", pdf),
                new EmlAttachment("Report.csv", "text/csv", csv),
            },
        };
        var parsed = MiniMime.Parse(EmlComposer.Compose(msg));
        Assert.Equal(3, parsed.Parts.Count); // body + 2 attachments
        Assert.Equal(pdf, parsed.Parts[1].DecodeBase64());
        Assert.Equal(csv, parsed.Parts[2].DecodeBase64());
    }

    [Fact]
    public void No_cc_omits_the_cc_header()
    {
        var msg = SampleMessage(SamplePdf()) with { Cc = System.Array.Empty<EmlAddress>() };
        var eml = EmlComposer.Compose(msg);
        Assert.DoesNotContain("Cc:", AsLatin1(eml));
    }

    [Fact]
    public void No_tally_branding_in_eml_bytes()
    {
        var eml = EmlComposer.Compose(SampleMessage(SamplePdf()));
        Assert.DoesNotContain("tally", AsLatin1(eml).ToLowerInvariant());
    }

    // ---------------------------------------------------------------------------------------------------
    // Fix 1 (HIGH, SECURITY): header-injection defence — CR/LF in a header value must NOT inject a header.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Subject_with_embedded_crlf_does_not_inject_a_header()
    {
        var msg = SampleMessage(SamplePdf()) with
        {
            Subject = "Invoice\r\nBcc: evil@example.com",
        };
        var eml = EmlComposer.Compose(msg);
        string s = AsLatin1(eml);

        // No injected header: "Bcc:" never appears at the start of a physical line (it only survives as
        // inert text inside the Subject value).
        Assert.DoesNotContain("\r\nBcc:", s);
        // The Subject stays a single physical line (the CRLF was stripped, not passed through).
        Assert.Contains("Subject: InvoiceBcc: evil@example.com\r\n", s);
        // The message still parses as a well-formed 2-part multipart (structure intact).
        Assert.Equal(2, MiniMime.Parse(eml).Parts.Count);
    }

    [Fact]
    public void Recipient_display_name_with_crlf_does_not_inject_a_header()
    {
        var msg = SampleMessage(SamplePdf()) with
        {
            To = new[] { new EmlAddress("client@buyer.example", "Client\r\nBcc: evil@example.com") },
        };
        var eml = EmlComposer.Compose(msg);
        string s = AsLatin1(eml);

        // No injected header: "Bcc:" never appears at the start of a physical line (it may only survive as
        // inert text inside the To value, e.g. "To: ClientBcc: ...").
        Assert.DoesNotContain("\r\nBcc:", s);
        // The To header is a single physical line (the CRLF was stripped, not passed through).
        Assert.Contains("To: ClientBcc: evil@example.com <client@buyer.example>\r\n", s);
    }

    [Fact]
    public void Attachment_filename_with_crlf_does_not_inject_a_header()
    {
        var msg = SampleMessage(SamplePdf()) with
        {
            Attachments = new[]
            {
                new EmlAttachment("Report\r\nBcc: evil@example.com.pdf", "application/pdf", SamplePdf()),
            },
        };
        var eml = EmlComposer.Compose(msg);
        string s = AsLatin1(eml);

        // No injected header: "Bcc:" never appears at the start of a physical line (it only survives as
        // inert text inside the quoted filename).
        Assert.DoesNotContain("\r\nBcc:", s);
        // The whole disposition is one physical line with the CRLF removed.
        Assert.Contains("filename=\"ReportBcc: evil@example.com.pdf\"\r\n", s);
    }

    [Fact]
    public void Structural_fields_reject_embedded_crlf()
    {
        // A CR/LF in an addr-spec, Date or Message-ID is only ever an injection attempt → reject.
        Assert.Throws<ArgumentException>(() =>
            EmlComposer.Compose(SampleMessage(SamplePdf()) with
            {
                From = new EmlAddress("a@b.example\r\nBcc: evil@example.com"),
            }));
        Assert.Throws<ArgumentException>(() =>
            EmlComposer.Compose(SampleMessage(SamplePdf()) with { Date = "x\r\nBcc: evil@example.com" }));
        Assert.Throws<ArgumentException>(() =>
            EmlComposer.Compose(SampleMessage(SamplePdf()) with { MessageId = "<a@b>\r\nBcc: evil@example.com" }));
    }

    // ---------------------------------------------------------------------------------------------------
    // Fix 2 (HIGH): fold long header lines to the RFC-5322 998-octet hard limit (78 soft where feasible).
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Long_recipient_list_folds_within_line_limits_and_all_recipients_survive()
    {
        var many = new EmlAddress[40];
        for (int i = 0; i < many.Length; i++)
            many[i] = new EmlAddress($"recipient{i:D2}@somewhat-long-domain.example");
        var msg = SampleMessage(SamplePdf()) with { To = many };
        var eml = EmlComposer.Compose(msg);

        // Every physical line is well under the 998-octet hard limit.
        var lines = MiniMime.RawLines(eml);
        foreach (string line in lines)
            Assert.True(line.Length <= 998, $"line exceeds RFC-5322 998 hard limit: {line.Length}");

        // The folded To header (its start line + continuation lines, which begin with a WSP) each stay
        // within the 78-octet soft limit — this is the header we fold.
        bool inTo = false;
        bool sawFold = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("To:", StringComparison.Ordinal)) { inTo = true; }
            else if (inTo && (line.StartsWith(" ") || line.StartsWith("\t"))) { sawFold = true; }
            else if (inTo) { inTo = false; continue; }
            if (inTo)
                Assert.True(line.Length <= 78, $"To line exceeds 78 soft limit: {line.Length} :: {line}");
        }
        Assert.True(sawFold, "expected the 40-recipient To header to fold across multiple lines");

        // Unfolding reproduces the logical To value and every recipient is present.
        string unfolded = MiniMime.Unfold(eml);
        int toAt = unfolded.IndexOf("\r\nTo:", StringComparison.Ordinal);
        int toEnd = unfolded.IndexOf("\r\n", toAt + 2, StringComparison.Ordinal);
        string toValue = unfolded.Substring(toAt + 5, toEnd - toAt - 5);
        for (int i = 0; i < many.Length; i++)
            Assert.Contains($"recipient{i:D2}@somewhat-long-domain.example", toValue);
    }

    // ---------------------------------------------------------------------------------------------------
    // Fix 3 (HIGH): non-ASCII attachment filename → ASCII-only Content-Disposition with RFC-2231 filename*.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Nonascii_attachment_filename_is_rfc2231_encoded_and_header_stays_ascii()
    {
        const string original = "résumé—2026.pdf";
        var msg = SampleMessage(SamplePdf()) with
        {
            Attachments = new[] { new EmlAttachment(original, "application/pdf", SamplePdf()) },
        };
        var eml = EmlComposer.Compose(msg);
        string s = AsLatin1(eml);

        // The Content-Disposition line is ASCII-only (no raw >127 bytes leaked).
        int cdAt = s.IndexOf("Content-Disposition:", StringComparison.Ordinal);
        int cdEnd = s.IndexOf("\r\n", cdAt, StringComparison.Ordinal);
        string cdLine = s.Substring(cdAt, cdEnd - cdAt);
        foreach (char c in cdLine) Assert.True(c <= 0x7F, $"non-ASCII char in Content-Disposition: {(int)c:X}");

        // It carries an RFC-2231 filename* that decodes back to the original.
        Assert.Contains("filename*=UTF-8''", cdLine);
        int star = cdLine.IndexOf("filename*=UTF-8''", StringComparison.Ordinal) + "filename*=UTF-8''".Length;
        string enc = cdLine.Substring(star).TrimEnd();
        Assert.Equal(original, DecodeRfc2231(enc));
    }

    // ---------------------------------------------------------------------------------------------------
    // Fix 4 (MED): split an over-long RFC-2047 encoded-word into multiple <=75-char encoded-words.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Long_unicode_subject_splits_into_encoded_words_each_within_75_chars()
    {
        // A long non-ASCII subject that, as a single encoded-word, would blow past the 75-char cap.
        string longSubject = string.Concat(Enumerable.Repeat("Grüße über Rechnung ", 12)).Trim();
        var msg = SampleMessage(SamplePdf()) with { Subject = longSubject };
        var eml = EmlComposer.Compose(msg);
        string s = AsLatin1(eml);

        // Collect every encoded-word token and assert each is <=75 chars.
        var words = System.Text.RegularExpressions.Regex.Matches(s, @"=\?utf-8\?B\?[^?]*\?=");
        Assert.True(words.Count >= 2, "expected the long subject to split into multiple encoded-words");
        foreach (System.Text.RegularExpressions.Match w in words)
            Assert.True(w.Value.Length <= 75, $"encoded-word exceeds 75 chars: {w.Value.Length}");

        // Unfold + decode reconstructs the original subject exactly.
        string unfolded = MiniMime.Unfold(eml);
        int subjAt = unfolded.IndexOf("\r\nSubject:", StringComparison.Ordinal);
        int subjEnd = unfolded.IndexOf("\r\n", subjAt + 2, StringComparison.Ordinal);
        string subjValue = unfolded.Substring(subjAt + "\r\nSubject: ".Length, subjEnd - subjAt - "\r\nSubject: ".Length);
        Assert.Equal(longSubject, MiniMime.DecodeEncodedWords(subjValue));
    }

    // Decode an RFC-2231 / RFC-5987 percent-encoded value (the part after UTF-8'').
    private static string DecodeRfc2231(string enc)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < enc.Length; i++)
        {
            if (enc[i] == '%')
            {
                bytes.Add(Convert.ToByte(enc.Substring(i + 1, 2), 16));
                i += 2;
            }
            else bytes.Add((byte)enc[i]);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static string FindHeader(string s, string name)
    {
        int at = s.IndexOf(name + ": ", StringComparison.Ordinal);
        int end = s.IndexOf("\r\n", at, StringComparison.Ordinal);
        return s.Substring(at + name.Length + 2, end - at - name.Length - 2);
    }
}
