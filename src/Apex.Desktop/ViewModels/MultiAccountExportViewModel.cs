using System;
using System.Collections.Generic;
using System.IO;
using Apex.Ledger.Io;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One document of a multi-account EXPORT job, paired with the account it is addressed to.
///
/// <para>🔴 <b>THE PAIRING IS THE SAFETY PROPERTY, NOT A CONVENIENCE.</b> A reminder letter and a confirmation
/// of accounts are documents an operator sends to a THIRD PARTY, so the one thing this job must never do is put
/// account A's figures in the file named for account B. The pair is built one account at a time (see
/// <see cref="MultiAccountPrintViewModel.BuildExportJob"/>) rather than by zipping a selection list against a
/// projected document list, because those two lists can differ in length — <c>MultiAccountPrintProjector.Project</c>
/// silently SKIPS an id it cannot find and an account the kind may not produce — and a zip of two lists of
/// different lengths mis-addresses every document after the skip. Pinned by
/// <c>MultiAccountExportTests.Each_exported_file_carries_its_OWN_party_and_no_other_partys_figures</c>.</para>
/// </summary>
public sealed class MultiAccountExportDocument
{
    /// <summary>The account this document is addressed to — the file-name stem comes from it.</summary>
    public string AccountName { get; }

    /// <summary>The already-projected document. Rendered as-is; nothing is recomputed at export time.</summary>
    public PrintReport Report { get; }

    public MultiAccountExportDocument(string accountName, PrintReport report)
    {
        AccountName = accountName ?? string.Empty;
        Report = report ?? throw new ArgumentNullException(nameof(report));
    }
}

