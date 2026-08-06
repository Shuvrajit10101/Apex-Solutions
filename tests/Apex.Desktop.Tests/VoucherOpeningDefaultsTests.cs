using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>The opening entry mode</b> — what an operator meets the instant a voucher screen opens.
///
/// <para><b>The three cash/bank vouchers open in SINGLE ENTRY.</b> The evidence is inference from absence, and it is
/// worth stating plainly because it is the whole basis of the change. Three separate walkthroughs in the GSTN notes
/// (<c>703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf</c>, <c>pdftotext -layout</c>) reach the Dr/Cr screen by turning
/// the single-entry setting <i>off</i> — extracted line 334 ("Use single entry mode for payment/receipt/contra
/// vouchers? NO"), line 1634 ("Press F12 &amp; Activate Use Single Entry Mode for Pymt/Rcpt/Contra set to No"),
/// line 1965 ("In F12: Configure … set to No") — and each then keys Cr and Dr fields. An instruction to turn a setting
/// off is only meaningful if the shipped state is on. Layout corroboration: BOOK pp.26-27, 29, 31-32; SG p.76.
/// The one apparent counter-example (line 330, "Select single entry mode …") is the setting being navigated to, whose
/// value arrives four lines later as NO; see <c>VoucherEntryViewModel.SeedOpeningMode</c> for the full reading.</para>
///
/// <para><b>The polarity is the money risk, and it is asserted on the POSTED ledger balances, never on a display
/// string.</b> Receipt/Contra: Account = Dr. Payment: Account = Cr (BOOK pp.29, 32). An inversion here silently
/// reverses every cash and bank entry an operator makes, so each of the three is posted from the OPENING screen —
/// no <c>ChangeMode()</c> — and the resulting ledger closing balance is read back. Reading <c>SingleEntryAccountSide</c>
/// instead would only re-assert the source line that the bug would live on.</para>
///
/// <para>Every figure below carries odd paisa on purpose. A round-number fixture cannot tell a correct total from one
/// that is out by fifty paisa, and this project has already lost a defect to exactly that.</para>
/// </summary>
public sealed class VoucherOpeningDefaultsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public VoucherOpeningDefaultsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexOpenDefaults_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid BankId { get; init; }
        public required Guid CashId { get; init; }
        public required Guid ExpenseId { get; init; }
        public required Guid IncomeId { get; init; }
    }

    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;

        var bank = AddLedger(c, "HDFC Current A/c", "Bank Accounts");
        var cash = AddLedger(c, "Cash-in-Hand A", "Cash-in-Hand");
        var expense = AddLedger(c, "Office Rent", "Indirect Expenses");
        var income = AddLedger(c, "Commission Received", "Indirect Incomes");

        _storage.Save(c);
        return new Kit { Vm = vm, BankId = bank.Id, CashId = cash.Id, ExpenseId = expense.Id, IncomeId = income.Id };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static Voucher Posted(Company c, VoucherBaseType baseType)
    {
        var type = c.VoucherTypes.Single(t => t.BaseType == baseType && t.IsActive);
        return c.Vouchers.Single(v => v.TypeId == type.Id);
    }

    // ============================================================ the opening mode, and the polarity it implies

    /// <summary>
    /// The opening mode is per type, not one literal: Single Entry on the three cash/bank vouchers, the classic
    /// Dr/Cr grid everywhere else. Asserted on the RENDER gates as well as on <c>Mode</c>, because the render gates
    /// are what the operator actually meets.
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.Payment, true)]
    [InlineData(VoucherBaseType.Receipt, true)]
    [InlineData(VoucherBaseType.Contra, true)]
    [InlineData(VoucherBaseType.Journal, false)]
    [InlineData(VoucherBaseType.Sales, false)]
    [InlineData(VoucherBaseType.Purchase, false)]
    [InlineData(VoucherBaseType.CreditNote, false)]
    [InlineData(VoucherBaseType.DebitNote, false)]
    public void The_opening_entry_mode_is_seeded_per_voucher_type(VoucherBaseType baseType, bool singleEntry)
    {
        var k = NewKit("Open Mode Co " + baseType);
        k.Vm.OpenVoucher(baseType);
        var e = k.Vm.VoucherEntry!;

        Assert.Equal(singleEntry, e.IsSingleEntry);
        Assert.Equal(singleEntry, e.Mode == VoucherEntryMode.SingleEntry);
        // The two grids are mutually exclusive: whichever one is on, exactly one is on.
        Assert.Equal(!singleEntry, e.ShowPlainDrCrGrid);
        Assert.True(e.IsAsVoucherMode); // Single Entry is a re-render of the As-Voucher lines, never a third posting path
    }

    /// <summary>
    /// <b>Receipt opens in Single Entry and the Account is DEBITED.</b> Posted from the opening screen with no mode
    /// switch at all; the proof is the ledger closing balance, not the screen. ₹63,417.83 of commission into cash.
    /// </summary>
    [Fact]
    public void A_receipt_posts_correctly_straight_from_the_opening_screen()
    {
        var k = NewKit("Open Receipt Co");
        k.Vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = k.Vm.VoucherEntry!;
        Assert.True(e.IsSingleEntry);       // no ChangeMode() — this is what the operator meets

        e.SingleEntryAccount = e.Ledgers.Single(l => l.Id == k.CashId);
        var p = e.SingleEntryParticulars[0];
        p.SelectedLedger = e.Ledgers.Single(l => l.Id == k.IncomeId);
        p.AmountText = "63417.83";
        e.Recalculate();
        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var v = Posted(c, VoucherBaseType.Receipt);
        Assert.Equal(DrCr.Debit, v.Lines.Single(l => l.LedgerId == k.CashId).Side);
        Assert.Equal(DrCr.Credit, v.Lines.Single(l => l.LedgerId == k.IncomeId).Side);

        // The balances a user would read off the Trial Balance: cash UP by the receipt, income UP by the same.
        var asOf = v.Date;
        var cashBal = LedgerBalances.Closing(c, c.FindLedger(k.CashId)!, asOf);
        var incomeBal = LedgerBalances.Closing(c, c.FindLedger(k.IncomeId)!, asOf);
        Assert.Equal(DrCr.Debit, cashBal.Side);
        Assert.Equal(63417.83m, cashBal.Amount.Amount);
        Assert.Equal(DrCr.Credit, incomeBal.Side);
        Assert.Equal(63417.83m, incomeBal.Amount.Amount);
    }

    /// <summary>
    /// <b>Payment INVERTS it: the Account is CREDITED</b> (BOOK p.32). This is the assertion that catches a
    /// copy-paste of the Receipt polarity into the seeding change. ₹18,236.47 of rent out of the bank: the bank must
    /// end up in CREDIT (overdrawn against a nil opening) and the expense in DEBIT.
    /// </summary>
    [Fact]
    public void A_payment_posts_correctly_straight_from_the_opening_screen()
    {
        var k = NewKit("Open Payment Co");
        k.Vm.OpenVoucher(VoucherBaseType.Payment);
        var e = k.Vm.VoucherEntry!;
        Assert.True(e.IsSingleEntry);

        e.SingleEntryAccount = e.Ledgers.Single(l => l.Id == k.BankId);
        var p = e.SingleEntryParticulars[0];
        p.SelectedLedger = e.Ledgers.Single(l => l.Id == k.ExpenseId);
        p.AmountText = "18236.47";
        e.Recalculate();
        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var v = Posted(c, VoucherBaseType.Payment);
        Assert.Equal(DrCr.Credit, v.Lines.Single(l => l.LedgerId == k.BankId).Side);
        Assert.Equal(DrCr.Debit, v.Lines.Single(l => l.LedgerId == k.ExpenseId).Side);

        var asOf = v.Date;
        var bankBal = LedgerBalances.Closing(c, c.FindLedger(k.BankId)!, asOf);
        var expBal = LedgerBalances.Closing(c, c.FindLedger(k.ExpenseId)!, asOf);
        Assert.Equal(DrCr.Credit, bankBal.Side);      // money LEFT the bank
        Assert.Equal(18236.47m, bankBal.Amount.Amount);
        Assert.Equal(DrCr.Debit, expBal.Side);
        Assert.Equal(18236.47m, expBal.Amount.Amount);
    }

    /// <summary>
    /// <b>Contra follows Receipt: the Account is DEBITED</b> (BOOK pp.26-29). ₹7,529.61 drawn out of the bank into
    /// cash — cash up, bank down, from the opening screen.
    /// </summary>
    [Fact]
    public void A_contra_posts_correctly_straight_from_the_opening_screen()
    {
        var k = NewKit("Open Contra Co");
        k.Vm.OpenVoucher(VoucherBaseType.Contra);
        var e = k.Vm.VoucherEntry!;
        Assert.True(e.IsSingleEntry);

        e.SingleEntryAccount = e.Ledgers.Single(l => l.Id == k.CashId);
        var p = e.SingleEntryParticulars[0];
        p.SelectedLedger = e.Ledgers.Single(l => l.Id == k.BankId);
        p.AmountText = "7529.61";
        e.Recalculate();
        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var v = Posted(c, VoucherBaseType.Contra);
        Assert.Equal(DrCr.Debit, v.Lines.Single(l => l.LedgerId == k.CashId).Side);
        Assert.Equal(DrCr.Credit, v.Lines.Single(l => l.LedgerId == k.BankId).Side);

        var asOf = v.Date;
        var cashBal = LedgerBalances.Closing(c, c.FindLedger(k.CashId)!, asOf);
        var bankBal = LedgerBalances.Closing(c, c.FindLedger(k.BankId)!, asOf);
        Assert.Equal(DrCr.Debit, cashBal.Side);
        Assert.Equal(7529.61m, cashBal.Amount.Amount);
        Assert.Equal(DrCr.Credit, bankBal.Side);
        Assert.Equal(7529.61m, bankBal.Amount.Amount);
    }

    /// <summary>
    /// Ctrl+H still reaches the Dr/Cr screen — that is the ONLY thing the three corpus walkthroughs actually
    /// instruct, so it must keep working from the new opening state. One press out, one press back.
    /// </summary>
    [Fact]
    public void Ctrl_H_still_reaches_the_double_entry_screen_from_the_new_default()
    {
        var k = NewKit("Open ChangeMode Co");
        k.Vm.OpenVoucher(VoucherBaseType.Payment);
        var e = k.Vm.VoucherEntry!;

        Assert.True(e.IsSingleEntry);
        e.ChangeMode();
        Assert.False(e.IsSingleEntry);
        Assert.True(e.ShowPlainDrCrGrid);
        e.ChangeMode();
        Assert.True(e.IsSingleEntry);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }
}
