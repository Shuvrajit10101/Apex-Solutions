using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>T2-123 ON THE SURFACE A USER CAN ACTUALLY REACH.</b> The engine half is pinned in
/// <c>Apex.Ledger.Tests.GstAmendmentRegistrationScopeTests</c>; this file asserts the thing a correct engine does
/// not by itself give you — that the <b>GST Amendments screen</b> opens at all on a multi-registration book, and
/// shows ONE registration's amendments.
///
/// <para><b>What it was.</b> <c>GstAmendmentsReportViewModel.Rebuild</c> called both projections with <b>no</b>
/// registration, and its own comment said so: "<i>This screen does not yet carry a registration selector, so the
/// refusal is surfaced as its own status</i>". On a multi-registration company the engine threw
/// <c>GstReportSupport.EnsureRegistrationScoped</c>'s refusal, the <c>catch</c> set <c>Applicable = false</c>, and
/// <b>both amendment tables were empty with a refusal message where the figures belong</b> — there was no path to
/// the amendments at all. Scoping <c>BuildTable9C</c> without this picker would have been a correct engine behind a
/// screen that still could not call it, which is the kind of "closed" this project has had to retract.</para>
///
/// <para><b>Vendor-attested (R7, opened by content):</b> "<i>Press F3 (Company/Tax Registration) and select the
/// registration for which you want to view the report</i>" — <c>help.tallysolutions.com/gstr-1-report-in-tallyprime/</c>,
/// the same page that lists "<i>Amended Credit or Debit Notes (Registered) – 9C</i>" as one of that report's
/// sections.</para>
/// </summary>
public sealed class GstAmendmentsScreenRegistrationScopeTests
{
    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly BooksFrom = new(2025, 1, 1);

