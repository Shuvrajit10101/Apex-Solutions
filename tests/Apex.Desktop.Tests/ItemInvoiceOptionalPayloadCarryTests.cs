using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Census T1-22 and T1-23 — the two limbs of <c>EntryLine</c>'s optional payload that an item-invoice
/// re-accept still destroyed.</b>
///
/// <para><b>Why one file.</b> They are one defect wearing two faces: the item-invoice inverse is written against
/// WHAT THE SCREEN CAN EXPRESS (a party picker, a stock-ledger picker, an item grid, an additional-cost panel and
/// ONE bill-wise panel bound to the party) rather than WHAT THE VOUCHER CARRIES, and nothing compared the
/// complement. The last test in this file is that missing comparison: a canonical-export byte comparison over an
/// invoice carrying the FULL optional payload, so the NEXT optional field cannot be dropped silently either.</para>
///
/// <para><b>The importer door.</b> Neither shape is keyable on the item-invoice screen — that is precisely why
/// the screen dropped them — so every fixture here posts through the REAL screen and then stamps the extra child
/// through <see cref="LedgerService.Replace"/>, which is the door <c>ImportPlan</c> uses and the same door the
/// census reproduction used. Nothing here hand-builds a voucher the engine would not accept from an import.</para>
/// </summary>
public sealed class ItemInvoiceOptionalPayloadCarryTests
{
    // The nonce set. Nothing is round and no two fields share a value.
    private const string WidgetRate = "1234.57";
    private const string WidgetQty = "2";
    // Derived by hand, never read off the engine. Line value = 2 × 1,234.57 = 2,469.14. GST at 18% on the rate
    // group: 2,469.14 × 1800/10000 = 444.4452 → 444.45 to the paisa, away from zero. The intra split derives from
    // that total so the pair foots: CGST = round(444.45 / 2) = round(222.225) = 222.23, SGST = 444.45 − 222.23 =
    // 222.22. Party total = 2,469.14 + 444.45 = 2,913.59.
    private const decimal TaxableValue = 2469.14m;
    private const decimal PartyTotal = 2913.59m;

    private const string ChequeNo = "CHQ-90210";
    private const string ValueLegBillRef = "VALUE-LEG-REF";

    private sealed class Kit
    {
        public required AlterationBook Book { get; init; }
        public required StockItem Widget { get; init; }
        public required Godown Main { get; init; }
        public required DomainLedger Bank { get; init; }
        public required DomainLedger Supplier { get; init; }
        public required DomainLedger Purchases { get; init; }
        public required VoucherType PurchaseType { get; init; }
    }

    /// <summary>
    /// A purchase book whose VALUE ledger maintains balances bill-by-bill (legal — <c>EnsureBillAllocationsValid</c>
    /// gates only on that flag and on the split footing the line, neither of which is party-specific) and which
    /// offers a BANK ledger as a party (legal — the party picker is <i>"(none)" + every ledger</i>).
    /// </summary>
    private static Kit Seed(AlterationBook book, bool valueLegBillWise)
    {
        var c = book.Company;
        book.EnableGst();

        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 3);
        var widget = masters.CreateStockItem("Widget", group.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var kit = new Kit
        {
            Book = book,
            Widget = widget,
            Main = c.MainLocation!,
            Bank = book.Ledger("Nonce Bank Current A/c", "Bank Accounts"),
            Supplier = book.Ledger("Nonce Suppliers", "Sundry Creditors"),
            Purchases = book.Ledger("Purchases", "Purchase Accounts", billWise: valueLegBillWise),
            PurchaseType = book.Type(VoucherBaseType.Purchase),
        };
        book.Storage.Save(c);
        return kit;
    }

    private static VoucherEntryViewModel NewEntry(Kit kit)
    {
        var entry = new VoucherEntryViewModel(
            kit.Book.Company, kit.PurchaseType, kit.Book.Storage,
            onSaved: () => { }, onCancelled: () => { }, kit.Book.On());
        entry.Mode = VoucherEntryMode.ItemInvoice;
        return entry;
    }