/// <summary>
/// The keyboard-first <b>Export</b> panel for a <b>multi-account</b> job (census row 12.7's export arm), hosted
/// as its own cascading Miller-column to the right of the Multi-Account Printing panel — never a stacked
/// overlay, exactly as <see cref="ExportViewModel"/> is hosted beside a report.
///
/// <para><b>GROUNDING, OPENED BY CONTENT 2026-10-23.</b>
/// <c>help.tallysolutions.com/printing-multi-account-reports</c> ("Configure for Print, Export, E-mail &amp;
/// WhatsApp"), under its <i>Configure multi-account reports</i> heading, states the export route verbatim:
/// <i>"For Export: <b>Alt+E</b> (Export) &gt; <b>Others</b> &gt; select a report from the List of Reports."</i>
/// The same page opens with <i>"Before printing, exporting, or sharing documents, like your invoices, financial
/// reports, payment reminder letters,"</i> — so that a payment reminder letter is a document the reference
/// product EXPORTS is attested on the page, not inferred from the print route. <b>That one sentence is the whole of
/// the fidelity claim this panel makes.</b> Everything below it — the file-per-account shape, the stem, the
/// PDF-only format — is OURS and is recorded as such in <c>docs/invented-vs-cloned.md</c> (IV-162, IV-163).</para>
///
/// <para>🔴 <b>WHY ONE FILE PER ACCOUNT AND NOT ONE FILE PER JOB.</b> The PRINT arm renders the whole selection
/// into a single collated PDF, and that is right for print: an operator takes the stack off the tray and posts
/// one sheet to each counterparty. An EXPORTED file is not a stack — it is a thing that gets attached to a
/// mail. A single file holding every selected party's balances, named "Reminder Letters", is one careless
/// attachment away from telling Acme what Bharat owes us; this project has already SHARED A WHOLE REGISTER TO A
/// THIRD PARTY once when one voucher was meant. So the export job writes <b>one file per account</b>, each
/// containing exactly that account's document. <b>The vendor page does not state a file shape, so this is not
/// offered as fidelity</b> — it is a divergence chosen on the safety argument above and labelled as ours.</para>
///
/// <para><b>No clock (ER-8/ER-12).</b> The timestamp VALUE is formatted here from an injected "now" and the
/// writers stay deterministic; the <see cref="PrintReport"/>s themselves are projected by the panel before this
/// one is built, so nothing is recomputed while the operator is choosing a folder.</para>
/// </summary>
public sealed partial class MultiAccountExportViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<MultiAccountExportDocument>> _projectDocuments;
    private readonly DateTime _now;
    private readonly Action<string, byte[]>? _writeBytes;

    public string Title => "Export";

    /// <summary>The job being exported — "Reminder Letters", "Confirmation of Accounts", "Ledger Accounts".</summary>
    public string JobTitle { get; }

    /// <summary>The target folder (defaults to the user's Documents, as every other export panel does).</summary>
    [ObservableProperty] private string _folder = string.Empty;

    /// <summary>Append the current timestamp to each file name (<c>Name_yyyyMMdd-HHmm.pdf</c>). Off by default.</summary>
    [ObservableProperty] private bool _appendTimestamp;

    /// <summary>A status line shown after Apply (how many files went where, or the reason none did).</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>
    /// 🔴 THE HONEST DISCLOSURE, shown on the panel. The vendor's export screen offers a File Format list
    /// (<c>help.tallysolutions.com/export-data-in-tally/</c>); this job offers <b>PDF only</b>, and the reason is
    /// structural rather than a shortcut: every other format in this application serialises a
    /// <see cref="TabularExport"/>, and these three documents are not tables. A reminder letter is a prose
    /// addressee line, a bill grid, a sentence asking for settlement and a signatory line; there is no
    /// <c>PrintReport</c>-to-<c>TabularExport</c> projection in this build and inventing one would flatten the
    /// prose rows away, so a CSV "reminder letter" would be a bill list that no longer says anything. A format
    /// row that produced that would hide the gap; this sentence shows it.
    /// </summary>
    public const string FormatDisclosure = "PDF only - a letter is not a table, so no CSV/XLSX for this job";

    /// <summary>
    /// One line per file <see cref="Apply"/> will write, in the order it will write them — bound by the panel's
    /// list so the operator can read every party he is about to write out BEFORE anything lands on disk.
    ///
    /// <para>🔴 <b>A PROPERTY, NOT A METHOD, AND THAT IS LOAD-BEARING RATHER THAN STYLE.</b> This is bound from
    /// XAML (<c>ItemsSource="{Binding PlannedFileNames}"</c>); Avalonia's reflection binding resolves a PROPERTY
    /// and silently binds NOTHING to a method of the same name, so as a method it would have left the list
    /// permanently empty with no error anywhere — an operator shown an empty file list would reasonably conclude
    /// the job was empty and press Export anyway. The same names are produced by the same
    /// <see cref="FileNameFor"/> the write loop calls, so the preview cannot disagree with what is written.</para>
    /// </summary>
    public IReadOnlyList<string> PlannedFileNames
    {
        get
        {
            var names = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in _projectDocuments()) names.Add(FileNameFor(doc, used));
            return names;
        }
    }

    /// <summary>The timestamp toggle is part of every file NAME, so the preview list has to be re-read when it
    /// moves. Without this the panel would promise "Reminder Letter - Acme.pdf" and write
    /// "Reminder Letter - Acme_20261023-1430.pdf".</summary>
    partial void OnAppendTimestampChanged(bool value) => OnPropertyChanged(nameof(PlannedFileNames));

    public MultiAccountExportViewModel(
        string jobTitle,
        Func<IReadOnlyList<MultiAccountExportDocument>> projectDocuments,
        string folder,
        DateTime now,
        Action<string, byte[]>? writeBytes)
    {
        JobTitle = jobTitle ?? string.Empty;
        _projectDocuments = projectDocuments ?? throw new ArgumentNullException(nameof(projectDocuments));
        _now = now;
        _writeBytes = writeBytes;
        Folder = folder ?? string.Empty;
    }

    /// <summary>
    /// Writes one PDF per selected account into <see cref="Folder"/> and returns true when every file was
    /// written. Each file is rendered by <see cref="ReportPdf.Render(PrintReport, PageConfig)"/> — the SAME
    /// renderer and the same <see cref="PageConfig"/> the print arm uses — so an exported reminder letter and a
    /// printed one are the same document, for the reason <see cref="ExportViewModel.TabularToPrint"/> already
    /// records about reports: giving the export path its own layout is the cheaper change and lets the two
    /// drift apart silently.
    ///
    /// <para>An empty selection writes NOTHING and says so. A job of nothing is a mistake to report, not a set
    /// of blank sheets to leave in the operator's Documents folder.</para>
    /// </summary>
    public bool Apply()
    {
        var documents = _projectDocuments();
        if (documents.Count == 0)
        {
            Status = "Nothing is selected, so there is nothing to export.";
            return false;
        }

        // 🔴 A BLANK FOLDER IS REFUSED RATHER THAN RESOLVED, and this is the one place in the export surface
        // where that matters most. Path.Combine("", name) collapses to a BARE FILE NAME, so the files land in
        // the process working directory — findable by nobody, and here that means a pile of letters naming
        // counterparties sitting somewhere the operator will not think to clear. The shell always seeds Folder
        // from ExportFolderDefault.Resolve() (the W2-03 one-seam fix), but the panel's folder is an editable
        // TextBox and an operator can empty it, which no seam upstream can prevent.
        if (string.IsNullOrWhiteSpace(Folder))
        {
            Status = "Choose a folder to export into.";
            return false;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0;
        long bytesWritten = 0;
        try
        {
            foreach (var doc in documents)
            {
                var name = FileNameFor(doc, used);
                var path = Path.Combine(Folder ?? string.Empty, name);
                var bytes = ReportPdf.Render(doc.Report, new PageConfig
                {
                    FooterText = "Apex Solutions  -  Page {page} of {pages}",
                });

                if (_writeBytes is not null) _writeBytes(path, bytes);
                else File.WriteAllBytes(path, bytes);

                written++;
                bytesWritten += bytes.Length;
            }
        }
        catch (Exception ex)
        {
            // Say how far it got. "Could not export" alone would leave an operator unsure whether the first
            // nine letters are sitting in the folder already.
            Status = written == 0
                ? "Could not export: " + ex.Message
                : $"Exported {written:#,0} of {documents.Count:#,0} files, then could not continue: " + ex.Message;
            return false;
        }

        Status = written == 1
            ? $"Exported 1 file ({bytesWritten:#,0} bytes) to {Folder}"
            : $"Exported {written:#,0} files ({bytesWritten:#,0} bytes) to {Folder}";
        return true;
    }

    /// <summary>
    /// The file name for one document: <c>&lt;document title&gt; - &lt;account&gt;[_timestamp].pdf</c>, made
    /// safe for the filesystem and made UNIQUE within the job.
    ///
    /// <para>🔴 <b>The uniqueness pass is not decoration.</b> <see cref="SafeStem"/> maps every invalid path
    /// character to '_', so two different accounts can collide on one stem — "A/c Ltd" and "A_c Ltd" both land
    /// on "A_c Ltd" — and the second <c>WriteAllBytes</c> would then OVERWRITE the first party's letter, leaving
    /// the operator with one file where he counted two and no indication which party lost his. A " (2)" suffix
    /// is a visible oddity; a silently missing letter is not.</para>
    ///
    /// <para>The account name is NOT folded to ASCII here. The fold
    /// (<c>ReportPrintProjector.Ascii</c>) exists because the PDF writer carries the standard-14 faces with no
    /// font embedding; a filesystem has no such limit, so folding a Devanagari party name to hyphens in the
    /// FILE NAME would destroy the one place it can still be read in full.</para>
    /// </summary>
    private string FileNameFor(MultiAccountExportDocument doc, HashSet<string> used)
    {
        var title = string.IsNullOrWhiteSpace(doc.Report.Title) ? JobTitle : doc.Report.Title;
        var stem = SafeStem(string.IsNullOrWhiteSpace(doc.AccountName)
            ? title
            : title + " - " + doc.AccountName);

        if (AppendTimestamp) stem += "_" + _now.ToString("yyyyMMdd-HHmm");

        var candidate = stem;
        int n = 2;
        while (!used.Add(candidate)) candidate = stem + " (" + n++.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        return candidate + ".pdf";
    }

    /// <summary>Turns a title into a safe file-name stem (invalid path chars to '_'; blank to "Export").</summary>
    private static string SafeStem(string? text)
    {
        var stem = string.IsNullOrWhiteSpace(text) ? "Export" : text.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        return stem;
    }
}
