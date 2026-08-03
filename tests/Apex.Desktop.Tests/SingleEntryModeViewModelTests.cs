using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>G-6</b> — <b>Single Entry mode</b> on Contra (F4), Payment (F5) and Receipt (F6).
///
/// <para>Every corpus walkthrough of the three cash/bank vouchers teaches Single Entry FIRST, and it is the routine
/// path in the worked exercises, not a tutorial nicety (BOOK pp.26, 29, 31–32; SG pp.75–76). The screen shows an
/// <c>Account</c> field (the one cash/bank side) and a <c>Particulars</c> list (the many side), with <b>no Dr/Cr
/// labels</b>.</para>
///
/// <para><b>The polarity is the whole risk, and the corpus flags it explicitly.</b> Receipt and Contra:
/// <i>"Dr means Account &amp; Cr means Particulars"</i> (BOOK p.29). Payment INVERTS it:
/// <i>"Cr means Account &amp; Dr means Particulars"</i> (BOOK p.32). Getting this backwards silently reverses every
/// cash and bank entry in the books — which is why each polarity is asserted on the POSTED voucher below, not on a
/// display string.</para>
///
/// <para><b>Design contract (D5): Single Entry is a DISPLAY mode — no posting path and no schema change.</b> It is a
/// re-render of the same <c>Lines</c> collection, so the balance rule, bill-wise, cost allocation, bank allocation
/// and every validator continue to apply untouched. These tests assert exactly that.</para>
/// </summary>
public sealed class SingleEntryModeViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public SingleEntryModeViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexSingleEntry_" + Guid.NewGuid().ToString("N"));
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

    // ================================================================ (1) availability

    /// <summary>Single Entry belongs to the three cash/bank types, and to no other.</summary>
    [Theory]
    [InlineData(VoucherBaseType.Payment, true)]
    [InlineData(VoucherBaseType.Receipt, true)]
    [InlineData(VoucherBaseType.Contra, true)]
    [InlineData(VoucherBaseType.Journal, false)]
    [InlineData(VoucherBaseType.Sales, false)]
    [InlineData(VoucherBaseType.Purchase, false)]
    public void Single_entry_is_offered_only_on_the_cash_and_bank_vouchers(VoucherBaseType baseType, bool expected)
    {
        var k = NewKit("SE Availability Co " + baseType);
        k.Vm.OpenVoucher(baseType);
        Assert.Equal(expected, k.Vm.VoucherEntry!.CanBeSingleEntry);
    }

    /// <summary>
    /// Ctrl+H is TallyPrime's one "Change Mode" picker. On Purchase/Sales it cycles the three invoice modes; on the
    /// cash/bank family it must flip Single ⟷ Double Entry. Same key, per-type mode list.
    /// </summary>
    [Fact]
    public void Ctrl_H_flips_single_and_double_entry_on_a_payment()
    {
        var k = NewKit("SE ChangeMode Co");
        k.Vm.OpenVoucher(VoucherBaseType.Payment);
        var e = k.Vm.VoucherEntry!;

        Assert.False(e.IsSingleEntry);      // Double Entry is the opening state
        e.ChangeMode();
        Assert.True(e.IsSingleEntry);
        e.ChangeMode();
        Assert.False(e.IsSingleEntry);
    }

    // ================================================================ (2) POLARITY — the money assertions

    /// <summary>
    /// <b>Receipt: Account = Dr.</b> (BOOK p.29.) Cash receives ₹47,382.19 of commission — the Account (Cash) is
    /// DEBITED and the Particulars ledger (Commission Received) is CREDITED.
    /// </summary>
    [Fact]
    public void Receipt_single_entry_debits_the_account_and_credits_the_particulars()
    {
        var k = NewKit("SE Receipt Co");
        k.Vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = k.Vm.VoucherEntry!;
        e.ChangeMode();
        Assert.True(e.IsSingleEntry);

        e.SingleEntryAccount = e.Ledgers.Single(l => l.Id == k.CashId);
        var p = e.SingleEntryParticulars[0];
        p.SelectedLedger = e.Ledgers.Single(l => l.Id == k.IncomeId);
        p.AmountText = "47382.19";
        e.Recalculate();

        Assert.True(e.CanAccept);
        Assert.True(e.Accept());

        var v = Posted(k.Vm.Company!, VoucherBaseType.Receipt);
        Assert.Equal(DrCr.Debit, v.Lines.Single(l => l.LedgerId == k.CashId).Side);
        Assert.Equal(47382.19m, v.Lines.Single(l => l.LedgerId == k.CashId).Amount.Amount);
        Assert.Equal(DrCr.Credit, v.Lines.Single(l => l.LedgerId == k.IncomeId).Side);
        Assert.Equal(47382.19m, v.Lines.Single(l => l.LedgerId == k.IncomeId).Amount.Amount);
    }

    /// <summary>
    /// <b>Payment INVERTS it: Account = Cr.</b> (BOOK p.32.) Rent of ₹23,456.78 paid from the bank — the Account
    /// (Bank) is CREDITED and the Particulars ledger (Office Rent) is DEBITED. This is the assertion that catches a
    /// copy-paste of the Receipt polarity.
    /// </summary>
    [Fact]
    public void Payment_single_entry_credits_the_account_and_debits_the_particulars()
    {
        var k = NewKit("SE Payment Co");
        k.Vm.OpenVoucher(VoucherBaseType.Payment);
        var e = k.Vm.VoucherEntry!;
        e.ChangeMode();

        e.SingleEntryAccount = e.Ledgers.Single(l => l.Id == k.BankId);
        var p = e.SingleEntryParticulars[0];
        p.SelectedLedger = e.Ledgers.Single(l => l.Id == k.ExpenseId);
        p.AmountText = "23456.78";
        e.Recalculate();

        Assert.True(e.Accept());

        var v = Posted(k.Vm.Company!, VoucherBaseType.Payment);
        Assert.Equal(DrCr.Credit, v.Lines.Single(l => l.LedgerId == k.BankId).Side);
        Assert.Equal(23456.78m, v.Lines.Single(l => l.LedgerId == k.BankId).Amount.Amount);
        Assert.Equal(DrCr.Debit, v.Lines.Single(l => l.LedgerId == k.ExpenseId).Side);
        Assert.Equal(23456.78m, v.Lines.Single(l => l.LedgerId == k.ExpenseId).Amount.Amount);
    }

    /// <summary>
    /// <b>Contra follows Receipt: Account = Dr.</b> (BOOK pp.26–29.) ₹9,876.54 drawn from the bank into cash — the
    /// Account (Cash, receiving) is DEBITED, the Particulars (Bank, paying) is CREDITED.
    /// </summary>
    [Fact]
    public void Contra_single_entry_debits_the_account_and_credits_the_particulars()
    {
        var k = NewKit("SE Contra Co");
        k.Vm.OpenVoucher(VoucherBaseType.Contra);
        var e = k.Vm.VoucherEntry!;
        e.ChangeMode();

        e.SingleEntryAccount = e.Ledgers.Single(l => l.Id == k.CashId);
        var p = e.SingleEntryParticulars[0];
        p.SelectedLedger = e.Ledgers.Single(l => l.Id == k.BankId);
        p.AmountText = "9876.54";
        e.Recalculate();

        Assert.True(e.Accept());

        var v = Posted(k.Vm.Company!, VoucherBaseType.Contra);
        Assert.Equal(DrCr.Debit, v.Lines.Single(l => l.LedgerId == k.CashId).Side);
        Assert.Equal(DrCr.Credit, v.Lines.Single(l => l.LedgerId == k.BankId).Side);
        Assert.Equal(9876.54m, v.Lines.Single(l => l.LedgerId == k.BankId).Amount.Amount);
    }

    // ================================================================ (3) the many side really is many

    /// <summary>
    /// SG p.77: <i>"multiple accounts are credited and one account is debited"</i>. A Receipt of ₹1,111.11 +
    /// ₹2,222.22 into one Account must post ONE debit of ₹3,333.33 against TWO credits.
    /// </summary>
    [Fact]
    public void Single_entry_supports_several_particulars_against_one_account()
    {
        var k = NewKit("SE Multi Co");
        var c0 = k.Vm.Company!;
        var otherIncome = AddLedger(c0, "Interest Received", "Indirect Incomes");

        k.Vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = k.Vm.VoucherEntry!;
        e.ChangeMode();

        e.SingleEntryAccount = e.Ledgers.Single(l => l.Id == k.CashId);
        e.SingleEntryParticulars[0].SelectedLedger = e.Ledgers.Single(l => l.Id == k.IncomeId);
        e.SingleEntryParticulars[0].AmountText = "1111.11";
        var second = e.AddSingleEntryParticular();
        second.SelectedLedger = e.Ledgers.Single(l => l.Id == otherIncome.Id);
        second.AmountText = "2222.22";
        e.Recalculate();

        // The Account amount is DERIVED — it is the balancing figure, never typed.
        Assert.Equal(3333.33m, e.SingleEntryAccountTotal);
        Assert.True(e.Accept());

        var v = Posted(k.Vm.Company!, VoucherBaseType.Receipt);
        Assert.Equal(3, v.Lines.Count);
        Assert.Equal(3333.33m, v.Lines.Single(l => l.LedgerId == k.CashId).Amount.Amount);
        Assert.Equal(DrCr.Debit, v.Lines.Single(l => l.LedgerId == k.CashId).Side);
        Assert.Equal(2, v.Lines.Count(l => l.Side == DrCr.Credit));
        Assert.Equal(3333.33m, v.TotalDebit.Amount);
        Assert.Equal(3333.33m, v.TotalCredit.Amount);
    }

    // ================================================================ (4) it is a DISPLAY mode only

    /// <summary>
    /// D5's contract: the mode is screen state, so the SAME entry keyed in Double Entry and in Single Entry must
    /// post the identical voucher. Proved by posting one of each and comparing the legs.
    /// </summary>
    [Fact]
    public void Single_and_double_entry_post_the_identical_voucher()
    {
        var k = NewKit("SE Equivalence Co");

        // Double Entry: Cr Bank / Dr Rent, 23,456.78.
        k.Vm.OpenVoucher(VoucherBaseType.Payment);
        var dbl = k.Vm.VoucherEntry!;
        dbl.Lines[0].SelectedLedger = dbl.Ledgers.Single(l => l.Id == k.ExpenseId);
        dbl.Lines[0].Side = DrCr.Debit;
        dbl.Lines[0].AmountText = "23456.78";
        dbl.Lines[1].SelectedLedger = dbl.Ledgers.Single(l => l.Id == k.BankId);
        dbl.Lines[1].Side = DrCr.Credit;
        dbl.Lines[1].AmountText = "23456.78";
        Assert.True(dbl.Accept());

        // Single Entry: the same transaction.
        k.Vm.OpenVoucher(VoucherBaseType.Payment);
        var single = k.Vm.VoucherEntry!;
        single.ChangeMode();
        single.SingleEntryAccount = single.Ledgers.Single(l => l.Id == k.BankId);
        single.SingleEntryParticulars[0].SelectedLedger = single.Ledgers.Single(l => l.Id == k.ExpenseId);
        single.SingleEntryParticulars[0].AmountText = "23456.78";
        single.Recalculate();
        Assert.True(single.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);
        var both = c.Vouchers.Where(v => v.TypeId == type.Id).ToList();
        Assert.Equal(2, both.Count);

        static string Shape(Voucher v, Company c) => string.Join(" | ",
            v.Lines.Select(l => $"{l.Side} {c.FindLedger(l.LedgerId)!.Name} {l.Amount.Amount:0.00}")
                   .OrderBy(s => s, StringComparer.Ordinal));

        Assert.Equal(Shape(both[0], c), Shape(both[1], c));
    }

    /// <summary>
    /// Leaving Single Entry must not strand the entry: flipping back to Double Entry shows the SAME lines with their
    /// Dr/Cr labels restored, so Ctrl+H is genuinely a view switch and never data loss.
    /// </summary>
    [Fact]
    public void Flipping_back_to_double_entry_keeps_the_lines_and_their_sides()
    {
        var k = NewKit("SE Roundtrip Co");
        k.Vm.OpenVoucher(VoucherBaseType.Payment);
        var e = k.Vm.VoucherEntry!;
        e.ChangeMode();

        e.SingleEntryAccount = e.Ledgers.Single(l => l.Id == k.BankId);
        e.SingleEntryParticulars[0].SelectedLedger = e.Ledgers.Single(l => l.Id == k.ExpenseId);
        e.SingleEntryParticulars[0].AmountText = "23456.78";
        e.Recalculate();

        e.ChangeMode();                       // back to Double Entry
        Assert.False(e.IsSingleEntry);

        var bankLine = e.Lines.Single(l => l.SelectedLedger?.Id == k.BankId);
        var rentLine = e.Lines.Single(l => l.SelectedLedger?.Id == k.ExpenseId);
        Assert.Equal(DrCr.Credit, bankLine.Side);
        Assert.Equal(DrCr.Debit, rentLine.Side);
        Assert.Equal("23456.78", rentLine.AmountText);
        Assert.True(e.IsBalanced);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }
}
