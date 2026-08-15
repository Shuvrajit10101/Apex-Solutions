using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W0-15 review — the FILED place of supply, covered from the project that OWNS the report.</b>
///
/// <para>W0-15 changed two GSTR-1 call sites from the raw s.10(1)(ca) ladder
/// (<see cref="GstReportSupport.PlaceOfSupply"/>) to the reconciled
/// <see cref="GstReportSupport.IssuedPlaceOfSupply"/>: <b>Table 4/7 B2B</b> and <b>Table 9B</b> (credit/debit notes).
/// Only the first had a red proof, and it lived in <c>Apex.Desktop.Tests</c> — so anyone running
/// <c>Apex.Ledger.Tests</c>, the project that owns <c>Gstr1.cs</c>, saw a green suite over BOTH, and reverting the
/// Table 9B call site left the ENTIRE repository green (measured). A credit note against an IGST invoice whose party
/// State was later cleared is the identical reachable shape the slice exists to fix, and Table 9B is a FILED return
/// row.</para>
///
/// <para><b>RED PROOFS (each measured, one mutation at a time).</b>
/// <list type="bullet">
/// <item>Revert <c>Gstr1.cs</c>'s Table 4/7 call to <c>GstReportSupport.PlaceOfSupply(company, voucher)</c> ⇒
/// <see cref="The_b2b_row_files_the_place_of_supply_the_document_states_not_the_raw_party_ladder"/> fails: the return
/// labels the voucher with the supplier's own State <c>27</c>.</item>
/// <item>Revert the Table 9B call the same way ⇒
/// <see cref="Table_9b_files_the_place_of_supply_the_document_states_not_the_raw_party_ladder"/> fails, for the same
/// reason on the note.</item>
/// <item>Delete <c>IssuedPlaceOfSupply</c>'s reduction of an unnameable code to <c>null</c> ⇒
/// <see cref="A_state_code_the_state_master_cannot_name_is_filed_by_no_return_row"/> fails: the return files
/// <c>"24 "</c>, a code no State master contains, while the reprint of the same voucher prints nothing.</item>
/// </list></para>
///
/// <para><b>Odd to the paisa throughout</b> — ₹41,317.63 and ₹4,317.63 — and every tax figure is read off the engine
/// rather than hand-picked, so a fixture cannot agree with the code by construction.</para>
/// </summary>
public sealed class Gstr1IssuedPlaceOfSupplyTests
{
    private const string HomeState = "27";                   // Maharashtra — the SUPPLIER's own State
    private const string PartyState = "24";                  // Gujarat — the buyer's
    private const string GstinHome = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private static readonly DateOnly NoteDate = new(2025, 4, 25);
    private static readonly DateOnly ToEnd = new(2026, 3, 31);