    /// <summary>
    /// Posts the specimen invoice through the REAL screen: one Widget row, 2 @ 1234.57, on
    /// <paramref name="party"/>. See the constants above for the hand-derived figures.
    /// </summary>
    private static Voucher PostInvoice(Kit kit, DomainLedger party, string narration = "Nonce narration ONE")
    {
        var entry = NewEntry(kit);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == party.Id);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == kit.Purchases.Id);
        entry.Narration = narration;

        var row = entry.InventoryLines[0];
        row.SelectedItem = entry.StockItems.Single(i => i.Id == kit.Widget.Id);
        row.SelectedGodown = entry.Godowns.Single(g => g.Id == kit.Main.Id);
        row.QuantityText = WidgetQty;
        row.RateText = WidgetRate;

        Assert.True(entry.Accept(), entry.Message);
        return kit.Book.Company.Vouchers.Last(v => v.TypeId == kit.PurchaseType.Id);
    }

    /// <summary>
    /// Re-posts <paramref name="posted"/> through <see cref="LedgerService.Replace"/> with
    /// <paramref name="rewrite"/> applied to every line — the importer-equivalent door, and the only way to put a
    /// child on a leg this screen has no panel for.
    /// </summary>
    private static Voucher Restamp(AlterationBook book, Voucher posted, Func<EntryLine, EntryLine> rewrite)
    {
        var service = new LedgerService(book.Company);
        var replacement = new Voucher(
            posted.Id, posted.TypeId, posted.Date, posted.Lines.Select(rewrite).ToList(),
            number: posted.Number, narration: posted.Narration, partyId: posted.PartyId,
            cancelled: posted.Cancelled, optional: posted.Optional, postDated: posted.PostDated,
            applicableUpto: posted.ApplicableUpto, inventoryLines: posted.InventoryLines,
            posTenders: posted.PosTenders, referenceNo: posted.ReferenceNo,
            referenceDate: posted.ReferenceDate, isAccountingInvoice: posted.IsAccountingInvoice);
        service.Replace(posted.Id, replacement);
        book.Storage.Save(book.Company);
        return book.Company.FindVoucher(posted.Id)!;
    }

    /// <summary>A copy of <paramref name="l"/> with every child preserved, and the named ones overridden.</summary>
    private static EntryLine CopyWith(
        EntryLine l, IReadOnlyList<BillAllocation>? bills = null, BankAllocation? bank = null) =>
        new(l.LedgerId, l.Amount, l.Side,
            bills ?? (l.BillAllocations.Count > 0 ? l.BillAllocations : null),
            l.CostAllocations.Count > 0 ? l.CostAllocations : null,
            bank ?? l.BankAllocation,
            l.Forex, l.Gst, l.Tds, l.Tcs, l.Payroll);

    private static EntryLine PartyLegOf(Voucher v, Guid partyId) =>
        v.Lines.Single(l => l.LedgerId == partyId && l.Side == DrCr.Credit);

    private static EntryLine ValueLegOf(Voucher v, Guid valueLedgerId) =>
        v.Lines.Single(l => l.LedgerId == valueLedgerId && l.Side == DrCr.Debit);

    // ================================================================ T1-22 — the bank allocation

    /// <summary>
    /// 🔴 <b>T1-22.</b> A cheque-paid item invoice — the party IS the bank — reconciled against the statement, then
    /// re-opened and re-accepted with only the narration changed. The instrument detail AND the reconciliation
    /// date must both still be there. Measured before the fix: <c>bank=False instr='' bankDate=</c>, under the
    /// message "Purchase No. 1 altered."
    /// </summary>
    [Fact]
    public void A_reconciled_bank_allocation_on_the_party_leg_survives_a_narration_only_re_accept()
    {
        using var book = AlterationBook.New("t1_22_carry");
        var kit = Seed(book, valueLegBillWise: false);
        var posted = PostInvoice(kit, kit.Bank);

        var instrumentDate = book.On(3);
        var bankDate = book.On(5);
        posted = Restamp(book, posted, l =>
            l.LedgerId == kit.Bank.Id && l.Side == DrCr.Credit
                ? CopyWith(l, bank: new BankAllocation(BankTransactionType.ChequeOrDD, ChequeNo, instrumentDate))
                : CopyWith(l));
        Assert.True(BankReconciliation.SetBankDate(book.Company, posted.Id, kit.Bank.Id, bankDate));
        book.Storage.Save(book.Company);
        Assert.Equal(PartyTotal, PartyLegOf(posted, kit.Bank.Id).Amount.Amount);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        entry.Narration = "Nonce narration TWO";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = PartyLegOf(book.Company.FindVoucher(posted.Id)!, kit.Bank.Id);
        Assert.NotNull(after.BankAllocation);
        Assert.Equal(ChequeNo, after.BankAllocation!.InstrumentNumber);
        Assert.Equal(BankTransactionType.ChequeOrDD, after.BankAllocation.TransactionType);
        Assert.Equal(instrumentDate, after.BankAllocation.InstrumentDate);
        Assert.Equal(bankDate, after.BankAllocation.BankDate);
        Assert.Equal(PartyTotal, after.Amount.Amount);

        // …and the operator was told nothing was lost, because nothing was.
        Assert.Equal($"Purchase No. {book.Company.FormatVoucherNumber(posted)} altered.", entry.Message);
    }

    /// <summary>
    /// 🔴 <b>T1-22, the OTHER half of the contract.</b> The instrument is not an amount and does not have to foot
    /// the leg, so an ordinary amendment that MOVES the party total is still accepted and still keeps the cheque
    /// reference — but the reconciliation tick goes, with a warning, because a cleared item that no longer matches
    /// the statement is not cleared (design §3.4). This is the engine's own rule, reached for the first time on
    /// this path: putting the allocation back is what gives <c>CarryBankDatesForward</c> a line to pair against.
    /// </summary>
    [Fact]
    public void Amending_the_quantity_keeps_the_instrument_and_clears_only_the_reconciliation()
    {
        using var book = AlterationBook.New("t1_22_clear");
        var kit = Seed(book, valueLegBillWise: false);
        var posted = PostInvoice(kit, kit.Bank);

        var bankDate = book.On(5);
        posted = Restamp(book, posted, l =>
            l.LedgerId == kit.Bank.Id && l.Side == DrCr.Credit
                ? CopyWith(l, bank: new BankAllocation(BankTransactionType.ChequeOrDD, ChequeNo, book.On(3)))
                : CopyWith(l));
        Assert.True(BankReconciliation.SetBankDate(book.Company, posted.Id, kit.Bank.Id, bankDate));
        book.Storage.Save(book.Company);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        entry.InventoryLines[0].QuantityText = "3";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = PartyLegOf(book.Company.FindVoucher(posted.Id)!, kit.Bank.Id);
        Assert.NotNull(after.BankAllocation);
        Assert.Equal(ChequeNo, after.BankAllocation!.InstrumentNumber);
        Assert.Null(after.BankAllocation.BankDate);
        Assert.NotEqual(PartyTotal, after.Amount.Amount);

        // The operator is TOLD, and the sentence names the reconciliation rather than the instrument.
        Assert.Contains("reconciliation date", entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconcile it again", entry.Message!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>T1-22, the refusal arm.</b> Re-pointing the party to another ledger is refused BY NAME: the cheque was
    /// drawn on the bank it was posted against, this screen has no panel to re-key an instrument on, and the new
    /// party may not even be a bank (which would make the engine refuse in words the operator never saw).
    /// </summary>
    [Fact]
    public void Repointing_the_party_of_a_cheque_paid_invoice_is_refused_by_name()
    {
        using var book = AlterationBook.New("t1_22_refuse");
        var kit = Seed(book, valueLegBillWise: false);
        var posted = PostInvoice(kit, kit.Bank);

        posted = Restamp(book, posted, l =>
            l.LedgerId == kit.Bank.Id && l.Side == DrCr.Credit
                ? CopyWith(l, bank: new BankAllocation(BankTransactionType.ChequeOrDD, ChequeNo, book.On(3)))
                : CopyWith(l));

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == kit.Supplier.Id);
        Assert.False(entry.AcceptAlteration());
        Assert.Contains("Nonce Bank Current A/c", entry.Message!);
        Assert.Contains("bank instrument", entry.Message!, StringComparison.OrdinalIgnoreCase);

        // Refused ⇒ the book is untouched, instrument and all.
        Assert.Equal(ChequeNo, PartyLegOf(book.Company.FindVoucher(posted.Id)!, kit.Bank.Id)
            .BankAllocation!.InstrumentNumber);
    }

    // ================================================================ T1-23 — the value leg's bill-wise split

    /// <summary>
    /// 🔴 <b>T1-23.</b> A bill-wise split on the VALUE leg — a Purchase Accounts ledger with
    /// <c>MaintainBillByBill</c> set — survives a narration-only re-accept. Measured before the fix:
    /// <c>bills=1 'VALUE-LEG-REF'</c> → <c>bills=0</c>, with NO warning at all, under "Purchase No. 1 altered."
    /// </summary>
    [Fact]
    public void A_bill_wise_split_on_the_value_leg_survives_a_narration_only_re_accept()
    {
        using var book = AlterationBook.New("t1_23_carry");
        var kit = Seed(book, valueLegBillWise: true);
        var posted = PostInvoice(kit, kit.Supplier);

        posted = Restamp(book, posted, l =>
            l.LedgerId == kit.Purchases.Id && l.Side == DrCr.Debit
                ? CopyWith(l, bills: new[]
                    { new BillAllocation(BillRefType.NewRef, ValueLegBillRef, new Money(TaxableValue)) })
                : CopyWith(l));
        Assert.Equal(TaxableValue, ValueLegOf(posted, kit.Purchases.Id).Amount.Amount);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        entry.Narration = "Nonce narration TWO";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = ValueLegOf(book.Company.FindVoucher(posted.Id)!, kit.Purchases.Id);
        Assert.Single(after.BillAllocations);
        Assert.Equal(ValueLegBillRef, after.BillAllocations[0].Name);
        Assert.Equal(BillRefType.NewRef, after.BillAllocations[0].RefType);
        Assert.Equal(TaxableValue, after.BillAllocations[0].Amount.Amount);
        Assert.Equal(TaxableValue, after.Amount.Amount);
    }

    /// <summary>
    /// 🔴 <b>T1-23, the refusal arm — and the reason this one CANNOT simply be carried like the instrument.</b> A
    /// bill-wise split must sum EXACTLY to its line amount (<c>EnsureBillAllocationsValid</c>), so an amendment
    /// that moves the value leg leaves the carried split short. The screen's ONE bill-wise panel is bound to the
    /// PARTY, so there is nothing to re-cut it on — refused by name, before the engine can refuse it in words the
    /// operator never saw.
    /// </summary>
    [Fact]
    public void Amending_a_quantity_under_a_bill_wise_value_leg_is_refused_by_name()
    {
        using var book = AlterationBook.New("t1_23_refuse");
        var kit = Seed(book, valueLegBillWise: true);
        var posted = PostInvoice(kit, kit.Supplier);

        posted = Restamp(book, posted, l =>
            l.LedgerId == kit.Purchases.Id && l.Side == DrCr.Debit
                ? CopyWith(l, bills: new[]
                    { new BillAllocation(BillRefType.NewRef, ValueLegBillRef, new Money(TaxableValue)) })
                : CopyWith(l));

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        entry.InventoryLines[0].QuantityText = "3";
        Assert.False(entry.AcceptAlteration());
        Assert.Contains("Purchases", entry.Message!);
        Assert.Contains("bill reference", entry.Message!, StringComparison.OrdinalIgnoreCase);

        var after = ValueLegOf(book.Company.FindVoucher(posted.Id)!, kit.Purchases.Id);
        Assert.Single(after.BillAllocations);
        Assert.Equal(TaxableValue, after.Amount.Amount);
    }

    /// <summary>
    /// 🔴 <b>T1-23, the flag-drift arm.</b> Turning the VALUE ledger's bill-by-bill flag off after posting is
    /// refused AT THE DOOR, the same way <c>RehydrateInvoiceBillWise</c> refuses it on the party: the split could
    /// then not be written back at all, and the operator is better told before the screen opens than after they
    /// have re-keyed it.
    /// </summary>
    [Fact]
    public void Turning_the_value_ledgers_bill_wise_flag_off_after_posting_is_refused_at_the_door()
    {
        using var book = AlterationBook.New("t1_23_flag");
        var kit = Seed(book, valueLegBillWise: true);
        var posted = PostInvoice(kit, kit.Supplier);

        Restamp(book, posted, l =>
            l.LedgerId == kit.Purchases.Id && l.Side == DrCr.Debit
                ? CopyWith(l, bills: new[]
                    { new BillAllocation(BillRefType.NewRef, ValueLegBillRef, new Money(TaxableValue)) })
                : CopyWith(l));

        kit.Purchases.MaintainBillByBill = false;

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("Purchases", open.Refusal!);
        Assert.Contains("bill-by-bill", open.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ the complement, measured

    /// <summary>
    /// 🔴 <b>THE INSTRUMENT THE ROOT NOTE ASKS FOR.</b> The shipped byte-identity test drives an invoice whose
    /// optional payload is exactly what the screen can express, so it could never have caught either defect above.
    /// This one carries the FULL payload on the legs the screen has no panel for — a reconciled bank instrument on
    /// the party leg AND a bill-wise split on the value leg, at once — and asserts the canonical export is
    /// byte-identical across an untouched re-accept, in memory and on disk. A future optional field dropped by the
    /// rebuild reddens here without anybody having to think of it.
    /// </summary>
    [Fact]
    public void An_invoice_carrying_the_full_optional_payload_re_accepted_unchanged_is_byte_identical()
    {
        using var book = AlterationBook.New("t1_full_payload");
        var kit = Seed(book, valueLegBillWise: true);
        var posted = PostInvoice(kit, kit.Bank);

        posted = Restamp(book, posted, l =>
        {
            if (l.LedgerId == kit.Bank.Id && l.Side == DrCr.Credit)
                return CopyWith(l, bank: new BankAllocation(
                    BankTransactionType.ChequeOrDD, ChequeNo, book.On(3)));
            if (l.LedgerId == kit.Purchases.Id && l.Side == DrCr.Debit)
                return CopyWith(l, bills: new[]
                    { new BillAllocation(BillRefType.NewRef, ValueLegBillRef, new Money(TaxableValue)) });
            return CopyWith(l);
        });
        Assert.True(BankReconciliation.SetBankDate(book.Company, posted.Id, kit.Bank.Id, book.On(5)));
        book.Storage.Save(book.Company);

        var before = book.Export();
        var beforeOnDisk = book.ExportReloaded();

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        Assert.True(open.Entry!.AcceptAlteration(), open.Entry.Message);

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeOnDisk, book.ExportReloaded());
    }
}
