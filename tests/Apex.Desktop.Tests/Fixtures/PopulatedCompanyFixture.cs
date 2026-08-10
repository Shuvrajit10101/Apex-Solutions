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
/// <para><b>⚠️ VOUCHER COVERAGE — READ THIS BEFORE TRUSTING AN "ABSENCE OF DEFECT" ON THIS FIXTURE.</b> Until
/// plan.md <b>Phase 10.12 W0-7</b> this fixture posted <b>51 accounting vouchers of 8 base kinds and nothing
/// else</b> — no inventory, order, provisional, job-work, POS or payroll voucher existed on it, despite three
/// doc comments describing it as "51 vouchers of every type". Every report, print and export surface fed by
/// those families therefore rendered EMPTY, and a sweep that found no defect on them had proved nothing. It now
/// posts <b>at least one voucher of every base kind its own <see cref="VoucherType"/> seed defines</b>, plus the
/// two families a base-kind sweep is blind to (the POS tender split and Attendance, which posts no voucher at
/// all). That coverage is <b>asserted as data</b> by <c>PopulatedFixtureCoverageTests</c> — including across a
/// real SQLite round trip, which is the only form of this fixture the UI tests ever see — so a future edit
/// cannot silently take a family back out.</para>
/// <para><b>The Payroll voucher type is deliberately left <see cref="VoucherType.IsActive"/> = false.</b> The
/// seed ships it inactive and <b>nothing in the product can activate it</b> (census T1-4: there is no Voucher
/// Type master, and <c>PayrollService.EnablePayroll</c> does not flip the flag). Flipping it here would let this
/// fixture assert a reachability the shipped app does not have. The pay run is posted through
/// <see cref="PayrollVoucherService"/> — the same engine the Payroll screen drives — so the voucher, the
/// payslip and the payroll register are all populated, while the Day-Book Alt+A picker still (correctly) omits
/// the type.</para>
/// </summary>
public static class PopulatedCompanyFixture
{
    public static readonly DateOnly FyStart = new(2025, 4, 1);
    public static readonly DateOnly FyEnd = new(2026, 3, 31);

    /// <summary>The Regular-GST company name (66 chars — a real Pvt Ltd style name, header/title-bar stress).
    /// ⚠️ This said 67; measured 66, which is the figure the class doc above already quotes.</summary>
    public const string RegularCompanyName =
        "Shreenath Industrial Fasteners & Engineering Works Private Limited";

    /// <summary>The Composition-registration company name (57 chars — this said 56; measured 57).</summary>
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
        c.PayrollEnabled = true;
        c.PayrollStatutoryEnabled = true;
        c.DefineBomComponentType = true;

        // Job Order Processing is NOT a plain flag set. Assigning Company.EnableJobOrderProcessing directly — which
        // is what this fixture used to do — turned the menu rows on while leaving the four seeded Job-Work voucher
        // types IsActive = false, so the fixture claimed a feature whose voucher types no screen would list and no
        // register would fill. The real F11 toggle (GstConfigViewModel) drives JobWorkService.SetEnabled, which also
        // activates those four types and stamps Use-for-Job-Work / Allow-Consumption. Drive the same service here so
        // the fixture models what a user's company actually looks like.
        new JobWorkService(c).SetEnabled(true);

        var ledgers = SeedLedgersAndParties(c);
        SeedCostStructure(c);
        var inv = SeedInventoryMasters(c);
        SeedBatches(c, inv);
        SeedBom(c, inv);              // ⇒ Company.SetComponentsBom infers true ⇒ BomMaster + ManufacturingJournalEntry
        SeedPriceLists(c, inv);
        SeedReorderLevels(c, inv);
        var payroll = SeedPayroll(c);
        SeedBudgetsAndScenarios(c);
        PostVouchers(c, ledgers, inv);

        // ---- W0-7: the fifteen base kinds this fixture used to have no specimen of, plus POS and Attendance.
        // Ordered so stock is brought IN before it is taken OUT and every (item, godown, batch) key stays
        // non-negative — the fixture must model a coherent business, not merely satisfy constructors.
        PostStockAndOrderVouchers(c, ledgers, inv);
        PostProvisionalVouchers(c, ledgers);
        PostPosBill(c, ledgers, inv);
        RunPayroll(c, payroll);

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
        // Through TypeFor for the same reason as PostVouchers: this company defines no specialised variant
        // today, but the resolution must not depend on that staying true.
        var salesType = TypeFor(c, VoucherBaseType.Sales).Id;
        var purchType = TypeFor(c, VoucherBaseType.Purchase).Id;

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
        // Three address lines + city line — a real Indian industrial address. The longest single line is 56
        // chars ("Behind Telco Trucks Weighbridge, Old Mumbai-Pune Highway"); this comment said 62, and the
        // class doc above already quotes the correct 56.
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

