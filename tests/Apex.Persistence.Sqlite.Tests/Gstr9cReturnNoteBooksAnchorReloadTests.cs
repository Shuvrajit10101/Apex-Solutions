using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>Census row 6.12 clause (ii) / T2-114 — GSTR-9C's "tax per books" and "ITC per books" anchors could not see
/// a RETURN NOTE, so a perfectly reconciled year reported a non-zero Table 11 and Table 12F.</b>
///
/// <para><b>The defect, on <c>origin/main</c> e0b9116.</b> <c>Gstr9c.OutputAccrual</c> summed the Output tax ledgers'
/// <b>CREDIT</b> legs only and <c>InputAccrual</c> the Input ledgers' <b>DEBIT</b> legs only — a directional sum
/// chosen so the Rule-88A set-off (Dr Output / Cr Input) and the PMT-06 cash discharge (Dr Output / Cr Cash) do not
/// drain the anchors. But a sales-return <b>Credit Note</b> posts its Output tax on the DEBIT side and a
/// purchase-return <b>Debit Note</b> posts its Input tax on the CREDIT side (<c>reverseSides: IsReturnNote</c>), so
/// the books anchors silently dropped every return while GSTR-9 Tables 4 and 6 (off GSTR-3B, signed since PR #145)
/// netted them. The unreconciled lines then reported the RETURN ITSELF as a book-vs-return difference.</para>
///
/// <para><b>The fix.</b> A tax-ledger leg on the opposite (reversing) side is subtracted when — and only when — the
/// voucher that posted it is a return note (<see cref="VoucherEffects.IsReturnNote"/>). Discharge and set-off legs
/// are posted by Journal / Payment vouchers and stay excluded, which the fixture below pins by discharging the whole
/// liability in the same FY.</para>
///
/// <para><b>Figures worked BY HAND, odd paisa on purpose</b> (intra-state 27→27, 18% = CGST 9% + SGST 9%):</para>
/// <list type="bullet">
/// <item>Sale taxable ₹33,333.00 ⇒ CGST ₹2,999.97, SGST ₹2,999.97 (Cr Output).</item>
/// <item>Unlinked Credit Note (sales return) taxable ₹7,777.00 ⇒ CGST ₹699.93, SGST ₹699.93 (Dr Output).</item>
/// <item>Purchase taxable ₹11,111.00 ⇒ CGST ₹999.99, SGST ₹999.99 (Dr Input).</item>
/// <item>Unlinked Debit Note (purchase return) taxable ₹2,222.00 ⇒ CGST ₹199.98, SGST ₹199.98 (Cr Input).</item>
/// <item>Net output tax = 2 × (2,999.97 − 699.93) = <b>₹4,600.08</b>; net ITC = 2 × (999.99 − 199.98) =
/// <b>₹1,600.02</b>. Then the whole liability is discharged: set-off ₹800.01 per head, cash ₹1,500.03 per head.</item>
/// <item><b>On main:</b> tax per books ₹5,999.94 ⇒ Table 11 = 4,600.08 − 5,999.94 = <b>−₹1,399.86</b>; ITC per books
/// ₹1,999.98 ⇒ Table 12F = 1,999.98 − 1,600.02 = <b>+₹399.96</b>. <b>Correct:</b> both <b>₹0.00</b>.</item>
/// </list>
///
/// <para><b>Statutory grounding</b> (opened by content, W8 fix pass: CBIC's consolidated CGST Rules 2017 Part B
/// (Forms), as amended to 01-01-2022 — cbic-gst.gov.in/pdf/01012022-CGST-Rules-2017-amended-Part-B.pdf, FORM GSTR-9C
/// Part A, which carries the Notification 30/2021-CT amendments; Notification 30/2021-CT itself
/// (cbic-gst.gov.in/pdf/central-tax/notfctn-30-central-tax-english-2021.pdf) is the AMENDING text and does not
/// reproduce these headings): Table 9 is "Reconciliation of rate wise liability and amount payable thereon", Table 11
/// is "Additional amount payable but not paid", and Table 12 is "Reconciliation of Net Input Tax Credit (ITC)". A
/// sales return reduces the liability the books record, and a purchase return reduces the ITC the books record — so
/// a book anchor that omits them reports a difference that does not exist.</para>
///
/// <para>Every figure is read off a report built from a <b>RELOADED</b> book (save → fresh
/// <see cref="SqliteCompanyStore"/> handle → load), and the filed artefact is asserted as the emitted JSON BYTES.</para>
/// </summary>
public sealed class Gstr9cReturnNoteBooksAnchorReloadTests
{
    private static readonly DateOnly FyFrom = new(2024, 4, 1);
    private static readonly DateOnly FyTo = new(2025, 3, 31);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinSupplier = "27AAACR5055K1Z5";

