using System;
using System.IO;
using System.Text;
using Apex.Desktop.Services;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first <b>"Share via WhatsApp"</b> panel (census row 14.10), hosted as its own cascading
/// Miller-column to the RIGHT of the report / invoice it shares — never a stacked overlay, mirroring
/// <see cref="EmailComposeViewModel"/>, <see cref="ExportViewModel"/> and <see cref="PrintPreviewViewModel"/>.
///
/// <para>🔴 <b>READ THIS BEFORE CHANGING ANYTHING HERE: THE VENDOR'S FEATURE IS NOT THE FEATURE THIS SHIPS,
/// AND THAT IS DELIBERATE.</b> The reference product's WhatsApp integration is a <b>WhatsApp Business API
/// (WABA)</b> integration mediated by a named commercial Business Solution Provider. Its own help pages state
/// that a WABA is mandatory, that a personal WhatsApp number <i>cannot</i> be used (the number must be
/// deregistered from any personal or business WhatsApp account first), and that without the BSP account
/// sharing is impossible — there is no offline or account-free path at all. We have no WABA, no BSP contract
/// and no user-supplied credentials, and sending would break this application's <b>offline-by-construction</b>
/// posture, which is a settled architectural property rather than an accident (see
/// <c>OfflineJsonConnector</c> on the GST path, and <see cref="EmailComposeViewModel"/>'s own
/// "Offline compose — nothing is sent" contract).</para>
///
/// <para>A "Send via WhatsApp" button that silently did nothing would be a third dead capability beside
/// <c>CostReports.BuildLedgerBreakup</c> and <c>MultiAccountPrintViewModel</c> (census T2-40). So this panel
/// ships the smaller HONEST thing and says so on its own face:</para>
///
/// <para><b>The contract, stated in <see cref="Notice"/> where the operator reads it, not buried here:</b>
/// the document is <b>saved to a file</b> and the operator is handed a prepared <c>wa.me</c> link. Apex
/// Solutions <b>does not send the message and cannot attach the file</b> — WhatsApp's URL scheme carries
/// <b>text only</b>. The message text NAMES the saved file so the operator knows what to attach in WhatsApp
/// themselves.</para>
///
/// <para><b>Save-then-share ordering is enforced, not advisory.</b> <see cref="Share"/> refuses until
/// <see cref="SaveDocument"/> has written the artefact, so the file the message names always exists. Handing
/// out a link that names a file nobody wrote is the same class of dishonesty as the button that does nothing.</para>
///
/// <para>Kept UI-toolkit-free so the whole panel is unit-testable headlessly. The only outward call is through
/// <see cref="Launcher"/>; no socket is opened here and no HTTP client exists in this file.</para>
/// </summary>
public sealed partial class WhatsAppShareViewModel : ViewModelBase
{
    /// <summary>The already-rendered document bytes (the exported report/invoice PDF) that
    /// <see cref="SaveDocument"/> writes. This thin layer never re-computes a figure.</summary>
    private readonly byte[] _document;

    /// <summary>Optional seam so tests can capture the written bytes/path without touching disk.</summary>
    private readonly Action<string, byte[]>? _writeBytes;

    public string Title => "Share via WhatsApp";

    /// <summary>The report/invoice being shared (its heading line), shown at the top of the panel.</summary>
    public string DocumentTitle { get; }

    /// <summary>The default file name the document is saved under (a safe stem + ".pdf").</summary>
    public string SuggestedFileName { get; }

    /// <summary>
    /// The always-visible statement of what this panel does and does NOT do. It is the panel's honesty lock and
    /// a test pins every clause of it: nothing is sent, the file is not attached, the operator attaches it.
    /// </summary>
    public string Notice =>
        "Apex Solutions does not send the message and cannot attach the file — WhatsApp's link carries text only. "
        + "Save the document first, then open the prepared link and attach the saved file in WhatsApp yourself.";

    /// <summary>
    /// The country calling code, digits only, without the "+". Defaults to <b>91</b> (India) because every
    /// statutory surface in this application — GSTIN, PAN, TDS, the state master — is Indian; it is editable
    /// for a foreign party. It is NOT read from the company's state, which carries no dialling information.
    /// </summary>
    [ObservableProperty] private string _countryCode = "91";