        // ---- Two SHORT-DATED batches, without which the Age Analysis of Expiring Batches report is vacuous.
        //
        // Every batch above carries a 24-month shelf life, so on this book's timeline NOTHING is ever expired or
        // near expiry and the report falls back to "No batches are expired or expiring within the next 30 days."
        // — which meant four of InventoryReportScrollReachabilityTests' cases were driving an EMPTY pane. These
        // two attach a realistic short shelf life to stock that genuinely exists on hand: one already past
        // expiry (which renders in the distinct alert colour, RQ-8) and one expiring inside the 30-day window.
        // Both must name the SAME (item, batch number) pair the stock actually arrived under —
        // BatchStockService resolves expiry by FindBatchByNumber(item, label), so a mismatched pair silently
        // resolves to no expiry and the report stays empty with nothing to show for the addition.
        var nut = inv.Items[2];       // SS-304 hex nut — opening stock in the finished-goods warehouse
        var brightBar = inv.Items[6]; // EN8 bright bar — the Receipt Note lot, still wholly on hand in stores

        c.AddBatchMaster(new BatchMaster(
            Guid.NewGuid(), nut.Id,
            batchNumber: "BN-2025-0021-OPENING",
            manufacturingDate: FyStart.AddDays(-210),
            expiryDate: FyStart.AddDays(150),          // already past as-of ⇒ the expired/alert row
            godownId: inv.Godowns[2].Id,
            inwardQuantity: 400m,
            inwardRate: nut.StandardCost));

        c.AddBatchMaster(new BatchMaster(
            Guid.NewGuid(), brightBar.Id,
            batchNumber: "HEAT-2025982/LOT-007-BHOSARI",
            manufacturingDate: FyStart.AddDays(100),
            expiryDate: FyStart.AddDays(190),          // inside the 30-day window ⇒ the expiring-soon row
            godownId: inv.Godowns[1].Id,
            inwardQuantity: 465.250m,
            inwardRate: Money.FromRupees(69.35m)));
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

    /// <summary>
    /// The payroll ids a pay run needs back from <see cref="SeedPayroll"/>: the employees in seed order and the
    /// two attendance types the On-Attendance / On-Production heads are computed from.
    /// </summary>
    private sealed record PayrollSet(
        IReadOnlyList<Employee> Employees,
        AttendanceType Present,
        AttendanceType Absent,
        AttendanceType Overtime);

    private static PayrollSet SeedPayroll(Company c)
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