    [Fact]
    public void Table_11_tax_per_books_nets_an_unlinked_sales_return_on_the_reloaded_book()
    {
        var r = Gstr9c.Build(Reload(BuildBook()), FyFrom, FyTo);

        Assert.True(r.Applicable);
        Assert.Equal(4_600.08m, r.Table9TaxPerReturn.Amount);
        Assert.Equal(4_600.08m, r.Table9TaxPerBooks.Amount);         // main: 5,999.94
        Assert.Equal(0.00m, r.Table11UnreconciledTax.Amount);         // main: −1,399.86
    }

    [Fact]
    public void Table_12_itc_per_books_nets_an_unlinked_purchase_return_on_the_reloaded_book()
    {
        var r = Gstr9c.Build(Reload(BuildBook()), FyFrom, FyTo);

        Assert.Equal(1_600.02m, r.Table12EReturnItc.Amount);
        Assert.Equal(1_600.02m, r.Table12ABooksItc.Amount);           // main: 1,999.98
        Assert.Equal(0.00m, r.Table12FUnreconciledItc.Amount);        // main: +399.96
    }

    /// <summary>The FILED artefact: the offline JSON bytes, emitted off the reloaded book, carry the reconciled
    /// figures in integer paisa — not merely the in-memory record.</summary>
    [Fact]
    public void The_emitted_9C_json_bytes_carry_zero_unreconciled_tax_and_itc()
    {
        var bytes = GstReturnJson.Gstr9c(Reload(BuildBook()), FyFrom, FyTo);

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        long Paisa(string key) => root.GetProperty(key).GetInt64();

        Assert.Equal(460_008L, Paisa("tbl9_tax_per_return_paisa"));
        Assert.Equal(460_008L, Paisa("tbl9_tax_per_books_paisa"));      // main: 599994
        Assert.Equal(0L, Paisa("tbl11_unreconciled_tax_paisa"));        // main: -139986
        Assert.Equal(160_002L, Paisa("tbl12e_return_itc_paisa"));
        Assert.Equal(160_002L, Paisa("tbl12a_books_itc_paisa"));        // main: 199998
        Assert.Equal(0L, Paisa("tbl12f_unreconciled_itc_paisa"));       // main: 39996
    }

