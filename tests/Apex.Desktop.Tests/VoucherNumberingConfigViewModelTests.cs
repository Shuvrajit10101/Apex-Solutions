using System;
using System.Collections.Generic;
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
/// Numbering slice S4 (numbering-design-v2 §5) — the F12 voucher-numbering CONFIG view model: it edits a type's
/// S3-persisted Prefix/Suffix/Width/Prefill/Prevent-duplicate fields, saves through the existing store path, and
/// enforces the §5.3/§5.4 validation (duplicate-date reject, digit-adjacent warn-not-block, historical-stability
/// block-on-filed / warn-and-confirm otherwise, allow future-dated add). These drive the REAL VM and store; the
/// F12-open + keyboard tests live in <see cref="VoucherNumberingConfigShellTests"/>.
/// </summary>
public sealed class VoucherNumberingConfigViewModelTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly PostDate = new(2025, 5, 10);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    // ================================================================ (1) save prefix + width → persists → renders

    [Fact]
    public void EditingConfig_savesPrefixWidth_persistsAndRenders()
    {
        using var t = new TempStore();
        var c = CompanyFactory.CreateSeeded("Num S4 Save", FyStart, FyStart);
        var jType = new VoucherType(Guid.NewGuid(), "Affix Journal", VoucherBaseType.Journal);
        c.AddVoucherType(jType);
        var cashA = Add(c, "Cash A", "Cash-in-Hand", true);
        var cashB = Add(c, "Cash B", "Cash-in-Hand", true);
        t.Storage.Save(c);

        var vm = new VoucherNumberingConfigViewModel(c, t.Storage);
        vm.SelectByTypeId(jType.Id);
        vm.WidthText = "3";
        vm.PrefillWithZero = true;
        vm.AddPrefixRow(FyStart, "25-26/");

        Assert.Equal(NumberingSaveResult.Saved, vm.Save());

        // Reload from disk — the config round-tripped through the S3 store — then post a NEW voucher and RENDER it.
        var reloaded = t.Storage.Load(t.Storage.ListCompanies().Single());
        var reType = reloaded.FindVoucherType(jType.Id)!;
        Assert.Equal(3, reType.NumberWidth);
        Assert.Single(reType.Prefixes, p => p.Particulars == "25-26/");

        var a = reloaded.FindLedgerByName("Cash A")!;
        var b = reloaded.FindLedgerByName("Cash B")!;
        var posted = new LedgerService(reloaded).Post(new Voucher(Guid.NewGuid(), reType.Id, PostDate,
            new List<EntryLine>
            {
                new(a.Id, Money.FromRupees(100m), DrCr.Debit),
                new(b.Id, Money.FromRupees(100m), DrCr.Credit),
            }));
        Assert.Equal("25-26/001", reloaded.FormatVoucherNumber(posted)); // ties S4 → S3 (persist) → S2/S1 (render)
    }

    // ================================================================ (2) duplicate ApplicableFrom → rejected

    [Fact]
    public void DuplicateApplicableFrom_isRejected()
    {
        using var t = new TempStore();
        var c = CompanyFactory.CreateSeeded("Num S4 Dup", FyStart, FyStart);
        var type = new VoucherType(Guid.NewGuid(), "Dup Sales", VoucherBaseType.Sales);
        c.AddVoucherType(type);
        t.Storage.Save(c);

        var vm = new VoucherNumberingConfigViewModel(c, t.Storage);
        vm.SelectByTypeId(type.Id);
        vm.AddPrefixRow(FyStart, "A/");
        vm.AddPrefixRow(FyStart, "B/"); // same date, same kind → forbidden

        Assert.Equal(NumberingSaveResult.Rejected, vm.Save());
        Assert.Contains("same", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(c.FindVoucherType(type.Id)!.Prefixes);       // nothing persisted
    }

    // ================================================================ (3) digit-adjacent → warns BUT saves

    [Fact]
    public void DigitAdjacentAffix_warnsButSaves()
    {
        using var t = new TempStore();
        var c = CompanyFactory.CreateSeeded("Num S4 Digit", FyStart, FyStart);
        var type = new VoucherType(Guid.NewGuid(), "Digit Sales", VoucherBaseType.Sales); // no posted vouchers
        c.AddVoucherType(type);
        t.Storage.Save(c);

        var vm = new VoucherNumberingConfigViewModel(c, t.Storage);
        vm.SelectByTypeId(type.Id);
        vm.AddPrefixRow(FyStart, "20"); // ends in a digit — a digit-adjacent boundary

        Assert.Equal(NumberingSaveResult.Saved, vm.Save());        // NOT blocked
        Assert.Contains("digit-adjacent", vm.Message, StringComparison.OrdinalIgnoreCase); // …but warned
        Assert.Single(c.FindVoucherType(type.Id)!.Prefixes, p => p.Particulars == "20");   // and it saved
    }

    // ================================================================ (4) edit covering an e-invoiced voucher → BLOCKED

    [Fact]
    public void EditCoveringEInvoicedVoucher_isBlocked()
    {
        using var t = new TempStore();
        var (c, sales, debtor) = BuildGstCo("Num S4 Block");
        var type = new VoucherType(Guid.NewGuid(), "Blocked Sales", VoucherBaseType.Sales,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "OLD/") });
        c.AddVoucherType(type);
        var sale = PostSale(c, type.Id, sales, debtor);
        // Give the posted voucher a GENERATED (IRN-bearing) e-invoice — a filed statutory document.
        c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(Guid.NewGuid(), sale.Id, "OLD/1",
            EInvoiceStatus.Generated, irn: new string('A', 64), ackNo: "112410000000000",
            ackDate: PostDate, signedQr: "QR", signedJson: null, cancelledOn: null, cancelReasonCode: null));
        t.Storage.Save(c);

        var vm = new VoucherNumberingConfigViewModel(c, t.Storage);
        vm.SelectByTypeId(type.Id);
        vm.Prefixes[0].Particulars = "NEW/"; // rewrites the covered, e-invoiced voucher's document number

        Assert.Equal(NumberingSaveResult.Blocked, vm.Save());
        Assert.Contains("filed", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("OLD/", c.FindVoucherType(type.Id)!.Prefixes.Single().Particulars); // unchanged — not saved
        Assert.Equal("OLD/1", c.FormatVoucherNumber(sale));
    }

    // ================================================================ (5) edit covering an UNFILED voucher → warn+proceed

    [Fact]
    public void EditCoveringUnfiledVoucher_warnsAndProceeds()
    {
        using var t = new TempStore();
        var (c, sales, debtor) = BuildGstCo("Num S4 Warn");
        var type = new VoucherType(Guid.NewGuid(), "Warn Sales", VoucherBaseType.Sales,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "OLD/") });
        c.AddVoucherType(type);
        var sale = PostSale(c, type.Id, sales, debtor);      // plain posted — no e-invoice / e-Way
        Assert.Equal("OLD/1", c.FormatVoucherNumber(sale));
        t.Storage.Save(c);

        var vm = new VoucherNumberingConfigViewModel(c, t.Storage);
        vm.SelectByTypeId(type.Id);
        vm.Prefixes[0].Particulars = "NEW/";

        Assert.Equal(NumberingSaveResult.NeedsConfirmation, vm.Save()); // warns, does not yet persist
        Assert.True(vm.IsConfirmPending);
        Assert.Contains("1", vm.ConfirmText);
        Assert.Equal("OLD/", c.FindVoucherType(type.Id)!.Prefixes.Single().Particulars); // not yet changed

        Assert.Equal(NumberingSaveResult.Saved, vm.ConfirmSave());     // on confirm it persists
        Assert.Equal("NEW/", c.FindVoucherType(type.Id)!.Prefixes.Single().Particulars);
        Assert.Equal("NEW/1", c.FormatVoucherNumber(sale));

        var reloaded = t.Storage.Load(t.Storage.ListCompanies().Single());
        Assert.Equal("NEW/", reloaded.FindVoucherType(type.Id)!.Prefixes.Single().Particulars); // and to disk
    }

    // ================================================================ (6) add a FUTURE-dated row covering nobody → no warn

    [Fact]
    public void AddFutureDatedRow_noWarn_whenNoVoucherCovered()
    {
        using var t = new TempStore();
        var (c, sales, debtor) = BuildGstCo("Num S4 Future");
        var type = new VoucherType(Guid.NewGuid(), "Future Sales", VoucherBaseType.Sales,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "25-26/") });
        c.AddVoucherType(type);
        var sale = PostSale(c, type.Id, sales, debtor);      // dated 10-May-2025 ⇒ "25-26/1"
        Assert.Equal("25-26/1", c.FormatVoucherNumber(sale));
        t.Storage.Save(c);

        var vm = new VoucherNumberingConfigViewModel(c, t.Storage);
        vm.SelectByTypeId(type.Id);
        vm.AddPrefixRow(new DateOnly(2026, 4, 1), "26-27/"); // in force only from next FY — covers no posted voucher

        Assert.Equal(NumberingSaveResult.Saved, vm.Save());  // straight save, NO historical-stability warning
        Assert.False(vm.IsConfirmPending);
        Assert.Equal("25-26/1", c.FormatVoucherNumber(sale)); // the past voucher's number is unchanged
    }

    // ============================== (7) FIX-1: edit covering a posted PURE-INVENTORY voucher → warn+confirm (not silent Save)

    [Fact]
    public void EditCoveringInventoryVoucher_warnsAndProceeds()
    {
        using var t = new TempStore();
        var c = CompanyFactory.CreateSeeded("Num S4 Inv", FyStart, FyStart);
        // A pure-inventory type (Delivery Note) with a covering prefix — no accounting voucher exists for it.
        var type = new VoucherType(Guid.NewGuid(), "Inv Delivery", VoucherBaseType.DeliveryNote,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "OLD/") });
        c.AddVoucherType(type);
        // A posted inventory voucher of the type — it renders "OLD/1" through the inventory FormatVoucherNumber overload.
        var inv = new InventoryVoucher(Guid.NewGuid(), type.Id, PostDate,
            allocations: Array.Empty<InventoryAllocation>(), number: 1);
        c.AddInventoryVoucher(inv);
        Assert.Equal("OLD/1", c.FormatVoucherNumber(inv));
        t.Storage.Save(c);

        var vm = new VoucherNumberingConfigViewModel(c, t.Storage);
        vm.SelectByTypeId(type.Id);
        vm.Prefixes[0].Particulars = "NEW/"; // rewrites the covered, already-issued inventory document number

        // The historical-stability guard must SEE the inventory voucher: warn-and-confirm, not a silent Saved.
        Assert.Equal(NumberingSaveResult.NeedsConfirmation, vm.Save());
        Assert.True(vm.IsConfirmPending);
        Assert.Contains("1", vm.ConfirmText);
        Assert.Equal("OLD/", c.FindVoucherType(type.Id)!.Prefixes.Single().Particulars); // nothing persisted yet
        Assert.Equal("OLD/1", c.FormatVoucherNumber(inv));

        Assert.Equal(NumberingSaveResult.Saved, vm.ConfirmSave());   // on confirm it persists
        Assert.Equal("NEW/", c.FindVoucherType(type.Id)!.Prefixes.Single().Particulars);
        Assert.Equal("NEW/1", c.FormatVoucherNumber(inv));
    }

    // ============================== (8) FIX-3: edit covering a CANCELLED e-invoice → BLOCKED (IRN reached the IRP)

    [Fact]
    public void EditCoveringCancelledEInvoicedVoucher_isBlocked()
    {
        using var t = new TempStore();
        var (c, sales, debtor) = BuildGstCo("Num S4 Cancelled");
        var type = new VoucherType(Guid.NewGuid(), "Cancelled Sales", VoucherBaseType.Sales,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "OLD/") });
        c.AddVoucherType(type);
        var sale = PostSale(c, type.Id, sales, debtor);
        // A CANCELLED e-invoice — the IRN was reported to the IRP and is permanently burned; its number cannot change.
        c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(Guid.NewGuid(), sale.Id, "OLD/1",
            EInvoiceStatus.Cancelled, irn: new string('A', 64), ackNo: "112410000000000",
            ackDate: PostDate, signedQr: "QR", signedJson: null, cancelledOn: PostDate, cancelReasonCode: "1"));
        t.Storage.Save(c);

        var vm = new VoucherNumberingConfigViewModel(c, t.Storage);
        vm.SelectByTypeId(type.Id);
        vm.Prefixes[0].Particulars = "NEW/"; // would rewrite the cancelled-but-filed voucher's document number

        Assert.Equal(NumberingSaveResult.Blocked, vm.Save());
        Assert.Contains("filed", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("OLD/", c.FindVoucherType(type.Id)!.Prefixes.Single().Particulars); // unchanged — not saved
        Assert.Equal("OLD/1", c.FormatVoucherNumber(sale));
    }

    // ---------------------------------------------------------------- helpers

    private sealed class TempStore : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "ApexNumS4_" + Guid.NewGuid().ToString("N"));
        public CompanyStorage Storage { get; }
        public TempStore() => Storage = new CompanyStorage(Dir);
        public void Dispose()
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
            }
            catch (IOException) { /* best effort */ }
        }
    }

    private static (Company c, DomainLedger sales, DomainLedger debtor) BuildGstCo(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart, FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        return (c, sales, debtor);
    }

    private static Voucher PostSale(Company c, Guid typeId, DomainLedger sales, DomainLedger debtor) =>
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), typeId, PostDate,
            new List<EntryLine>
            {
                new(debtor.Id, Money.FromRupees(1000m), DrCr.Debit),
                new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: debtor.Id));

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