        var employees = new List<Employee>();
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
            employees.Add(e);
        }

        // ---- Attendance / production types.
        //
        // ⚠️ THESE KINDS WERE ALL WRONG. Every one of the four was seeded AttendancePaid — including "Absent — Loss
        // of Pay" and "Overtime Hours Worked", which are by definition NOT paid attendance. That made the masters
        // decorative: an On-Attendance head cannot be pro-rated against a loss-of-pay type that claims to be paid,
        // and NO On-Production head could exist at all because PayHeadService requires a Production-kind type and
        // the company had none. The kinds now match their names.
        var days = PayrollUnit.Simple(Guid.NewGuid(), "Days", "Days", decimalPlaces: 2);
        var hours = PayrollUnit.Simple(Guid.NewGuid(), "Hrs", "Hours", decimalPlaces: 2);
        c.AddPayrollUnit(days);
        c.AddPayrollUnit(hours);

        var present = new AttendanceType(
            Guid.NewGuid(), "Present — Full Working Day (General Shift)",
            AttendanceTypeKind.AttendancePaid, payrollUnitId: days.Id);
        var absent = new AttendanceType(
            Guid.NewGuid(), "Absent — Loss of Pay (Unauthorised)",
            AttendanceTypeKind.LeaveWithoutPay, payrollUnitId: days.Id);
        var leave = new AttendanceType(
            Guid.NewGuid(), "Paid Earned Leave / Privilege Leave Availed",
            AttendanceTypeKind.AttendancePaid, payrollUnitId: days.Id);
        var overtime = new AttendanceType(
            Guid.NewGuid(), "Overtime Hours Worked Beyond Rostered Shift",
            AttendanceTypeKind.Production, payrollUnitId: hours.Id);
        foreach (var at in new[] { present, absent, leave, overtime }) c.AddAttendanceType(at);

        // ---- Pay heads.
        //
        // ⚠️ THESE WERE UNPOSTABLE. They were constructed with `new PayHead(...)` straight onto the company, which
        // BYPASSES PayHeadService's validation, and every one of them violated it: the On-Attendance head linked no
        // attendance type, and three As-Computed-Value heads carried no computation formula at all. Any attempt to
        // actually RUN payroll on this fixture threw. They are now created through the real service — same names,
        // same order, same list geometry for the layout locks — with the accounting group linkage, attendance
        // linkage and computation formulae the engine needs, so the masters are what they claim to be.
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liabilities = c.FindGroupByName("Current Liabilities")!.Id;
        var ph = new PayHeadService(c);

        var basic = ph.CreatePayHead(
            "Basic Salary & Dearness Allowance (Consolidated)", PayHeadType.Earnings,
            PayHeadCalculationType.OnAttendance, underGroupId: indirect,
            attendanceTypeId: present.Id, perDayCalculationBasisDays: 30);
        ph.CreatePayHead(
            "House Rent Allowance @ 40% of Basic + DA", PayHeadType.Earnings,
            PayHeadCalculationType.AsComputedValue, underGroupId: indirect,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(4000) }));
        ph.CreatePayHead(
            "Conveyance & Travelling Allowance (Monthly Fixed)", PayHeadType.Earnings,
            PayHeadCalculationType.FlatRate, underGroupId: indirect);
        ph.CreatePayHead(
            "Production Incentive & Overtime Earnings", PayHeadType.Earnings,
            PayHeadCalculationType.OnProduction, underGroupId: indirect, attendanceTypeId: overtime.Id);
        ph.CreatePayHead(
            "Employee Provident Fund Contribution @ 12%", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: liabilities,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(1200) }));
        ph.CreatePayHead(
            "Employees' State Insurance Contribution @ 0.75%", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsComputedValue, underGroupId: liabilities,
            computation: new PayHeadComputation(
                new[] { new PayHeadComputationComponent(basic.Id) },
                new[] { PayHeadComputationSlab.Percentage(75) }));
        ph.CreatePayHead(
            "Professional Tax Deduction (Maharashtra Slab)", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.FlatRate, underGroupId: liabilities);
        ph.CreatePayHead(
            "Salary Advance & Loan Recovery Instalment", PayHeadType.LoansAndAdvances,
            PayHeadCalculationType.FlatRate, underGroupId: liabilities);

        return new PayrollSet(employees, present, absent, overtime);
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
        // Through TypeFor rather than a bare First(v => v.BaseType == b), so the ten item-invoice sales below
        // resolve by the STATED rule (prefer the predefined seeded row) instead of by the order VoucherTypes
        // happens to be in. ⚠️ The bare form is NOT broken today and reordering BuildRegular's calls does not
        // break it — see TypeFor's own doc comment, which corrects that claim with a measurement. This is the
        // single place the rule lives, and the invariant it protects is locked by
        // Only_the_pos_bill_is_posted_on_a_specialised_voucher_type.
        var t = (VoucherBaseType b) => TypeFor(c, b).Id;

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

    // ------------------------------------------------------------------ W0-7: the missing voucher families

    /// <summary>
    /// The company's own type for a base kind, preferring the <b>predefined seeded</b> row — the same rule
    /// <see cref="VoucherTypeResolver.ResolveForEntry"/> applies.
    ///
    /// <para>⚠️ <b>CORRECTION — the claim this comment used to make is FALSE, and it was measured false.</b> It
    /// said a bare <c>First(t =&gt; t.BaseType == x)</c> "would start returning the POS till the moment
    /// <see cref="PostPosBill"/> adds a second Sales type". It would not, at any call order:
    /// <c>Company.AddVoucherType</c> is a plain <c>List.Add</c> and <c>CompanyFactory.CreateSeeded</c> seeds
    /// every predefined type first, so the POS till is always APPENDED after the predefined Sales row and
    /// <c>First</c> keeps returning the predefined one. Measured: moving <c>PostPosBill</c> ahead of
    /// <c>PostVouchers</c> left all ten item-invoice sales on the predefined Sales type.</para>
    ///
    /// <para><b>What this helper is actually for</b> is therefore not call order but LIST order: it states the
    /// resolution rule explicitly instead of depending on an append-order accident that nothing asserts, and it
    /// is the one place to change if a load path ever returns <c>VoucherTypes</c> in a different order (the
    /// SQLite round trip that every consuming UI test goes through is exactly such a path). Measured red: drop
    /// the <c>IsPredefined</c> preference here AND run <c>PostPosBill</c> first, and all ten item-invoice sales
    /// post under "Sales (POS) — Bhosari Retail Counter", inheriting its numbering series and <c>UseForPos</c>
    /// flag while the base kind — and therefore every coverage assertion in
    /// <c>PopulatedFixtureCoverageTests</c> except <c>Only_the_pos_bill_is_posted_on_a_specialised_voucher_type</c>
    /// — stays unchanged.</para>
    /// </summary>
    private static VoucherType TypeFor(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.FirstOrDefault(t => t.BaseType == baseType && t.IsPredefined)
        ?? c.VoucherTypes.First(t => t.BaseType == baseType);

    /// <summary>
    /// Posts one specimen of every <b>stock/order</b> base kind — the twelve the inventory engine owns and this
    /// fixture had none of, which is why the Order Register, Receipt Note Register, Physical Stock Register, Job
    /// Work Order Books and Material In/Out Registers all rendered "no rows" and proved nothing.
    ///
    /// <para><b>Every quantity and rate here is deliberately odd-valued</b> (60.125 kg, ₹248.65, 1068.375 Ltr).
    /// Round numbers assert nothing: a unit-conversion or paisa-truncation defect in any of these registers would
    /// be invisible against whole units. Kg/Ltr items carry fractional quantities; Nos items carry whole
    /// quantities with fractional rates, because no real book ships 60.125 bolts.</para>
    ///
    /// <para><b>Stock coherence is load-bearing.</b> Dates and quantities are chosen so every (item, godown,
    /// batch) key stays non-negative across the whole timeline: outward lines draw only on keys that the opening
    /// balances or the Phase-3 purchases actually filled, and every batched outward line names the SAME batch
    /// label the stock arrived under. Omitting the label would open a distinct <c>(item, godown, "")</c> key and
    /// drive it straight negative — the detector would fire on a fixture that models nothing real.</para>
    /// </summary>
    private static void PostStockAndOrderVouchers(Company c, LedgerSet led, InventorySet inv)
    {
        var post = new InventoryPostingService(c);
        var stores = inv.Godowns[1];      // Bhosari raw-material stores — where the purchases landed
        var fg = inv.Godowns[2];          // Chakan finished goods — where the sales despatched from
        var jobWorker = inv.Godowns[4];   // Bharat Zinc, the third-party job-work premises

        var bar = inv.Items[6];           // EN8 bright bar, Kg, batch-tracked
        var wire = inv.Items[7];          // SAE 1018 wire coil, Kg, batch-tracked
        var ssBar = inv.Items[8];         // SS 304 bar, Kg, no batches
        var oil = inv.Items[9];           // cutting oil, Ltr
        var zinc = inv.Items[11];         // passivation chemical, Ltr
        var washer = inv.Items[3];        // MS flat washer, Nos, no batches
        var allen = inv.Items[4];         // Allen cap screw, Nos, no batches
        var roofing = inv.Items[5];       // self-drilling screw, Box
        var boltM12 = inv.Items[0];       // M12x75 bolt, Nos, batch-tracked
        var boltM16 = inv.Items[1];       // M16x100 HDG bolt, Nos, batch-tracked

        // The batch each key's stock actually arrived under (see SeedInventoryMasters / PostVouchers).
        const string WireOpeningBatch = "BN-2025-0056-OPENING";

        // ---- Receipt Note (GRN): goods received, supplier's bill not yet booked. Stock inward, accounts untouched.
        post.Post(new InventoryVoucher(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.ReceiptNote).Id, FyStart.AddDays(105),
            new[]
            {
                new InventoryAllocation(bar.Id, stores.Id, 465.250m, StockDirection.Inward,
                    Money.FromRupees(69.35m), "HEAT-2025982/LOT-007-BHOSARI"),
                new InventoryAllocation(ssBar.Id, stores.Id, 212.500m, StockDirection.Inward,
                    Money.FromRupees(248.65m)),
            },
            partyId: led.Creditors[0].Id,
            narration: "Goods received against supplier challan; invoice awaited, GRN booked into stores."));

        // ---- Delivery Note: goods despatched ahead of the invoice. Stock outward, accounts untouched.
        post.Post(new InventoryVoucher(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.DeliveryNote).Id, FyStart.AddDays(118),
            new[]
            {
                new InventoryAllocation(washer.Id, fg.Id, 47m, StockDirection.Outward, Money.FromRupees(2.63m)),
                new InventoryAllocation(allen.Id, fg.Id, 63m, StockDirection.Outward, Money.FromRupees(17.44m)),
            },
            partyId: led.Debtors[1].Id,
            narration: "Despatched by road against customer schedule; tax invoice to follow on despatch proof."));

        // ---- Rejection In: the customer returns part of that despatch. Stock inward.
        post.Post(new InventoryVoucher(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.RejectionIn).Id, FyStart.AddDays(126),
            new[] { new InventoryAllocation(washer.Id, fg.Id, 6m, StockDirection.Inward, Money.FromRupees(2.63m)) },
            partyId: led.Debtors[1].Id,
            narration: "Customer rejection — plating thickness below drawing; returned to finished-goods stores."));

        // ---- Rejection Out: we return short-supplied material to the supplier. Stock outward.
        post.Post(new InventoryVoucher(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.RejectionOut).Id, FyStart.AddDays(131),
            new[] { new InventoryAllocation(ssBar.Id, stores.Id, 18.750m, StockDirection.Outward, Money.FromRupees(248.65m)) },
            partyId: led.Creditors[0].Id,
            narration: "Returned to supplier — mill test certificate mismatch on the SS-304 heat number."));

        // ---- Stock Journal: an inter-godown transfer. Source and destination MUST balance in the base unit.
        post.Post(InventoryVoucher.StockJournal(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.StockJournal).Id, FyStart.AddDays(137),
            source: new[] { new InventoryAllocation(oil.Id, stores.Id, 42.750m, StockDirection.Outward, Money.FromRupees(178.00m)) },
            destination: new[] { new InventoryAllocation(oil.Id, fg.Id, 42.750m, StockDirection.Inward, Money.FromRupees(178.00m)) },
            narration: "Cutting-oil transferred from Bhosari stores to the Chakan assembly line."));

        // ---- Physical Stock: a counted quantity SETS the book quantity as of its date (DP-3). Counted below book
        // models ordinary evaporation/spillage on a liquid consumable.
        post.Post(InventoryVoucher.PhysicalStock(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.PhysicalStock).Id, FyStart.AddDays(144),
            new[] { new PhysicalStockLine(zinc.Id, stores.Id, 1068.375m, batchLabel: null) },
            narration: "Half-yearly physical verification of chemical stores; shortage written to book quantity."));

        // ---- Purchase Order: a commitment. Affects NEITHER stock nor accounts.
        post.Post(InventoryVoucher.Order(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.PurchaseOrder).Id, FyStart.AddDays(149),
            new[]
            {
                new OrderLine(bar.Id, stores.Id, 1_250.750m, Money.FromRupees(69.35m)),
                new OrderLine(oil.Id, stores.Id, 96.500m, Money.FromRupees(181.27m)),
            },
            partyId: led.Creditors[0].Id,
            narration: "Purchase order placed on the depot for Q3 raw-material and consumable requirement."));

        // ---- Sales Order: the customer's commitment to us. Also no stock, no accounts.
        post.Post(InventoryVoucher.Order(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.SalesOrder).Id, FyStart.AddDays(152),
            new[]
            {
                new OrderLine(roofing.Id, fg.Id, 145m, Money.FromRupees(431.63m)),
                new OrderLine(boltM12.Id, fg.Id, 320m, Money.FromRupees(25.47m)),
            },
            partyId: led.Debtors[0].Id,
            narration: "Customer purchase order accepted; delivery scheduled against the Chakan warehouse."));

        // ---- Job Work OUT Order: we are the PRINCIPAL — we delegate plating to Bharat Zinc and must ISSUE the
        // components (Track = PendingToIssue). Order-only: no stock, no accounts.
        var outOrder = post.Post(InventoryVoucher.JobWork(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.JobWorkOutOrder).Id, FyStart.AddDays(156),
            new JobWorkOrder(
                JobWorkDirection.Out,
                orderNo: "JW/OUT/2025-26/0041",
                finishedGoodStockItemId: boltM16.Id,
                finishedGoodQuantity: 2_400m,
                lines: new[]
                {
                    new JobWorkOrderLine(wire.Id, JobWorkComponentTrack.PendingToIssue, 318.625m,
                        godownId: stores.Id, dueDate: FyStart.AddDays(178), rate: Money.FromRupees(61.27m)),
                },
                finishedGoodRate: Money.FromRupees(47.86m),
                finishedGoodDueDate: FyStart.AddDays(185),
                finishedGoodGodownId: fg.Id,
                durationOfProcess: "21 days",
                natureOfProcessing: "Hot dip galvanising to IS 1367 Part-13, 45 micron minimum"),
            partyId: led.Creditors[1].Id,
            narration: "Job work out order on the plating contractor for the M16 HDG bolt schedule."));

        // ---- Job Work IN Order: we are the WORKER — a principal sends us the order and will send the components,
        // which are therefore PendingToReceive. The SAME payload shape serves both roles (RQ-50).
        post.Post(InventoryVoucher.JobWork(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.JobWorkInOrder).Id, FyStart.AddDays(159),
            new JobWorkOrder(
                JobWorkDirection.In,
                orderNo: "JW/IN/2025-26/0017",
                finishedGoodStockItemId: boltM12.Id,
                finishedGoodQuantity: 1_750m,
                lines: new[]
                {
                    new JobWorkOrderLine(bar.Id, JobWorkComponentTrack.PendingToReceive, 214.375m,
                        godownId: stores.Id, dueDate: FyStart.AddDays(170), rate: Money.FromRupees(68.53m)),
                },
                finishedGoodRate: Money.FromRupees(21.35m),
                finishedGoodDueDate: FyStart.AddDays(190),
                finishedGoodGodownId: fg.Id,
                durationOfProcess: "30 days",
                natureOfProcessing: "Cold forging and thread rolling of M12 x 75 hex head bolt"),
            partyId: led.Debtors[3].Id,
            narration: "Job work in order received from the principal for cold-forged M12 bolts."));

        // ---- Material Out: the components physically leave for the worker's premises. A BALANCED transfer — the
        // stock stays on our books, at a third-party godown — so JobWorkService values the inward leg at the
        // outward's own live issue cost (value-neutral, RQ-46) rather than at the order's planned rate.
        var jobWork = new JobWorkService(c);
        post.Post(jobWork.BuildMaterialOutTransfer(
            TypeFor(c, VoucherBaseType.MaterialOut).Id, FyStart.AddDays(163),
            source: new[]
            {
                new InventoryAllocation(wire.Id, stores.Id, 318.625m, StockDirection.Outward,
                    Money.FromRupees(61.27m), WireOpeningBatch),
            },
            destination: new[]
            {
                new InventoryAllocation(wire.Id, jobWorker.Id, 318.625m, StockDirection.Inward,
                    Money.FromRupees(61.27m), WireOpeningBatch),
            },
            orderLinks: new[] { outOrder.Id },
            narration: "Components issued to the plating contractor against JW/OUT/2025-26/0041.",
            partyId: led.Creditors[1].Id));

        // ---- Material In (Allow Consumption): the worker returns the finished goods. A TRANSFORM — the consumed
        // wire becomes plated bolts — so it is balance-EXEMPT, and the finished good is valued from the live cost
        // of what was consumed, never from a hand-typed rate (RQ-49/ER-4).
        post.Post(jobWork.BuildConsumingMaterialIn(
            TypeFor(c, VoucherBaseType.MaterialIn).Id, FyStart.AddDays(171),
            consume: new[]
            {
                new InventoryAllocation(wire.Id, jobWorker.Id, 306.500m, StockDirection.Outward,
                    Money.FromRupees(61.27m), WireOpeningBatch),
            },
            produce: new[]
            {
                new InventoryAllocation(boltM16.Id, fg.Id, 2_400m, StockDirection.Inward,
                    Money.FromRupees(47.86m), "JW-2025-PLATED-0041"),
            },
            orderLinks: new[] { outOrder.Id },
            narration: "Plated M16 bolts received back from the contractor; wire consumed at his premises.",
            partyId: led.Creditors[1].Id));
    }

    /// <summary>
    /// The <b>provisional</b> families — the ones deliberately kept OUT of the real books
    /// (<see cref="Apex.Ledger.Reports.LedgerBalance"/> treats Memorandum, Reversing Journal and Optional as
    /// non-affecting). They are what the Memorandum Register and the Reversing Journal Register project, and both
    /// registers rendered empty on this fixture.
    ///
    /// <para>Because these never reach the Trial Balance, adding them cannot move any figure a consuming test
    /// measures — which is exactly why they are safe to add to a shared fixture, and why their <b>odd paisa</b>
    /// matters: it is the only signal that would expose a register that truncated or re-rounded them.</para>
    /// </summary>
    private static void PostProvisionalVouchers(Company c, LedgerSet led)
    {
        var svc = new LedgerService(c);

        var memoAmount = Money.FromRupees(12_473.85m);
        svc.Post(new Voucher(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.Memorandum).Id, FyStart.AddDays(121),
            new[]
            {
                new EntryLine(led.Expenses[2].Id, memoAmount, DrCr.Debit),
                new EntryLine(led.Cash.Id, memoAmount, DrCr.Credit),
            },
            narration: "Memo — canteen advance paid without supporting bills; to be regularised on production of vouchers."));

        var reversingAmount = Money.FromRupees(18_942.37m);
        svc.Post(new Voucher(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.ReversingJournal).Id, FyStart.AddDays(128),
            new[]
            {
                new EntryLine(led.Expenses[3].Id, reversingAmount, DrCr.Debit),
                new EntryLine(led.Creditors[5].Id, reversingAmount, DrCr.Credit),
            },
            applicableUpto: FyStart.AddDays(180),
            narration: "Reversing journal — provisional tool-room maintenance accrual, applicable up to the half-year close."));

        // An OPTIONAL voucher (Ctrl+L): a real base kind carrying the provisional FLAG rather than a provisional
        // type — the third way a voucher can be excluded from the books, and the one no register is named after.
        var optionalAmount = Money.FromRupees(9_617.42m);
        svc.Post(new Voucher(
            Guid.NewGuid(), TypeFor(c, VoucherBaseType.Journal).Id, FyStart.AddDays(133),
            new[]
            {
                new EntryLine(led.Expenses[4].Id, optionalAmount, DrCr.Debit),
                new EntryLine(led.Creditors[2].Id, optionalAmount, DrCr.Credit),
            },
            optional: true,
            narration: "Optional — draft statutory audit fee estimate, not to be taken into the books until agreed."));
    }

    /// <summary>
    /// A <b>POS retail bill</b> with a genuine multi-tender split, and the POS-flagged Sales voucher type it
    /// needs. A POS till is a SECOND Sales type wearing <see cref="VoucherType.UseForPos"/> — exactly how the
    /// reference application models it — so this is also the fixture's only case of two types sharing one base
    /// kind, which is what <see cref="TypeFor"/> and <see cref="VoucherTypeResolver.ResolveForEntry"/> exist to
    /// disambiguate. Adding it is SAFE for every F8/menu route precisely because the resolver skips specialised
    /// variants.
    ///
    /// <para>The tax is computed by the real <see cref="GstService"/> and the tenders reconciled by the real
    /// <see cref="PosTenderService"/>, so the bill total is never a hand-typed literal that could drift: the
    /// cash tender posts the RESIDUAL payable and the change is informational only (it produces no ledger line).</para>
    /// </summary>
    private static void PostPosBill(Company c, LedgerSet led, InventorySet inv)
    {
        // The three tender ledgers a POS bill needs, each under the group PosTenderService REQUIRES: Gift →
        // Sundry Debtors, Card/Cheque → Bank. Cash is the seeded Cash-in-Hand ledger.
        var gift = Add(c, "Gift Voucher & Loyalty Card Redemption (Retail Counter)", "Sundry Debtors", true);
        var card = Add(c, "HDFC Bank POS Card Settlement A/c 50200012345678", "Bank Accounts", true);
        var cheque = Add(c, "Cheque Collection A/c — Retail Counter Deposits", "Bank Accounts", true);
        var posSales = Add(c, "Sales — Retail Counter (POS, Taxable @ 18%)", "Sales Accounts", false);

        var posType = new VoucherType(
            Guid.NewGuid(), "Sales (POS) — Bhosari Retail Counter", VoucherBaseType.Sales, useForPos: true,
            posConfig: new PosConfig
            {
                DefaultGodownId = inv.Godowns[2].Id,
                PrintAfterSave = true,
                DefaultTitle = "Retail Invoice",
                Message1 = "Thank you for shopping with Shreenath Industrial Fasteners.",
                Message2 = "Goods once sold are taken back only against the original retail invoice.",
                Declaration = "Prices are inclusive of all taxes unless separately shown.",
            });
        posType.PosConfig!.SetTenderLedgerDefault(PosTenderType.GiftVoucher, gift.Id);
        posType.PosConfig.SetTenderLedgerDefault(PosTenderType.Card, card.Id);
        posType.PosConfig.SetTenderLedgerDefault(PosTenderType.Cheque, cheque.Id);
        posType.PosConfig.SetTenderLedgerDefault(PosTenderType.Cash, led.Cash.Id);
        c.AddVoucherType(posType);

        // 34 Allen cap screws at an odd counter rate — the taxable value is DERIVED, never asserted as a literal.
        var item = inv.Items[4];
        const decimal qty = 34m;
        var rate = Money.FromRupees(247.99m);
        var taxable = Money.FromRupees(decimal.Round(qty * rate.Amount, 2));

        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(taxable, 1800) }, interState: false, GstTaxDirection.Output);
        var billTotal = Money.FromRupees(taxable.Amount + tax.TotalTax.Amount);

        var gifted = Money.FromRupees(500m);
        var carded = Money.FromRupees(4_000m);
        var chequed = Money.FromRupees(2_500m);
        var residual = PosTenderService.CashResidual(billTotal, gifted, carded, chequed);
        var tendered = Money.FromRupees(3_000m);
        var change = PosTenderService.Change(tendered, residual);

        var tenders = new[]
        {
            new PosTender(PosTenderType.GiftVoucher, gift.Id, gifted),
            new PosTender(PosTenderType.Card, card.Id, carded, CardNo: "4184"),
            new PosTender(PosTenderType.Cheque, cheque.Id, chequed, BankName: "Bank of Maharashtra", ChequeNo: "418327"),
            new PosTender(PosTenderType.Cash, led.Cash.Id, residual, tendered, change),
        };
        PosTenderService.EnsureBalanced(billTotal, tenders);
        PosTenderService.EnsureGrouping(c, tenders);

        var lines = new List<EntryLine>();
        lines.AddRange(PosTenderService.BuildTenderDebitLines(tenders));   // the tender debits replace the customer Dr
        lines.Add(new EntryLine(posSales.Id, taxable, DrCr.Credit));
        lines.AddRange(tax.TaxLines);                                     // Cr Output CGST + SGST

        // ⚠️ DATE ORDERING IS LOAD-BEARING, AND IT EXPOSED A REAL DEFECT. Every report opens at
        // ReportsViewModel.ComputeAsOf, which scans `Company.Vouchers` ONLY — it never looks at
        // `Company.InventoryVouchers`. So a company whose most recent activity is a stock/order voucher (goods
        // despatched today, the invoice raised next week — the commonest sequence in an Indian trading book)
        // opens every report with an as-of date EARLIER than its own newest stock movement, and that movement is
        // invisible until the operator changes the date by hand. It was reproduced here: with this bill on day
        // 167 the Material In on day 171 fell outside the window and the Material In Register rendered EMPTY.
        // This bill is therefore dated AFTER the last inventory voucher so the fixture's registers are all
        // reachable at the default as-of. The underlying defect in ComputeAsOf is untouched and reported.
        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), posType.Id, FyStart.AddDays(178), lines,
            narration: "Retail counter sale settled by gift voucher, card, cheque and cash.",
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, inv.Godowns[2].Id, qty, rate) },
            posTenders: tenders));
    }

    /// <summary>
    /// Records a month of attendance and posts one <b>Payroll</b> voucher for every employee, through the same
    /// <see cref="PayrollVoucherService"/> the Payroll screen drives (which auto-creates the earnings/deduction/
    /// Salary-Payable ledgers and posts ONE balanced integrated voucher carrying a self-describing
    /// <c>PayrollLineDetail</c> per line — which is what the payslip, pay sheet and payroll register read back).
    ///
    /// <para>Attendance is recorded FIRST because the On-Attendance and On-Production heads are pro-rated from
    /// it: a pay run over a period with no attendance rows computes a zero Basic, and the service then rejects
    /// the run for producing no postable lines. Per-employee days and overtime hours differ (and the overtime
    /// hours are fractional), so the payslip is not eight identical rows.</para>
    ///
    /// <para><b>No income-tax pay head is defined</b>, deliberately. A head tagged
    /// <c>IncomeTaxComponent.TaxDeductedAtSource</c> would route this run through <c>SalaryIncomeTax</c>, whose
    /// own comment records that its 4% Health &amp; Education Cess rate could not be verified — a live user
    /// decision (census T0-5). A shared fixture must not bake an unresolved statutory figure into the numbers
    /// every other test reads.</para>
    /// </summary>
    private static void RunPayroll(Company c, PayrollSet payroll)
    {
        var attendance = new PayrollAttendanceService(c);
        var periodFrom = new DateOnly(2025, 8, 1);
        var periodTo = new DateOnly(2025, 8, 31);

        // Per-employee attendance: worked days, unauthorised absence, and fractional overtime hours.
        var pattern = new (decimal Present, decimal Absent, decimal Overtime)[]
        {
            (26m, 1m, 14.25m), (25m, 2m, 6.50m), (27m, 0m, 18.75m), (24m, 3m, 0m),
            (26m, 1m, 11.25m), (27m, 0m, 4.75m), (25m, 2m, 9.50m), (26m, 1m, 21.25m),
        };

        for (var i = 0; i < payroll.Employees.Count; i++)
        {
            var e = payroll.Employees[i];
            var (present, absent, overtime) = pattern[i % pattern.Length];

            attendance.Record(e.Id, payroll.Present.Id, periodFrom, periodTo, present);
            if (absent > 0m) attendance.Record(e.Id, payroll.Absent.Id, periodFrom, periodTo, absent);
            if (overtime > 0m) attendance.Record(e.Id, payroll.Overtime.Id, periodFrom, periodTo, overtime);
        }

        // Salary structures — employee-scoped (which is the arm ER-4 resolves FIRST), with a distinct odd-valued
        // basic per member so no two payslips are the same figure.
        var heads = c.PayHeads;
        var basic = heads[0];
        var hra = heads[1];
        var conveyance = heads[2];
        var incentive = heads[3];
        var epf = heads[4];
        var esi = heads[5];
        var professionalTax = heads[6];
        var advance = heads[7];

        var structures = new SalaryStructureService(c);
        var basics = new[]
        {
            31_486.50m, 27_354.75m, 29_812.25m, 42_675.80m,
            33_129.45m, 46_208.65m, 25_947.35m, 21_368.90m,
        };
        var advances = new[]
        {
            1_943.25m, 0m, 2_617.40m, 0m, 1_284.75m, 0m, 3_071.60m, 0m,
        };

        for (var i = 0; i < payroll.Employees.Count; i++)
        {
            structures.DefineForEmployee(payroll.Employees[i].Id, FyStart, new[]
            {
                new SalaryStructureLine(basic.Id, 0, Money.FromRupees(basics[i % basics.Length])),
                new SalaryStructureLine(hra.Id, 1),
                new SalaryStructureLine(conveyance.Id, 2, Money.FromRupees(1_687.50m)),
                new SalaryStructureLine(incentive.Id, 3, Money.FromRupees(118.25m)),   // per overtime hour
                new SalaryStructureLine(epf.Id, 4),
                new SalaryStructureLine(esi.Id, 5),
                new SalaryStructureLine(professionalTax.Id, 6, Money.FromRupees(200m)),
                new SalaryStructureLine(advance.Id, 7, Money.FromRupees(advances[i % advances.Length])),
            });
        }

        new PayrollVoucherService(c).Post(
            periodFrom, periodTo,
            payroll.Employees.Select(e => e.Id).ToList(),
            narration: "Salary for August 2025 — computed from recorded attendance and overtime production.");
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
