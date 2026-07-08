using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first "E-Mail" compose panel (RQ-25/26), hosted as its own cascading Miller-column to the
/// RIGHT of the report / invoice it e-mails — never a stacked overlay, mirroring <see cref="ExportViewModel"/>
/// and <see cref="PrintPreviewViewModel"/>. It captures <see cref="To"/> / <see cref="Cc"/> /
/// <see cref="Subject"/> / <see cref="Body"/>; the attachment defaults to the exported PDF of the current
/// report/invoice (supplied to the ctor as already-rendered bytes + mime + filename — this thin layer never
/// re-computes figures).
///
/// <para><b>Offline hand-off only (RQ-26).</b> On <see cref="SaveEml"/> it composes a byte-stable
/// RFC-5322 / MIME multipart/mixed <c>.eml</c> via <see cref="EmlComposer"/> (the IO layer) and writes it to a
/// chosen path; on <see cref="MailtoUri"/> it builds an attachment-less <c>mailto:</c> the OS mail client can
/// open. <b>Nothing is sent</b> — live SMTP transport is DEFERRED; no code path here opens a socket. The panel
/// makes this explicit via <see cref="Notice"/>.</para>
///
/// <para><b>No clock / no RNG in IO (ER-8/12).</b> The IO composer has no clock, so this thin layer formats the
/// <c>Date</c> header <i>value</i> from the injected "now" and derives a <b>deterministic</b> <c>Message-ID</c>
/// (from the document identity + the same timestamp) — never <c>Guid.NewGuid</c>. All the email logic lives in
/// <c>Apex.Ledger.Io</c>; this VM only gathers recipients, calls the composer, and writes / hands off the bytes.
/// Output is de-branded — never a third-party brand.</para>
/// </summary>
public sealed partial class EmailComposeViewModel : ViewModelBase
{
    /// <summary>The sender identity the composed message is From (host default; a later phase reads the SMTP
    /// profile). Kept ASCII addr-spec so no header needs encoding.</summary>
    private readonly EmlAddress _from;

    /// <summary>The already-rendered attachment (the exported report/invoice PDF, or the chosen export format).
    /// Null ⇒ an attachment-less compose (mailto only). The bytes are carried verbatim into the .eml.</summary>
    private readonly EmlAttachment? _attachment;

    /// <summary>The "now" the Date header + the deterministic Message-ID are derived from — injected so the VM
    /// stays deterministic in tests (the IO layer itself has no clock; the UI passes the value in).</summary>
    private readonly DateTime _now;

    /// <summary>A stable seed (the document identity, e.g. the report title / invoice number) folded into the
    /// deterministic Message-ID so the same document + same timestamp re-composes byte-identical (no RNG).</summary>
    private readonly string _messageIdSeed;

    /// <summary>Optional seam so tests can capture the written bytes/path without touching disk. Null ⇒ write
    /// to the real filesystem via <see cref="File.WriteAllBytes(string, byte[])"/>.</summary>
    private readonly Action<string, byte[]>? _writeBytes;

    public string Title => "E-Mail";

    /// <summary>The report/invoice being e-mailed (its heading line), shown at the top of the panel.</summary>
    public string DocumentTitle { get; }

    /// <summary>A one-line, always-visible notice that this is an OFFLINE compose — nothing is sent (RQ-26).</summary>
    public string Notice =>
        "Offline compose — nothing is sent. Save an .eml (with the attachment) or open your mail client (mailto, no attachment).";

    /// <summary>Recipient address(es), comma/semicolon separated. At least one is needed to save/hand off.</summary>
    [ObservableProperty] private string _to = string.Empty;

    /// <summary>Optional carbon-copy address(es), comma/semicolon separated.</summary>
    [ObservableProperty] private string _cc = string.Empty;

    /// <summary>The subject line (defaults to the document title).</summary>
    [ObservableProperty] private string _subject = string.Empty;

    /// <summary>The plain-text body (defaults to a short covering note).</summary>
    [ObservableProperty] private string _body = string.Empty;

    /// <summary>A status line shown after Save / hand-off (success + path, or the failure reason).</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>The attachment file name shown in the panel (blank when there is no attachment).</summary>
    public string AttachmentName => _attachment?.FileName ?? string.Empty;

