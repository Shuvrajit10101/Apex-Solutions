using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Path = System.IO.Path;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>T2-162 (census 6.12), THE SHAPE THE SCREEN ACTUALLY POSTS — wave 9 review, track fa.</b>
///
/// <para>The §34 picker offers Purchase originals to a CREDIT note as well as to a debit note, and voucher entry gives
/// a Credit Note OUTPUT legs whatever it is linked to (<c>VoucherEffects.IsPurchaseSideInvoice</c> is Purchase /
/// Debit Note only). So a credit note keyed against a purchase books Output CGST/SGST on the debit side. The first
/// cut of the T2-162 fix read every purchase-linked note as an ITC reduction, so on this book it filed GSTR-3B 3.1(a)
/// CGST ₹180 against books ₹162 and 4(A)(5) ₹72 against books ₹90, and GSTR-9C 12F <b>+36.00</b> and Table 11
/// <b>+36.00</b>, where the returns had reconciled before it. A return must say what the books posted.</para>
///
/// <para>Figures: sale 20 @ ₹100 = ₹2,000 @ 18% intra ⇒ ₹180 / ₹180; purchase 10 @ ₹100 = ₹1,000 ⇒ ₹90 / ₹90;
/// credit note 2 @ ₹100 = ₹200 ⇒ ₹18 / ₹18 on OUTPUT. Everything is read off a book loaded through a FRESH
/// <see cref="CompanyStorage"/> over the same folder, on the emitted JSON bytes.</para>
/// </summary>
public sealed class PurchaseLinkedCreditNoteEntryReloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexPurLinkCn_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_credit_note_keyed_against_a_purchase_files_returns_that_agree_with_its_output_legs()
    {
        const string name = "Purchase Linked CN Co";
        var storage = new CompanyStorage(_dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(50m));
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        item.Gst = new StockItemGstDetails { HsnSac = "850440", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var sales = Add(c, "Sales", "Sales Accounts");
        var salesReturns = Add(c, "Sales Returns", "Sales Accounts");
        var purchases = Add(c, "Purchases", "Purchase Accounts");
        var customer = Add(c, "Gamma Retail", "Sundry Debtors");
        var supplier = Add(c, "Delta Mfg", "Sundry Creditors");
        storage.Save(c);

        Voucher Post(VoucherBaseType nature, DomainLedger party, DomainLedger stockLedger, string qty, Voucher? original)
        {
            vm.OpenVoucher(nature);
            var e = vm.VoucherEntry!;
            vm.ChangeMode();
            Assert.True(e.IsItemInvoice);
            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == party.Id);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == stockLedger.Id);
            var line = e.InventoryLines[0];
            line.SelectedItem = e.StockItems.Single(i => i.Id == item.Id);
            line.SelectedGodown = e.Godowns.Single(g => g.Id == c.MainLocation!.Id);
            line.QuantityText = qty;
            line.RateText = "100.00";
            if (original is not null)
            {
                Assert.True(e.CanBeSection34Note);
                e.IsSection34Note = true;
                e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice is { } v && v.Id == original.Id);
                e.SelectedCdnReasonCode = "01 Sales return";
            }
            Assert.True(e.Accept(), e.Message);
            var typeId = vm.Company!.VoucherTypes.Single(t => t.BaseType == nature && t.IsActive).Id;
            return vm.Company!.Vouchers.Last(v => v.TypeId == typeId);
        }

        Post(VoucherBaseType.Sales, customer, sales, "20", null);
        var purchase = Post(VoucherBaseType.Purchase, supplier, purchases, "10", null);
        var note = Post(VoucherBaseType.CreditNote, supplier, salesReturns, "2", purchase);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_dir);
        var book = fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));

        // The premise, read off the reloaded book: the screen posted OUTPUT tax, on the debit side, and linked the
        // note to the purchase.
        var outputCgst = book.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output))!;
        var posted = book.FindVoucher(note.Id)!;
        var leg = posted.Lines.Single(l => l.LedgerId == outputCgst.Id);
        Assert.Equal(DrCr.Debit, leg.Side);
        Assert.Equal(18m, leg.Amount.Amount);
        Assert.Equal(purchase.Id, book.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == note.Id).OriginalInvoiceVoucherId);

        var from = book.FinancialYearStart;
        var to = from.AddYears(1).AddDays(-1);
        using (var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(book, from, to)))
        {
            Assert.Equal(0L, g9c.RootElement.GetProperty("tbl12f_unreconciled_itc_paisa").GetInt64());   // first cut: 3,600
            Assert.Equal(0L, g9c.RootElement.GetProperty("tbl11_unreconciled_tax_paisa").GetInt64());    // first cut: 3,600
        }
        using (var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(book, from, to)))
        {
            Assert.Equal(16_200L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());   // 180 − 18; first cut: 18,000
            Assert.Equal(9_000L, g3b.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());     // ITC untouched; first cut: 7,200
        }
    }

    private static DomainLedger Add(Company c, string name, string group)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }
}
