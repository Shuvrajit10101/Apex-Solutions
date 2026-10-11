using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>A11 REVIEW FINDING (census 6.23) — GSTR-1 TABLES 11A/11B WERE THE SECOND UNSCOPED LEG OF
/// <c>Gstr1.Build</c>.</b> The wave that scoped Table 9B scoped the §34 notes and left the advance tables walking
/// <c>company.AdvanceReceipts</c> wholesale, so a <b>Tamil Nadu</b> service advance was declared in the
/// <b>Karnataka</b> return: Karnataka's Table 11A carried advance ₹1,00,000 with IGST advance tax ₹18,000 against a
/// true <b>empty</b> table, and <c>AdvanceTaxReceived</c> read ₹18,000 against ₹0.00.
///
/// <para><b>Why it is not merely cosmetic.</b> Until the GSTR-1 emitter took a registration, a multi-registration
/// book's payload <b>threw</b> <c>EnsureRegistrationScoped</c>'s refusal, so the leak lived only on the filing
/// screen's projection. From this wave the payload builds, so the figure reaches the emitted <c>at</c> /
/// <c>atadj</c> sections of the file a taxpayer uploads.</para>
///
/// <para><b>Attribution rule.</b> The advance belongs to the registration whose <b>receipt</b> voucher collected it
/// — that registration owes the tax on receipt (CGST Act §13(2); the Rule 50 receipt voucher is issued by it) — so
/// both 11A and 11B follow the receipt rather than being split across registrations.</para>
///
/// <para><b>Vendor-attested (R7), opened by content:</b> "<i>Press F3 (Company/Tax Registration) and select the
/// registration for which you want to view the report.</i>"
/// (<c>help.tallysolutions.com/gstr-1-report-in-tallyprime/</c>) and "<i>If you have multiple registrations, select
/// the required GST Registration.</i>" (<c>help.tallysolutions.com/upload-gstr-1/</c>).</para>
/// </summary>
public sealed class Gstr1AdvanceTableRegistrationScopeTests
{
    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);
    private static readonly DateOnly ReceiptDate = new(2025, 4, 11);

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    internal sealed class Fixture
    {
        public required Company Company { get; init; }
        public required GstRegistration Branch { get; init; }
    }

    /// <summary>
    /// 🔴 <b>THE MEASURED DEFECT.</b> Karnataka received no advance at all, so its Table 11A must be empty and its
    /// advance tax ₹0.00. Before the fix the table held the Tamil Nadu branch's row and ₹18,000 of IGST.
    /// </summary>
    [Fact]
    public void A_second_registrations_advance_is_not_declared_in_this_registrations_Table_11A()
    {
        var f = Build();

        var r = Gstr1.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Empty(r.Table11A);                                   // was one row
        Assert.Equal(0m, r.AdvanceTaxReceived.Amount);              // was 18,000.00
    }

    /// <summary>
    /// 🔴 <b>THE ADVANCE IS NOT LOST — IT IS DECLARED ONCE, BY ITS OWNER.</b> A filter that scoped the record out of
    /// both returns would replace a double declaration with a missing one, which is the worse artefact.
    /// </summary>
    [Fact]
    public void The_owning_registration_still_declares_its_own_advance()
    {
        var f = Build();

        var r = Gstr1.Build(f.Company, From, To, f.Branch.Id);

        var row = Assert.Single(r.Table11A);
        Assert.True(row.InterState);
        Assert.Equal(1800, row.RateBasisPoints);
        Assert.Equal(100_000m, row.AdvanceReceived.Amount);
        Assert.Equal(18_000m, row.Igst.Amount);
        Assert.Equal(18_000m, r.AdvanceTaxReceived.Amount);
    }

    /// <summary>
    /// 🔴 <b>AT THE ARTEFACT, which is where the figure newly reaches a user.</b> The branch's ₹18,000 (1800000
    /// paisa, ER-10) must appear nowhere in Karnataka's emitted bytes, and must still appear in the branch's own.
    /// </summary>
    [Fact]
    public void The_emitted_advance_section_carries_only_this_registrations_advance()
    {
        var f = Build();

        var ka = System.Text.Encoding.UTF8.GetString(
            GstReturnJson.Gstr1(f.Company, From, To, GstRegistration.PrimaryId));
        var tn = System.Text.Encoding.UTF8.GetString(
            GstReturnJson.Gstr1(f.Company, From, To, f.Branch.Id));

        Assert.DoesNotContain("1800000", ka);
        Assert.Contains("1800000", tn);
    }

    /// <summary>
    /// <b>ER-13.</b> A single-registration book is unchanged: <c>registrationId</c> is <c>null</c>, the filter never
    /// runs, and the advance is declared exactly as it was before the filter existed.
    /// </summary>
    [Fact]
    public void A_single_registration_book_is_unchanged_by_the_advance_scope_filter()
    {
        var c = BuildSingleRegistrationBook();

        var r = Gstr1.Build(c, From, To);

        var row = Assert.Single(r.Table11A);
        Assert.Equal(100_000m, row.AdvanceReceived.Amount);
        Assert.Equal(18_000m, r.AdvanceTaxReceived.Amount);
    }

    // ================================================================ fixture

    /// <summary>A Karnataka primary registration plus a Tamil Nadu branch; the branch receives a ₹1,00,000 service
    /// advance at 18% inter-State ⇒ IGST ₹18,000.</summary>
    internal static Fixture Build()
    {
        var c = NewGstCompany("Advance Multi GSTIN Co");
        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinFor(TamilNadu, "AABCC1206D"),
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        AddServiceAdvance(c, branch.Id);
        return new Fixture { Company = c, Branch = branch };
    }

    private static Company BuildSingleRegistrationBook()
    {
        var c = NewGstCompany("Advance Single GSTIN Co");
        AddServiceAdvance(c, registrationId: null);
        return c;
    }

    private static Company NewGstCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinFor(Karnataka, "AAPFU0939F"),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    /// <summary>Posts the receipt voucher that collects a ₹1,00,000 + ₹18,000 service advance under
    /// <paramref name="registrationId"/>, and records the <see cref="GstAdvanceReceipt"/> against it.</summary>
    private static void AddServiceAdvance(Company c, Guid? registrationId)
    {
        var ledgers = new LedgerService(c);
        var bank = Add(c, "Advance Bank", "Bank Accounts", true);
        var debtor = Add(c, "Advance Debtor", "Sundry Debtors", true);
        var receiptType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id;

        var receiptId = Guid.NewGuid();
        var receipt = new Voucher(receiptId, receiptType, ReceiptDate, new List<EntryLine>
        {
            new(bank.Id, Money.FromRupees(118_000m), DrCr.Debit),
            new(debtor.Id, Money.FromRupees(118_000m), DrCr.Credit),
        }, partyId: debtor.Id);
        if (registrationId is { } r) receipt.GstRegistrationId = r;
        ledgers.Post(receipt);

        c.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), receiptId, isService: true, Money.FromRupees(100_000m), 1800,
            interState: true, TamilNadu, Money.FromRupees(18_000m)));
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