    /// <summary>The recipient's mobile number as typed — spaces, "+", hyphens, dots and brackets are tolerated
    /// and stripped by <see cref="NormalisedNumber"/>.</summary>
    [ObservableProperty] private string _phoneNumber = string.Empty;

    /// <summary>The message text. Defaults to a short covering note; <see cref="SaveDocument"/> appends the
    /// saved file's name so the operator can see what they are being asked to attach.</summary>
    [ObservableProperty] private string _message = string.Empty;

    /// <summary>A status line shown after Save / Share (success + path, or the refusal reason).</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>The path the document was written to, or empty until <see cref="SaveDocument"/> succeeds.
    /// <see cref="Share"/> refuses while this is empty.</summary>
    [ObservableProperty] private string _savedFilePath = string.Empty;

    /// <summary>The OS hand-off seam. Defaults to the real shell launcher; a test substitutes a double.</summary>
    public IExternalLauncher Launcher { get; set; } = ShellExternalLauncher.Default;

    /// <summary>Shell ctor over an open report: renders it to a de-branded PDF, exactly as the e-mail panel does
    /// (so the shared file and the e-mailed attachment are byte-identical).</summary>
    public WhatsAppShareViewModel(ReportsViewModel report)
        : this(
            documentTitle: report?.Title ?? string.Empty,
            document: RenderReportPdf(report ?? throw new ArgumentNullException(nameof(report))),
            writeBytes: null)
    { }

    /// <summary>Shell ctor over a drilled voucher / tax invoice: reuses the print-preview projection so the
    /// shared PDF is byte-identical to what Print would produce.</summary>
    public WhatsAppShareViewModel(VoucherDetailViewModel voucher)
        : this(
            documentTitle: voucher?.Title ?? string.Empty,
            document: (voucher ?? throw new ArgumentNullException(nameof(voucher))).BuildPrintPreview().PdfBytes,
            writeBytes: null)
    { }

    /// <summary>Testable ctor: supply the document title, the already-rendered bytes, and an optional write seam.</summary>
    public WhatsAppShareViewModel(string documentTitle, byte[] document, Action<string, byte[]>? writeBytes)
    {
        DocumentTitle = documentTitle ?? string.Empty;
        _document = document ?? Array.Empty<byte>();
        _writeBytes = writeBytes;
        SuggestedFileName = SafeFileName(DocumentTitle) + ".pdf";

        Message = string.IsNullOrWhiteSpace(DocumentTitle)
            ? "Please find the document from Apex Solutions."
            : $"Please find the {DocumentTitle} from Apex Solutions.";
    }

    // ---- the number ----

    /// <summary>
    /// The recipient in E.164 digits (country code + subscriber number), or empty when the input cannot make
    /// one. Spaces, "+", hyphens, dots, and round brackets are stripped — those are how people write a phone
    /// number, not part of it. <b>Any other non-digit rejects the whole number</b> rather than being silently
    /// dropped: quietly deleting a letter out of the middle of a number produces a DIFFERENT, valid-looking
    /// number, and the operator would have no way to see they were about to message a stranger.
    /// </summary>
    public string NormalisedNumber
    {
        get
        {
            var cc = StripFormatting(CountryCode);
            var num = StripFormatting(PhoneNumber);
            if (cc is null || num is null) return string.Empty;
            if (cc.Length == 0 || num.Length == 0) return string.Empty;
            return cc + num;
        }
    }