    /// <summary>🔴 The discharge exclusion MUST survive: the opposite-side legs of the set-off journal and the
    /// cash-discharge payment are NOT return notes, so they stay out. A fix that netted EVERY opposite-side leg would
    /// drive tax per books to 0 and ITC per books to 0 here (the liability is fully discharged) — this pins the
    /// accrual figures absolutely, so that over-correction reddens.</summary>
    [Fact]
    public void Set_off_and_cash_discharge_legs_still_do_not_drain_the_books_anchors()
    {
        var c = Reload(BuildBook());
        var gst = new GstService(c);

        // The book really is fully discharged — the Output ledgers net to zero over the FY.
        foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State })
            Assert.Equal(0.00m, NetCredit(c, gst.FindTaxLedger(head, GstTaxDirection.Output)!.Id));

        var r = Gstr9c.Build(c, FyFrom, FyTo);
        Assert.Equal(4_600.08m, r.Table9TaxPerBooks.Amount);
        Assert.Equal(1_600.02m, r.Table12ABooksItc.Amount);
    }

    /// <summary>ER-13 baseline: a book with no return note is unchanged — the books anchors are the gross accrual.</summary>
    [Fact]
    public void A_book_without_return_notes_keeps_its_gross_accrual_anchors()
    {
        var r = Gstr9c.Build(Reload(BuildBook(withNotes: false)), FyFrom, FyTo);

        Assert.Equal(5_999.94m, r.Table9TaxPerBooks.Amount);
        Assert.Equal(0.00m, r.Table11UnreconciledTax.Amount);
        Assert.Equal(1_999.98m, r.Table12ABooksItc.Amount);
        Assert.Equal(0.00m, r.Table12FUnreconciledItc.Amount);
    }

    /// <summary>🔴 W8 review F2 — the LINKED §34 credit note, which is the default voucher-entry shape (the note is
    /// raised through <see cref="CreditDebitNoteService"/> against the sale itself). Sale ₹33,333.00 ⇒ tax ₹5,999.94;
    /// linked credit note ₹7,777.00 ⇒ ₹1,399.86 on the DEBIT side of Output. GSTR-3B folds it through <c>ReadCdn</c>,
    /// so per return = 5,999.94 − 1,399.86 = ₹4,600.08, and the books anchor must net it the same way. Before the
    /// T2-114 fix the anchor stayed gross at ₹5,999.94 here too.</summary>
    [Fact]
    public void Table_11_nets_a_linked_section_34_credit_note_on_the_reloaded_book()
    {
        var c = Reload(LinkedNoteBook(CdnType.Credit));
        Assert.NotNull(Assert.Single(c.CreditDebitNoteLinks).OriginalInvoiceVoucherId);   // the linked shape survived

        var r = Gstr9c.Build(c, FyFrom, FyTo);
        Assert.Equal(4_600.08m, r.Table9TaxPerReturn.Amount);
        Assert.Equal(4_600.08m, r.Table9TaxPerBooks.Amount);
        Assert.Equal(0.00m, r.Table11UnreconciledTax.Amount);

        using var doc = JsonDocument.Parse(GstReturnJson.Gstr9c(c, FyFrom, FyTo));
        Assert.Equal(460_008L, doc.RootElement.GetProperty("tbl9_tax_per_books_paisa").GetInt64());
        Assert.Equal(0L, doc.RootElement.GetProperty("tbl11_unreconciled_tax_paisa").GetInt64());
    }

    /// <summary>W8 review F2 — a §34(3) DEBIT note linked to the sale (an upward revision). It is a return-note BASE
    /// type, but it posts its Output tax on the CREDIT (accrual) side, so the anchor must ADD it: ₹2,222.00 ⇒
    /// ₹399.96, books = per return = 5,999.94 + 399.96 = ₹6,399.90. A rule that subtracted every return-note leg by
    /// base type rather than by posted side would file ₹5,599.98 here.</summary>
    [Fact]
    public void Table_11_adds_a_linked_section_34_debit_note_on_the_reloaded_book()
    {
        var c = Reload(LinkedNoteBook(CdnType.Debit));
        Assert.NotNull(Assert.Single(c.CreditDebitNoteLinks).OriginalInvoiceVoucherId);

        var r = Gstr9c.Build(c, FyFrom, FyTo);
        Assert.Equal(6_399.90m, r.Table9TaxPerReturn.Amount);
        Assert.Equal(6_399.90m, r.Table9TaxPerBooks.Amount);
        Assert.Equal(0.00m, r.Table11UnreconciledTax.Amount);

        using var doc = JsonDocument.Parse(GstReturnJson.Gstr9c(c, FyFrom, FyTo));
        Assert.Equal(639_990L, doc.RootElement.GetProperty("tbl9_tax_per_books_paisa").GetInt64());
        Assert.Equal(0L, doc.RootElement.GetProperty("tbl11_unreconciled_tax_paisa").GetInt64());
    }

    // ================================================================ helpers

    /// <summary>A sale ₹33,333.00 @ 18% and ONE §34 note LINKED to it, raised through the engine's
    /// <see cref="CreditDebitNoteService"/>: a credit note of ₹7,777.00 or a debit note of ₹2,222.00.</summary>
    private static Company LinkedNoteBook(CdnType type)
    {
        var c = CompanyFactory.CreateSeeded("Linked Note 9C Co", FyFrom);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

        var saleTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(33_333m), 1800) }, false, GstTaxDirection.Output);
        Assert.Equal(5_999.94m, saleTax.TotalTax.Amount);
        var saleLines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(33_333m + saleTax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(33_333m), DrCr.Credit),
        };
        saleLines.AddRange(saleTax.TaxLines);
        var saleDate = new DateOnly(2024, 5, 10);
        var sale = new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), saleDate, saleLines, partyId: debtor.Id);
        ledgers.Post(sale);

        var value = type == CdnType.Credit ? 7_777m : 2_222m;
        var noteId = Guid.NewGuid();
        var noteDate = new DateOnly(2024, 6, 10);
        var posting = new CreditDebitNoteService(c).BuildCreditDebitNote(type,
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) }, interState: false, noteId,
            sale.Id, null, saleDate, noteDate, type == CdnType.Credit ? "01 Sales return" : "04 Correction in invoice");
        Assert.Equal(type == CdnType.Credit ? 1_399.86m : 399.96m, posting.Computed.TotalTax.Amount);
        var tax = posting.Computed.TotalTax.Amount;
        var noteLines = type == CdnType.Credit
            ? new List<EntryLine>
            {
                new(sales.Id, Money.FromRupees(value), DrCr.Debit),
                new(debtor.Id, Money.FromRupees(value + tax), DrCr.Credit),
            }
            : new List<EntryLine>
            {
                new(debtor.Id, Money.FromRupees(value + tax), DrCr.Debit),
                new(sales.Id, Money.FromRupees(value), DrCr.Credit),
            };
        noteLines.AddRange(posting.TaxLines);
        ledgers.Post(new Voucher(noteId,
            TypeOf(type == CdnType.Credit ? VoucherBaseType.CreditNote : VoucherBaseType.DebitNote),
            noteDate, noteLines, partyId: debtor.Id));
        return c;
    }

    private static decimal NetCredit(Company c, Guid ledgerId) =>
        c.Vouchers.Where(v => v.Date >= FyFrom && v.Date <= FyTo)
            .SelectMany(v => v.Lines).Where(l => l.LedgerId == ledgerId)
            .Sum(l => l.Side == DrCr.Credit ? l.Amount.Amount : -l.Amount.Amount);

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-9c-notes-{Guid.NewGuid():N}.db");
        try
        {
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original);
            using var read = new SqliteCompanyStore(dbPath);
            return read.Load(original.Id)!;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

    private static Company BuildBook(bool withNotes = true)
    {
        var c = CompanyFactory.CreateSeeded("Return Note 9C Co", FyFrom);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        var ledgers = new LedgerService(c);

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var bank = Add(c, "Bank", "Bank Accounts", true);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var creditor = Add(c, "Supplier", "Sundry Creditors", false);
        creditor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };

        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

        // ---- the SALE (Cr Output)
        var saleTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(33_333m), 1800) }, false, GstTaxDirection.Output);
        Assert.Equal(5_999.94m, saleTax.TotalTax.Amount);
        var saleLines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(33_333m + saleTax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(33_333m), DrCr.Credit),
        };
        saleLines.AddRange(saleTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), new(2024, 5, 10), saleLines, partyId: debtor.Id));

        // ---- the PURCHASE (Dr Input)
        var purTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(11_111m), 1800) }, false, GstTaxDirection.Input);
        Assert.Equal(1_999.98m, purTax.TotalTax.Amount);
        var purLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(11_111m), DrCr.Debit),
            new(creditor.Id, Money.FromRupees(11_111m + purTax.TotalTax.Amount), DrCr.Credit),
        };
        purLines.AddRange(purTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Purchase), new(2024, 5, 3), purLines, partyId: creditor.Id));

        long liab = 299_997, credit = 99_999;
        if (withNotes)
        {
            // ---- UNLINKED Credit Note (sales return): tax legs on the DEBIT side of Output.
            var cnTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(7_777m), 1800) }, false, GstTaxDirection.Output, reverseSides: true);
            Assert.Equal(1_399.86m, cnTax.TotalTax.Amount);
            var cnLines = new List<EntryLine>
            {
                new(sales.Id, Money.FromRupees(7_777m), DrCr.Debit),
                new(debtor.Id, Money.FromRupees(7_777m + cnTax.TotalTax.Amount), DrCr.Credit),
            };
            cnLines.AddRange(cnTax.TaxLines);
            ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.CreditNote), new(2024, 6, 2), cnLines, partyId: debtor.Id));

            // ---- UNLINKED Debit Note (purchase return): tax legs on the CREDIT side of Input.
            var dnTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(2_222m), 1800) }, false, GstTaxDirection.Input, reverseSides: true);
            Assert.Equal(399.96m, dnTax.TotalTax.Amount);
            var dnLines = new List<EntryLine>
            {
                new(creditor.Id, Money.FromRupees(2_222m + dnTax.TotalTax.Amount), DrCr.Debit),
                new(purchases.Id, Money.FromRupees(2_222m), DrCr.Credit),
            };
            dnLines.AddRange(dnTax.TaxLines);
            ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.DebitNote), new(2024, 6, 5), dnLines, partyId: creditor.Id));

            liab = 230_004; credit = 80_001;
        }

        // ---- discharge the WHOLE liability in the FY: Rule-88A set-off, then PMT-06 cash for the residual.
        var alloc = GstSetOffService.Allocate(new GstSetOffService.SetOffDemand(liab, liab, 0, 0, 0, credit, credit, 0, 0));
        new GstSetOffService(c).PostSetOff("2024-06", alloc, new(2024, 6, 30));
        var cash = Money.FromRupees((liab - credit) / 100m);
        var deposit = new GstDepositService(c);
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, cash, bank, new(2024, 6, 30), "CPIN-C", "CIN-C");
        deposit.PostPmt06(GstTaxHead.State, GstMinorHead.Tax, cash, bank, new(2024, 6, 30), "CPIN-S", "CIN-S");
        deposit.PostCashDischarge(GstTaxHead.Central, cash, new(2024, 6, 30));
        deposit.PostCashDischarge(GstTaxHead.State, cash, new(2024, 6, 30));

        return c;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