    /// <summary>True when an attachment (the exported PDF) travels with the .eml.</summary>
    public bool HasAttachment => _attachment is not null;

    /// <summary>
    /// Shell ctor over an open report: renders the report to a de-branded PDF (via <see cref="ReportPrintProjector"/>
    /// + <see cref="ReportPdf"/>) and attaches it. The Date + deterministic Message-ID come from <see cref="DateTime.Now"/>.
    /// </summary>
    public EmailComposeViewModel(ReportsViewModel report)
        : this(
            documentTitle: report?.Title ?? string.Empty,
            attachment: RenderReportPdf(report ?? throw new ArgumentNullException(nameof(report))),
            now: DateTime.Now,
            writeBytes: null)
    { }

    /// <summary>
    /// Shell ctor over a drilled voucher / tax invoice: renders it (plain voucher or GST tax invoice) to a
    /// de-branded PDF via the print-preview projection and attaches it.
    /// </summary>
    public EmailComposeViewModel(VoucherDetailViewModel voucher)
        : this(
            documentTitle: voucher?.Title ?? string.Empty,
            attachment: RenderVoucherPdf(voucher ?? throw new ArgumentNullException(nameof(voucher))),
            now: DateTime.Now,
            writeBytes: null)
    { }

    /// <summary>
    /// Testable ctor: supply the document title, the already-rendered attachment (or null for an
    /// attachment-less compose), the "now" the Date/Message-ID derive from, and an optional write seam.
    /// </summary>
    public EmailComposeViewModel(
        string documentTitle,
        EmlAttachment? attachment,
        DateTime now,
        Action<string, byte[]>? writeBytes,
        EmlAddress? from = null)
    {
        DocumentTitle = documentTitle ?? string.Empty;
        _attachment = attachment;
        _now = now;
        _writeBytes = writeBytes;
        _from = from ?? new EmlAddress("no-reply@apexsolutions.example", "Apex Solutions");
        _messageIdSeed = string.IsNullOrWhiteSpace(DocumentTitle) ? "document" : DocumentTitle;

        Subject = DocumentTitle;
        Body = string.IsNullOrWhiteSpace(DocumentTitle)
            ? "Please find the attached document."
            : $"Please find the attached {DocumentTitle}.";
    }

    // ---- IO projections (render the attachment PDF; all IO stays in Apex.Ledger.Io) ----

    private static EmlAttachment RenderReportPdf(ReportsViewModel report)
    {
        var pdf = ReportPdf.Render(ReportPrintProjector.Project(report), new PageConfig
        {
            FooterText = "Apex Solutions  -  Page {page} of {pages}",
        });
        return new EmlAttachment(SafeFileName(report.Title) + ".pdf", "application/pdf", pdf);
    }

    private static EmlAttachment RenderVoucherPdf(VoucherDetailViewModel voucher)
    {
        // Reuse the print-preview projection so the e-mailed PDF is byte-identical to what Print would produce.
        var preview = voucher.BuildPrintPreview();
        return new EmlAttachment(SafeFileName(voucher.Title) + ".pdf", "application/pdf", preview.PdfBytes);
    }

    // ---- offline hand-off #1: write a byte-stable .eml (carries the attachment) ----

    /// <summary>
    /// Composes the <c>.eml</c> for the current fields (via <see cref="EmlComposer"/>, passing the formatted
    /// Date header value + a deterministic Message-ID) and writes it to <paramref name="path"/> (chosen by the
    /// Avalonia layer). Returns true on success and sets a status line either way. Nothing is sent.
    /// </summary>
    public bool SaveEml(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Choose a file path to save the .eml.";
            return false;
        }

        var recipients = ParseAddresses(To);
        if (recipients.Count == 0)
        {
            Status = "Enter at least one recipient (To).";
            return false;
        }

