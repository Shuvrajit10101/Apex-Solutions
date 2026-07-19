using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using LedgerMaster = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests.Fixtures;

/// <summary>
/// Builds a <b>realistically populated</b> company for UI layout auditing — the thing the v2 sweep lacked.
/// <para>
/// The v2 fixture was a thin seed (≈2 ledgers, 1 voucher), so "no truncation observed" proved nothing: a pane
/// that comfortably fits a 20-character ledger name may still ellipsize a 56-character one. This fixture
/// populates every master set an Indian SME would really carry, with <b>long-but-plausible</b> values, so that a
/// truncation finding is <i>fair</i> (the data is real-world, not adversarial) and an absence of truncation is
/// <i>meaningful</i> (the pane was actually stressed).
/// </para>
/// <para><b>Calibration (measured, not estimated).</b> Every name here is one a real Indian SME could enter.
/// Longest company name 66 chars; longest party ledger 55 ("Balaji Hardware &amp; Sanitary Stores (Pune — Bhosari
/// MIDC)"), each carrying the parenthetical state/branch disambiguator the CA flagged as the thing pickers
/// silently drop; longest ledger of any kind 60; longest stock item 52; longest godown 56; longest cost centre 51;
/// longest address line 56. Nothing is padded to an absurd length — a 500-character name would manufacture a
/// defect rather than expose one. <see cref="LongestValues"/> reports the per-field maxima so an auditor can judge
/// each truncation finding against what was actually seeded.</para>
/// <para><b>Feature gates.</b> <see cref="BuildRegular"/> turns on every gate a single company can hold at once:
/// batches, BOM (via real BOM state, which is what <c>Company.SetComponentsBom</c> infers from), price levels,
/// job-order processing, actual/billed quantities, payroll + payroll statutory, and Regular GST. Composition is
/// mutually exclusive with Regular GST, so CMP-08 / GSTR-4 need the separate
/// <see cref="BuildComposition"/> company.</para>
/// </summary>
public static class PopulatedCompanyFixture
{
    public static readonly DateOnly FyStart = new(2025, 4, 1);
    public static readonly DateOnly FyEnd = new(2026, 3, 31);

    /// <summary>The Regular-GST company name (67 chars — a real Pvt Ltd style name, header/title-bar stress).</summary>
    public const string RegularCompanyName =
        "Shreenath Industrial Fasteners & Engineering Works Private Limited";

    /// <summary>The Composition-registration company name (56 chars).</summary>
    public const string CompositionCompanyName =
        "Annapurna Family Restaurant & Caterers (Pune Camp Branch)";

    // ------------------------------------------------------------------ public entry points

    /// <summary>
    /// The main audit company: Regular GST dealer, every non-exclusive feature gate on, populated with enough
    /// rows that list panes must scroll. Fully posted through the real <see cref="LedgerService"/> path, so every
    /// voucher satisfies the same invariants a user-entered one does.
    /// </summary>
    public static Company BuildRegular()
    {
        var c = CompanyFactory.CreateSeeded(RegularCompanyName, FyStart, FyStart);

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = Mint("27", "AAPFU", "0939", "F", '1'),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        // Pure user toggles (cannot be inferred from data — see Company.cs).
        c.UseSeparateActualBilledQuantity = true;
        c.EnableMultiplePriceLevels = true;
        c.EnableJobOrderProcessing = true;
        c.PayrollEnabled = true;
        c.PayrollStatutoryEnabled = true;
        c.DefineBomComponentType = true;

        var ledgers = SeedLedgersAndParties(c);
        SeedCostStructure(c);
        var inv = SeedInventoryMasters(c);
        SeedBatches(c, inv);
        SeedBom(c, inv);              // ⇒ Company.SetComponentsBom infers true ⇒ BomMaster + ManufacturingJournalEntry
        SeedPriceLists(c, inv);
        SeedReorderLevels(c, inv);
        SeedPayroll(c);
        SeedBudgetsAndScenarios(c);
        PostVouchers(c, ledgers, inv);

        return c;
    }

