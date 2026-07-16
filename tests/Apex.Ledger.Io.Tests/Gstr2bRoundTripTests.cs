using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-6 <b>GSTR-2B Io fold-in</b> gate (RQ-12/RQ-13; RQ-23; ER-13; ER-16). An imported 2B snapshot (owning
/// its lines) + the advisory reconciliation results export and re-import <b>exact in JSON AND XML</b>, both byte-stable,
/// into a fresh (differently-Guid'd) company. The CRITICAL divergence from S4/S5: snapshots + lines import
/// <b>unconditionally</b> (external portal data — NOT orphans of a source voucher); only the recon matched-voucher pin
/// re-links via the voucher map. A batch with a Matched recon whose voucher is absent rejects wholesale (all-or-nothing).
/// A plain GST company (no 2B) serialises with empty <c>gstr2bSnapshots</c> — byte-identical shape to a v42 company
/// (ER-13). No NIC credential appears (ER-16). De-brand clean (no "Tally").
/// </summary>
public sealed class Gstr2bRoundTripTests
{
    private const string GstinMe = "27AAPFU0939F1ZV";
    private const string GstinSupplierA = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2026, 3, 31);
    private static readonly DateOnly D1 = new(2025, 10, 10);
    private static readonly DateTimeOffset Imported = new(2025, 11, 15, 9, 0, 0, TimeSpan.FromHours(5.5));

    private static Company Build2bCompany()
    {
        var c = CompanyFactory.CreateSeeded("2B Recon Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMe, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            ReconValueTolerance = new Money(1m), ReconDateWindowDays = 2,
        });

        var supplier = new Domain.Ledger(Guid.NewGuid(), "Supplier A", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false, maintainBillByBill: true)
        {
            PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplierA, StateCode = "24" },
        };
        c.AddLedger(supplier);
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        c.AddLedger(purchases);

        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10000m), 1800) }, true, GstTaxDirection.Input);
        var credit = new Money(10000m + tax.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(10000m), DrCr.Debit),
            new(supplier.Id, credit, DrCr.Credit, billAllocations: new[] { new BillAllocation(BillRefType.NewRef, "INV-001", credit) }),
        };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines, partyId: supplier.Id));

        // Stage a 2B snapshot: one line matching the booked purchase, one non-RCM in-2B-only line (no booked purchase ⇒
        // InPortalOnly), and one supplier-RCM (reverse-charge) line — the RCM line still PERSISTS as immutable external
        // data but is EXCLUDED from reconciliation (finding #2: it must not surface as InPortalOnly).
        var snapshot = new Gstr2bSnapshot(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-10", GstinMe,
            new DateOnly(2025, 11, 14), "ABC123HASH", Imported, 180_000, 0, 0, 20_000, new[]
            {
                new Gstr2bLine(Guid.NewGuid(), GstinSupplierA, "Supplier A", Gstr2bDocType.B2b, "INV-001", "INV001", D1,
                    "27", 1_000_000, 180_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
                new Gstr2bLine(Guid.NewGuid(), "09ZZZZZ0000Z1Z9", "Unbooked Co", Gstr2bDocType.B2b, "UNB-1", "UNB1", D1,
                    "27", 300_000, 54_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
                new Gstr2bLine(Guid.NewGuid(), "06ZZZZZ0000Z1Z5", "Other Co", Gstr2bDocType.CreditNote, "CN-9", "CN9", D1,
                    "27", 500_000, 90_000, 0, 0, 20_000, itcAvailable: false, "P", reverseCharge: true),
            });
        c.AddGstr2bSnapshot(snapshot);

        var report = Gstr2bReconciler.Reconcile(c, snapshot, From, To, c.Gst!.ReconTolerance);
        foreach (var result in report.ToPersistedResults(Guid.NewGuid, Imported))
            c.AddGstr2bReconResult(result);
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh 2B Co", FyStart);

    [Fact]
    public void Json_round_trips_byte_stable_no_secret_no_brand()
    {
        var c = Build2bCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        var text = Encoding.UTF8.GetString(first);
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nic_", text, StringComparison.OrdinalIgnoreCase); // ER-16: no credential surface
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = Build2bCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_payload()
    {
        var c = Build2bCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Import_into_fresh_company_preserves_snapshot_lines_and_relinks_matched_voucher()
    {
        var source = Build2bCompany();
        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // All three lines survived (paisa + cess + ITC bifurcation exact) — even the in-2B-only line whose supplier
            // was never a booked purchase AND the supplier-RCM line (external data, imported unconditionally).
            var snap = Assert.Single(fresh.Gstr2bSnapshots);
            Assert.Equal("2025-10", snap.ReturnPeriod);
            Assert.Equal(Imported, snap.ImportedAt);
            Assert.Equal(3, snap.Lines.Count);
            var tobacco = snap.Lines.Single(l => l.DocNumber == "CN-9");
            Assert.Equal(20_000, tobacco.CessPaisa);
            Assert.False(tobacco.ItcAvailable);
            Assert.True(tobacco.ReverseCharge);

            // The Matched recon result re-linked to the imported voucher; the InPortalOnly result (the non-RCM unbooked
            // supplier) carries no voucher. The supplier-RCM line is excluded from reconciliation (finding #2), so the
            // ONLY InPortalOnly result is the non-RCM one.
            var matched = fresh.Gstr2bReconResults.Single(r => r.Bucket == ReconBucket.Matched);
            var purchase = fresh.Vouchers.Single(v => fresh.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Purchase);
            Assert.Equal(purchase.Id, matched.MatchedVoucherId);
            var portalOnly = fresh.Gstr2bReconResults.Single(r => r.Bucket == ReconBucket.InPortalOnly);
            Assert.Null(portalOnly.MatchedVoucherId);
            Assert.Equal("UNB-1", snap.FindLine(portalOnly.LineId)!.DocNumber);
        }
    }

    [Fact]
    public void Import_preserves_the_recon_tolerance()
    {
        // Finding #5: the reconciliation tolerance (Build2bCompany sets ₹1 / ±2 days) survives export→import in BOTH
        // JSON and XML — it was silently dropped from the canonical model before.
        var source = Build2bCompany();
        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            Assert.True(new CompanyImportService(fresh).Apply(model!).Applied);
            Assert.Equal(new Money(1m), fresh.Gst!.ReconValueTolerance);
            Assert.Equal(2, fresh.Gst!.ReconDateWindowDays);
        }
    }

    [Fact]
    public void A_matched_recon_with_a_dangling_voucher_rejects_the_whole_batch()
    {
        var source = Build2bCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));

        // Corrupt one Matched recon result so its matched voucher is not in the payload (a dangling pointer). The bucket
        // invariant (Matched ⇒ a voucher) then fails on Rehydrate ⇒ the whole batch is rejected (all-or-nothing).
        var corrupted = model!.Payload.Gstr2bReconResults
            .Select(r => r.Bucket == "Matched" ? r with { MatchedVoucherId = Guid.NewGuid() } : r)
            .ToList();
        var badModel = model with { Payload = model.Payload with { Gstr2bReconResults = corrupted } };

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(badModel);
        Assert.False(result.Applied);
        // Rolled back cleanly: nothing staged.
        Assert.Empty(fresh.Gstr2bSnapshots);
        Assert.Empty(fresh.Gstr2bReconResults);
    }

    [Fact]
    public void A_plain_gst_company_serialises_with_empty_2b_collections()
    {
        var c = CompanyFactory.CreateSeeded("Plain GST Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMe, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Empty(model!.Payload.Gstr2bSnapshots);
        Assert.Empty(model.Payload.Gstr2bReconResults);
        Assert.Empty(model.Payload.ImsActions);            // ER-13 off (S6b): no IMS rows when the mirror is unused
    }

    // ---- S6b: IMS mirror + §17(5) flag Io fold-in ----

    /// <summary>A 2B company that ALSO carries S6b state: a §17(5)-blocked stock item + a §17(5)-flagged Purchases
    /// ledger, and three IMS decisions (an Accept, an Accept + Oct-2025 partial CDN reversal, and a Pending) on the
    /// non-RCM 2B lines.</summary>
    private static Company BuildImsCompany()
    {
        var c = CompanyFactory.CreateSeeded("IMS Recon Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMe, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var supplier = new Domain.Ledger(Guid.NewGuid(), "Supplier A", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false, maintainBillByBill: true)
        {
            PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplierA, StateCode = "24" },
        };
        c.AddLedger(supplier);

        // A §17(5)-flagged Purchases ledger (Ineligible/None — valid; exercises the ledger SalesPurchaseGst §17(5) path).
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true)
        {
            SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, ItcEligibility = ItcEligibility.Ineligible },
        };
        c.AddLedger(purchases);

        // A §17(5)-blocked stock item (BlockedSection17_5/MotorVehicles — valid; exercises the item §17(5) path + the
        // import-side EnsureValid guard).
        var inv = new InventoryService(c);
        var sg = inv.CreateStockGroup("Vehicles");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 0);
        var car = inv.CreateStockItem("Company Car", sg.Id, nos.Id);
        car.Gst = new StockItemGstDetails
        {
            HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            ItcEligibility = ItcEligibility.BlockedSection17_5, BlockedCreditCategory = BlockedCreditCategory.MotorVehicles,
        };

        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10000m), 1800) }, true, GstTaxDirection.Input);
        var credit = new Money(10000m + tax.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(10000m), DrCr.Debit),
            new(supplier.Id, credit, DrCr.Credit, billAllocations: new[] { new BillAllocation(BillRefType.NewRef, "INV-001", credit) }),
        };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines, partyId: supplier.Id));

        var snapshot = new Gstr2bSnapshot(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-10", GstinMe,
            new DateOnly(2025, 11, 14), "IMSHASH", Imported, 180_000, 0, 0, 0, new[]
            {
                new Gstr2bLine(Guid.NewGuid(), GstinSupplierA, "Supplier A", Gstr2bDocType.B2b, "INV-001", "INV001", D1,
                    "27", 1_000_000, 180_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
                new Gstr2bLine(Guid.NewGuid(), GstinSupplierA, "Supplier A", Gstr2bDocType.CreditNote, "CN-7", "CN7", D1,
                    "27", 200_000, 36_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
                new Gstr2bLine(Guid.NewGuid(), "09ZZZZZ0000Z1Z9", "Unbooked Co", Gstr2bDocType.B2b, "UNB-1", "UNB1", D1,
                    "27", 300_000, 54_000, 0, 0, 0, itcAvailable: true, null, reverseCharge: false),
            });
        c.AddGstr2bSnapshot(snapshot);

        var report = Gstr2bReconciler.Reconcile(c, snapshot, From, To, c.Gst!.ReconTolerance);
        foreach (var result in report.ToPersistedResults(Guid.NewGuid, Imported))
            c.AddGstr2bReconResult(result);

        var matched = snapshot.Lines.Single(l => l.DocNumber == "INV-001");
        var cn = snapshot.Lines.Single(l => l.DocNumber == "CN-7");
        var portalOnly = snapshot.Lines.Single(l => l.DocNumber == "UNB-1");
        ImsService.SetAction(c, matched.Id, ImsStatus.Accepted, actedOn: new DateOnly(2025, 11, 18));
        ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, remarks: "partial per contract",
            declaredReversalPaisa: 12_000, actedOn: new DateOnly(2025, 11, 18));
        ImsService.SetAction(c, portalOnly.Id, ImsStatus.Pending);
        return c;
    }

    [Fact]
    public void Ims_and_section17_5_json_round_trips_byte_stable()
    {
        var c = BuildImsCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        var text = Encoding.UTF8.GetString(first);
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nic_", text, StringComparison.OrdinalIgnoreCase); // ER-16: no credential surface
    }

    [Fact]
    public void Ims_and_section17_5_xml_round_trips_byte_stable()
    {
        var c = BuildImsCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Import_preserves_ims_actions_and_section17_5_flags_and_relinks_lines()
    {
        var source = BuildImsCompany();
        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // All three IMS decisions survived, each re-linked to its RE-MINTED 2B line (the ImportPlan mints fresh line
            // ids; the IMS lineId re-links through the same recon Guid-remap). Paisa + remarks are exact.
            Assert.Equal(3, fresh.ImsActions.Count);
            var snap = Assert.Single(fresh.Gstr2bSnapshots);
            var cn = snap.Lines.Single(l => l.DocNumber == "CN-7");
            var cnAction = fresh.FindImsActionForLine(cn.Id)!;
            Assert.Equal(ImsStatus.Accepted, cnAction.Status);
            Assert.Equal(12_000, cnAction.DeclaredReversalPaisa);
            Assert.Equal("partial per contract", cnAction.Remarks);
            var portalOnly = snap.Lines.Single(l => l.DocNumber == "UNB-1");
            Assert.Equal(ImsStatus.Pending, fresh.FindImsActionForLine(portalOnly.Id)!.Status);

            // §17(5) flags survived on BOTH the ledger block and the item block (paisa-free enum round-trip).
            Assert.Equal(ItcEligibility.Ineligible, fresh.FindLedgerByName("Purchases")!.SalesPurchaseGst!.ItcEligibility);
            var car = fresh.StockItems.Single(i => i.Name == "Company Car");
            Assert.Equal(ItcEligibility.BlockedSection17_5, car.Gst!.ItcEligibility);
            Assert.Equal(BlockedCreditCategory.MotorVehicles, car.Gst!.BlockedCreditCategory);
        }
    }

    [Fact]
    public void A_malformed_ims_action_rejects_the_whole_batch()
    {
        var source = BuildImsCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));

        // Corrupt one IMS action into an invalid state (a declared reversal on a REJECTED line violates the invariant).
        // Rehydrate fails fast in pre-flight ⇒ the whole batch rolls back (all-or-nothing, RQ-23).
        var corrupted = model!.Payload.ImsActions
            .Select((a, idx) => idx == 0 ? a with { Status = "Rejected", DeclaredReversalPaisa = 9_999, Remarks = "x" } : a)
            .ToList();
        var badModel = model with { Payload = model.Payload with { ImsActions = corrupted } };

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(badModel);
        Assert.False(result.Applied);
        Assert.Empty(fresh.ImsActions);
        Assert.Empty(fresh.Gstr2bSnapshots);      // rolled back cleanly
    }

    [Fact]
    public void A_malformed_section17_5_ledger_block_rejects_the_whole_batch()
    {
        // FIX 6: the sales/purchase LEDGER import path builds SalesPurchaseGst but must ALSO run EnsureValid (mirror the
        // stock-item guard) — otherwise a malformed §17(5) block on a ledger (a BlockedCreditCategory with a non-blocked
        // eligibility) bypasses the domain's fail-fast bijection guard. The whole batch must reject (all-or-nothing).
        var source = BuildImsCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));

        // Corrupt the Purchases ledger's §17(5) block into a contradiction: a MotorVehicles blocked-credit category on an
        // "Eligible" eligibility (EnsureValid demands a BlockedSection17_5 eligibility for any named §17(5) category).
        var corrupted = model!.Payload.Ledgers
            .Select(l => l.Name == "Purchases" && l.SalesPurchaseGst is { } g
                ? l with { SalesPurchaseGst = g with { ItcEligibility = "Eligible", BlockedCreditCategory = "MotorVehicles" } }
                : l)
            .ToList();
        var badModel = model with { Payload = model.Payload with { Ledgers = corrupted } };

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(badModel);
        Assert.False(result.Applied);
        // Rolled back cleanly: nothing staged — the malformed ledger block never persisted, and neither did the rest.
        Assert.Empty(fresh.Gstr2bSnapshots);
        Assert.Empty(fresh.ImsActions);
    }
}
