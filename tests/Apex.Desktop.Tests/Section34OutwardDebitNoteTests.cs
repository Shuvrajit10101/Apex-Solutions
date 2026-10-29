using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Path = System.IO.Path;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>T2-163 (wave 9) — A §34 DEBIT NOTE TO A CUSTOMER, RAISED FROM VOUCHER ENTRY, POSTS OUTPUT TAX.</b>
///
/// <para><b>The defect, as measured on main 0fbf8e2</b> (census T2-163, from the wave-8 verifier's V1): Alt+F5 Debit
/// Note → Ctrl+H item mode, party the CUSTOMER, §34 on, "Consolidated…" INV-77, reason 04, accepted, posted
/// <c>Beta Buyers Dr 708; Input CGST Cr 54; Input SGST Cr 54</c> — an ITC reversal against a customer — and GSTR-9
/// Table 18 filed a NEGATIVE inward row <c>847130 −600.00 / −54.00 / −54.00</c>. <c>VoucherEntryViewModel.GstDirection</c>
/// chose the head from the base type alone, and a Debit Note is always purchase-side.</para>
///
/// <para><b>The rule followed now</b> is the engine's own: <c>CreditDebitNoteService.BuildCreditDebitNote</c> posts a
/// <see cref="CdnType.Debit"/> note's tax as OUTPUT legs on the CREDIT (increasing) side. Every figure below is read
/// off the book RELOADED from disk through a fresh <see cref="CompanyStorage"/>, after the note was accepted with the
/// real Ctrl+A. Hand-worked: 4 Widgets × ₹150 = ₹600 at 18% ⇒ ₹108 ⇒ CGST 54 + SGST 54; party ₹708.</para>
/// </summary>
public sealed class Section34OutwardDebitNoteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexS34Dn_" + Guid.NewGuid().ToString("N"));

    private const string GstinHome = "27AAPFU0939F1ZV";
    private const string GstinParty = "27AAACR5055K1Z5";

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private sealed record Kit(MainWindowViewModel Vm, Guid ItemId, Guid GodownId, Guid PurchaseReturnsId,
        Guid CustomerId, Guid SupplierId);

    private static DomainLedger AddLedger(Company c, string name, string group, string? stateCode = null)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, openingIsDebit: false);
        if (stateCode is not null)
            l.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinParty, StateCode = stateCode };
        c.AddLedger(l);
        return l;
    }

    private Kit Seed(string name)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinHome,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        var masters = new InventoryService(c);
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", masters.CreateStockGroup("Goods").Id, nos.Id);
        item.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));
        AddLedger(c, "Purchases", "Purchase Accounts");
        var pr = AddLedger(c, "Purchase Returns", "Purchase Accounts");
        AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors", "27");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors", "27");
        new CompanyStorage(_dir).Save(c);
        return new Kit(vm, item.Id, c.MainLocation!.Id, pr.Id, customer.Id, supplier.Id);
    }

    private Company Reload(string name)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    /// <summary>Alt+F5, Ctrl+H, a 4 × ₹150 Widget line, §34 on against a CONSOLIDATED reference, then Ctrl+A.</summary>
    private static void KeyDebitNote(MainWindow w, Kit k, Guid partyId, Guid? linkedOriginalId = null)
    {
        w.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
        Pump(w);
        var e = k.Vm.VoucherEntry!;
        Assert.Equal(VoucherBaseType.DebitNote, e.Type.BaseType);
        w.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Pump(w);
        Assert.True(e.IsItemInvoice);

        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == partyId);
        e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.PurchaseReturnsId);
        var line = e.InventoryLines[0];
        line.SelectedItem = e.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = e.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = "4";
        line.RateText = "150.00";
        e.IsSection34Note = true;
        if (linkedOriginalId is { } originalId)
            e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == originalId);
        else
        {
            e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.IsConsolidated);
            e.CdnOriginalInvoiceNumber = "INV-77";
        }
        e.SelectedCdnReasonCode = "04 Correction in invoice";
        Pump(w);

        // w9 review (fc): the derived-legs line the operator READS before Ctrl+A must name the head the note will
        // POST. The item line is keyed BEFORE §34 is switched on, so only the §34 change handlers' recalculation
        // can move it. Read off the REALISED TextBlock, not the view-model string. Reverting either the handlers'
        // RecalculateItemInvoice or the preview head (back to IsPurchaseSideInvoice) leaves it reading "Input" on
        // the customer note, and reddens the customer test here.
        var preview = w.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Single(t => t.StartsWith("Dr " + e.SelectedParty!.Ledger!.Name + " 708.00", StringComparison.Ordinal));
        var head = partyId == k.CustomerId ? "Output" : "Input";
        Assert.Contains("Cr " + head + " CGST 54.00", preview, StringComparison.Ordinal);
        Assert.Contains("Cr " + head + " SGST 54.00", preview, StringComparison.Ordinal);
        Assert.DoesNotContain(head == "Output" ? "Input" : "Output", preview, StringComparison.Ordinal);

        w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(w);
        if (k.Vm.VoucherEntry is { } still && still.Message is { Length: > 0 } m && k.Vm.Company!.Vouchers.Count == 0)
            Assert.Fail("The note was not accepted: " + m);
    }

    private static Voucher TheNote(Company c) =>
        c.Vouchers.Single(v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.DebitNote);

    private static Guid TaxLedger(Company c, GstTaxHead head, GstTaxDirection dir) =>
        c.FindLedgerByName(GstService.TaxLedgerName(head, dir))?.Id ?? Guid.Empty;

    /// <summary>
    /// 🔴 <b>THE FIX, ON THE RELOADED BOOK.</b> The note to the customer credits OUTPUT CGST/SGST 54/54 and touches no
    /// Input ledger; the customer is debited 708; GSTR-9 Table 18 (and its filed JSON) carries NO inward row, so no
    /// negative one.
    /// <para><b>Mutation (T1):</b> restoring <c>GstDirection =&gt; IsPurchaseSideInvoice ? Input : Output</c> and
    /// <c>reverseSides: IsReturnNote</c> reddens this at the Output CGST leg (and, past it, at Table 18, which then
    /// reads <c>847130 −600.00</c>).</para>
    /// </summary>
    [AvaloniaFact]
    public void A_section_34_debit_note_to_a_customer_posts_output_tax_and_files_no_inward_row()
    {
        const string name = "S34 Outward DN Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        try
        {
            w.Show();
            Pump(w);
            KeyDebitNote(w, k, k.CustomerId);
        }
        finally { w.Close(); }

        var c = Reload(name);
        var note = TheNote(c);
        var outCgst = TaxLedger(c, GstTaxHead.Central, GstTaxDirection.Output);
        var outSgst = TaxLedger(c, GstTaxHead.State, GstTaxDirection.Output);
        var inCgst = TaxLedger(c, GstTaxHead.Central, GstTaxDirection.Input);
        var inSgst = TaxLedger(c, GstTaxHead.State, GstTaxDirection.Input);

        var cg = Assert.Single(note.Lines, l => l.LedgerId == outCgst);
        Assert.Equal(DrCr.Credit, cg.Side);
        Assert.Equal(54.00m, cg.Amount.Amount);
        var sg = Assert.Single(note.Lines, l => l.LedgerId == outSgst);
        Assert.Equal(DrCr.Credit, sg.Side);
        Assert.Equal(54.00m, sg.Amount.Amount);
        Assert.DoesNotContain(note.Lines, l => l.LedgerId == inCgst || l.LedgerId == inSgst);
        var party = Assert.Single(note.Lines, l => l.LedgerId == k.CustomerId);
        Assert.Equal(DrCr.Debit, party.Side);
        Assert.Equal(708.00m, party.Amount.Amount);
        // The §34 link survived the round-trip as a consolidated debit note.
        var link = Assert.Single(c.CreditDebitNoteLinks);
        Assert.Equal(CdnType.Debit, link.CdnType);
        Assert.Null(link.OriginalInvoiceVoucherId);

        var fy = (c.FinancialYearStart, c.FinancialYearStart.AddYears(1).AddDays(-1));
        var r = Gstr9.Build(c, fy.Item1, fy.Item2);
        Assert.DoesNotContain(r.Table18InwardHsn, h => h.TaxableValue.Amount < 0m || h.TotalTax.Amount < 0m);
        Assert.Empty(r.Table18InwardHsn);
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr9(c, fy.Item1, fy.Item2));
        Assert.Empty(doc.RootElement.GetProperty("tbl18_hsn").EnumerateArray());
    }

    /// <summary>
    /// THE CONTROL: the SAME keys with the SUPPLIER as party (a §34-flagged purchase return against a consolidated
    /// reference) still credits INPUT tax, as a purchase return must. <b>Mutation (T2):</b> making every §34 debit note
    /// outward (dropping the counterparty test from <c>IsOutwardSection34DebitNote</c>) reddens this.
    /// </summary>
    [AvaloniaFact]
    public void A_section_34_debit_note_to_a_supplier_still_reverses_input_tax()
    {
        const string name = "S34 Inward DN Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        try
        {
            w.Show();
            Pump(w);
            KeyDebitNote(w, k, k.SupplierId);
        }
        finally { w.Close(); }

        var c = Reload(name);
        var note = TheNote(c);
        var inCgst = TaxLedger(c, GstTaxHead.Central, GstTaxDirection.Input);
        var cg = Assert.Single(note.Lines, l => l.LedgerId == inCgst);
        Assert.Equal(DrCr.Credit, cg.Side);
        Assert.Equal(54.00m, cg.Amount.Amount);
        var outCgst = TaxLedger(c, GstTaxHead.Central, GstTaxDirection.Output);
        Assert.DoesNotContain(note.Lines, l => outCgst != Guid.Empty && l.LedgerId == outCgst);
    }

    /// <summary>
    /// w9 review (fc): the OTHER limb of <c>IsOutwardSection34DebitNote</c> — the note LINKED to a posted SALES
    /// invoice, the commonest shape of a §34(3) debit note. The builder's tests reach only the consolidated limb, so
    /// pointing the linked limb at <c>VoucherBaseType.Purchase</c> passed them all (review mutation M10). Sale: 10 ×
    /// ₹150 to the customer, accepted with the real Ctrl+A on the Sales screen. The note: the same keys as above,
    /// linked to that sale. On the RELOADED book it credits Output CGST/SGST 54/54, touches no Input ledger, and its
    /// link names the sale.
    /// </summary>
    [AvaloniaFact]
    public void A_section_34_debit_note_linked_to_a_sale_posts_output_tax()
    {
        const string name = "S34 Linked DN Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid saleId;
        try
        {
            w.Show();
            Pump(w);
            var c0 = k.Vm.Company!;
            k.Vm.OpenVoucher(c0.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive));
            Pump(w);
            var s = k.Vm.VoucherEntry!;
            w.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(w);
            Assert.True(s.IsItemInvoice);
            s.SelectedParty = s.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
            s.SelectedStockLedger = s.StockLedgers.Single(l => l.Name == "Sales");
            var sl = s.InventoryLines[0];
            sl.SelectedItem = s.StockItems.Single(i => i.Id == k.ItemId);
            sl.SelectedGodown = s.Godowns.Single(g => g.Id == k.GodownId);
            sl.QuantityText = "10";
            sl.RateText = "150.00";
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);
            saleId = Assert.Single(c0.Vouchers, v => c0.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Sales).Id;
            k.Vm.Back();
            Pump(w);

            KeyDebitNote(w, k, k.CustomerId, linkedOriginalId: saleId);
        }
        finally { w.Close(); }

        var c = Reload(name);
        var note = TheNote(c);
        var outCgst = TaxLedger(c, GstTaxHead.Central, GstTaxDirection.Output);
        var outSgst = TaxLedger(c, GstTaxHead.State, GstTaxDirection.Output);
        var inCgst = TaxLedger(c, GstTaxHead.Central, GstTaxDirection.Input);
        var inSgst = TaxLedger(c, GstTaxHead.State, GstTaxDirection.Input);
        var cg = Assert.Single(note.Lines, l => l.LedgerId == outCgst);
        Assert.Equal(DrCr.Credit, cg.Side);
        Assert.Equal(54.00m, cg.Amount.Amount);
        var sg = Assert.Single(note.Lines, l => l.LedgerId == outSgst);
        Assert.Equal(DrCr.Credit, sg.Side);
        Assert.Equal(54.00m, sg.Amount.Amount);
        Assert.DoesNotContain(note.Lines, l => l.LedgerId == inCgst || l.LedgerId == inSgst);
        var link = Assert.Single(c.CreditDebitNoteLinks);
        Assert.Equal(CdnType.Debit, link.CdnType);
        Assert.Equal(saleId, link.OriginalInvoiceVoucherId);
    }

    /// <summary>
    /// w9 verifier (fc): the §34 TOGGLE's own recalculation, on its own. The operator ticks §34 and picks
    /// "Consolidated…", unticks it, keys the item line, then ticks §34 again. The re-tick changes no other field, so
    /// only <c>OnIsSection34NoteChanged</c>'s recalculation can move the derived-legs line to the head the note will
    /// post. <b>Mutation V-M8a</b> (that one call dropped, the original-invoice handler's kept) left the realised
    /// line reading "Input" on a note that posts Output, and passed every note test before this one.
    /// </summary>
    [AvaloniaFact]
    public void Re_ticking_section_34_alone_moves_the_derived_legs_to_output()
    {
        const string name = "S34 Retick DN Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        try
        {
            w.Show();
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
            Pump(w);
            var e = k.Vm.VoucherEntry!;
            Assert.Equal(VoucherBaseType.DebitNote, e.Type.BaseType);
            w.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(w);
            Assert.True(e.IsItemInvoice);
            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.PurchaseReturnsId);

            e.IsSection34Note = true;
            e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.IsConsolidated);
            e.CdnOriginalInvoiceNumber = "INV-77";
            e.SelectedCdnReasonCode = "04 Correction in invoice";
            e.IsSection34Note = false;
            Pump(w);

            var line = e.InventoryLines[0];
            line.SelectedItem = e.StockItems.Single(i => i.Id == k.ItemId);
            line.SelectedGodown = e.Godowns.Single(g => g.Id == k.GodownId);
            line.QuantityText = "4";
            line.RateText = "150.00";
            Pump(w);

            e.IsSection34Note = true;
            Pump(w);
            var preview = w.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .Single(t => t.StartsWith("Dr Beta Buyers 708.00", StringComparison.Ordinal));
            Assert.Contains("Cr Output CGST 54.00", preview, StringComparison.Ordinal);
            Assert.Contains("Cr Output SGST 54.00", preview, StringComparison.Ordinal);
            Assert.DoesNotContain("Input", preview, StringComparison.Ordinal);

            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);
            Assert.Single(k.Vm.Company!.Vouchers);
        }
        finally { w.Close(); }

        // The posting the line described, read off the RELOADED book.
        var c = Reload(name);
        var note = TheNote(c);
        var outCgst = TaxLedger(c, GstTaxHead.Central, GstTaxDirection.Output);
        var cg = Assert.Single(note.Lines, l => l.LedgerId == outCgst);
        Assert.Equal(DrCr.Credit, cg.Side);
        Assert.Equal(54.00m, cg.Amount.Amount);
        var inCgst = TaxLedger(c, GstTaxHead.Central, GstTaxDirection.Input);
        var inSgst = TaxLedger(c, GstTaxHead.State, GstTaxDirection.Input);
        Assert.DoesNotContain(note.Lines, l => l.LedgerId == inCgst || l.LedgerId == inSgst);
    }

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
}
