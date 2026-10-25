using System;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 12.6, the multi-VOUCHER half — a RANGE of vouchers printed as one collated job.</b>
///
/// <para>The row reads "Multi-account printing / multi-voucher (range) printing". The account half landed in
/// PR #56; the cell has said ever since that <i>"the multi-voucher (range) half of this row is not addressed —
/// the range knob selects sheets within a job, not a range of vouchers to print"</i>. These tests pin the
/// range.</para>
///
/// <para>🔴 <b>EVERY ASSERTION THAT MATTERS READS THE EMITTED PDF BYTES</b>, not the projector's return value
/// and never an internal the test itself set. The printing track has been proved this way throughout, for the
/// measured reason recorded on <c>MatrixSecondBandEgressTests</c>: a report whose rows are wider than its
/// column band is silently amputated by <c>ReportPdf</c>'s <c>Math.Min(Columns.Count, row.Cells.Count)</c>
/// clamp, and an assertion on the view model passes happily over the amputation. A filed annual return went out
/// with its money deleted that way.</para>
///
/// <para>🔴 <b>PDF string literals escape <c>(</c> and <c>)</c></b>, so no assertion here looks for a
/// parenthesis — a reviewer on this project once reported clipping that did not exist by forgetting it.</para>
/// </summary>
public sealed class MultiVoucherPrintTests : IDisposable
{
    private readonly string _tempDir =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ApexMultiVoucher_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (System.IO.Directory.Exists(_tempDir)) System.IO.Directory.Delete(_tempDir, recursive: true); }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>
    /// Three receipts from Northwind, two payments to Southgate, all inside the year. Distinct amounts, so a
    /// voucher that reached (or failed to reach) the paper can be identified by its own figure.
    ///
    /// <para>🔴 <b>No <c>PartyId</c> is set on any of them</b>, because the plain accounting grid never sets one
    /// (A12 review w6, F3): the party is a posting line, exactly as a real Receipt or Payment records it. These
    /// fixtures used to hand-set <c>PartyId</c>, which made the party filter look right while it found nothing
    /// a user had actually keyed.</para>
    /// </summary>
    private static (Company C, Guid Northwind, Guid Southgate, Guid ReceiptType, Guid PaymentType) Books()
    {
        var c = CompanyFactory.CreateSeeded("Range Print Co", FyStart);

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(500_000m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital A/c",
            c.FindGroupByName("Capital Account")!.Id, Money.FromRupees(500_000m), openingIsDebit: false);
        c.AddLedger(capital);

        var northwind = new Domain.Ledger(Guid.NewGuid(), "Northwind Traders",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.FromRupees(300_000m), openingIsDebit: true);
        c.AddLedger(northwind);

        var southgate = new Domain.Ledger(Guid.NewGuid(), "Southgate Supplies",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.FromRupees(200_000m), openingIsDebit: false);
        c.AddLedger(southgate);

        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var payment = c.FindVoucherTypeByName("Payment")!;
        var svc = new LedgerService(c);

        // Northwind receipts: 11,111.11 / 22,222.22 / 33,333.33 on three different months.
        var receiptAmounts = new[] { 11_111.11m, 22_222.22m, 33_333.33m };
        var receiptDates = new[]
        {
            new DateOnly(2025, 4, 10), new DateOnly(2025, 5, 10), new DateOnly(2025, 6, 10),
        };
        for (int i = 0; i < 3; i++)
        {
            var v = new Voucher(Guid.NewGuid(), receipt.Id, receiptDates[i], new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(receiptAmounts[i]), DrCr.Debit),
                new EntryLine(northwind.Id, Money.FromRupees(receiptAmounts[i]), DrCr.Credit),
            });
            svc.Post(v);
        }

        // Southgate payments: 44,444.44 / 55,555.55.
        var paymentAmounts = new[] { 44_444.44m, 55_555.55m };
        var paymentDates = new[] { new DateOnly(2025, 4, 20), new DateOnly(2025, 7, 20) };
        for (int i = 0; i < 2; i++)
        {
            var v = new Voucher(Guid.NewGuid(), payment.Id, paymentDates[i], new[]
            {
                new EntryLine(southgate.Id, Money.FromRupees(paymentAmounts[i]), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(paymentAmounts[i]), DrCr.Credit),
            });
            svc.Post(v);
        }

        return (c, northwind.Id, southgate.Id, receipt.Id, payment.Id);
    }

    private static string RenderRange(
        Company c, Guid? typeId, Guid? partyId, DateOnly from, DateOnly to)
    {
        var chosen = MultiVoucherPrintProjector.Select(c, typeId, partyId, from, to);
        var docs = MultiVoucherPrintProjector.Project(c, chosen);
        return AsLatin1(RenderJob(docs, new PageConfig()));
    }

    /// <summary>The job exactly as the shell's preview renders it (no F12 knob touched).</summary>
    private static byte[] RenderJob(System.Collections.Generic.IReadOnlyList<VoucherSetDocument> docs, PageConfig page)
        => VoucherSetPdf.Render(docs, new PrintConfig(), page, MultiVoucherPrintProjector.JobTitle);

    /// <summary>Every page content stream in a PDF, in order — what is actually drawn on each sheet.</summary>
    private static string[] ContentStreams(byte[] pdf)
        => System.Text.RegularExpressions.Regex
            .Matches(AsLatin1(pdf), "/Length \\d+ >>\nstream\n(.*?)endstream",
                System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => m.Groups[1].Value)
            .Where(c => c.Contains(" Tf", StringComparison.Ordinal))   // page text streams, not image samples
            .ToArray();

    /// <summary>
    /// 🔴 <b>"PRINTS EXACTLY AS IT PRINTS ALONE", ASSERTED ON THE BYTES.</b> Every sheet the voucher's single print
    /// draws must appear, verbatim, as a sheet of the batch — same header, same rows, same footer.
    /// </summary>
    private static void AssertEverySheetOfTheSinglePrintIsInTheBatch(byte[] single, byte[] batch)
    {
        var alone = ContentStreams(single);
        var inBatch = ContentStreams(batch);
        Assert.NotEmpty(alone);
        foreach (var sheet in alone)
            Assert.True(inBatch.Contains(sheet),
                "a sheet of the voucher's single print is not in the batch byte-for-byte — the batch prints it "
              + "differently. First 300 chars of the missing sheet: " + sheet[..Math.Min(300, sheet.Length)]);
    }

    /// <summary>
    /// A company with one GST item Sales invoice (item "Zqwidget", HSN 847130, buyer GSTIN 27AAPFU0939F1ZV) and one
    /// plain Journal — the A12 review's probe P2 shape — saved and opened through the shell, so both prints go
    /// through the routes an operator uses.
    /// </summary>
    private (MainWindowViewModel Shell, Voucher Sale, Voucher Journal) InvoiceCompanyOnTheShell()
    {
        var c = CompanyFactory.CreateSeeded("Inv Co", FyStart);
        var gst = new Apex.Ledger.Services.GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Zqwidget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 1000m, Money.FromRupees(40000m));
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Local Debtor", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        c.AddLedger(debtor);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = "27AAPFU0939F1ZV", StateCode = "27",
        };
        var tax = gst.ComputeInvoiceTax(
            new[] { new Apex.Ledger.Services.GstService.TaxableLine(Money.FromRupees(50000m), 1800) },
            interState: false, GstTaxDirection.Output);
        var lines = new System.Collections.Generic.List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(59000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(50000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        var svc = new LedgerService(c);
        svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 10), lines, partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, Money.FromRupees(50000m)) }));
        svc.Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Journal")!.Id, new DateOnly(2025, 4, 12), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(777.77m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(777.77m), DrCr.Credit),
        }));

        var storage = new CompanyStorage(System.IO.Path.Combine(_tempDir, "Companies"));
        storage.Save(c);
        var shell = new MainWindowViewModel(storage);
        shell.ShowCompanySelect();
        shell.Menu.Single(m => m.Label == "Inv Co").Activate();
        var company = shell.Company!;
        var sale = company.Vouchers.Single(v => v.TypeId == salesType.Id);
        var journal = company.Vouchers.Single(v => v.TypeId != salesType.Id);
        return (shell, sale, journal);
    }

    /// <summary>The batch the operator prints: the shell's panel, all vouchers, PrintMultiVoucherJob.</summary>
    private static byte[] PrintEverythingThroughTheShell(MainWindowViewModel shell)
    {
        shell.OpenMultiVoucherPrint();
        Assert.Equal(2, shell.MultiVoucherPrint!.MatchCount);
        shell.PrintMultiVoucherJob();
        return shell.PrintPreview!.PdfBytes;
    }

    /// <summary>
    /// 🔴 <b>F2 (A12 review w6): an item Sales INVOICE prints in a batch EXACTLY as it prints alone.</b> The vendor
    /// page this row cites uses Multi-Voucher Printing to print sales invoices. Printed alone (the voucher-detail
    /// route, <c>VoucherDetailViewModel.BuildPrintPreview</c>) it carries its item, HSN and GSTIN; the first cut of
    /// the batch printed it as a bare Particulars/Debit/Credit list with none of them. Asserted on the emitted
    /// bytes twice over: the three particulars by name, and every sheet of the single print found verbatim among
    /// the batch's sheets.
    /// </summary>
    [Fact]
    public void An_item_invoice_prints_in_the_batch_exactly_as_it_prints_alone()
    {
        var (shell, sale, _) = InvoiceCompanyOnTheShell();
        var single = new VoucherDetailViewModel(shell.Company!, sale).BuildPrintPreview().PdfBytes;
        var batch = PrintEverythingThroughTheShell(shell);

        var b = AsLatin1(batch);
        Assert.Contains("Zqwidget", b, StringComparison.Ordinal);
        Assert.Contains("847130", b, StringComparison.Ordinal);
        Assert.Contains("27AAPFU0939F1ZV", b, StringComparison.Ordinal);
        AssertEverySheetOfTheSinglePrintIsInTheBatch(single, batch);
    }

    /// <summary>
    /// The same promise for a PLAIN voucher in the same job: the Journal's single print (VoucherPdf, with its
    /// amount in words and its own footer) is in the batch sheet for sheet. A batch that printed the invoice right
    /// and every other voucher as a report-shaped list would pass the test above alone.
    /// </summary>
    [Fact]
    public void A_plain_voucher_prints_in_the_batch_exactly_as_it_prints_alone()
    {
        var (shell, _, journal) = InvoiceCompanyOnTheShell();
        var single = new VoucherDetailViewModel(shell.Company!, journal).BuildPrintPreview().PdfBytes;
        var batch = PrintEverythingThroughTheShell(shell);

        Assert.Contains("777.77", AsLatin1(batch), StringComparison.Ordinal);
        AssertEverySheetOfTheSinglePrintIsInTheBatch(single, batch);
    }

    /// <summary>
    /// The JOB's knobs are the job's: F10 selects the job's own sheets (sheets 2 to 3 of a five-voucher job are
    /// the 20-Apr payment and the 10-May receipt, and nothing else), and F5 repeats the whole job collated.
    /// Read off the emitted page objects and figures.
    /// </summary>
    [Fact]
    public void The_page_range_selects_the_jobs_sheets_and_copies_repeat_the_whole_job()
    {
        var (c, _, _, _, _) = Books();
        var docs = MultiVoucherPrintProjector.Project(c, MultiVoucherPrintProjector.Select(c, null, null, FyStart, FyEnd));
        Assert.Equal(5, docs.Count);

        string ranged = AsLatin1(RenderJob(docs, new PageConfig { FirstPage = 2, LastPage = 3 }));
        Assert.Equal(2, CountOccurrences(ranged, "/Type /Page "));
        Assert.Contains("44,444.44", ranged, StringComparison.Ordinal);   // 20-Apr payment: sheet 2
        Assert.Contains("22,222.22", ranged, StringComparison.Ordinal);   // 10-May receipt: sheet 3
        Assert.DoesNotContain("11,111.11", ranged, StringComparison.Ordinal);
        Assert.DoesNotContain("33,333.33", ranged, StringComparison.Ordinal);

        string twice = AsLatin1(RenderJob(docs, new PageConfig { Copies = 2 }));
        Assert.Equal(10, CountOccurrences(twice, "/Type /Page "));
    }

    // ------------------------------------------------------------------ the range itself

    /// <summary>
    /// 🔴 <b>THE HEADLINE: a PERIOD selects a range of vouchers, and the vouchers outside it are not on the
    /// paper.</b> Both halves are asserted, because "the right ones printed" and "the wrong ones did not" are
    /// different claims and a projector that printed everything would satisfy only the first.
    /// </summary>
    [Fact]
    public void A_period_prints_every_voucher_inside_it_and_none_outside_it()
    {
        var (c, _, _, _, _) = Books();

        // April through June: the three Northwind receipts and the 20-Apr Southgate payment. NOT the 20-Jul one.
        string s = RenderRange(c, null, null, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));

        Assert.Contains("11,111.11", s, StringComparison.Ordinal);
        Assert.Contains("22,222.22", s, StringComparison.Ordinal);
        Assert.Contains("33,333.33", s, StringComparison.Ordinal);
        Assert.Contains("44,444.44", s, StringComparison.Ordinal);
        Assert.DoesNotContain("55,555.55", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// The range is INCLUSIVE at both ends. A one-day window on the first voucher's own date must print it —
    /// an exclusive upper bound would silently drop the last day of every month-end run, and nothing on the
    /// paper would say so.
    /// </summary>
    [Fact]
    public void The_period_bounds_are_inclusive_at_both_ends()
    {
        var (c, _, _, _, _) = Books();

        string justTheTenth = RenderRange(
            c, null, null, new DateOnly(2025, 4, 10), new DateOnly(2025, 4, 10));

        Assert.Contains("11,111.11", justTheTenth, StringComparison.Ordinal);
        Assert.DoesNotContain("22,222.22", justTheTenth, StringComparison.Ordinal);
        Assert.DoesNotContain("44,444.44", justTheTenth, StringComparison.Ordinal);
    }

    /// <summary>The VOUCHER TYPE axis — the vendor's own Configure field.</summary>
    [Fact]
    public void A_voucher_type_restricts_the_job_to_that_type()
    {
        var (c, _, _, receiptType, _) = Books();

        string s = RenderRange(c, receiptType, null, FyStart, FyEnd);

        Assert.Contains("11,111.11", s, StringComparison.Ordinal);
        Assert.Contains("33,333.33", s, StringComparison.Ordinal);
        // Both payments are a different type and must not be in a receipts job.
        Assert.DoesNotContain("44,444.44", s, StringComparison.Ordinal);
        Assert.DoesNotContain("55,555.55", s, StringComparison.Ordinal);
    }

    /// <summary>The PARTY axis — attested on the vendor's release notes alongside the period.</summary>
    [Fact]
    public void A_party_restricts_the_job_to_that_partys_vouchers()
    {
        var (c, _, southgate, _, _) = Books();

        string s = RenderRange(c, null, southgate, FyStart, FyEnd);

        Assert.Contains("44,444.44", s, StringComparison.Ordinal);
        Assert.Contains("55,555.55", s, StringComparison.Ordinal);
        Assert.DoesNotContain("11,111.11", s, StringComparison.Ordinal);
        Assert.DoesNotContain("22,222.22", s, StringComparison.Ordinal);
        Assert.DoesNotContain("33,333.33", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>F3 (A12 review w6): a Receipt KEYED ON THE REAL ENTRY GRID is found by its party and reaches the
    /// paper.</b> Entered through <see cref="VoucherEntryViewModel"/> — party Cr, Cash Dr — exactly as an
    /// operator keys it, which records the party only as a posting line. Then the shipped route: the shell's
    /// Multi-Voucher Printing panel, the party chosen, <c>PrintMultiVoucherJob</c>, and the assertion on the
    /// previewed PDF bytes. Before the fix the panel said "No vouchers in this range. Nothing to print." and
    /// opened no preview at all. The other party's job must NOT carry it.
    /// </summary>
    [Fact]
    public void A_receipt_keyed_on_the_entry_grid_is_found_by_its_party_and_printed()
    {
        var (c, northwind, southgate, receiptType, _) = Books();
        var storage = new CompanyStorage(System.IO.Path.Combine(_tempDir, "Companies"));
        storage.Save(c);

        var shell = new MainWindowViewModel(storage);
        shell.ShowCompanySelect();
        shell.Menu.Single(m => m.Label == "Range Print Co").Activate();
        var company = shell.Company!;

        var entry = new VoucherEntryViewModel(company, company.FindVoucherType(receiptType)!, storage,
            onSaved: () => { }, onCancelled: () => { });
        entry.Lines[0].SelectedLedger = company.FindLedger(northwind);
        entry.Lines[0].Side = DrCr.Credit;
        entry.Lines[0].AmountText = "1234.56";
        entry.Lines[1].SelectedLedger = company.FindLedgerByName("Cash");
        entry.Lines[1].Side = DrCr.Debit;
        entry.Lines[1].AmountText = "1234.56";
        entry.Recalculate();
        Assert.True(entry.Accept(), entry.Message);

        shell.OpenMultiVoucherPrint();
        var panel = shell.MultiVoucherPrint!;
        panel.SelectedParty = panel.Parties.Single(p => p.Id == northwind);
        Assert.Equal(4, panel.MatchCount);   // three fixture receipts and the one just keyed
        Assert.Contains(panel.Matches, r => r.PartyName == "Northwind Traders");

        shell.PrintMultiVoucherJob();
        var bytes = AsLatin1(shell.PrintPreview!.PdfBytes);
        Assert.Contains("1,234.56", bytes, StringComparison.Ordinal);
        Assert.Contains("33,333.33", bytes, StringComparison.Ordinal);
        Assert.DoesNotContain("44,444.44", bytes, StringComparison.Ordinal);

        // …and the OTHER party's job does not carry it.
        panel.SelectedParty = panel.Parties.Single(p => p.Id == southgate);
        shell.PrintMultiVoucherJob();
        var other = AsLatin1(shell.PrintPreview!.PdfBytes);
        Assert.DoesNotContain("1,234.56", other, StringComparison.Ordinal);
        Assert.Contains("44,444.44", other, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three axes COMPOSE: type AND party AND period, all at once, down to a single voucher.
    /// </summary>
    [Fact]
    public void The_three_axes_compose_down_to_one_voucher()
    {
        var (c, northwind, _, receiptType, _) = Books();

        string s = RenderRange(c, receiptType, northwind, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31));

        Assert.Contains("22,222.22", s, StringComparison.Ordinal);
        Assert.DoesNotContain("11,111.11", s, StringComparison.Ordinal);
        Assert.DoesNotContain("33,333.33", s, StringComparison.Ordinal);
        Assert.DoesNotContain("44,444.44", s, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ one job, many sheets

    /// <summary>
    /// 🔴 <b>IT IS ONE COLLATED JOB, NOT N SEPARATE DOCUMENTS — asserted on the PDF's own page objects.</b>
    /// Five vouchers in the range produce five sheets in ONE byte stream. A projector that returned the right
    /// documents but a renderer that emitted one page would satisfy every content assertion above.
    /// </summary>
    [Fact]
    public void Every_voucher_in_the_range_starts_its_own_sheet_in_one_pdf()
    {
        var (c, _, _, _, _) = Books();

        var chosen = MultiVoucherPrintProjector.Select(c, null, null, FyStart, FyEnd);
        Assert.Equal(5, chosen.Count);

        var docs = MultiVoucherPrintProjector.Project(c, chosen);
        Assert.Equal(5, docs.Count);

        string s = AsLatin1(RenderJob(docs, new PageConfig()));

        // Read the page count off the emitted PDF structure, not off docs.Count.
        int pageObjects = CountOccurrences(s, "/Type /Page\n") + CountOccurrences(s, "/Type /Page ");
        Assert.True(pageObjects >= 5,
            $"five vouchers must occupy at least five sheets; the PDF declares {pageObjects}.");
        Assert.StartsWith("%PDF-", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// The vouchers reach the paper in DATE order, so a reprint of the same range matches the first copy.
    /// Asserted on the byte offsets of each figure in the emitted stream — the order the sheets really came out
    /// in, not the order of a list.
    /// </summary>
    [Fact]
    public void The_sheets_come_out_in_date_order()
    {
        var (c, northwind, _, receiptType, _) = Books();

        string s = RenderRange(c, receiptType, northwind, FyStart, FyEnd);

        int april = s.IndexOf("11,111.11", StringComparison.Ordinal);
        int may = s.IndexOf("22,222.22", StringComparison.Ordinal);
        int june = s.IndexOf("33,333.33", StringComparison.Ordinal);

        Assert.True(april >= 0 && may >= 0 && june >= 0, "all three receipts must be on the paper");
        Assert.True(april < may, "the April receipt must precede the May one in the emitted stream");
        Assert.True(may < june, "the May receipt must precede the June one in the emitted stream");
    }

    /// <summary>
    /// A CANCELLED voucher stays in the range and is MARKED on the paper. It is not dropped — its number was
    /// spent — and it is not printed looking live, which would put a withdrawn document in a counterparty's
    /// hands. The marking is read off the emitted bytes.
    /// </summary>
    [Fact]
    public void A_cancelled_voucher_prints_marked_rather_than_silently_or_not_at_all()
    {
        var (c, northwind, _, receiptType, _) = Books();

        var target = c.Vouchers.Single(v => v.TypeId == receiptType && v.Date == new DateOnly(2025, 5, 10));
        new LedgerService(c).Cancel(target.Id);

        string s = RenderRange(c, receiptType, northwind, FyStart, FyEnd);

        Assert.Contains("CANCELLED", s, StringComparison.Ordinal);
        // The live ones are untouched by the cancellation.
        Assert.Contains("11,111.11", s, StringComparison.Ordinal);
        Assert.Contains("33,333.33", s, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the panel

    /// <summary>
    /// The panel offers the three axes as real, populated choices and its match list is what the job will
    /// print. "All Voucher Types" and "All Parties" are the opening state, so the panel opens on the widest
    /// range rather than on nothing.
    /// </summary>
    [Fact]
    public void The_panel_opens_on_the_whole_period_and_lists_what_it_would_print()
    {
        var (c, _, _, _, _) = Books();

        var vm = new MultiVoucherPrintViewModel(c, FyStart, FyEnd);

        Assert.Equal("All Voucher Types", vm.VoucherTypes[0].Name);
        Assert.Null(vm.VoucherTypes[0].Id);
        Assert.Equal("All Parties", vm.Parties[0].Name);
        Assert.Null(vm.Parties[0].Id);

        // Only PARTY accounts are offered as party filters — a filter row that can never match is a dead field.
        Assert.Contains(vm.Parties, p => p.Name == "Northwind Traders");
        Assert.Contains(vm.Parties, p => p.Name == "Southgate Supplies");
        Assert.DoesNotContain(vm.Parties, p => p.Name == "Capital A/c");

        Assert.Equal(5, vm.MatchCount);
        Assert.True(vm.HasMatches);
        Assert.Equal(5, vm.BuildJob().Count);
    }

    /// <summary>
    /// Narrowing the panel's period narrows the JOB — the panel's selection really drives the documents, which
    /// is the half a projector-only test cannot see.
    /// </summary>
    [Fact]
    public void Narrowing_the_panels_period_narrows_the_emitted_job()
    {
        var (c, _, _, _, _) = Books();

        var vm = new MultiVoucherPrintViewModel(c, FyStart, FyEnd);
        Assert.Equal(5, vm.MatchCount);

        vm.PeriodFromText = "01-05-2025";
        vm.PeriodToText = "31-05-2025";

        Assert.Equal(1, vm.MatchCount);
        string s = AsLatin1(RenderJob(vm.BuildJob(), new PageConfig()));
        Assert.Contains("22,222.22", s, StringComparison.Ordinal);
        Assert.DoesNotContain("11,111.11", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// An INVERTED range is refused with a message naming both dates, and builds NOTHING — it is not silently
    /// swapped, which would print a batch the operator did not ask for while telling him he got what he typed.
    /// </summary>
    [Fact]
    public void An_inverted_range_is_refused_and_prints_nothing()
    {
        var (c, _, _, _, _) = Books();

        var vm = new MultiVoucherPrintViewModel(c, FyStart, FyEnd);
        vm.PeriodFromText = "30-06-2025";
        vm.PeriodToText = "01-04-2025";

        Assert.Equal(0, vm.MatchCount);
        Assert.False(vm.HasMatches);
        Assert.Empty(vm.BuildJob());
        Assert.Contains("before", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An EMPTY range builds nothing and says so. The caller must not open a preview over a blank sheet —
    /// presenting a mistake as output is the shape this project keeps filing.
    /// </summary>
    [Fact]
    public void An_empty_range_builds_no_job_and_says_why()
    {
        var (c, _, _, _, _) = Books();

        var vm = new MultiVoucherPrintViewModel(c, FyStart, FyEnd);
        vm.PeriodFromText = "01-01-2030";
        vm.PeriodToText = "31-01-2030";

        Assert.Equal(0, vm.MatchCount);
        Assert.Empty(vm.BuildJob());
        Assert.Contains("No vouchers", vm.Status, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }
        return n;
    }
}
