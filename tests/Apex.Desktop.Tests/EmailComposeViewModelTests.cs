using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-12 (RQ-25/26 e-mail compose + hand-off): the M / Ctrl+M E-Mail action
/// composes a byte-stable <c>.eml</c> (via <c>Apex.Ledger.Io</c>'s <see cref="EmlComposer"/>) whose attachment
/// is the CURRENT report's exported PDF, and builds a correct <c>mailto:</c> for a quick attachment-less compose.
///
/// <para>The composer / mailto builder themselves are trusted (covered by <c>Apex.Ledger.Io.Tests</c>); these
/// tests pin the thin Avalonia layer: the shell opens a compose column, the composed <c>.eml</c> parses as a
/// valid MIME message carrying To/Subject, its attachment decodes to the EXACT report PDF bytes, the hand-off is
/// offline (nothing is sent — a Notice says so, no socket path exists), the mailto URI is correct + percent-
/// encoded, and nothing carries "tally" (RQ-13). A write seam captures the bytes so the projection is asserted
/// without touching disk; one test also writes a real temp file.</para>
/// </summary>
public sealed class EmailComposeViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;
    private static readonly DateTime FixedNow = new(2026, 7, 6, 12, 0, 0, DateTimeKind.Utc);

    public EmailComposeViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexEmailTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel ShellWithReport(ReportKind kind)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.OpenReport(kind);
        return vm;
    }

    /// <summary>A compose VM over the shell's open report with a captured-bytes write seam (no disk).</summary>
    private static EmailComposeViewModel Capture(MainWindowViewModel shell, out Captured cap)
    {
        // Render the report PDF the same way the shell ctor does, then wrap it in a testable compose VM.
        var reportPdf = ReportPdf.Render(ReportPrintProjector.Project(shell.Reports!), new PageConfig
        {
            FooterText = "Apex Solutions  -  Page {page} of {pages}",
        });
        var attachment = new EmlAttachment(shell.Reports!.Title + ".pdf", "application/pdf", reportPdf);

        var captured = new Captured { ReportPdf = reportPdf };
        var vm = new EmailComposeViewModel(
            documentTitle: shell.Reports!.Title,
            attachment: attachment,
            now: FixedNow,
            writeBytes: (path, bytes) => { captured.Path = path; captured.Bytes = bytes; })
        {
            To = "buyer@example.com",
        };
        cap = captured;
        return vm;
    }

    private sealed class Captured
    {
        public string? Path;
        public byte[] Bytes = Array.Empty<byte>();
        public byte[] ReportPdf = Array.Empty<byte>();
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ---------------------------------------------------------------- .eml validity + attachment fidelity

    [Fact]
    public void SaveEml_composes_a_valid_message_with_to_and_subject()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out var cap);
        vm.Subject = "Trial Balance for July";

        Assert.True(vm.SaveEml("C:\\Out\\tb.eml"));
        Assert.EndsWith(".eml", cap.Path);

        var msg = MiniEml.Parse(cap.Bytes);
        Assert.Equal("buyer@example.com", msg.Header("To"));
        Assert.Equal("Trial Balance for July", msg.Header("Subject"));
        Assert.Equal("1.0", msg.Header("MIME-Version"));
        Assert.StartsWith("multipart/mixed", msg.Header("Content-Type"));
        Assert.NotEmpty(msg.Header("Date"));
        Assert.StartsWith("<", msg.Header("Message-ID"));
    }

    [Fact]
    public void Attachment_decodes_to_the_exact_report_pdf_bytes()
    {
        var shell = ShellWithReport(ReportKind.BalanceSheet);
        var vm = Capture(shell, out var cap);
        Assert.True(vm.SaveEml("C:\\Out\\bs.eml"));

        var msg = MiniEml.Parse(cap.Bytes);
        var pdfPart = msg.Parts.Single(p => p.ContentType.StartsWith("application/pdf"));

        // The attachment part decodes byte-for-byte to the exported report PDF.
        Assert.Equal(cap.ReportPdf, pdfPart.DecodedContent);
        Assert.StartsWith("%PDF-", AsLatin1(pdfPart.DecodedContent));
        Assert.Contains("attachment", pdfPart.Disposition);
        Assert.Contains(".pdf", pdfPart.Disposition);
    }

    [Fact]
    public void Compose_is_byte_stable_for_the_same_inputs()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);

        var a = Capture(shell, out var capA);
        a.Subject = "Same";
        Assert.True(a.SaveEml("C:\\Out\\a.eml"));

        var b = Capture(shell, out var capB);
        b.Subject = "Same";
        Assert.True(b.SaveEml("C:\\Out\\b.eml"));

        Assert.Equal(capA.Bytes, capB.Bytes);   // deterministic: no clock, no RNG cross into the composer
    }

    [Fact]
    public void No_tally_anywhere_in_the_eml()
    {
        var shell = ShellWithReport(ReportKind.ProfitAndLoss);
        var vm = Capture(shell, out var cap);
        Assert.True(vm.SaveEml("C:\\Out\\pl.eml"));
        Assert.DoesNotContain("tally", AsLatin1(cap.Bytes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveEml_without_a_recipient_is_rejected()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out _);
        vm.To = "   ";
        Assert.False(vm.SaveEml("C:\\Out\\x.eml"));
        Assert.Contains("recipient", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Panel_makes_clear_nothing_is_sent()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out var cap);
        Assert.Contains("nothing is sent", vm.Notice, StringComparison.OrdinalIgnoreCase);

        Assert.True(vm.SaveEml("C:\\Out\\tb.eml"));
        Assert.Contains("Nothing was sent", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- mailto: URI

    [Fact]
    public void MailtoUri_is_correct_and_percent_encoded()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out _);
        vm.To = "buyer@example.com";
        vm.Cc = "cc@example.com";
        vm.Subject = "Q1 & Q2";
        vm.Body = "See attached.";

        var uri = vm.MailtoUri;
        Assert.StartsWith("mailto:buyer@example.com", uri);   // '@' kept literal in the path
        Assert.Contains("cc=cc%40example.com", uri);          // '@' encoded in the query field
        Assert.Contains("subject=Q1%20%26%20Q2", uri);        // space + ampersand percent-encoded
        Assert.Contains("body=See%20attached.", uri);
    }

    [Fact]
    public void MailtoUri_is_empty_without_a_recipient()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var vm = Capture(shell, out _);
        vm.To = string.Empty;
        Assert.Equal(string.Empty, vm.MailtoUri);
    }

    // ---------------------------------------------------------------- shell wiring + real-disk write

    [Fact]
    public void OpenEmailCompose_over_a_report_opens_a_column_and_does_not_stack()
    {
        var empty = new MainWindowViewModel(_storage);
        empty.OpenEmailCompose();                             // no report/invoice open
        Assert.Null(empty.EmailCompose);

        var shell = ShellWithReport(ReportKind.DayBook);
        shell.OpenEmailCompose();
        var first = shell.EmailCompose;
        Assert.NotNull(first);
        Assert.Equal(Screen.EmailCompose, shell.CurrentScreen);
        Assert.True(first!.HasAttachment);
        shell.OpenEmailCompose();                             // re-press: must not stack a second panel
        Assert.Same(first, shell.EmailCompose);
    }

    [Fact]
    public void SaveEml_writes_a_real_file_to_disk()
    {
        var shell = ShellWithReport(ReportKind.TrialBalance);
        var reportPdf = ReportPdf.Render(ReportPrintProjector.Project(shell.Reports!), new PageConfig());
        var attachment = new EmlAttachment("tb.pdf", "application/pdf", reportPdf);
        var vm = new EmailComposeViewModel("Trial Balance", attachment, FixedNow, writeBytes: null)
        {
            To = "buyer@example.com",
        };

        var path = Path.Combine(_tempDir, "tb.eml");
        Assert.True(vm.SaveEml(path));
        Assert.True(File.Exists(path));

        var onDisk = File.ReadAllBytes(path);
        var msg = MiniEml.Parse(onDisk);
        Assert.Equal(reportPdf, msg.Parts.Single(p => p.ContentType.StartsWith("application/pdf")).DecodedContent);
        Assert.DoesNotContain("tally", AsLatin1(onDisk), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}

/// <summary>A minimal test-only parser for the <c>.eml</c> the composer produces: top-level headers, the
/// multipart boundary, and each part's headers + base64-decoded content. Exercises the REAL produced bytes.</summary>
internal sealed class MiniEml
{
    private readonly Dictionary<string, string> _headers;
    public IReadOnlyList<MiniPart> Parts { get; }

    private MiniEml(Dictionary<string, string> headers, IReadOnlyList<MiniPart> parts)
    {
        _headers = headers;
        Parts = parts;
    }

    public string Header(string name) => _headers.TryGetValue(name.ToLowerInvariant(), out var v) ? v : string.Empty;

    public static MiniEml Parse(byte[] bytes)
    {
        string text = Encoding.Latin1.GetString(bytes);
        var (headers, body) = SplitHeaders(text);

        var ct = headers.TryGetValue("content-type", out var c) ? c : string.Empty;
        string boundary = ExtractBoundary(ct);

        var parts = new List<MiniPart>();
        if (boundary.Length > 0)
        {
            var chunks = body.Split(new[] { "--" + boundary }, StringSplitOptions.None);
            foreach (var raw in chunks)
            {
                var chunk = raw.TrimStart('\r', '\n');
                if (chunk.Length == 0 || chunk.StartsWith("--")) continue;     // preamble or closing delimiter
                if (!chunk.Contains("\r\n\r\n")) continue;
                var (ph, pbody) = SplitHeaders(chunk);
                if (ph.Count == 0) continue;
                parts.Add(new MiniPart(ph, pbody));
            }
        }
        return new MiniEml(headers, parts);
    }

    private static (Dictionary<string, string>, string) SplitHeaders(string text)
    {
        int sep = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        string headerBlock = sep >= 0 ? text[..sep] : text;
        string body = sep >= 0 ? text[(sep + 4)..] : string.Empty;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in headerBlock.Split("\r\n"))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }
        return (headers, body);
    }

    private static string ExtractBoundary(string contentType)
    {
        const string key = "boundary=";
        int i = contentType.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return string.Empty;
        var v = contentType[(i + key.Length)..].Trim();
        if (v.StartsWith('"'))
        {
            int end = v.IndexOf('"', 1);
            return end > 0 ? v[1..end] : v.Trim('"');
        }
        int semi = v.IndexOf(';');
        return semi >= 0 ? v[..semi] : v;
    }
}

internal sealed class MiniPart
{
    private readonly Dictionary<string, string> _headers;
    private readonly string _body;

    public MiniPart(Dictionary<string, string> headers, string body)
    {
        _headers = headers;
        _body = body;
    }

    public string ContentType => _headers.TryGetValue("content-type", out var v) ? v : string.Empty;
    public string Disposition => _headers.TryGetValue("content-disposition", out var v) ? v : string.Empty;

    public byte[] DecodedContent
    {
        get
        {
            var b64 = _body.Replace("\r", "").Replace("\n", "").Trim();
            return Convert.FromBase64String(b64);
        }
    }
}