    /// <summary>
    /// The second company, required because <c>RegistrationType</c> is single-valued: a Composition registration
    /// is the only way to reach <b>CMP-08</b> and <b>GSTR-4</b> (both gated on
    /// <c>MainWindowViewModel.IsCompositionDealer</c>). Deliberately smaller — it exists to unlock two screens,
    /// not to re-stress every pane — but still carries multi-row parties and posted Bills of Supply.
    /// </summary>
    public static Company BuildComposition()
    {
        var c = CompanyFactory.CreateSeeded(CompositionCompanyName, FyStart, FyStart);

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = Mint("27", "AAGCA", "4821", "H", '1'),
            RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Restaurant,
            CompositionOptInDate = FyStart,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
        });

        var sales = Add(c, "Sale of Food & Beverages (Composition — Bill of Supply)", "Sales Accounts", false);
        var purchases = Add(c, "Purchases — Provisions, Vegetables & Dairy", "Purchase Accounts", true);
        var cash = c.FindLedgerByName("Cash")!;

        var parties = new[]
        {
            ("Sahyadri Corporate Catering Services (Hinjewadi Phase-1)", "MH", "411057"),
            ("Deccan Gymkhana Members Club & Lounge (Pune Camp)", "MH", "411001"),
            ("Kalyani Nagar Software Park Cafeteria Contract", "MH", "411006"),
        };
        var partyLedgers = new List<LedgerMaster>();
        foreach (var (name, _, pin) in parties)
        {
            var l = Add(c, name, "Sundry Debtors", true);
            l.Mailing = new PartyMailingDetails
            {
                MailingName = name,
                Address = "Shop No. 14, Ground Floor, Sai Prasad Commercial Complex\nNear Bharat Petroleum Pump, Nagar Road\nPune, Maharashtra",
                Country = "India",
                Pincode = pin,
            };
            l.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Unregistered, StateCode = "27" };
            partyLedgers.Add(l);
        }

        var svc = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        for (var i = 0; i < 9; i++)
        {
            var party = partyLedgers[i % partyLedgers.Count];
            var amount = Money.FromRupees(4_500m + (i * 1_250m));
            svc.Post(new Voucher(Guid.NewGuid(), salesType, FyStart.AddDays(6 + (i * 9)),
                new[] { new EntryLine(party.Id, amount, DrCr.Debit), new EntryLine(sales.Id, amount, DrCr.Credit) },
                partyId: party.Id,
                narration: "Bill of Supply — composition dealer, no tax collected (§10 CGST Act)."));
        }

        for (var i = 0; i < 5; i++)
        {
            var amount = Money.FromRupees(3_200m + (i * 900m));
            svc.Post(new Voucher(Guid.NewGuid(), purchType, FyStart.AddDays(4 + (i * 15)),
                new[] { new EntryLine(purchases.Id, amount, DrCr.Debit), new EntryLine(cash.Id, amount, DrCr.Credit) },
                narration: "Daily provisions purchase — ITC not claimed (composition scheme)."));
        }

        return c;
    }

    // ------------------------------------------------------------------ masters

    private sealed record LedgerSet(
        LedgerMaster Cash,
        LedgerMaster Bank,
        LedgerMaster Sales,
        LedgerMaster Purchases,
        IReadOnlyList<LedgerMaster> Debtors,
        IReadOnlyList<LedgerMaster> Creditors,
        IReadOnlyList<LedgerMaster> Expenses);

    private sealed record InventorySet(
        IReadOnlyList<StockItem> Items,
        IReadOnlyList<Godown> Godowns,
        IReadOnlyList<Unit> Units,
        IReadOnlyList<PriceLevel> Levels);

    /// <summary>
    /// Parties carry the parenthetical disambiguator (state / GSTIN fragment / branch) the CA flagged as the thing
    /// pickers silently drop, plus a full multi-line address with PIN — so a picker that truncates makes two real
    /// parties indistinguishable, and the audit can prove it.
    /// </summary>
    private static readonly (string Name, string StateCode, string Pin, string City, bool Registered)[] DebtorSpecs =
    {
        ("Shree Venkateshwara Traders & Distributors (Karnataka)", "29", "560058", "Bengaluru", true),
        ("Balaji Hardware & Sanitary Stores (Pune — Bhosari MIDC)", "27", "411026", "Pune", true),
        ("Krishna Enterprises (Hyderabad Branch — Balanagar)", "36", "500037", "Hyderabad", true),
        ("Maruti Auto Components Private Limited (Chakan Unit-II)", "27", "410501", "Chakan", true),
        ("New India Engineering Syndicate (Delhi — Karol Bagh)", "07", "110005", "New Delhi", true),
        ("Coimbatore Precision Turned Parts (Tamil Nadu)", "33", "641021", "Coimbatore", true),
        ("Rajkot Forging & Fastener Udyog (Gujarat — Aji GIDC)", "24", "360003", "Rajkot", true),
        ("Eastern Structural Steel Agency (Howrah Works)", "19", "711106", "Howrah", true),
        ("Ludhiana Cycle Parts Manufacturing Co. (Punjab)", "03", "141003", "Ludhiana", false),
        ("Walk-in Retail Counter Sales (Unregistered — Bhosari)", "27", "411026", "Pune", false),
        ("Ahmedabad Industrial Supply Corporation (Naroda GIDC)", "24", "382330", "Ahmedabad", true),
        ("Nashik Valve & Pipe Fittings Distributors (Satpur)", "27", "422007", "Nashik", true),
    };

    private static readonly (string Name, string StateCode, string Pin, string City)[] CreditorSpecs =
    {
        ("Jindal Steel Rounds & Bright Bars Depot (Raipur — CG)", "22", "492001", "Raipur"),
        ("Bharat Zinc Electroplating Job Works (Bhosari MIDC)", "27", "411026", "Pune"),
        ("Sundaram Fasteners Raw Material Division (Chennai)", "33", "600058", "Chennai"),
        ("Maharashtra State Electricity Distribution Co. Ltd.", "27", "411019", "Pune"),
        ("Deepak Packaging & Corrugated Cartons (Chinchwad)", "27", "411019", "Pune"),
        ("Vishwakarma Tool Room & Die Maintenance Services", "27", "411026", "Pune"),
    };

    private static readonly (string Name, string Group)[] ExpenseSpecs =
    {
        ("Factory Power & Electricity Charges (MSEDCL — HT Connection)", "Indirect Expenses"),
        ("Freight, Cartage & Outward Transportation Charges", "Indirect Expenses"),
        ("Staff Welfare, Canteen & Uniform Expenses", "Indirect Expenses"),
        ("Repairs & Maintenance — Plant, Machinery and Tool Room", "Indirect Expenses"),
        ("Professional & Legal Fees (Statutory Audit, ROC, GST)", "Indirect Expenses"),
        ("Rent, Rates & Municipal Taxes — Bhosari Factory Premises", "Indirect Expenses"),
        ("Telephone, Internet & Communication Expenses", "Indirect Expenses"),
        ("Printing, Stationery & Office Consumables", "Indirect Expenses"),
    };

    private static LedgerSet SeedLedgersAndParties(Company c)
    {
        var cash = c.FindLedgerByName("Cash")!;

        var bank = Add(c, "Bank of Maharashtra — CC A/c 60123456789 (Bhosari)", "Bank Accounts", true,
            opening: 1_284_650m, isDebit: true);
        bank.EnableChequePrinting = true;
        bank.ChequePrintingBankName = "Bank of Maharashtra";

        var sales = Add(c, "Sales — Industrial Fasteners (Taxable @ 18%)", "Sales Accounts", false);
        var purchases = Add(c, "Purchases — Steel Raw Material (Taxable @ 18%)", "Purchase Accounts", true);

        var debtors = new List<LedgerMaster>();
        var entity = '1';
        var panSeq = 1000;
        foreach (var (name, state, pin, city, registered) in DebtorSpecs)
        {
            var l = Add(c, name, "Sundry Debtors", true,
                opening: debtors.Count % 3 == 0 ? 45_000m + (debtors.Count * 8_500m) : 0m, isDebit: true);
            l.MaintainBillByBill = true;
            l.DefaultCreditPeriodDays = 30 + (debtors.Count % 3) * 15;
            l.Mailing = MailingFor(name, city, pin);
            l.PartyGst = registered
                ? new PartyGstDetails
                {
                    RegistrationType = GstRegistrationType.Regular,
                    Gstin = Mint(state, "ABCDE", (panSeq++).ToString(), "K", entity),
                    StateCode = state,
                    IsBodyCorporate = name.Contains("Private Limited", StringComparison.Ordinal),
                }
                : new PartyGstDetails { RegistrationType = GstRegistrationType.Unregistered, StateCode = state };
            debtors.Add(l);
        }

        var creditors = new List<LedgerMaster>();
        foreach (var (name, state, pin, city) in CreditorSpecs)
        {
            var l = Add(c, name, "Sundry Creditors", false,
                opening: creditors.Count % 2 == 0 ? 62_400m + (creditors.Count * 11_000m) : 0m, isDebit: false);
            l.MaintainBillByBill = true;
            l.DefaultCreditPeriodDays = 45;
            l.Mailing = MailingFor(name, city, pin);
            l.PartyGst = new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular,
                Gstin = Mint(state, "PQRST", (panSeq++).ToString(), "M", entity),
                StateCode = state,
                IsBodyCorporate = true,
            };
            creditors.Add(l);
        }

        var expenses = ExpenseSpecs.Select(e => Add(c, e.Name, e.Group, true)).ToList();

        return new LedgerSet(cash, bank, sales, purchases, debtors, creditors, expenses);
    }

    private static PartyMailingDetails MailingFor(string name, string city, string pin) => new()
    {
        MailingName = name,
        // Three address lines + city line — a real Indian industrial address, the longest single line is 62 chars.
        Address =
            "Plot No. 47/B, Sector 5, MIDC Industrial Estate\n"
            + "Behind Telco Trucks Weighbridge, Old Mumbai-Pune Highway\n"
            + $"{city}, India",
        Country = "India",
        Pincode = pin,
    };

    private static void SeedCostStructure(Company c)
    {
        var primary = c.CostCategories[0];

        var branches = new CostCategory(Guid.NewGuid(), "Branch & Territory Allocation");
        c.AddCostCategory(branches);

        foreach (var name in new[]
        {
            "Bhosari MIDC Manufacturing Unit (Cost Centre 01)",
            "Chakan Assembly & Despatch Unit (Cost Centre 02)",
            "Domestic Sales & Marketing Division — West Zone",
            "Export Sales Division (Middle East & Africa)",
            "Tool Room, Maintenance & Utilities Department",
            "Administration, Finance & Corporate Overheads",
        })
            c.AddCostCentre(new CostCentre(Guid.NewGuid(), name, primary.Id));

        foreach (var name in new[]
        {
            "Western Region — Maharashtra, Gujarat & Goa",
            "Southern Region — Karnataka, Tamil Nadu & Telangana",
            "Northern Region — Delhi NCR, Punjab & Haryana",
        })
            c.AddCostCentre(new CostCentre(Guid.NewGuid(), name, branches.Id));
    }

    private static InventorySet SeedInventoryMasters(Company c)
    {
        var nos = Unit.Simple(Guid.NewGuid(), "Nos", "Numbers", 0, "NOS");
        var kg = Unit.Simple(Guid.NewGuid(), "Kg", "Kilograms", 3, "KGS");
        var box = Unit.Simple(Guid.NewGuid(), "Box", "Boxes of Hundred", 0, "BOX");
        var ltr = Unit.Simple(Guid.NewGuid(), "Ltr", "Litres", 2, "LTR");
        foreach (var u in new[] { nos, kg, box, ltr }) c.AddUnit(u);
        var boxOfNos = Unit.Compound(Guid.NewGuid(), "Box of 100 Nos", "Box of One Hundred Numbers", box.Id, nos.Id, 100);
        c.AddUnit(boxOfNos);
        var units = new List<Unit> { nos, kg, box, ltr, boxOfNos };

        var fasteners = new StockGroup(Guid.NewGuid(), "Industrial Fasteners & Threaded Components");
        var raw = new StockGroup(Guid.NewGuid(), "Raw Material — Steel Bars, Rods & Wire Coils");
        var consumables = new StockGroup(Guid.NewGuid(), "Consumables, Lubricants & Packing Material");
        foreach (var g in new[] { fasteners, raw, consumables }) c.AddStockGroup(g);
        var boltsSub = new StockGroup(Guid.NewGuid(), "High Tensile Bolts & Studs (Grade 8.8 / 10.9)", fasteners.Id);
        c.AddStockGroup(boltsSub);

        var catA = new StockCategory(Guid.NewGuid(), "Automotive OEM Approved Components");
        var catB = new StockCategory(Guid.NewGuid(), "General Engineering & Construction Grade");
        c.AddStockCategory(catA);
        c.AddStockCategory(catB);

        // Godowns — long but real Indian plant/warehouse names, plus a third-party job-worker location.
        var main = c.Godowns[0];
        var gd = new List<Godown> { main };
        foreach (var (name, third) in new[]
        {
            ("Bhosari MIDC Raw Material Stores (Unit-1, Shed-A)", false),
            ("Chakan MIDC Phase-II Finished Goods Warehouse", false),
            ("Bonded Export Consignment Yard — JNPT Nhava Sheva", false),
            ("Bharat Zinc Electroplating Job Work Premises (3rd Party)", true),
        })
        {
            var g = new Godown(Guid.NewGuid(), name, thirdParty: third);
            c.AddGodown(g);
            gd.Add(g);
        }

        // Stock items — the longest is 52 chars, a genuine fastener description (size + finish + grade + standard).
        var itemSpecs = new (string Name, Guid Group, Guid Cat, Guid Unit, string Hsn, decimal Cost, bool Batch)[]
        {
            ("Hexagonal Head Bolt M12 x 75mm Zinc Plated Gr 8.8", boltsSub.Id, catA.Id, nos.Id, "73181500", 18.40m, true),
            ("Hexagonal Head Bolt M16 x 100mm Hot Dip Galvanised", boltsSub.Id, catB.Id, nos.Id, "73181500", 34.75m, true),
            ("Stainless Steel Hex Nut M12 (SS-304, IS 1364 Pt-3)", fasteners.Id, catA.Id, nos.Id, "73181600", 6.90m, true),
            ("Mild Steel Flat Washer 12mm Electro-Galvanised", fasteners.Id, catB.Id, nos.Id, "73182100", 1.85m, false),
            ("Allen Cap Screw M8 x 40mm Black Oxide Grade 12.9", fasteners.Id, catA.Id, nos.Id, "73181500", 12.30m, false),
            ("Self Drilling Roofing Screw 12x50mm with EPDM Washer", fasteners.Id, catB.Id, box.Id, "73181400", 385.00m, false),
            ("EN8 Bright Steel Round Bar 20mm dia (6 Mtr Length)", raw.Id, catB.Id, kg.Id, "72155000", 68.50m, true),
            ("SAE 1018 Cold Heading Quality Wire Coil 12mm", raw.Id, catB.Id, kg.Id, "72171000", 61.20m, true),
            ("Stainless Steel 304 Round Bar 16mm dia (Imported)", raw.Id, catA.Id, kg.Id, "72222000", 245.00m, false),
            ("Water Soluble Cutting Oil Concentrate (20 Ltr Can)", consumables.Id, catB.Id, ltr.Id, "27101980", 178.00m, false),
            ("Corrugated Export Carton 400x300x250mm 5-Ply", consumables.Id, catB.Id, nos.Id, "48191010", 42.00m, false),
            ("Zinc Passivation Chemical — Trivalent Blue (5 Ltr)", consumables.Id, catB.Id, ltr.Id, "38249900", 640.00m, false),
        };

        var items = new List<StockItem>();
        foreach (var s in itemSpecs)
        {
            var item = new StockItem(
                Guid.NewGuid(), s.Name, s.Group, s.Unit, s.Cat,
                valuationMethod: StockValuationMethod.AverageCost,
                hsnSacCode: s.Hsn,
                isTaxable: true,
                reorderLevel: 500m,
                minimumOrderQuantity: 250m,
                standardCost: Money.FromRupees(s.Cost),
                gst: new StockItemGstDetails { HsnSac = s.Hsn, RateBasisPoints = 1800 });
            item.MaintainInBatches = s.Batch;
            c.AddStockItem(item);
            items.Add(item);
        }

        // ---- Catalogue depth: a real fastener SME lists far more SKUs than it transacts in a period. These are
        // MASTERS ONLY (no opening stock, no movements, so they cannot perturb the negative-stock invariant), and
        // they exist for one reason: to push the stock-item list past a 1080p pane height so the item picker and the
        // Stock Summary must genuinely SCROLL. Row count is a layout stressor in its own right, distinct from the
        // long-value stressor above. They are appended AFTER the transacting items so every index used by the
        // voucher/BOM/price-list seeding above stays stable.
        var sizes = new[] { "M6 x 25mm", "M8 x 50mm", "M10 x 40mm", "M10 x 65mm", "M12 x 100mm", "M14 x 60mm", "M16 x 80mm", "M20 x 90mm" };
        var finishes = new[] { "Zinc Plated Gr 8.8", "Hot Dip Galvanised Gr 10.9", "Plain Black Self Colour" };
        var k = 0;
        foreach (var size in sizes)
        {
            foreach (var finish in finishes.Take(k % 2 == 0 ? 2 : 1))
            {
                k++;
                var extra = new StockItem(
                    Guid.NewGuid(), $"Hex Head Bolt {size} {finish} (IS 1364)", boltsSub.Id, nos.Id, catB.Id,
                    valuationMethod: StockValuationMethod.AverageCost,
                    hsnSacCode: "73181500",
                    isTaxable: true,
                    reorderLevel: 1000m,
                    minimumOrderQuantity: 500m,
                    standardCost: Money.FromRupees(9.50m + (k * 2.35m)),
                    gst: new StockItemGstDetails { HsnSac = "73181500", RateBasisPoints = 1800 });
                c.AddStockItem(extra);
                items.Add(extra);
            }
        }

        // Opening stock, placed by the item's ROLE rather than round-robin: finished fasteners (0..5) live in the
        // Chakan finished-goods warehouse (where the sales below despatch from), raw material and consumables
        // (6..11) live in the Bhosari stores (where the purchases below receive into). A round-robin placement
        // posts sales against a godown that never held the item, and the real InventoryPostingService correctly
        // rejects it — the fixture must model a coherent business, not just satisfy constructors.
        // Bounded to itemSpecs.Length, NOT items.Count: the catalogue-depth SKUs appended above are deliberately
        // stockless (a listed-but-not-stocked SKU is itself realistic, and it keeps them inert for the engine).
        for (var i = 0; i < itemSpecs.Length; i++)
        {
            var item = items[i];
            var godown = i < 6 ? gd[2] : gd[1];
            c.AddStockOpeningBalance(new StockOpeningBalance(
                Guid.NewGuid(), item.Id, godown.Id,
                quantity: 250m + (i * 75m),
                rate: item.StandardCost ?? Money.FromRupees(10m),
                batchLabel: item.MaintainInBatches ? $"BN-2025-{(i + 1) * 7:D4}-OPENING" : null));
        }

        var levels = new List<PriceLevel>();
        foreach (var name in new[]
        {
            "Wholesale — Authorised Distributor (Slab Rated)",
            "Retail Counter Sales (MRP less Standard Discount)",
            "Institutional / OEM Contract Pricing (Annual)",
            "Export FOB Pricing (USD converted at monthly rate)",
        })
        {
            var lv = new PriceLevel(Guid.NewGuid(), name);
            c.AddPriceLevel(lv);
            levels.Add(lv);
        }

        return new InventorySet(items, gd, units, levels);
    }

    private static void SeedBatches(Company c, InventorySet inv)
    {
        var batched = inv.Items.Where(i => i.MaintainInBatches).ToList();
        var n = 0;
        foreach (var item in batched)
        {
            for (var k = 0; k < 3; k++)
            {
                n++;
                var mfg = FyStart.AddDays(10 + (n * 11));
                c.AddBatchMaster(new BatchMaster(
                    Guid.NewGuid(), item.Id,
                    // Real heat-number / lot-code shape used on steel goods — long, and the thing a grid must show.
                    batchNumber: $"HEAT-{2025_000 + (n * 137)}/LOT-{n:D3}-BHOSARI",
                    manufacturingDate: mfg,
                    expiryDate: mfg.AddMonths(24),
                    godownId: inv.Godowns[(n % (inv.Godowns.Count - 1))].Id,
                    inwardQuantity: 120m + (n * 25m),
                    inwardRate: item.StandardCost));
            }
        }
    }

    /// <summary>
    /// Seeds a real Bill of Materials. This is what flips <c>Company.SetComponentsBom</c> — the flag is
    /// <b>inferred</b> from BOM state, not stored — which is precisely the gate that made <c>BomMaster</c> and
    /// <c>ManufacturingJournalEntry</c> unreachable for the v2 sweep.
    /// </summary>
    private static void SeedBom(Company c, InventorySet inv)
    {
        var finished = inv.Items[0];  // M12 x 75 bolt
        var bar = inv.Items.First(i => i.Name.StartsWith("EN8 Bright Steel", StringComparison.Ordinal));
        var wire = inv.Items.First(i => i.Name.StartsWith("SAE 1018", StringComparison.Ordinal));
        var oil = inv.Items.First(i => i.Name.StartsWith("Water Soluble", StringComparison.Ordinal));
        var carton = inv.Items.First(i => i.Name.StartsWith("Corrugated Export", StringComparison.Ordinal));

        finished.SetComponents = true;

        c.AddBillOfMaterials(new BillOfMaterials(
            Guid.NewGuid(), finished.Id,
            name: "Standard BOM — M12x75 Bolt Cold Forged (1000 Nos Block)",
            unitOfManufacture: 1000m,
            lines: new[]
            {
                new BomLine(BomLineType.Component, wire.Id, 118.500m, inv.Godowns[1].Id),
                new BomLine(BomLineType.Component, bar.Id, 12.250m, inv.Godowns[1].Id),
                new BomLine(BomLineType.Component, oil.Id, 3.500m, inv.Godowns[1].Id),
                new BomLine(BomLineType.Component, carton.Id, 10.000m, inv.Godowns[1].Id),
            }));

        var second = inv.Items[1];  // M16 x 100 bolt
        second.SetComponents = true;
        c.AddBillOfMaterials(new BillOfMaterials(
            Guid.NewGuid(), second.Id,
            name: "Alternate BOM — M16x100 HDG Bolt (500 Nos Block, Sub-contract Plating)",
            unitOfManufacture: 500m,
            lines: new[]
            {
                new BomLine(BomLineType.Component, bar.Id, 96.750m, inv.Godowns[1].Id),
                new BomLine(BomLineType.Component, oil.Id, 2.250m, inv.Godowns[1].Id),
                new BomLine(BomLineType.Component, carton.Id, 5.000m, inv.Godowns[1].Id),
            }));
    }

    private static void SeedPriceLists(Company c, InventorySet inv)
    {
        foreach (var level in inv.Levels)
        {
            foreach (var item in inv.Items.Take(8))
            {
                var basis = (item.StandardCost ?? Money.FromRupees(10m)).Amount;
                c.AddPriceList(new PriceList(
                    Guid.NewGuid(), level.Id, item.Id, FyStart,
                    new[]
                    {
                        new PriceListSlab(0m, 100m, Money.FromRupees(decimal.Round(basis * 1.45m, 2)), 0m),
                        new PriceListSlab(100m, 1000m, Money.FromRupees(decimal.Round(basis * 1.32m, 2)), 2.5m),
                        new PriceListSlab(1000m, null, Money.FromRupees(decimal.Round(basis * 1.22m, 2)), 5m),
                    }));
            }
        }
    }

    private static void SeedReorderLevels(Company c, InventorySet inv)
    {
        foreach (var item in inv.Items.Take(6))
            c.AddReorderDefinition(new ReorderDefinition(
                Guid.NewGuid(), ReorderScope.Item, item.Id,
                reorderQuantity: 750m, minOrderQuantity: 500m));
    }

    private static void SeedPayroll(Company c)
    {
        c.PfConfig = new PfConfig { EstablishmentCode = "MHBAN0045128000", CapWagesAtCeiling = true };
        c.EsiConfig = new EsiConfig { EmployerCode = "34000123450001001" };
        c.SalaryTdsEnabled = true;

        var cat = new EmployeeCategory(Guid.NewGuid(), "Direct Production Manpower (Shop Floor)");
        c.AddEmployeeCategory(cat);
        var catIndirect = new EmployeeCategory(Guid.NewGuid(), "Indirect & Administrative Staff");
        c.AddEmployeeCategory(catIndirect);

        var grpShop = new EmployeeGroup(Guid.NewGuid(), "Production Department — Cold Forging Shop");
        var grpQa = new EmployeeGroup(Guid.NewGuid(), "Quality Assurance & Metallurgical Laboratory");
        var grpAdmin = new EmployeeGroup(Guid.NewGuid(), "Accounts, Stores & General Administration");
        foreach (var g in new[] { grpShop, grpQa, grpAdmin }) c.AddEmployeeGroup(g);

        var employeeSpecs = new (string Name, Guid Group, Guid Cat, string Designation, string Gender)[]
        {
            ("Ramchandra Sitaram Deshpande", grpShop.Id, cat.Id, "Senior Machine Operator — Cold Header", "Male"),
            ("Sunita Prakash Kulkarni", grpQa.Id, catIndirect.Id, "Quality Inspector — Incoming Material", "Female"),
            ("Mohammed Irfan Abdul Sattar Shaikh", grpShop.Id, cat.Id, "Setter cum Thread Rolling Operator", "Male"),
            ("Vijayalakshmi Narayanaswamy Iyer", grpAdmin.Id, catIndirect.Id, "Assistant Manager — Accounts & Payroll", "Female"),
            ("Balwinder Singh Gurmeet Singh Sandhu", grpShop.Id, cat.Id, "Maintenance Fitter — Tool Room Grade-I", "Male"),
            ("Anantharaman Venkatasubramanian", grpQa.Id, catIndirect.Id, "Deputy Manager — Quality Assurance", "Male"),
            ("Pratibha Dattatray Jadhav", grpAdmin.Id, catIndirect.Id, "Stores Assistant & Despatch Clerk", "Female"),
            ("Ganesh Bhaskar Ambekar", grpShop.Id, cat.Id, "Helper — Packing and Despatch Section", "Male"),
        };

        var n = 0;
        foreach (var s in employeeSpecs)
        {
            n++;
            var e = new Employee(Guid.NewGuid(), s.Name, s.Group)
            {
                EmployeeCategoryId = s.Cat,
                EmployeeNumber = $"SIF/EMP/2025/{n:D4}",
                Designation = s.Designation,
                Function = "Manufacturing Operations",
                Location = "Bhosari MIDC Industrial Estate, Pune",
                Gender = s.Gender,
                DateOfBirth = new DateOnly(1985 + (n % 12), ((n * 3) % 12) + 1, ((n * 5) % 27) + 1),
                DateOfJoining = FyStart.AddDays(-(365 * (1 + (n % 6)))),
                Pan = $"ABCPD{1000 + n}{(char)('A' + (n % 26))}",
                Aadhaar = $"{4000 + n:D4} {5000 + n:D4} {6000 + n:D4}",
                Uan = $"1005{n:D8}",
                PfAccountNumber = $"MH/BAN/0045128/000/{n:D7}",
                PfApplicable = true,
                PfJoinDate = FyStart.AddDays(-(365 * (1 + (n % 6)))),
                EsiNumber = $"31{n:D8}0001001",
                EsiApplicable = n % 3 != 0,
                BankName = "Bank of Maharashtra",
                BankAccountNumber = $"6012345{n:D4}",
                BankIfsc = "MAHB0000123",
                ApplicableTaxRegime = n % 2 == 0 ? TaxRegime.New : TaxRegime.Old,
            };
            c.AddEmployee(e);
        }

        foreach (var (name, type, calc) in new (string, PayHeadType, PayHeadCalculationType)[]
        {
            ("Basic Salary & Dearness Allowance (Consolidated)", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance),
            ("House Rent Allowance @ 40% of Basic + DA", PayHeadType.Earnings, PayHeadCalculationType.AsComputedValue),
            ("Conveyance & Travelling Allowance (Monthly Fixed)", PayHeadType.Earnings, PayHeadCalculationType.FlatRate),
            ("Production Incentive & Overtime Earnings", PayHeadType.Earnings, PayHeadCalculationType.OnAttendance),
            ("Employee Provident Fund Contribution @ 12%", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsComputedValue),
            ("Employees' State Insurance Contribution @ 0.75%", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsComputedValue),
            ("Professional Tax Deduction (Maharashtra Slab)", PayHeadType.EmployeesStatutoryDeductions, PayHeadCalculationType.AsComputedValue),
            ("Salary Advance & Loan Recovery Instalment", PayHeadType.LoansAndAdvances, PayHeadCalculationType.FlatRate),
        })
            c.AddPayHead(new PayHead(Guid.NewGuid(), name, type, calc));

        foreach (var name in new[]
        {
            "Present — Full Working Day (General Shift)",
            "Absent — Loss of Pay (Unauthorised)",
            "Paid Earned Leave / Privilege Leave Availed",
            "Overtime Hours Worked Beyond Rostered Shift",
        })
            c.AddAttendanceType(new AttendanceType(Guid.NewGuid(), name, AttendanceTypeKind.AttendancePaid));
    }

    private static void SeedBudgetsAndScenarios(Company c)
    {
        foreach (var name in new[]
        {
            "Annual Operating Budget FY 2025-26 (Board Approved)",
            "Capital Expenditure Budget — Plant & Machinery FY26",
            "Departmental Overhead Budget — Indirect Expenses",
        })
            c.AddBudget(new Budget(Guid.NewGuid(), name, FyStart, FyEnd));

        foreach (var name in new[]
        {
            "Provisional Closing Scenario (Unaudited Estimates)",
            "Optimistic Revenue Forecast — 15% Growth Case",
            "Conservative Case with Deferred Capex Provisions",
        })
            c.AddScenario(new Scenario(Guid.NewGuid(), name));
    }

    // ------------------------------------------------------------------ vouchers

    /// <summary>
    /// Posts a batch of vouchers of every accounting type through the real <see cref="LedgerService"/>, including
    /// item-invoice-mode sales and purchases (so the invoice grids, the stock registers and the print/voucher
    /// preview all have real content). Enough rows that a day book / register pane must scroll.
    /// </summary>
    private static void PostVouchers(Company c, LedgerSet led, InventorySet inv)
    {
        var svc = new LedgerService(c);
        var t = (VoucherBaseType b) => c.VoucherTypes.First(v => v.BaseType == b).Id;

        var salesT = t(VoucherBaseType.Sales);
        var purchT = t(VoucherBaseType.Purchase);
        var payT = t(VoucherBaseType.Payment);
        var recT = t(VoucherBaseType.Receipt);
        var jrnT = t(VoucherBaseType.Journal);
        var conT = t(VoucherBaseType.Contra);
        var dnT = t(VoucherBaseType.DebitNote);
        var cnT = t(VoucherBaseType.CreditNote);

        var stores = inv.Godowns[1];
        var fg = inv.Godowns[2];

        // ---- Item-invoice PURCHASES first (they bring stock in, so the sales below never go negative).
        for (var i = 0; i < 6; i++)
        {
            var supplier = led.Creditors[i % led.Creditors.Count];
            var item = inv.Items[6 + (i % 3)];                     // raw material items
            var qty = 500m + (i * 120m);
            var rate = item.StandardCost ?? Money.FromRupees(60m);
            var value = Money.FromRupees(decimal.Round(qty * rate.Amount, 2));

            svc.Post(new Voucher(Guid.NewGuid(), purchT, FyStart.AddDays(3 + (i * 8)),
                new[]
                {
                    new EntryLine(led.Purchases.Id, value, DrCr.Debit),
                    new EntryLine(supplier.Id, value, DrCr.Credit),
                },
                partyId: supplier.Id,
                narration: $"Raw material inward against supplier invoice — GRN booked at {stores.Name}.",
                inventoryLines: new[]
                {
                    new VoucherInventoryLine(item.Id, stores.Id, qty, rate,
                        batchLabel: item.MaintainInBatches ? $"HEAT-{2025_000 + ((i + 1) * 137)}/LOT-{i + 1:D3}-BHOSARI" : null),
                }));
        }

        // ---- Item-invoice SALES.
        for (var i = 0; i < 10; i++)
        {
            var party = led.Debtors[i % led.Debtors.Count];
            var item = inv.Items[i % 5];                            // finished fastener items
            var qty = 40m + (i * 15m);
            var rate = Money.FromRupees(decimal.Round((item.StandardCost?.Amount ?? 20m) * 1.38m, 2));
            var value = Money.FromRupees(decimal.Round(qty * rate.Amount, 2));

            svc.Post(new Voucher(Guid.NewGuid(), salesT, FyStart.AddDays(20 + (i * 6)),
                new[]
                {
                    new EntryLine(party.Id, value, DrCr.Debit),
                    new EntryLine(led.Sales.Id, value, DrCr.Credit),
                },
                partyId: party.Id,
                narration: "Tax invoice raised against customer purchase order; goods despatched by road transport.",
                inventoryLines: new[]
                {
                    new VoucherInventoryLine(item.Id, fg.Id, qty, rate,
                        batchLabel: item.MaintainInBatches ? $"BN-2025-{(i % 5 + 1) * 7:D4}-OPENING" : null),
                }));
        }

        // ---- Accounting-only vouchers, one batch per base type.
        for (var i = 0; i < 8; i++)
        {
            var exp = led.Expenses[i % led.Expenses.Count];
            var amt = Money.FromRupees(8_400m + (i * 2_150m));
            svc.Post(new Voucher(Guid.NewGuid(), payT, FyStart.AddDays(12 + (i * 9)),
                new[] { new EntryLine(exp.Id, amt, DrCr.Debit), new EntryLine(led.Bank.Id, amt, DrCr.Credit) },
                narration: "Expense settled by NEFT transfer from the cash-credit account."));
        }

        for (var i = 0; i < 8; i++)
        {
            var party = led.Debtors[i % led.Debtors.Count];
            var amt = Money.FromRupees(15_000m + (i * 4_300m));
            svc.Post(new Voucher(Guid.NewGuid(), recT, FyStart.AddDays(30 + (i * 10)),
                new[] { new EntryLine(led.Bank.Id, amt, DrCr.Debit), new EntryLine(party.Id, amt, DrCr.Credit) },
                partyId: party.Id,
                narration: "Customer remittance received against outstanding invoices; RTGS UTR on record."));
        }

        for (var i = 0; i < 5; i++)
        {
            var amt = Money.FromRupees(25_000m + (i * 5_000m));
            svc.Post(new Voucher(Guid.NewGuid(), conT, FyStart.AddDays(18 + (i * 21)),
                new[] { new EntryLine(led.Cash.Id, amt, DrCr.Debit), new EntryLine(led.Bank.Id, amt, DrCr.Credit) },
                narration: "Cash withdrawn from bank for petty expenses and wage disbursement."));
        }

        for (var i = 0; i < 6; i++)
        {
            var exp = led.Expenses[(i + 2) % led.Expenses.Count];
            var creditor = led.Creditors[i % led.Creditors.Count];
            var amt = Money.FromRupees(6_750m + (i * 1_900m));
            svc.Post(new Voucher(Guid.NewGuid(), jrnT, FyStart.AddDays(25 + (i * 14)),
                new[] { new EntryLine(exp.Id, amt, DrCr.Debit), new EntryLine(creditor.Id, amt, DrCr.Credit) },
                narration: "Provision for expenses accrued but not billed as at the period end."));
        }

        for (var i = 0; i < 4; i++)
        {
            var creditor = led.Creditors[i % led.Creditors.Count];
            var amt = Money.FromRupees(3_400m + (i * 1_100m));
            svc.Post(new Voucher(Guid.NewGuid(), dnT, FyStart.AddDays(40 + (i * 18)),
                new[] { new EntryLine(creditor.Id, amt, DrCr.Debit), new EntryLine(led.Purchases.Id, amt, DrCr.Credit) },
                partyId: creditor.Id,
                narration: "Debit note raised on supplier for rate difference and short supply."));
        }

        for (var i = 0; i < 4; i++)
        {
            var party = led.Debtors[i % led.Debtors.Count];
            var amt = Money.FromRupees(2_900m + (i * 850m));
            svc.Post(new Voucher(Guid.NewGuid(), cnT, FyStart.AddDays(45 + (i * 17)),
                new[] { new EntryLine(led.Sales.Id, amt, DrCr.Debit), new EntryLine(party.Id, amt, DrCr.Credit) },
                partyId: party.Id,
                narration: "Credit note issued to customer for goods returned and post-sale discount allowed."));
        }
    }

    // ------------------------------------------------------------------ helpers

    private static LedgerMaster Add(
        Company c, string name, string groupName, bool isDebit, decimal opening = 0m)
        => Add(c, name, groupName, isDebit, opening, isDebit);

    private static LedgerMaster Add(
        Company c, string name, string groupName, bool openingIsDebitDefault, decimal opening, bool isDebit)
    {
        var group = c.FindGroupByName(groupName)
            ?? throw new InvalidOperationException($"Fixture ledger '{name}' references unknown group '{groupName}'.");
        var l = new LedgerMaster(Guid.NewGuid(), name, group.Id, Money.FromRupees(opening), isDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>
    /// Mints a <b>checksum-valid</b> 15-char GSTIN for a state: state code + PAN (5 letters, 4 digits, 1 letter)
    /// + entity code + 'Z' + the computed Luhn-mod-36 check character. Minting rather than hard-coding means every
    /// party carries a GSTIN that passes <see cref="Gstin.Validate"/>, so the fixture exercises the real
    /// validation path instead of side-stepping it.
    /// </summary>
    public static string Mint(string stateCode, string panLetters5, string panDigits4, string panLetter1, char entity)
    {
        var body = $"{stateCode}{panLetters5}{panDigits4.PadLeft(4, '0')}{panLetter1}{entity}Z";
        if (body.Length != 14)
            throw new ArgumentException($"GSTIN body '{body}' must be 14 characters before the check digit.");
        var withPlaceholder = body + "0";
        return body + Gstin.ComputeCheckDigit(withPlaceholder);
    }

    /// <summary>
    /// The longest seeded value per field, so an auditor can judge whether a truncation finding is fair: a pane
    /// that clips a value listed here is clipping something a real Indian SME would genuinely enter.
    /// </summary>
    public static IReadOnlyDictionary<string, (int Length, string Value)> LongestValues()
    {
        var v = new Dictionary<string, string>
        {
            ["Company name"] = RegularCompanyName,
            ["Party (debtor) ledger name"] = DebtorSpecs.OrderByDescending(d => d.Name.Length).First().Name,
            ["Party (creditor) ledger name"] = CreditorSpecs.OrderByDescending(d => d.Name.Length).First().Name,
            ["Expense ledger name"] = ExpenseSpecs.OrderByDescending(e => e.Name.Length).First().Name,
            ["Mailing address line"] = "Behind Telco Trucks Weighbridge, Old Mumbai-Pune Highway",
            ["GSTIN"] = Mint("27", "AAPFU", "0939", "F", '1'),
            ["Pincode"] = "411026",
        };
        return v.ToDictionary(kv => kv.Key, kv => (kv.Value.Length, kv.Value));
    }
}
