using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Numbering slice S5 (numbering-design-v2 §8) — the <b>counterparty captured field</b> on the entry screen and the
/// printed document. The field is labelled PER BASE TYPE ("Reference No." on a Sales, "Supplier Invoice No." on a
/// Purchase), keyboard-editable, and the typed value flows through Accept onto the posted <see cref="Voucher"/>; the
/// projector then surfaces it on the printed voucher/invoice. Free text — never touched by our own numbering.
/// </summary>
public sealed class VoucherReferenceCaptureTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================ Sales: "Reference No."

    [Fact]
    public void SalesEntry_capturesReferenceNo_labelledReferenceNo()
    {
        var tempDir = NewTempDir();
        try
        {
            var c = CompanyFactory.CreateSeeded("Sales Ref Co", FyStart, FyStart);
            var sales = Add(c, "Sales", "Sales Accounts", false);
            var debtor = Add(c, "Acme Ltd", "Sundry Debtors", true);
            var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);

            var vm = new VoucherEntryViewModel(c, salesType, new CompanyStorage(tempDir),
                onSaved: () => { }, onCancelled: () => { });

            // Labelled per base type — a Sales voucher captures the buyer's "Reference No.".
            Assert.True(vm.ShowReferenceCapture);
            Assert.Equal("Reference No.", vm.ReferenceNoCaption);

            vm.Lines[0].SelectedLedger = debtor; vm.Lines[0].Side = DrCr.Debit; vm.Lines[0].AmountText = "1000";
            vm.Lines[1].SelectedLedger = sales; vm.Lines[1].Side = DrCr.Credit; vm.Lines[1].AmountText = "1000";
            vm.ReferenceNo = "PO-778";
            vm.ReferenceDateText = "08-Apr-2025";
            vm.Recalculate();

            Assert.True(vm.Accept(), vm.Message);

            var posted = c.Vouchers.Single(v => v.TypeId == salesType.Id);
            Assert.Equal("PO-778", posted.ReferenceNo);
            Assert.Equal(new DateOnly(2025, 4, 8), posted.ReferenceDate);
        }
        finally { DeleteTempDir(tempDir); }
    }

    // ================================================================ Purchase: "Supplier Invoice No."

    [Fact]
    public void PurchaseEntry_capturesSupplierInvoiceNo_labelledSupplierInvoiceNo()
    {
        var tempDir = NewTempDir();
        try
        {
            var c = CompanyFactory.CreateSeeded("Purchase Ref Co", FyStart, FyStart);
            var purchases = Add(c, "Purchases", "Purchase Accounts", true);
            var creditor = Add(c, "Supplier Co", "Sundry Creditors", false);
            var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);

            var vm = new VoucherEntryViewModel(c, purchaseType, new CompanyStorage(tempDir),
                onSaved: () => { }, onCancelled: () => { });

            // Labelled per base type — a Purchase voucher captures the supplier's own invoice number.
            Assert.True(vm.ShowReferenceCapture);
            Assert.Equal("Supplier Invoice No.", vm.ReferenceNoCaption);

            vm.Lines[0].SelectedLedger = purchases; vm.Lines[0].Side = DrCr.Debit; vm.Lines[0].AmountText = "1000";
            vm.Lines[1].SelectedLedger = creditor; vm.Lines[1].Side = DrCr.Credit; vm.Lines[1].AmountText = "1000";
            vm.ReferenceNo = "SUP/2025/42";
            vm.Recalculate();

            Assert.True(vm.Accept(), vm.Message);

            var posted = c.Vouchers.Single(v => v.TypeId == purchaseType.Id);
            Assert.Equal("SUP/2025/42", posted.ReferenceNo);
            Assert.Null(posted.ReferenceDate); // date left blank ⇒ null
        }
        finally { DeleteTempDir(tempDir); }
    }

    // ================================================================ print: the captured value surfaces

    [Fact]
    public void Print_showsSupplierInvoiceNo()
    {
        var c = CompanyFactory.CreateSeeded("Print Ref Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var creditor = Add(c, "Supplier Co", "Sundry Creditors", false);
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);

        var purchase = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), purchaseType.Id, new DateOnly(2025, 4, 10),
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(creditor.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: creditor.Id, referenceNo: "SUP/77", referenceDate: new DateOnly(2025, 4, 8)));

        // The plain-voucher projector surfaces the supplier's invoice number, labelled per base type.
        var printed = VoucherPrintProjector.ProjectVoucher(c, purchase);
        Assert.Equal("SUP/77", printed.ReferenceNo);
        Assert.Equal("Supplier Invoice No.", printed.ReferenceCaption);
        Assert.Equal("08-04-2025", printed.ReferenceDateText);

        // A Sales tax invoice surfaces the buyer's reference through the invoice projector, labelled "Reference No.".
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Acme Ltd", "Sundry Debtors", true);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        inv.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(5000m));
        var sale = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 12),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(500m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(500m), DrCr.Credit),
            }, partyId: debtor.Id, referenceNo: "BUYER-PO-9",
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, 1m, Money.FromRupees(500m)) }));

        var invoice = VoucherPrintProjector.ProjectInvoice(c, sale);
        Assert.Equal("BUYER-PO-9", invoice.ReferenceNo);
        Assert.Equal("Reference No.", invoice.ReferenceCaption);
    }

    // ---- helpers ----

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "ApexRefCapture_" + Guid.NewGuid().ToString("N"));

    private static void DeleteTempDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best-effort */ }
    }
}
