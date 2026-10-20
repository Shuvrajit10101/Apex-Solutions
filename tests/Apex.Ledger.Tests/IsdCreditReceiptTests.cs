using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>ITC received from an Input Service Distributor — GSTR-3B Table 4(A)(4) "Inward supplies from ISD"</b>
/// (census row 6.24).
///
/// <para><b>Grounding (R7, ruling 14 tier 1), read BY CONTENT.</b>
/// <c>help.tallysolutions.com/tally-prime/india-gst/isd-credit/</c>: <i>"You can account for Input Service
/// Distributor Credit (ISD) Credit in TallyPrime when your branch receives Input Tax Credit distributed by the
/// head office"</i>, on a Journal whose Stat Adjustment is <i>Nature of Adjustment</i> = <i>"Increase in Input
/// Tax Credit"</i> with <i>Additional Nature of Adjustment</i> = <i>"ISD Invoice/Debit Note"</i>; and the
/// destination, verbatim: <i>"The voucher will be included in GSTR-3B and appear under 4A: Input Tax Credit
/// Available (either in part or in full) &gt; Inward supplies from ISD."</i></para>
///
/// <para>The Table 4(C) formula is CBIC Circular No. 170/02/2022-GST, Annexure, row <i>"(C) Net ITC Available
/// (A)-(B)"</i>: <c>C = A1+A2+A3+A4+A5-B1-B2</c>. <b>A4 is the ISD term</b>, which is why these tests assert it
/// moves 4(C) and not merely a display field.</para>
///
/// <para><b>THE HAND-COMPUTED ARITHMETIC every test below asserts against.</b> The head office distributes to the
/// Karnataka branch: <b>CGST 1,500.00 + SGST 1,500.00 + IGST 2,000.00 + Cess 500.00 = ₹5,500.00</b>, of which
/// ₹5,000.00 is the three GST heads (cess is ring-fenced, ER-2, so <c>TotalItc4A4Isd</c> is 5,000.00, not
/// 5,500.00). Every figure below is that split or a sum of it; none was read off the engine first.</para>
///
/// <para>🔴 <b>WHAT IS DELIBERATELY NOT TESTED HERE, BECAUSE IT IS DELIBERATELY NOT BUILT.</b> Credit
/// <b>distribution</b> and the GSTR-6 return. Measured across three of the vendor's own pages: the ISD page
/// above describes only credit <i>received</i> and never mentions GSTR-6; the GST-returns FAQ
/// (<c>help.tallysolutions.com/india-gst-faq-tally/</c>) enumerates GSTR-1, GSTR-2/2A/2B, GSTR-3B, GSTR-9/9C and
/// CMP-08 with <b>no GSTR-6</b>, and states <i>"Using TallyPrime Release 5.0, you can upload GSTR-1, GSTR-3B,
/// and CMP-08."</i>; the release-wise feature list
/// (<c>help.tallysolutions.com/tallyprime-features-release-wise/</c>) contains no occurrence of "GSTR-6", "ISD"
/// or "Input Service Distributor".</para>
/// </summary>
public class IsdCreditReceiptTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2025, 5, 1);
    private static readonly DateOnly To = new(2025, 5, 31);
    private static readonly DateOnly DocDate = new(2025, 5, 10);
    private static readonly DateOnly PostDate = new(2025, 5, 15);

    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    // The distributed credit, in paisa — the one place these figures are written down.
    private const long Cgst = 150_000L;   // ₹1,500.00
    private const long Sgst = 150_000L;   // ₹1,500.00
    private const long Igst = 200_000L;   // ₹2,000.00
    private const long Cess = 50_000L;    // ₹  500.00

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Karnataka, "AAPFU0939F");
    private static readonly string GstinBranch = GstinFor(TamilNadu, "AABCC1206D");

    private sealed class Fixture
    {
        public required Company Company { get; init; }
        public required GstRegistration Branch { get; init; }
        public required Domain.Ledger HeadOffice { get; init; }
        public required GstIsdCreditService Service { get; init; }
    }

    /// <summary>A Karnataka primary registration with a Tamil Nadu branch beside it, and a "Head Office Account"
    /// ledger to take the credit against — the vendor's <i>"branch account ledger"</i>.</summary>
    private static Fixture Build()
    {
        var c = CompanyFactory.CreateSeeded("ISD Receipt Co", FyStart);
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

        var group = c.Groups.First(g => g.Name == "Indirect Expenses");
        var headOffice = new Domain.Ledger(Guid.NewGuid(), "Head Office Account", group.Id, Money.Zero, true);
        c.AddLedger(headOffice);

        return new Fixture
        {
            Company = c, Branch = branch, HeadOffice = headOffice,
            Service = new GstIsdCreditService(c),
        };
    }

    private static GstIsdCreditService.IsdCreditAmount Amount =>
        new(Cgst, Sgst, Igst, Cess);

    private static Voucher Record(Fixture f, string docNo = "ISD/2025-26/001", Guid? registrationId = null) =>
        f.Service.Post(docNo, DocDate, Amount, f.HeadOffice.Id, PostDate, registrationId);

    // ==============================================================================================================
    //  1. THE FILED FIGURE — Table 4(A)(4), and NOT Table 4(A)(5)
    // ==============================================================================================================

    /// <summary>
    /// 🔴 <b>THE MONEY TEST.</b> The credit files in Table 4(A)(4) at the hand-computed split, and <b>Table
    /// 4(A)(5) "All other ITC" does not move</b> — the vendor names 4(A)(4) specifically, so folding it into "all
    /// other ITC" would file the right total under the wrong row.
    /// </summary>
    [Fact]
    public void An_isd_receipt_files_in_table_4a4_at_its_own_split_and_leaves_4a5_untouched()
    {
        var f = Build();

        var before = Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId);
        Assert.Equal(0m, before.TotalItc4A4Isd.Amount);

        Record(f);

        var after = Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId);

        // Table 4(A)(4), per head — the hand-computed split.
        Assert.Equal(1_500m, after.Itc4A4IsdCgst.Amount);
        Assert.Equal(1_500m, after.Itc4A4IsdSgst.Amount);
        Assert.Equal(2_000m, after.Itc4A4IsdIgst.Amount);
        Assert.Equal(500m, after.Itc4A4IsdCess.Amount);

        // Σ excludes cess (ER-2), exactly as TotalItc and TotalItcReversed do: 1,500 + 1,500 + 2,000 = 5,000.
        Assert.Equal(5_000m, after.TotalItc4A4Isd.Amount);

        // 🔴 4(A)(5) MUST NOT MOVE. An ISD receipt is a Journal-base stat adjustment, which ReadSide excludes —
        // this is the assertion that keeps it excluded.
        Assert.Equal(before.ItcCgst.Amount, after.ItcCgst.Amount);
        Assert.Equal(before.ItcSgst.Amount, after.ItcSgst.Amount);
        Assert.Equal(before.ItcIgst.Amount, after.ItcIgst.Amount);
        Assert.Equal(before.ItcReportedAllOtherCgst.Amount, after.ItcReportedAllOtherCgst.Amount);
    }

    /// <summary>
    /// 🔴 <b>Table 4(C) "Net ITC Available" moves by exactly the ISD credit</b> — the A4 term of the Annexure's
    /// <c>C = A1+A2+A3+A4+A5-B1-B2</c>. Without this the figure would be a display-only field: shown on the
    /// screen, absent from the credit the return actually claims.
    /// </summary>
    [Fact]
    public void Table_4c_net_itc_available_moves_by_exactly_the_isd_credit()
    {
        var f = Build();

        var before = Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId);
        Record(f);
        var after = Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(before.NetItcAvailableCgst.Amount + 1_500m, after.NetItcAvailableCgst.Amount);
        Assert.Equal(before.NetItcAvailableSgst.Amount + 1_500m, after.NetItcAvailableSgst.Amount);
        Assert.Equal(before.NetItcAvailableIgst.Amount + 2_000m, after.NetItcAvailableIgst.Amount);
    }

    /// <summary>
    /// The posted voucher is the observable artefact, so it is asserted on directly: a balanced stat-adjustment
    /// Journal, one <b>debit</b> leg per non-zero head tagged <see cref="GstAdjustmentKind.IsdCreditReceived"/>,
    /// against a single credit of the counterpart ledger for the total. The debit side is what makes it an
    /// INCREASE in credit — the opposite side from every reversal.
    /// </summary>
    [Fact]
    public void The_posted_voucher_is_a_balanced_stat_adjustment_with_one_tagged_debit_per_head()
    {
        var f = Build();
        var v = Record(f);

        var type = f.Company.FindVoucherType(v.TypeId);
        Assert.NotNull(type);
        Assert.True(type!.IsGstStatAdjustmentType);
        Assert.Equal(VoucherBaseType.Journal, type.BaseType);

        var tagged = v.Lines
            .Where(l => l.Gst is { Adjustment: GstAdjustmentKind.IsdCreditReceived })
            .ToList();
        Assert.Equal(4, tagged.Count);
        Assert.All(tagged, l => Assert.Equal(DrCr.Debit, l.Side));

        decimal Head(GstTaxHead h) => tagged.Single(l => l.Gst!.TaxHead == h).Amount.Amount;
        Assert.Equal(1_500m, Head(GstTaxHead.Central));
        Assert.Equal(1_500m, Head(GstTaxHead.State));
        Assert.Equal(2_000m, Head(GstTaxHead.Integrated));
        Assert.Equal(500m, Head(GstTaxHead.Cess));

        var credit = Assert.Single(v.Lines.Where(l => l.Side == DrCr.Credit));
        Assert.Equal(f.HeadOffice.Id, credit.LedgerId);
        Assert.Equal(5_500m, credit.Amount.Amount);

        // Balanced — the one property a posted voucher may never lose.
        Assert.Equal(
            v.Lines.Where(l => l.Side == DrCr.Debit).Sum(l => l.Amount.Amount),
            v.Lines.Where(l => l.Side == DrCr.Credit).Sum(l => l.Amount.Amount));
    }

    /// <summary>
    /// The credit lands in the <b>electronic credit ledger pool</b>, not merely in a report field: the
    /// <c>Input {head}</c> debit balance — which <see cref="ElectronicLedgersView"/> projects AS the credit
    /// ledger — rises by the distributed amount, and the movement decomposition books it as an ADDITION rather
    /// than a utilisation or a reversal.
    /// </summary>
    [Fact]
    public void The_isd_credit_increases_the_electronic_credit_ledger_pool_as_an_addition()
    {
        var f = Build();

        var before = ElectronicLedgersView.Build(f.Company, From, To);
        Record(f);
        var after = ElectronicLedgersView.Build(f.Company, From, To);

        Assert.Equal(before.CreditCgst.Amount + 1_500m, after.CreditCgst.Amount);
        Assert.Equal(before.CreditIgst.Amount + 2_000m, after.CreditIgst.Amount);

        Assert.Equal(before.CreditAdditionsCgst.Amount + 1_500m, after.CreditAdditionsCgst.Amount);
        Assert.Equal(before.CreditReversedCgst.Amount, after.CreditReversedCgst.Amount);
        Assert.Equal(before.CreditUtilisedCgst.Amount, after.CreditUtilisedCgst.Amount);
    }

    // ==============================================================================================================
    //  2. SCOPE — one registration's credit is not another's
    // ==============================================================================================================

    /// <summary>
    /// 🔴 ITC lands in the electronic credit ledger of exactly ONE GSTIN, so a branch's ISD receipt appears in
    /// <b>its own</b> Table 4(A)(4) and in <b>no sister return's</b>. Unscoped, the same ₹5,000.00 would be
    /// claimed on two filed returns.
    /// </summary>
    [Fact]
    public void An_isd_receipt_reaches_only_the_registration_it_was_received_into()
    {
        var f = Build();

        Record(f, registrationId: f.Branch.Id);

        var branch = Gstr3b.Build(f.Company, From, To, f.Branch.Id);
        var primary = Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(5_000m, branch.TotalItc4A4Isd.Amount);
        Assert.Equal(1_500m, branch.Itc4A4IsdCgst.Amount);

        Assert.Equal(0m, primary.TotalItc4A4Isd.Amount);
        Assert.Equal(0m, primary.Itc4A4IsdCgst.Amount);
        Assert.Equal(0m, primary.Itc4A4IsdCess.Amount);
    }

    /// <summary>
    /// ER-13: the primary registration normalises to a <c>null</c> stamp, so a single-registration book stores
    /// exactly what it always stored. The receipt still reads back under the primary.
    /// </summary>
    [Fact]
    public void A_receipt_into_the_primary_stores_no_registration_stamp()
    {
        var f = Build();

        var v = Record(f, registrationId: GstRegistration.PrimaryId);

        Assert.Null(v.GstRegistrationId);
        Assert.Equal(5_000m, Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId).TotalItc4A4Isd.Amount);
    }

    // ==============================================================================================================
    //  3. THE GUARDS — each one named, and each one ours where it is ours
    // ==============================================================================================================

    /// <summary>
    /// The duplicate refusal (OURS — no vendor page settles it). Re-recording the same ISD document number under
    /// the same registration would double a filed Table 4(A)(4) figure and double the credit in the books.
    /// </summary>
    [Fact]
    public void A_duplicate_isd_document_number_is_refused_under_the_same_registration()
    {
        var f = Build();
        Record(f, "ISD/2025-26/007");

        var ex = Assert.Throws<InvalidOperationException>(() => Record(f, "ISD/2025-26/007"));
        Assert.Contains("ISD/2025-26/007", ex.Message, StringComparison.Ordinal);

        // And the refusal held: still exactly one receipt's worth of credit filed.
        Assert.Equal(5_000m, Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId).TotalItc4A4Isd.Amount);
    }

    /// <summary>
    /// …but the SAME number may legitimately reach two different registrations of one company, each with its own
    /// credit. The guard is keyed on (document, registration), not on the document alone.
    /// </summary>
    [Fact]
    public void The_same_document_number_is_accepted_under_a_different_registration()
    {
        var f = Build();
        Record(f, "ISD/2025-26/007", GstRegistration.PrimaryId);
        Record(f, "ISD/2025-26/007", f.Branch.Id);

        Assert.Equal(5_000m, Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId).TotalItc4A4Isd.Amount);
        Assert.Equal(5_000m, Gstr3b.Build(f.Company, From, To, f.Branch.Id).TotalItc4A4Isd.Amount);
    }

    /// <summary>
    /// A CANCELLED receipt claims no credit and files no figure, so it must not lock its document number out
    /// forever — otherwise cancelling a mis-keyed receipt would make the real one unrecordable.
    /// </summary>
    [Fact]
    public void A_cancelled_receipt_does_not_block_re_entry_of_its_document_number()
    {
        var f = Build();
        var first = Record(f, "ISD/2025-26/009");

        first.Cancelled = true;

        // The cancelled receipt files nothing…
        Assert.Equal(0m, Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId).TotalItc4A4Isd.Amount);

        // …and its number is free again.
        Record(f, "ISD/2025-26/009");
        Assert.Equal(5_000m, Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId).TotalItc4A4Isd.Amount);
    }

    /// <summary>A receipt with no credit in any head records nothing and says so.</summary>
    [Fact]
    public void A_zero_receipt_is_refused()
    {
        var f = Build();
        Assert.Throws<ArgumentException>(() => f.Service.Post(
            "ISD/2025-26/010", DocDate, new GstIsdCreditService.IsdCreditAmount(0, 0, 0, 0),
            f.HeadOffice.Id, PostDate));
    }

    /// <summary>A negative head is refused — an ISD credit note is a different document under Rule 39(1)(l) and
    /// is not built, so netting one in here would file a figure no rule produced.</summary>
    [Fact]
    public void A_negative_head_is_refused()
    {
        var f = Build();
        Assert.Throws<ArgumentException>(() => f.Service.Post(
            "ISD/2025-26/011", DocDate, new GstIsdCreditService.IsdCreditAmount(-1, 0, 0, 0),
            f.HeadOffice.Id, PostDate));
    }

    /// <summary>The ISD document number is the audit trail — a blank one is refused.</summary>
    [Fact]
    public void A_blank_document_number_is_refused()
    {
        var f = Build();
        Assert.Throws<ArgumentException>(() => Record(f, "   "));
    }

    // ==============================================================================================================
    //  4. NO BLEED INTO THE REVERSAL TABLES
    // ==============================================================================================================

    /// <summary>
    /// An ISD receipt is an ADDITION, so it must leave Tables 4(B)(1), 4(B)(2) and 4(D)(1) at zero. A tag that
    /// fell through into a reversal bucket would reverse credit the operator had just received.
    /// </summary>
    [Fact]
    public void An_isd_receipt_enters_no_reversal_or_reclaim_table()
    {
        var f = Build();
        Record(f);

        var b = Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(0m, b.ItcReversed4B1Cgst.Amount);
        Assert.Equal(0m, b.ItcReversed4B1Sgst.Amount);
        Assert.Equal(0m, b.ItcReversed4B1Igst.Amount);
        Assert.Equal(0m, b.ItcReversed4B1Cess.Amount);
        Assert.Equal(0m, b.ItcReversed4B2Cgst.Amount);
        Assert.Equal(0m, b.ItcReversed4B2Igst.Amount);
        Assert.Equal(0m, b.ItcReclaimed4D1Cgst.Amount);
        Assert.Equal(0m, b.ItcReclaimed4D1Igst.Amount);
        Assert.Equal(0m, b.TotalItcReversed.Amount);
        Assert.Equal(0m, b.TotalItcReclaimed.Amount);
    }

    /// <summary>
    /// ER-13: a book that never receives ISD credit reads exactly as it did before this feature existed — every
    /// 4(A)(4) field zero and 4(C) unchanged by it.
    /// </summary>
    [Fact]
    public void A_book_that_receives_no_isd_credit_is_unchanged()
    {
        var f = Build();
        var b = Gstr3b.Build(f.Company, From, To, GstRegistration.PrimaryId);

        Assert.Equal(0m, b.Itc4A4IsdCgst.Amount);
        Assert.Equal(0m, b.Itc4A4IsdSgst.Amount);
        Assert.Equal(0m, b.Itc4A4IsdIgst.Amount);
        Assert.Equal(0m, b.Itc4A4IsdCess.Amount);
        Assert.Equal(0m, b.TotalItc4A4Isd.Amount);
    }

    /// <summary>A receipt dated outside the return window is not in that window's return.</summary>
    [Fact]
    public void A_receipt_outside_the_window_is_not_in_that_windows_return()
    {
        var f = Build();
        Record(f);

        var april = Gstr3b.Build(f.Company, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30), GstRegistration.PrimaryId);
        Assert.Equal(0m, april.TotalItc4A4Isd.Amount);
    }
}