    /// <summary>The original invoices sit in the PRIOR financial year, because this screen's window is the WHOLE
    /// FY — a note whose original falls inside the same FY is an ordinary Table 9B CDN, not an amendment.</summary>
    private static readonly DateOnly PriorFyInvoiceDate = new(2025, 3, 10);
    private static readonly DateOnly NoteDate = new(2025, 5, 14);

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Karnataka, "AAPFU0939F");
    private static readonly string GstinBranch = GstinFor(TamilNadu, "AABCC1206D");
    private static readonly string GstinDebtorKa = GstinFor(Karnataka, "AAACS1206D");
    private static readonly string GstinDebtorTn = GstinFor(TamilNadu, "AADCS1206D");

    private const string KaInvoiceReference = "KA/S/11";
    private const string TnInvoiceReference = "TN/S/22";

    /// <summary>
    /// 🔴 <b>THE SCREEN OPENS, AND IT SHOWS KARNATAKA'S AMENDMENT ONLY.</b> Before this slice
    /// <c>Applicable</c> was <c>false</c> and <c>StatusText</c> carried the engine's refusal, so the two
    /// assertions below could not both be made at all. Asserted on the rendered row collection — what the grid
    /// binds — not on the engine record behind it.
    /// </summary>
    [Fact]
    public void The_amendments_screen_opens_on_a_multi_registration_book_and_shows_one_registration()
    {
        var page = OpenAmendments(multiRegistration: true);

        Assert.True(page.ShowsRegistrationPicker);
        Assert.True(page.Applicable);
        Assert.DoesNotContain("multiple GST registrations", page.StatusText);

        var row = Assert.Single(page.Table9C);
        Assert.Equal(KaInvoiceReference, row.OriginalInvoice);
        Assert.DoesNotContain(page.Table9C, r => r.OriginalInvoice == TnInvoiceReference);

        // The 3B correction footing the screen draws its conclusion from is this registration's alone:
        // -900 CGST + -900 SGST = -1,800.00, not the book's -5,400.00.
        Assert.Equal("1", page.CorrectionCountText);
        Assert.Contains("1,800.00", page.CorrectionTaxText);
    }

    /// <summary>
    /// 🔴 <b>SWITCHING THE PICKER RE-PROJECTS, AND THE BRANCH'S OWN NOTE IS STILL DECLARED (§34(2)).</b> A filter
    /// that scoped the note out of both registrations' screens would trade a double declaration for a missing one.
    /// </summary>
    [Fact]
    public void Selecting_the_branch_registration_shows_its_own_amendment_and_not_the_primarys()
    {
        var page = OpenAmendments(multiRegistration: true);

        page.SelectedRegistration = page.Registrations.Single(r => r.Gstin == GstinBranch);

        Assert.True(page.Applicable);
        var row = Assert.Single(page.Table9C);
        Assert.Equal(TnInvoiceReference, row.OriginalInvoice);
        Assert.DoesNotContain(page.Table9C, r => r.OriginalInvoice == KaInvoiceReference);
        Assert.Equal("1", page.CorrectionCountText);
        Assert.Contains("3,600.00", page.CorrectionTaxText);
    }

    /// <summary>
    /// <b>ER-13.</b> A single-registration company's screen is exactly what it was: the picker is hidden (one
    /// entry is a statement of fact, not a choice) and BOTH notes are that registration's own, so both are shown.
    /// </summary>
    [Fact]
    public void A_single_registration_screen_hides_the_picker_and_shows_both_notes()
    {
        var page = OpenAmendments(multiRegistration: false);

        Assert.False(page.ShowsRegistrationPicker);
        Assert.Single(page.Registrations);
        Assert.True(page.Applicable);
        Assert.Equal(2, page.Table9C.Count);
        Assert.Equal("2", page.CorrectionCountText);
    }

    // ---------------------------------------------------------------- scaffolding

    private static GstAmendmentsReportViewModel OpenAmendments(bool multiRegistration)
    {
        var c = CompanyFactory.CreateSeeded("Amend Scope Co", FyStart);
        c.BooksBeginFrom = BooksFrom;
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = BooksFrom,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        Guid? branchId = null;
        if (multiRegistration)
        {
            var branch = new GstRegistration(
                Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinBranch,
                GstRegistrationType.Regular, BooksFrom);
            c.Gst!.AddRegistration(branch);
            c.Gst!.EnsureValid();
            branchId = branch.Id;
        }

        Populate(c, gst, branchId);
        return new GstAmendmentsReportViewModel(c);
    }

    private static void Populate(Company c, GstService gst, Guid? branchId)
    {
        var ledgers = new LedgerService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtorKa = Add(c, "Karnataka Debtor", "Sundry Debtors", true);
        debtorKa.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorKa, StateCode = Karnataka,
        };
        var debtorTn = Add(c, "Tamil Nadu Debtor", "Sundry Debtors", true);
        debtorTn.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorTn, StateCode = TamilNadu,
        };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var creditNoteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;

        var kaInvoice = PostOutward(ledgers, gst, salesType, sales, debtorKa, 100_000m, interState: false,
            reverseSides: false, PriorFyInvoiceDate, registrationId: null);
        var tnInvoice = PostOutward(ledgers, gst, salesType, sales, debtorTn, 100_000m, interState: true,
            reverseSides: false, PriorFyInvoiceDate, registrationId: branchId);

        var kaNote = PostOutward(ledgers, gst, creditNoteType, sales, debtorKa, 10_000m, interState: false,
            reverseSides: true, NoteDate, registrationId: null);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), kaNote, CdnType.Credit, kaInvoice, KaInvoiceReference, PriorFyInvoiceDate,
            "01 sales return", is9BTarget: true));

        var tnNote = PostOutward(ledgers, gst, creditNoteType, sales, debtorTn, 20_000m, interState: true,
            reverseSides: true, NoteDate, registrationId: branchId);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), tnNote, CdnType.Credit, tnInvoice, TnInvoiceReference, PriorFyInvoiceDate,
            "01 sales return", is9BTarget: true));
    }

    private static Guid PostOutward(
        LedgerService ledgers, GstService gst, Guid typeId, Ledger.Domain.Ledger sales, Ledger.Domain.Ledger party,
        decimal value, bool interState, bool reverseSides, DateOnly date, Guid? registrationId)
    {
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) },
            interState, GstTaxDirection.Output, reverseSides: reverseSides);
        var gross = value + tax.TotalTax.Amount;

        var lines = reverseSides
            ? new List<EntryLine>
            {
                new(sales.Id, Money.FromRupees(value), DrCr.Debit),
                new(party.Id, Money.FromRupees(gross), DrCr.Credit),
            }
            : new List<EntryLine>
            {
                new(party.Id, Money.FromRupees(gross), DrCr.Debit),
                new(sales.Id, Money.FromRupees(value), DrCr.Credit),
            };
        lines.AddRange(tax.TaxLines);

        var id = Guid.NewGuid();
        var v = new Voucher(id, typeId, date, lines, partyId: party.Id);
        if (registrationId is { } r) v.GstRegistrationId = r;
        ledgers.Post(v);
        return id;
    }

    private static Ledger.Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Ledger.Domain.Ledger(
            Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
