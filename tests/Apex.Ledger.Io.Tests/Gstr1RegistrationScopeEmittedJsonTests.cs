using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>T2-119 (ii), AT THE ARTEFACT.</b> The companion to
/// <c>Apex.Ledger.Tests.GstOutwardRegistrationScopeAndSignTests</c>, which proves the defect and the fix on the
/// projection. This file proves it on <b>THE EMITTED BYTES</b> — the thing a user files — because a correct
/// projection that never reaches the payload is worth nothing, and an artefact that nothing downstream questions is
/// the worse place for a wrong figure to sit.
///
/// <para><b>The defect.</b> <c>Gstr1.BuildTable9B</c> walked <c>company.CreditDebitNoteLinks</c> wholesale with no
/// registration filter while every other leg of <c>Gstr1.Build</c> was scoped, so a <b>second registration's</b>
/// §34 credit note appeared in this registration's <c>cdnr</c> section <i>and</i> its tax moved this registration's
/// header. One note, filed twice, against two different GSTINs.</para>
///
/// <para>🔴 <b>WHAT COULD NOT BE MEASURED ON <c>main</c>, SAID PLAINLY RATHER THAN GLOSSED.</b> On <c>main</c> this
/// emitter took no registration and called <c>Gstr1.Build(company, from, to)</c>, so for a multi-registration
/// company it <b>threw</b> <c>GstReportSupport.EnsureRegistrationScoped</c>'s refusal — the payload was not wrong,
/// it was <b>unreachable</b>. The wrong rupee figure is therefore measured on the PROJECTION (reachable today
/// through the filing screen's F3 picker, <c>GstOfflineReturnsViewModel.ProjectGstr1</c>) and recorded in that
/// file's summary: Karnataka's <c>TotalIgst</c> read <b>−₹1,800.00</b> against a true ₹0.00, and Table 9B held two
/// rows instead of one. What THIS file adds is that the scoped figures survive serialisation — asserted by
/// reverting the filter, which reddens every test below by name.</para>
///
/// <para><b>🔴 Figures by hand.</b> Karnataka primary (home State 29) sells ₹50,000 intra-State at 18% ⇒ CGST 4,500
/// + SGST 4,500, then issues an <b>UNLINKED</b> sales-return credit note of ₹20,000 ⇒ 1,800 per head on the DEBIT
/// side. The <b>Tamil Nadu</b> branch registration issues a <b>LINKED</b> §34 credit note of ₹10,000 inter-State
/// at 18% ⇒ IGST 1,800, reference <c>TN/S/7</c>. In integer paisa (ER-10) Karnataka's payload must carry
/// <c>total_cgst_paisa</c> / <c>total_sgst_paisa</c> <b>270000</b>, <c>total_igst_paisa</c> <b>0</b>, and a
/// <c>cdnr</c> array of <b>one</b> row with <c>orig_inum</c> null. Tamil Nadu's must carry <c>total_igst_paisa</c>
/// <b>−180000</b> and its own one <c>cdnr</c> row naming <c>TN/S/7</c>.</para>
///
/// <para><b>🔴 WHAT THIS FILE DOES NOT CLAIM.</b> Exactly as <c>Gstr1Gstr3bJsonTests</c> records, the GSTN GSTR-1
/// upload payload schema is published only behind the authenticated GST developer portal, so these <i>key names</i>
/// remain a <b>documented divergence, labelled as ours</b>, and nothing here joins the compared set as
/// portal-accepted. What is asserted is the one thing independent of the key names: <b>the figures the artefact
/// carries, and whose GSTIN it carries them under.</b></para>
///
/// <para><b>Sources (R7), opened by content.</b> "<i>If you have multiple registrations, select the required GST
/// Registration</i>" — <c>help.tallysolutions.com/upload-gstr-1/</c>. CGST Act §34(2) —
/// <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/acts/2017_CGST_act/active/chapter7/section34_v1.00.html</c>
/// — makes declaring the note the duty of the person "<i>who issues</i>" it, so it is owed by its own
/// registration's return.</para>
/// </summary>
public sealed class Gstr1RegistrationScopeEmittedJsonTests
{
    private const string Karnataka = "29";
    private const string TamilNadu = "33";
    private const string BranchNoteReference = "TN/S/7";

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);
    private static readonly DateOnly SaleDate = new(2025, 4, 9);
    private static readonly DateOnly ReturnDate = new(2025, 4, 20);
    private static readonly DateOnly BranchNoteDate = new(2025, 4, 22);

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Karnataka, "AAPFU0939F");
    private static readonly string GstinBranch = GstinFor(TamilNadu, "AABCC1206D");
    private static readonly string GstinDebtorKa = GstinFor(Karnataka, "AAACS1206D");
    private static readonly string GstinDebtorTn = GstinFor(TamilNadu, "AADCS1206D");

    /// <summary>
    /// 🔴 <b>THE FILED HEADER.</b> Karnataka made no inter-State supply, so <c>total_igst_paisa</c> must be 0. The
    /// CGST/SGST pair is the sale net of Karnataka's own return and is asserted alongside so a filter that
    /// over-reached and dropped this registration's own rows would fail too.
    /// </summary>
    [Fact]
    public void The_emitted_payload_header_carries_only_this_registrations_section_34_tax()
    {
        var (company, _) = Build();

        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(company, From, To, GstRegistration.PrimaryId));
        var root = doc.RootElement;

        Assert.Equal(0L, root.GetProperty("total_igst_paisa").GetInt64());
        Assert.Equal(270_000L, root.GetProperty("total_cgst_paisa").GetInt64());
        Assert.Equal(270_000L, root.GetProperty("total_sgst_paisa").GetInt64());
    }

    /// <summary>
    /// 🔴 <b>THE FILED <c>cdnr</c> SECTION.</b> Exactly one row — Karnataka's own unlinked note, whose
    /// <c>orig_inum</c> is null by construction — and the other registration's reference nowhere in the bytes.
    /// </summary>
    [Fact]
    public void The_emitted_cdnr_section_holds_only_this_registrations_note()
    {
        var (company, _) = Build();

        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(company, From, To, GstRegistration.PrimaryId));
        var cdnr = doc.RootElement.GetProperty("cdnr");

        Assert.Equal(1, cdnr.GetArrayLength());
        var row = cdnr[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("orig_inum").ValueKind);
        Assert.Equal(-180_000L, row.GetProperty("camt_paisa").GetInt64());
        Assert.Equal(-180_000L, row.GetProperty("samt_paisa").GetInt64());
        Assert.Equal(0L, row.GetProperty("iamt_paisa").GetInt64());

        Assert.DoesNotContain(
            BranchNoteReference,
            cdnr.EnumerateArray().Select(r => r.GetProperty("orig_inum").ToString()));
    }

    /// <summary>
    /// 🔴 <b>THE ENVELOPE NAMES THE REGISTRATION BEING FILED FOR, NOT ALWAYS THE PRIMARY.</b> Scoping the figures
    /// while emitting them under the primary's GSTIN would be a worse artefact than the one this replaces: the
    /// payload would be internally correct and filed against the wrong taxpayer.
    /// </summary>
    [Fact]
    public void The_emitted_envelope_gstin_is_the_registrations_own()
    {
        var (company, branch) = Build();

        using var primary = JsonDocument.Parse(GstReturnJson.Gstr1(company, From, To, GstRegistration.PrimaryId));
        using var tamilNadu = JsonDocument.Parse(GstReturnJson.Gstr1(company, From, To, branch.Id));

        Assert.Equal(GstinPrimary, primary.RootElement.GetProperty("gstin").GetString());
        Assert.Equal(GstinBranch, tamilNadu.RootElement.GetProperty("gstin").GetString());
    }

    /// <summary>
    /// 🔴 <b>THE NOTE IS NOT LOST — IT IS FILED ONCE, BY ITS OWNER, IN THE BYTES.</b> The defect was a DUPLICATE, so
    /// a fix that scoped the note out of both payloads would breach §34(2)'s first limb (the issuer "shall declare
    /// the details"). Asserted deliberately rather than assumed from the projection test.
    /// </summary>
    [Fact]
    public void The_owning_registrations_payload_still_declares_its_own_note()
    {
        var (company, branch) = Build();

        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(company, From, To, branch.Id));
        var root = doc.RootElement;

        Assert.Equal(-180_000L, root.GetProperty("total_igst_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("total_cgst_paisa").GetInt64());

        var row = Assert.Single(root.GetProperty("cdnr").EnumerateArray().ToList());
        Assert.Equal(BranchNoteReference, row.GetProperty("orig_inum").GetString());
        Assert.Equal(-180_000L, row.GetProperty("iamt_paisa").GetInt64());
    }

    /// <summary>
    /// <b>ER-13 at the artefact.</b> A single-registration book emits byte-for-byte what it emitted before the
    /// parameter existed: the default <c>null</c> argument must reach the same payload as no argument did. Asserted
    /// on the raw bytes, not on parsed figures, because "the same numbers" is a weaker claim than "the same file".
    /// </summary>
    [Fact]
    public void A_single_registration_book_emits_identical_bytes_with_and_without_the_scope()
    {
        var company = BuildSingleRegistrationBook();

        Assert.Equal(
            GstReturnJson.Gstr1(company, From, To),
            GstReturnJson.Gstr1(company, From, To, GstRegistration.PrimaryId));
    }

    // ================================================================ fixture

    /// <summary>
    /// A Karnataka primary Regular registration and a Tamil Nadu Regular branch. Karnataka: a ₹50,000 intra-State
    /// sale at 18% and an UNLINKED ₹20,000 sales-return credit note. Tamil Nadu: a LINKED §34 credit note of
    /// ₹10,000 inter-State at 18%, reference <c>TN/S/7</c> — the document that leaked into Karnataka's payload.
    /// <para>Posted ledger-only (no inventory): the <c>cdnr</c> section and the header totals read off the posted
    /// tax lines, so stock lines would add noise without adding coverage.</para>
    /// </summary>
    private static (Company Company, GstRegistration Branch) Build()
    {
        var c = CompanyFactory.CreateSeeded("Multi GSTIN Io Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinBranch,
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        var ledgers = new LedgerService(c);
        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var debtorKa = AddLedger(c, "Karnataka Debtor", "Sundry Debtors", true);
        debtorKa.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorKa, StateCode = Karnataka,
        };
        var debtorTn = AddLedger(c, "Tamil Nadu Debtor", "Sundry Debtors", true);
        debtorTn.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorTn, StateCode = TamilNadu,
        };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var creditNoteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;

        PostOutward(ledgers, gst, salesType, sales, debtorKa, 50_000m, false, false, SaleDate, null);
        PostOutward(ledgers, gst, creditNoteType, sales, debtorKa, 20_000m, false, true, ReturnDate, null);

        var branchNoteId = PostOutward(
            ledgers, gst, creditNoteType, sales, debtorTn, 10_000m, true, true, BranchNoteDate, branch.Id);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), branchNoteId, CdnType.Credit, null, BranchNoteReference, SaleDate,
            "01 sales return", is9BTarget: true));

        return (c, branch);
    }

    /// <summary>The same book with ONE registration, for the ER-13 byte-identity check.</summary>
    private static Company BuildSingleRegistrationBook()
    {
        var c = CompanyFactory.CreateSeeded("Single GSTIN Io Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var ledgers = new LedgerService(c);
        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var debtorKa = AddLedger(c, "Karnataka Debtor", "Sundry Debtors", true);
        debtorKa.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorKa, StateCode = Karnataka,
        };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var creditNoteType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;

        PostOutward(ledgers, gst, salesType, sales, debtorKa, 50_000m, false, false, SaleDate, null);
        var noteId = PostOutward(
            ledgers, gst, creditNoteType, sales, debtorKa, 20_000m, false, true, ReturnDate, null);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), noteId, CdnType.Credit, null, BranchNoteReference, SaleDate,
            "01 sales return", is9BTarget: true));

        return c;
    }

    /// <summary>Posts one outward document of <paramref name="value"/> at 18%, returning its voucher id.
    /// <paramref name="reverseSides"/> mirrors <c>ComputeItemInvoiceGst</c>'s own <c>reverseSides: IsReturnNote</c>,
    /// so a return note genuinely DEBITS the Output head — the real entry screen's shape.</summary>
    private static Guid PostOutward(
        LedgerService ledgers, GstService gst, Guid typeId, Domain.Ledger sales, Domain.Ledger party,
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

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