    /// <summary>Removes the punctuation people write phone numbers with; returns null if anything else remains.</summary>
    private static string? StripFormatting(string? raw)
    {
        if (raw is null) return string.Empty;
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsAsciiDigit(c)) { sb.Append(c); continue; }
            if (c is ' ' or '+' or '-' or '.' or '(' or ')' or '\t') continue;
            return null;    // a letter or symbol — reject the number outright, never "clean" it into another one
        }
        return sb.ToString();
    }

    // ---- the link ----

    /// <summary>
    /// The prepared <c>https://wa.me/&lt;digits&gt;?text=&lt;percent-encoded&gt;</c> link. Empty until there is
    /// a usable number. The text is encoded through <see cref="Mailto.EncodeQueryValue"/> — the same encoder
    /// the mailto path uses, deliberately, so this codebase does not grow a second one.
    /// </summary>
    public string WaMeUri
    {
        get
        {
            var number = NormalisedNumber;
            if (number.Length == 0) return string.Empty;
            var text = MessageWithFileName();
            return text.Length == 0
                ? "https://wa.me/" + number
                : "https://wa.me/" + number + "?text=" + Mailto.EncodeQueryValue(text);
        }
    }

    /// <summary>True once the link can be built AND the artefact has been written — the two conditions
    /// <see cref="Share"/> enforces, exposed so the panel can dim its own button instead of offering a
    /// control that refuses.</summary>
    public bool CanShare => WaMeUri.Length > 0 && SavedFilePath.Length > 0;

    /// <summary>The message text plus, once the document has been saved, the file name to attach.</summary>
    private string MessageWithFileName()
    {
        var body = Message ?? string.Empty;
        if (SavedFilePath.Length == 0) return body;
        var name = Path.GetFileName(SavedFilePath);
        if (name.Length == 0) return body;
        return body.Length == 0
            ? $"Attaching: {name}"
            : $"{body}\n\nAttaching: {name}";
    }

    partial void OnCountryCodeChanged(string value) => NotifyLink();
    partial void OnPhoneNumberChanged(string value) => NotifyLink();
    partial void OnMessageChanged(string value) => NotifyLink();
    partial void OnSavedFilePathChanged(string value) => NotifyLink();

    private void NotifyLink()
    {
        OnPropertyChanged(nameof(NormalisedNumber));
        OnPropertyChanged(nameof(WaMeUri));
        OnPropertyChanged(nameof(CanShare));
    }

    // ---- step 1: write the artefact ----

    /// <summary>
    /// Writes the rendered document to <paramref name="path"/>. This is step ONE of the two-step share, and
    /// <see cref="Share"/> refuses until it has succeeded — the message names a file, so the file must exist.
    /// Returns success and sets <see cref="Status"/> either way. Nothing is sent.
    /// </summary>
    public bool SaveDocument(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "Choose a file path to save the document.";
            return false;
        }

        try
        {
            if (_writeBytes is not null) _writeBytes(path, _document);
            else File.WriteAllBytes(path, _document);

            SavedFilePath = path;
            Status = $"Saved the document ({_document.Length:#,0} bytes) to {path}. "
                   + "Nothing was sent — open the link and attach this file in WhatsApp.";
            return true;
        }
        catch (Exception ex)
        {
            SavedFilePath = string.Empty;
            Status = "Could not save the document: " + ex.Message;
            return false;
        }
    }

    // ---- step 2: hand the prepared link to the OS ----

    /// <summary>
    /// Opens the prepared <c>wa.me</c> link through <see cref="Launcher"/>. Refuses — with a reason on
    /// <see cref="Status"/> — when there is no usable number, or when the document has not been saved yet.
    /// <b>This sends nothing.</b> It opens WhatsApp with the message text pre-filled; the operator attaches
    /// the saved file and presses send in WhatsApp themselves.
    /// </summary>
    public bool Share()
    {
        var uri = WaMeUri;
        if (uri.Length == 0)
        {
            Status = "Enter the recipient's country code and mobile number (digits only).";
            return false;
        }

        if (SavedFilePath.Length == 0)
        {
            Status = "Save the document first — the message names the file you are asked to attach.";
            return false;
        }

        if (!Launcher.Open(uri))
        {
            Status = "Could not open WhatsApp. The saved document is at " + SavedFilePath + ".";
            return false;
        }

        Status = "Opened WhatsApp with the message prepared. Nothing was sent, and the file is NOT attached — "
               + "attach " + Path.GetFileName(SavedFilePath) + " in WhatsApp before sending.";
        return true;
    }

    // ---- helpers (identical conventions to EmailComposeViewModel) ----

    private static byte[] RenderReportPdf(ReportsViewModel report) =>
        ReportPdf.Render(Apex.Desktop.Services.ReportPrintProjector.Project(report), new PageConfig
        {
            FooterText = "Apex Solutions  -  Page {page} of {pages}",
        });

    /// <summary>Turns a document title into a safe file-name stem (invalid path chars → '_'; blank → "Document").</summary>
    private static string SafeFileName(string? title)
    {
        var stem = string.IsNullOrWhiteSpace(title) ? "Document" : title.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');
        return stem;
    }
}
