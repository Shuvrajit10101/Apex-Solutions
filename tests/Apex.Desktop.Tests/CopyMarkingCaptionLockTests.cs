using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// The <b>F12 copy-marking radio captions</b> — the text an operator actually reads when choosing which of the
/// CGST Rule 48(1) statutory copies to print.
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> The T0-11 review (C10/L1-10) found the Duplicate and Triplicate copy
/// markings transposed against Rule 48(1)(b)/(c) across SIX sites, and the fix pass corrected all six. QA then
/// mutation-verified them and found that two of the six were <b>pinned by nothing at all</b> — and they were the
/// two the operator reads before printing: <c>MainWindow.axaml</c>'s "Duplicate for Transporter" / "Triplicate for
/// Supplier" radio captions. Both were reverted to the old transposed wording and <b>11 of 11</b>
/// CopyMarking/PrintConfig tests still passed; the strings occurred nowhere under <c>tests/</c>. The renderer half
/// is locked by <c>Apex.Ledger.Io.Tests/CopyMarkingRule48Tests.cs</c>; this file locks the operator-facing half.
/// <b>The shipped captions are CORRECT — what was missing is the alarm, not the fix.</b></para>
///
/// <para><b>WHAT THIS FILE LOCKS — exactly two things, and no more.</b>
/// <list type="number">
///   <item><see cref="The_scan_actually_finds_the_copy_marking_radios"/> — the F12 group exists in the shipped
///     window and has its four choices, located by <c>GroupName</c> and never by text.</item>
///   <item><see cref="The_realised_F12_radios_carry_the_Rule_48_1_captions"/> — <b>the assertion that matters.</b>
///     The real MainWindow, laid out headlessly, asked what its realised <c>RadioButton</c>s actually SAY, against
///     literals transcribed from the CBIC rule text and NEVER read back out of <c>PrintConfig</c>. It is red on
///     QA's exact mutation. It reads the realised control rather than the markup, so it bites whether a caption is
///     a literal today or a binding after the refactor below — including the refactor's own new failure mode, a
///     caption that renders EMPTY because the binding broke.</item>
/// </list></para>
///
/// <para><b>🔴 WHAT IS DELIBERATELY ABSENT, AND WHAT WOULD RESTORE IT.</b> This pin is the bounded fix. The
/// stronger fix — which retires the defect CLASS rather than this instance — is the single-source refactor: give
/// <c>PrintConfigViewModel</c> a <c>CopyMarkingCaption(CopyMarking)</c> deriving the caption from
/// <see cref="PrintConfig.CopyMarkingLabel"/>, bind the four radios' <c>Content</c> to it, and the statutory
/// pairing then has exactly ONE spelling in the tree. Three further locks were written for that shape and are
/// NOT in this file because neither the method nor the bindings exist yet, so they cannot compile:
/// <list type="bullet">
///   <item><c>The_derived_captions_are_the_Rule_48_1_pairings</c> — the derivation against the transcribed rule.</item>
///   <item><c>The_caption_is_the_printed_label_recased_and_nothing_else</c> — the derivation re-cases, never re-pairs.</item>
///   <item><c>The_statutory_captions_are_not_respelled_in_the_XAML</c> — the drift lock, in the idiom of
///     <c>Apex.Ledger.Tests/OneRuleDriftLockTests.cs</c>: a hard-coded pairing must not reappear beside the radios.
///     A behavioural test cannot notice a re-duplication; only reading the source can.</item>
/// </list>
/// They are recorded as an R6 item in <c>plan.md</c> (T0-11 / Phase 10.13) and are recoverable from this session's
/// transcript rather than needing redesign. Until they land, the one-rule-several-places shape survives: a seventh
/// site could still be written and only the two locks above would notice.</para>
///
/// <para><b>The rule is not re-derived here.</b> Rule 48(1)(a)/(b)/(c) and its verbatim CBIC source
/// (<c>cbic-gst.gov.in/pdf/cgst-rules-30122017.pdf</c>, PDF p.40) are transcribed and cited in
/// <c>CopyMarkingRule48Tests</c>; the literals below are that same transcription in the sentence case the panel
/// shows. RQ-12, <c>docs/phase5-reports-io-requirements.md:306</c>.</para>
/// </summary>
public sealed class CopyMarkingCaptionLockTests
{
    // ============================================================ the rule, transcribed — the only authority here

    /// <summary>CGST Rule 48(1)(a) — "the original copy being marked as ORIGINAL FOR RECIPIENT".</summary>
    private const string Rule48a = "Original for Recipient";

    /// <summary>CGST Rule 48(1)(b) — "the duplicate copy being marked as DUPLICATE FOR TRANSPORTER".</summary>
    private const string Rule48b = "Duplicate for Transporter";

    /// <summary>CGST Rule 48(1)(c) — "the triplicate copy being marked as TRIPLICATE FOR SUPPLIER".</summary>
    private const string Rule48c = "Triplicate for Supplier";