    private static readonly Money SaleValue = Money.FromRupees(41_317.63m);
    private static readonly Money NoteValue = Money.FromRupees(4_317.63m);

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required Domain.Ledger Sales { get; init; }
        public required Domain.Ledger Party { get; init; }
        public required Voucher Sale { get; init; }
    }

    private static Fx Build(string? partyStateCode = PartyState, bool interState = true)
    {
        var c = CompanyFactory.CreateSeeded("Filed POS Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = HomeState, Gstin = GstinHome, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var sales = Add(c, "Sales", "Sales Accounts", openingIsDebit: false);
        var party = Add(c, "Buyer", "Sundry Debtors", openingIsDebit: true);
        party.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = partyStateCode };

        var sale = PostSale(c, sales, party, SaleValue, interState);
        return new Fx { Company = c, Sales = sales, Party = party, Sale = sale };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Voucher PostSale(Company c, Domain.Ledger sales, Domain.Ledger party, Money value, bool interState)
    {
        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(value, 1800) }, interState, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(party.Id, new Money(value.Amount + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, value, DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var typeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), typeId, SaleDate, lines, partyId: party.Id));
    }

    /// <summary>Posts a §34 credit note against <paramref name="original"/> and returns it.</summary>
    private static Voucher PostCreditNote(Fx f, Voucher original)
    {
        var svc = new CreditDebitNoteService(f.Company);
        var noteId = Guid.NewGuid();
        var posting = svc.BuildCreditDebitNote(
            CdnType.Credit, new[] { new GstService.TaxableLine(NoteValue, 1800) }, interState: true, noteId,
            original.Id, "INV-POS-1", SaleDate, NoteDate, reasonCode: "01 sales return");

        var total = new Money(NoteValue.Amount + posting.Computed.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(f.Sales.Id, NoteValue, DrCr.Debit),
            new(f.Party.Id, total, DrCr.Credit),
        };
        lines.AddRange(posting.TaxLines);
        var typeId = f.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id;
        return new LedgerService(f.Company).Post(
            new Voucher(noteId, typeId, NoteDate, lines, partyId: f.Party.Id));
    }

    private static Reports.Gstr1 Filed(Fx f) => Gstr1.Build(f.Company, FyStart, ToEnd);

    private static decimal PostedHead(Voucher v, GstTaxHead head) =>
        v.Lines.Where(l => l.Gst?.TaxHead == head).Sum(l => l.Amount.Amount);

    // ================================================================ Table 4/7 — B2B

    /// <summary>
    /// Clear an IGST-bearing invoice's party State — permitted; nothing validates it — and the raw ladder falls back
    /// to the company home State, so the return labels an inter-state supply with the SUPPLIER's own State. NIC
    /// e-invoice validation 24 ("the state code of the Supplier GSTIN and POS will decide whether the supply type is
    /// Interstate or Intrastate") makes that self-refuting.
    /// </summary>
    [Fact]
    public void The_b2b_row_files_the_place_of_supply_the_document_states_not_the_raw_party_ladder()
    {
        var f = Build();
        Assert.True(PostedHead(f.Sale, GstTaxHead.Integrated) > 0m);      // the books really do carry IGST

        // Control — before anything drifts, the filed POS is the buyer's own State.
        Assert.Equal(PartyState, Assert.Single(Filed(f).B2B).PlaceOfSupplyStateCode);

        f.Party.PartyGst!.StateCode = null;

        var row = Assert.Single(Filed(f).B2B);
        Assert.NotEqual(HomeState, row.PlaceOfSupplyStateCode);           // ← the raw ladder filed "27" here
        Assert.Null(row.PlaceOfSupplyStateCode);
        // …and it is exactly what the document itself states, which is the whole point of one shared rule.
        Assert.Equal(GstReportSupport.IssuedPlaceOfSupply(f.Company, f.Sale), row.PlaceOfSupplyStateCode);
    }

    // ================================================================ Table 9B — credit/debit notes

    /// <summary>
    /// The same shape on a §34 note. Table 9B is a FILED return row and its call site had NO test at all: reverting it
    /// to <c>PlaceOfSupply</c> left <c>Apex.Ledger.Tests</c> and <c>Apex.Desktop.Tests</c> both fully green.
    /// </summary>
    [Fact]
    public void Table_9b_files_the_place_of_supply_the_document_states_not_the_raw_party_ladder()
    {
        var f = Build();
        var note = PostCreditNote(f, f.Sale);
        Assert.True(PostedHead(note, GstTaxHead.Integrated) > 0m);        // the note carries IGST too

        // Control — undrifted, the note files the buyer's State exactly as before (ER-13).
        Assert.Equal(PartyState, Assert.Single(Filed(f).Table9B).PlaceOfSupplyStateCode);

        f.Party.PartyGst!.StateCode = null;

        var row = Assert.Single(Filed(f).Table9B);
        Assert.NotEqual(HomeState, row.PlaceOfSupplyStateCode);           // ← the raw ladder filed "27" here
        Assert.Null(row.PlaceOfSupplyStateCode);
        Assert.Equal(GstReportSupport.IssuedPlaceOfSupply(f.Company, note), row.PlaceOfSupplyStateCode);
    }

    // ================================================================ a code the master cannot name

    /// <summary>
    /// <b>Sharing one VALUE between the paper and the return was not enough on its own.</b> The print path renders a
    /// State code through <c>IndianState.FromCode</c> — an exact dictionary lookup that does not trim — while GSTR-1
    /// files the raw string. A party State of <c>"24 "</c> (a trailing space) against a home of <c>"27"</c> therefore
    /// posted IGST, printed NOTHING, and filed <c>"24 "</c>: two answers again, one of them a code no State master
    /// contains. <c>IssuedPlaceOfSupply</c> now reduces an unnameable code to <c>null</c>, so neither surface states
    /// it.
    /// <para>The padded code being accepted onto the master at all is a separate, still-open input-validation defect,
    /// and is deliberately NOT asserted here as though this slice fixed it. Nothing trims: trimming would flip
    /// <c>GstService.IsInterState("24 ")</c> and re-route the TAX.</para>
    /// </summary>
    [Fact]
    public void A_state_code_the_state_master_cannot_name_is_filed_by_no_return_row()
    {
        var f = Build(partyStateCode: PartyState + " ");
        Assert.True(PostedHead(f.Sale, GstTaxHead.Integrated) > 0m);      // posted INTER, on a space

        Assert.Null(IndianState.FromCode(PartyState + " "));             // the premise, stated rather than assumed
        var row = Assert.Single(Filed(f).B2B);
        Assert.Null(row.PlaceOfSupplyStateCode);                          // ← filed "24 " before the reduction
        Assert.Equal(GstReportSupport.IssuedPlaceOfSupply(f.Company, f.Sale), row.PlaceOfSupplyStateCode);
    }

    // ================================================================ ER-13 — the undrifted book is untouched

    /// <summary>
    /// Every voucher whose party was never edited must file exactly what it filed before W0-15 — the reconciliation
    /// and the raw ladder agree on it by construction. Without this, "the return now says nothing" would be a passing
    /// answer to every case above.
    /// </summary>
    [Theory]
    [InlineData(true, PartyState)]     // inter-state ⇒ the buyer's State
    [InlineData(false, HomeState)]     // intra-state ⇒ the supplier's, which is what CGST+SGST already asserts
    public void An_undrifted_voucher_files_exactly_what_the_raw_ladder_filed(bool interState, string expected)
    {
        var f = Build(partyStateCode: interState ? PartyState : HomeState, interState: interState);

        var row = Assert.Single(Filed(f).B2B);
        Assert.Equal(expected, row.PlaceOfSupplyStateCode);
        Assert.Equal(GstReportSupport.PlaceOfSupply(f.Company, f.Sale), row.PlaceOfSupplyStateCode);
    }
}
