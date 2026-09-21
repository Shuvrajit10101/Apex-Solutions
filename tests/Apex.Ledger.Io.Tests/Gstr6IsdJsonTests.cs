using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Census row <b>6.24</b> — the <b>emitted GSTR-6 file</b>, not the screen.
///
/// <para>🔴 <b>WHY THIS FILE EXISTS AT ALL.</b> GSTR-6 is a return that is FILED. The standing rule for a filed
/// document in this project is that the test asserts on the emitted file or the printed page, never on our own
/// in-memory shape — because every defect that has ever put wrong money on a return in this codebase was invisible
/// from the projection record and visible in the artefact. Before this slice the ISD work had a screen and a pure
/// projection and <b>no emitter at all</b>, so nothing a filer could actually submit was under test. These tests
/// read the bytes back.</para>
///
/// <para><b>What is and is not claimed about the schema.</b> The GSTN offline-utility key names for GSTR-6 are not
/// published unauthenticated — the same position <see cref="GstReturnJson"/> already records for GSTR-1/3B and the
/// composition returns — so the envelope follows this class's house convention and carries the
/// <c>schemaStatus</c> flag. These tests therefore assert what is genuinely ours to guarantee: the
/// <b>arithmetic</b>, the <b>footing</b>, the <b>head conversion</b>, determinism, integer-paisa money and
/// de-branding. They do not assert portal acceptance, and must never be read as doing so.</para>
///
/// <para>The statutory grounding for the figures is Rule 39 of the CGST Rules at
/// <c>taxinformation.cbic.gov.in/content/html/tax_repository/gst/rules/cgst_rules/active/chapter5/rule39_v1.00.html</c>
/// and CGST Act §20 / §24(viii) / §39(4) in the same repository — all fetched and read by content for this slice.</para>
/// </summary>
public sealed class Gstr6IsdJsonTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly From = new(2025, 5, 1);
    private static readonly DateOnly To = new(2025, 5, 31);
    private static readonly DateOnly PurchaseDate = new(2025, 5, 12);

    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    /// <summary>
    /// A Karnataka head office registered as ISD, an operating Karnataka registration beside it (lawful under
    /// §24(viii), "whether or not separately registered under this Act") and a Tamil Nadu branch. Preceding-FY
    /// turnover ₹6,00,000 Karnataka / ₹4,00,000 Tamil Nadu, so Rule 39(1)(f) splits 60/40. One ₹50,000 input
    /// service at 18% intra-State gives the ISD CGST 4,500 + SGST 4,500 to distribute.
    /// </summary>
    private static (Company Company, Guid IsdId) Build()
    {
        var c = CompanyFactory.CreateSeeded("ISD Json Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinFor(Karnataka, "AAPFU0939F"),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var isd = new GstRegistration(
            Guid.NewGuid(), "Head Office (ISD)", Karnataka, GstinFor(Karnataka, "AAACC1206D"),
            GstRegistrationType.InputServiceDistributor, FyStart);
        c.Gst!.AddRegistration(isd);

        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinFor(TamilNadu, "AABCC1206D"),
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        var ledgers = new LedgerService(c);
        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var debtor = AddLedger(c, "Debtor", "Sundry Debtors", true);
        var creditor = AddLedger(c, "Service Supplier", "Sundry Creditors", false);
        creditor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinFor(Karnataka, "AAACS1206D"),
            StateCode = Karnataka,
        };
        var services = AddLedger(c, "Software Licence & Maintenance", "Indirect Expenses", true);

        var salesTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        PostSale(ledgers, salesTypeId, debtor, sales, 600_000m, null);
        PostSale(ledgers, salesTypeId, debtor, sales, 400_000m, branch.Id);

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(50_000m), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(services.Id, Money.FromRupees(50_000m), DrCr.Debit),
            new(creditor.Id, Money.FromRupees(59_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseTypeId, PurchaseDate, lines, partyId: creditor.Id)
        {
            GstRegistrationId = isd.Id,
        });

        return (c, isd.Id);
    }

    private static Domain.Ledger AddLedger(Company c, string name, string group, bool taxable)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, false);
        if (taxable)
        {
            l.SalesPurchaseGst = new StockItemGstDetails
            {
                Taxability = GstTaxability.Taxable,
                RateBasisPoints = 1800,
                SupplyType = GstSupplyType.Services,
            };
        }
        c.AddLedger(l);
        return l;
    }

    private static void PostSale(
        LedgerService ledgers, Guid salesTypeId, Domain.Ledger debtor, Domain.Ledger sales,
        decimal amount, Guid? registrationId)
    {
        var v = new Voucher(Guid.NewGuid(), salesTypeId, new DateOnly(2024, 6, 10), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(amount), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(amount), DrCr.Credit),
        }, partyId: debtor.Id);
        if (registrationId is { } id) v.GstRegistrationId = id;
        ledgers.Post(v);
    }

    private static JsonElement Emit(Company c, Guid isdId)
    {
        var bytes = GstReturnJson.Gstr6(c, From, To, isdId);
        return JsonDocument.Parse(bytes).RootElement.Clone();
    }

    // ==============================================================================================================
    //  The footing — the one property that makes the file safe to file
    // ==============================================================================================================

    /// <summary>
    /// Rule 39(1)(b): "<i>the amount of the credit distributed shall not exceed the amount of credit available for
    /// distribution</i>". On the emitted file that becomes an identity a reader can check without the app:
    /// distributed + undistributed = received, exactly, in integer paisa. If this ever fails, the file either
    /// invents credit or loses it, and both are wrong money on a filed return.
    /// </summary>
    [Fact]
    public void The_emitted_file_foots_distributed_plus_undistributed_to_received()
    {
        var (c, isdId) = Build();
        var root = Emit(c, isdId);

        var received = root.GetProperty("total_received_paisa").GetInt64();
        var distributed = root.GetProperty("total_distributed_paisa").GetInt64();
        var undistributed = root.GetProperty("undistributed_credit_paisa").GetInt64();

        Assert.Equal(received, distributed + undistributed);
        Assert.True(undistributed >= 0, "Rule 39(1)(b) forbids distributing more than was available.");

        // The ₹9,000 of central + State tax the ISD received in the month, to the paisa.
        Assert.Equal(900_000L, received);
        Assert.Equal(900_000L, distributed);
        Assert.Equal(0L, undistributed);
    }

    /// <summary>
    /// The footing identity on a credit whose pro-rata share does <b>not</b> divide evenly: ₹10,001 at 18% gives
    /// CGST of 90,009 paisa, and 60% of that is 54,005.4, so a whole-paisa split cannot be exact and something has
    /// to absorb the odd paisa for Rule 39(1)(b) to hold. This asserts that every paisa received still leaves the
    /// distributor.
    ///
    /// <para>🔴 <b>WHAT THIS TEST DOES NOT CATCH, MEASURED RATHER THAN ASSUMED — DO NOT ADD A CLAIM HERE THAT IT
    /// GUARDS THE REMAINDER RULE.</b> Mutating <c>IsdDistribution.Split</c> so the last recipient takes a plain
    /// pro-rata share instead of <c>credit − Σ(others)</c> leaves this test GREEN. The reason is
    /// <see cref="ProRata.Paisa"/> rounding away from zero: with exactly TWO recipients the two roundings are
    /// complementary — 54,005.4 rounds down by .4 and 36,003.6 rounds up by .4 — so the pair still sums to 90,009
    /// even with no remainder absorption at all. Catching that mutation needs three or more recipients, where the
    /// roundings can agree in direction; it is caught today by
    /// <c>IsdDistributionRule39Tests.The_distribution_foots_to_the_credit_available_to_the_paisa_when_it_does_not_divide_evenly</c>
    /// and <c>Every_head_foots_independently_so_no_head_can_borrow_from_another</c>, both of which fail under that
    /// mutation. An earlier draft of this comment asserted the opposite and was wrong; the mutation was then run
    /// against it and the claim withdrawn.</para>
    /// </summary>
    [Fact]
    public void The_emitted_file_foots_exactly_when_the_pro_rata_share_does_not_divide_evenly()
    {
        var (c, isdId) = Build();

        var gst = new GstService(c);
        var creditor = c.Ledgers.First(l => l.Name == "Service Supplier");
        var services = c.Ledgers.First(l => l.Name.Contains("Software Licence"));

        // ₹10,001 @ 18% intra ⇒ CGST 90,009 paisa + SGST 90,009 paisa. 60% of 90,009 is not a whole paisa.
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(10_001m), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(services.Id, Money.FromRupees(10_001m), DrCr.Debit),
            new(creditor.Id, Money.FromRupees(11_801.18m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(
            new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
                PurchaseDate, lines, partyId: creditor.Id)
            { GstRegistrationId = isdId });

        var root = Emit(c, isdId);

        var received = root.GetProperty("total_received_paisa").GetInt64();
        var distributed = root.GetProperty("total_distributed_paisa").GetInt64();
        var undistributed = root.GetProperty("undistributed_credit_paisa").GetInt64();

        // The odd paisa is absorbed, not lost: every paisa received leaves the distributor.
        Assert.Equal(received, distributed);
        Assert.Equal(0L, undistributed);

        // …and the rows themselves still sum to it, so the absorption happened in a row and not in the summary.
        long rowSum = 0;
        foreach (var row in root.GetProperty("tbl8_distribution").EnumerateArray())
        {
            rowSum += row.GetProperty("camt_paisa").GetInt64()
                    + row.GetProperty("samt_paisa").GetInt64()
                    + row.GetProperty("iamt_paisa").GetInt64()
                    + row.GetProperty("csamt_paisa").GetInt64();
        }
        Assert.Equal(distributed, rowSum);

        // The credit that had to be split unevenly is genuinely present, so the test cannot pass vacuously.
        Assert.Equal(900_000L + 90_009L + 90_009L, received);
    }

    /// <summary>
    /// The per-row heads in the emitted file must themselves sum to the emitted distributed total. This is the
    /// check that catches a summary figure that has drifted from the rows beneath it — the shape of error that a
    /// screen showing only the summary would never reveal.
    /// </summary>
    [Fact]
    public void The_distribution_rows_sum_to_the_emitted_distributed_total()
    {
        var (c, isdId) = Build();
        var root = Emit(c, isdId);

        long rowSum = 0;
        foreach (var row in root.GetProperty("tbl8_distribution").EnumerateArray())
        {
            rowSum += row.GetProperty("camt_paisa").GetInt64()
                    + row.GetProperty("samt_paisa").GetInt64()
                    + row.GetProperty("iamt_paisa").GetInt64()
                    + row.GetProperty("csamt_paisa").GetInt64();
        }

        Assert.Equal(root.GetProperty("total_distributed_paisa").GetInt64(), rowSum);
    }

    // ==============================================================================================================
    //  Rule 39(1)(j) — the head conversion, asserted on the file
    // ==============================================================================================================

    /// <summary>
    /// Rule 39(1)(j): a recipient in the distributor's own State keeps central and State tax; a recipient elsewhere
    /// receives the aggregate of those two as integrated tax. Getting this wrong hands a unit credit it cannot use,
    /// so it is asserted on the emitted rows and not only in the engine's unit tests.
    /// <para>60/40 on ₹4,500 + ₹4,500: Karnataka takes 2,700 + 2,700 and keeps the heads; Tamil Nadu's
    /// 1,800 + 1,800 aggregate into ₹3,600 of integrated tax and nothing else.</para>
    /// </summary>
    [Fact]
    public void An_out_of_state_recipient_receives_integrated_tax_only_in_the_emitted_file()
    {
        var (c, isdId) = Build();
        var root = Emit(c, isdId);

        var rows = root.GetProperty("tbl8_distribution").EnumerateArray().ToList();

        var home = rows.Single(r => r.GetProperty("state_cd").GetString() == Karnataka);
        Assert.Equal(270_000L, home.GetProperty("camt_paisa").GetInt64());
        Assert.Equal(270_000L, home.GetProperty("samt_paisa").GetInt64());
        Assert.Equal(0L, home.GetProperty("iamt_paisa").GetInt64());

        var away = rows.Single(r => r.GetProperty("state_cd").GetString() == TamilNadu);
        Assert.Equal(0L, away.GetProperty("camt_paisa").GetInt64());
        Assert.Equal(0L, away.GetProperty("samt_paisa").GetInt64());
        Assert.Equal(360_000L, away.GetProperty("iamt_paisa").GetInt64());
    }

    // ==============================================================================================================
    //  Reverse charge — the withheld credit must be VISIBLE in the file, not only on screen
    // ==============================================================================================================

    /// <summary>
    /// §20(1) brings section 9(3)/9(4) invoices into the ISD mechanism and §20(2) conditions their distribution on
    /// the tax having been "<i>paid by a distinct person registered in the same State as the said Input Service
    /// Distributor</i>", which this build cannot confirm. The credit is therefore received and withheld — and the
    /// emitted file has to SAY SO, because a filer working from the JSON alone would otherwise submit a return that
    /// is short with nothing in the artefact to reveal it.
    /// </summary>
    [Fact]
    public void Withheld_reverse_charge_credit_is_visible_in_the_emitted_file()
    {
        var (c, isdId) = Build();
        var gst = new GstService(c);

        // 🔴 THE RCM VOUCHER IS BUILT BY RcmService, NOT BY HAND, AND THAT IS THE POINT OF THIS TEST.
        // A hand-assembled fixture that posts the liability to the ordinary "Output CGST" ledger does not describe
        // anything this product can produce: RcmService puts the §49(4) liability in a DEDICATED "RCM Output {head}"
        // ledger carrying IsReverseCharge in its own classification, and the ITC in the ordinary Input ledger. The
        // report tells the two apart by exactly that classification, so a hand-built fixture would either miss a
        // double-count or invent one. Both this test and its sibling in Gstr6IsdReturnTests were rewritten onto the
        // real posting path after the hand-built version reported ₹10,800 received where ₹9,009 is right.
        gst.SeedAdvancedGst();

        var legal = new Domain.Ledger(
            Guid.NewGuid(), "Legal Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true)
        {
            SalesPurchaseGst = new StockItemGstDetails
            {
                Taxability = GstTaxability.Taxable,
                RateBasisPoints = 1800,
                SupplyType = GstSupplyType.Services,
                ReverseChargeApplicable = true,
                RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Legal").Id,
            },
        };
        c.AddLedger(legal);

        var advocate = new Domain.Ledger(
            Guid.NewGuid(), "Advocate (Karnataka)", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
        {
            PartyGst = new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular,
                Gstin = GstinFor(Karnataka, "AAACL1206D"),
                StateCode = Karnataka,
            },
        };
        c.AddLedger(advocate);

        var posting = new RcmService(c).BuildReverseCharge(
            Money.FromRupees(10_000m), null, legal, advocate.PartyGst, PurchaseDate,
            RcmService.SupplyKind.Domestic);
        Assert.True(posting.Applies);

        var rcmLines = new List<EntryLine>
        {
            new(legal.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(advocate.Id, Money.FromRupees(10_000m), DrCr.Credit),
        };
        rcmLines.AddRange(posting.Lines);

        new LedgerService(c).Post(
            new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
                PurchaseDate, rcmLines, partyId: advocate.Id)
            { GstRegistrationId = isdId });

        var root = Emit(c, isdId);

        // An intra-Karnataka legal service at 18% gives the ISD CGST 900 + SGST 900 = ₹1,800 of ITC — and exactly
        // ₹1,800, not ₹3,600: the matching §49(4) output liability is NOT credit and must not be counted again.
        // Received grew by ₹1,800; distributed did not; the gap is on the face of the file and still foots.
        Assert.Equal(1_080_000L, root.GetProperty("total_received_paisa").GetInt64());
        Assert.Equal(900_000L, root.GetProperty("total_distributed_paisa").GetInt64());
        Assert.Equal(180_000L, root.GetProperty("undistributed_credit_paisa").GetInt64());
        Assert.Equal(
            root.GetProperty("total_received_paisa").GetInt64(),
            root.GetProperty("total_distributed_paisa").GetInt64()
            + root.GetProperty("undistributed_credit_paisa").GetInt64());

        var diagnostics = root.GetProperty("diagnostics").EnumerateArray().Select(d => d.GetString()!).ToList();
        Assert.Contains(diagnostics, d => d.Contains("reverse-charge credit") && d.Contains("20(2)"));
    }

    // ==============================================================================================================
    //  House rules the whole emitter is held to
    // ==============================================================================================================

    /// <summary>The same book must emit byte-identical GSTR-6 twice — no clock, no RNG, no dictionary ordering.</summary>
    [Fact]
    public void The_emitted_file_is_deterministic()
    {
        var (c, isdId) = Build();
        Assert.Equal(GstReturnJson.Gstr6(c, From, To, isdId), GstReturnJson.Gstr6(c, From, To, isdId));
    }

    /// <summary>
    /// The envelope carries the distributor's own GSTIN — not the company's operating GSTIN. GSTR-6 is filed BY the
    /// ISD registration, so an emitter that reached for <c>company.Gst.Gstin</c> (as every other writer in this
    /// class legitimately does) would file the return under the wrong registration.
    /// </summary>
    [Fact]
    public void The_envelope_carries_the_isd_gstin_and_not_the_operating_gstin()
    {
        var (c, isdId) = Build();
        var root = Emit(c, isdId);

        var isd = c.Gst!.FindRegistration(isdId)!;
        Assert.Equal(isd.Gstin, root.GetProperty("gstin").GetString());
        Assert.NotEqual(c.Gst!.Gstin, root.GetProperty("gstin").GetString());

        Assert.Equal("052025", root.GetProperty("fp").GetString());
        // §39(4): "within thirteen days after the end of such month".
        Assert.Equal("2025-06-13", root.GetProperty("due_date").GetString());
    }

    /// <summary>The shipped product never says "Tally" — the brand is Apex Solutions (standing project rule).</summary>
    [Fact]
    public void The_emitted_file_is_debranded()
    {
        var (c, isdId) = Build();
        var text = Encoding.UTF8.GetString(GstReturnJson.Gstr6(c, From, To, isdId));
        Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A registration that is NOT an ISD must not produce a distribution statement. GSTR-6 is filed by an ISD and by
    /// nobody else (§39(4)), so emitting a populated file for a Regular registration would create a document with no
    /// legal basis.
    /// </summary>
    [Fact]
    public void A_non_isd_registration_emits_an_empty_return()
    {
        var (c, _) = Build();
        var regular = c.Gst!.AllRegistrations
            .First(r => r.RegistrationType == GstRegistrationType.Regular && r.StateCode == TamilNadu);

        var root = Emit(c, regular.Id);

        Assert.Equal(0L, root.GetProperty("total_received_paisa").GetInt64());
        Assert.Equal(0L, root.GetProperty("total_distributed_paisa").GetInt64());
        Assert.Empty(root.GetProperty("tbl8_distribution").EnumerateArray());
    }
}