    /// <summary>The non-statutory fourth choice: print no marking at all.</summary>
    private const string NoMarking = "None";

    // ============================================================ lock 1 — the group exists in the shipped markup

    private static readonly XNamespace Av = "https://github.com/avaloniaui";

    private static string AxamlPath([CallerFilePath] string thisFile = "")
        => Path.Combine(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..")),
            "src", "Apex.Desktop", "Views", "MainWindow.axaml");

    /// <summary>Every RadioButton in the F12 copy-marking group, located by its GroupName — never by its text.</summary>
    private static IReadOnlyList<XElement> CopyMarkRadios()
    {
        var doc = XDocument.Load(AxamlPath());
        return doc.Descendants(Av + "RadioButton")
                  .Where(e => (string?)e.Attribute("GroupName") == "CopyMark")
                  .ToList();
    }

    /// <summary>
    /// <b>The copy-marking group is in the shipped window and offers exactly its four choices</b> — None plus the
    /// three Rule 48(1) markings. Deleting a marking from the panel is a fidelity regression that the realised-
    /// caption lock below would report only as a count mismatch; this says so at the markup where it happened.
    /// </summary>
    [Fact]
    public void The_scan_actually_finds_the_copy_marking_radios()
        => Assert.Equal(4, CopyMarkRadios().Count);

    // ============================================================ lock 2 — what the realised control actually says

    /// <summary>Taxable ₹4,321.00 @ 18% intra-State — a config-capable (Invoice-kind) preview.</summary>
    private static InvoicePrintData OutwardTaxInvoice()
    {
        var taxable = new Money(4_321m);
        var tax = GstService.ComputeLineTax(taxable, 1800, interState: false);
        string Gstin(string first14) => first14 + Apex.Ledger.Domain.Gstin.ComputeCheckDigit(first14 + "0");

        return new InvoicePrintData
        {
            DocumentTitle = GstReportSupport.TaxInvoiceTitle,
            Seller = new InvoicePartyBlock
            {
                Name = "Bright Traders", AddressLines = new[] { "12 Market Street", "Kolkata" },
                Gstin = Gstin("19AAAAA0000A1Z"), StateText = "West Bengal (19)",
            },
            Buyer = new InvoicePartyBlock
            {
                Name = "Gujarat Supplier", AddressLines = new[] { "9 Dockyard Road", "Surat" },
                Gstin = Gstin("24EEEEE0000E1Z"), StateText = "Gujarat (24)",
            },
            InvoiceNumber = "INV-0007",
            InvoiceDateText = "10-04-2025",
            PlaceOfSupply = "West Bengal (19)",
            IsInterState = false,
            Items = new[]
            {
                new InvoiceItemRow
                {
                    Description = "Raw Cotton", HsnSac = "520100",
                    QuantityText = "8.000", RateText = "540.125", TaxableValue = taxable,
                },
            },
            TaxRows = new[]
            {
                new InvoiceTaxRow
                {
                    RateLabel = "18%", TaxableValue = taxable, Cgst = tax.Cgst, Sgst = tax.Sgst, Igst = Money.Zero,
                },
            },
            TotalTaxable = taxable,
            TotalCgst = tax.Cgst,
            TotalSgst = tax.Sgst,
            TotalIgst = Money.Zero,
        };
    }

    /// <summary>Flushes bindings and forces a layout pass so the F12 panel's DataTemplate is realised.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// <b>THE OPERATOR-FACING ASSERTION.</b> The real window, the real F12 panel, the realised radios — asked what
    /// they SAY. This is the lock QA's mutation escaped: it goes red if either caption is transposed, and equally
    /// red if a caption ever renders blank.
    /// </summary>
    [AvaloniaFact]
    public void The_realised_F12_radios_carry_the_Rule_48_1_captions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ApexCopyCaption_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
            vm.NewCompanyName = "Bright Traders";
            vm.CreateCompany();
            vm.ShowGateway();

            // A config-capable preview is the gate OpenPrintConfig sits behind (Kind = Invoice).
            var preview = new PrintPreviewViewModel(OutwardTaxInvoice());
            Assert.True(preview.SupportsPrintConfig);
            vm.PrintPreview = preview;
            vm.OpenPrintConfig();
            Assert.NotNull(vm.PrintConfigPanel);

            var window = new MainWindow { DataContext = vm };
            window.Show();
            Pump(window);

            var captions = window.GetVisualDescendants()
                                 .OfType<RadioButton>()
                                 .Where(r => r.GroupName == "CopyMark")
                                 .Select(r => r.Content as string ?? string.Empty)
                                 .ToList();

            // Non-vacuity: the panel really realised, so an absent caption below is a WRONG caption, not an
            // unrealised template.
            Assert.Equal(4, captions.Count);

            Assert.Equal(new[] { NoMarking, Rule48a, Rule48b, Rule48c }, captions);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