        try
        {
            byte[] bytes = EmlComposer.Compose(BuildMessage(recipients));
            if (_writeBytes is not null) _writeBytes(path, bytes);
            else File.WriteAllBytes(path, bytes);

            Status = $"Saved e-mail draft ({bytes.Length:#,0} bytes) to {path}. Nothing was sent.";
            return true;
        }
        catch (Exception ex)
        {
            Status = "Could not save the e-mail: " + ex.Message;
            return false;
        }
    }

    // ---- offline hand-off #2: a quick, attachment-less mailto: for the OS mail client ----

    /// <summary>
    /// The <c>mailto:</c> URI for a quick compose in the OS default mail client (RFC-6068, percent-encoded via
    /// <see cref="Mailto"/>). Attachments cannot ride a mailto — for the PDF the user saves an <c>.eml</c>
    /// instead. Empty when there is no recipient yet.
    /// </summary>
    public string MailtoUri
    {
        get
        {
            var to = ParseAddresses(To);
            if (to.Count == 0) return string.Empty;
            var cc = ParseAddresses(Cc);
            return Mailto.Build(
                to,
                cc.Count > 0 ? cc : null,
                string.IsNullOrWhiteSpace(Subject) ? null : Subject,
                string.IsNullOrWhiteSpace(Body) ? null : Body);
        }
    }

    partial void OnToChanged(string value) => OnPropertyChanged(nameof(MailtoUri));
    partial void OnCcChanged(string value) => OnPropertyChanged(nameof(MailtoUri));
    partial void OnSubjectChanged(string value) => OnPropertyChanged(nameof(MailtoUri));
    partial void OnBodyChanged(string value) => OnPropertyChanged(nameof(MailtoUri));

    /// <summary>Builds the <see cref="EmlMessage"/> the composer turns into bytes. The Date VALUE is formatted
    /// here from the injected "now" (RFC 5322), and the Message-ID is deterministic (document seed + timestamp)
    /// so the same compose re-produces identical bytes — no clock and no RNG cross into the IO layer.</summary>
    public EmlMessage BuildMessage() => BuildMessage(ParseAddresses(To));

    private EmlMessage BuildMessage(IReadOnlyList<string> recipients) => new()
    {
        From = _from,
        To = recipients.Select(a => new EmlAddress(a)).ToArray(),
        Cc = ParseAddresses(Cc).Select(a => new EmlAddress(a)).ToArray(),
        Subject = Subject ?? string.Empty,
        Body = Body ?? string.Empty,
        Date = FormatDateHeader(_now),
        MessageId = BuildMessageId(),
        Attachments = _attachment is null
            ? Array.Empty<EmlAttachment>()
            : new[] { _attachment },
    };

    /// <summary>RFC-5322 Date header value, formatted from the injected "now" (the IO layer has no clock).
    /// The zone offset is emitted as <c>+HHmm</c> (RFC 5322 §3.3), e.g. "Mon, 06 Jul 2026 12:00:00 +0530".</summary>
    private static string FormatDateHeader(DateTime now)
    {
        var local = now.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(now, TimeSpan.Zero)
            : new DateTimeOffset(now);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var offset = local.Offset;
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        // "+HHmm" — no colon, per RFC 5322.
        string zone = $"{sign}{abs.Hours:D2}{abs.Minutes:D2}";
        return local.ToString("ddd, dd MMM yyyy HH:mm:ss ", inv) + zone;
    }

    /// <summary>A deterministic Message-ID (no RNG): a stable hash of the document seed + the timestamp, in the
    /// Apex domain, wrapped in angle brackets as RFC 5322 requires.</summary>
    private string BuildMessageId()
    {
        // Stable, order-independent identity: seed + the exact "now" ticks. No Guid.NewGuid / Random.
        long stamp = _now.Ticks;
        uint hash = Fnv1a($"{_messageIdSeed}|{stamp}");
        return $"<{stamp:x}.{hash:x8}@apexsolutions.example>";
    }

    // FNV-1a 32-bit — a tiny deterministic string hash (no RNG, stable across runs/platforms).
    private static uint Fnv1a(string s)
    {
        uint hash = 2166136261u;
        foreach (char c in s)
        {
            hash ^= (byte)(c & 0xFF);
            hash *= 16777619u;
            hash ^= (byte)((c >> 8) & 0xFF);
            hash *= 16777619u;
        }
        return hash;
    }

    // ---- helpers ----

    /// <summary>Splits a To/Cc field on comma/semicolon, trims, and drops blanks.</summary>
    private static IReadOnlyList<string> ParseAddresses(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return Array.Empty<string>();
        return field
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .ToArray();
    }

    /// <summary>Turns a document title into a safe file-name stem (invalid path chars → '_'; blank → "Document").</summary>
    private static string SafeFileName(string? title)
    {
        var stem = string.IsNullOrWhiteSpace(title) ? "Document" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }
}
