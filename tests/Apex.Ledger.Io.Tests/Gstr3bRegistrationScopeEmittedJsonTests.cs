using System.Text.Json;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>REVIEW-ADDED (wave 47, adversarial review of <c>claude/apex-bn-gstr3b-rcm</c>). THE SCOPE PARAMETER THE
/// SLICE ADDED TO <see cref="GstReturnJson.Gstr3b"/> HAD NO TEST AT ALL.</b>
///
/// <para>The slice gave <c>GstReturnJson.Gstr3b</c> a <c>registrationId</c> and made
/// <c>GstOfflineReturnsViewModel</c> pass <c>ScopedRegistrationId</c> — correctly, because the preview beside it
/// was already scoped and a multi-registration book therefore previewed one registration and then threw
/// <c>EnsureRegistrationScoped</c>'s refusal on export. But <b>no test in the repository passed a fourth argument
/// to that writer</b>: measured with
/// <c>grep -rn "GstReturnJson.Gstr3b(" tests/</c> — 27 call sites across five files, every one of them
/// three-argument. Reverting the ViewModel's <c>ScopedRegistrationId</c> left the whole suite green, which makes
/// the fix decorative until something pins it. This file pins it, on the EMITTED BYTES.</para>
///
/// <para><b>The book.</b> A Karnataka (29) primary registration and a Tamil Nadu (33) branch. Karnataka buys
/// ₹10,000 intra-State at 18% ⇒ CGST 900 + SGST 900 (90,000 paisa a head). Tamil Nadu buys ₹20,000 intra-State at
/// 18% ⇒ CGST 1,800 + SGST 1,800 (180,000 paisa a head). Neither registration's input credit may appear on the
/// other's filed return: ITC lands in the electronic credit ledger of exactly one GSTIN
/// (CGST Act §49(2)/(4), cbic-gst.gov.in), and 4(C) is the figure credited there
/// (CBIC Circular No. 170/02/2022-GST para 4.2).</para>
/// </summary>
public sealed class Gstr3bRegistrationScopeEmittedJsonTests
{
    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2025, 4, 30);

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Karnataka, "AAPFU0939F");
    private static readonly string GstinBranch = GstinFor(TamilNadu, "AABCC1206D");

    /// <summary>
    /// 🔴 THE HEADLINE. Karnataka's filed 4(A)(5) and 4(C) carry Karnataka's ₹900 a head and NOT Tamil Nadu's
    /// ₹1,800 — and the whole-book figure (₹2,700) appears on neither return.
    /// <para><b>Mutation:</b> drop <c>registrationId</c> from <c>Reports.Gstr3b.Build(company, from, to,
    /// registrationId)</c> inside <c>GstReturnJson.Gstr3b</c> ⇒ this test throws
    /// <c>InvalidOperationException</c> (<c>EnsureRegistrationScoped</c>'s refusal) and fails by name.</para>
    /// </summary>
    [Fact]
    public void Each_registrations_filed_3b_carries_only_its_own_input_credit()
    {
        var (company, branch) = Build();

        using var ka = JsonDocument.Parse(GstReturnJson.Gstr3b(company, From, To, GstRegistration.PrimaryId));
        using var tn = JsonDocument.Parse(GstReturnJson.Gstr3b(company, From, To, branch.Id));

        Assert.Equal(90_000L, ka.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(90_000L, ka.RootElement.GetProperty("tbl4a5_samt_paisa").GetInt64());
        Assert.Equal(90_000L, ka.RootElement.GetProperty("tbl4c_net_itc_camt_paisa").GetInt64());

        Assert.Equal(180_000L, tn.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.Equal(180_000L, tn.RootElement.GetProperty("tbl4a5_samt_paisa").GetInt64());
        Assert.Equal(180_000L, tn.RootElement.GetProperty("tbl4c_net_itc_camt_paisa").GetInt64());

        // The whole-book sum is on NEITHER return — that is the defect this guards.
        Assert.NotEqual(270_000L, ka.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
        Assert.NotEqual(270_000L, tn.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());
    }

    /// <summary>
    /// The unscoped call on a multi-registration book still REFUSES rather than folding two GSTINs into one filed
    /// document — the guard the scope parameter exists to let the caller answer, not to bypass.
    /// </summary>
    [Fact]
    public void The_unscoped_call_still_refuses_on_a_multi_registration_book()
    {
        var (company, _) = Build();

        var ex = Assert.Throws<InvalidOperationException>(() => GstReturnJson.Gstr3b(company, From, To));
        Assert.Contains("multiple GST registrations", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>ER-13 at the artefact.</b> A single-registration book emits byte-for-byte what it emitted before the
    /// parameter existed — the default <c>null</c> and the explicit primary id must reach the same file.
    /// </summary>
    [Fact]
    public void A_single_registration_book_emits_identical_bytes_with_and_without_the_scope()
    {
        var company = BuildSingleRegistrationBook();

        Assert.Equal(
            GstReturnJson.Gstr3b(company, From, To),
            GstReturnJson.Gstr3b(company, From, To, GstRegistration.PrimaryId));
    }

    // ================================================================ fixture

    private static (Company Company, GstRegistration Branch) Build()
    {
        var c = BuildSingleRegistrationBook();

        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinBranch,
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        PostPurchase(c, 20_000m, new DateOnly(2025, 4, 12), branch.Id);
        return (c, branch);
    }

    private static Company BuildSingleRegistrationBook()
    {
        var c = CompanyFactory.CreateSeeded("Scoped 3B Io Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        AddLedger(c, "Purchases 3B", "Purchase Accounts", true);
        var supplier = AddLedger(c, "Scoped Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinFor(Karnataka, "AAACS1206D"),
            StateCode = Karnataka,
        };

        PostPurchase(c, 10_000m, new DateOnly(2025, 4, 6), null);
        return c;
    }

    /// <summary>Posts one intra-State purchase of <paramref name="value"/> at 18% under
    /// <paramref name="registrationId"/> (<c>null</c> ⇒ the primary).</summary>
    private static void PostPurchase(Company c, decimal value, DateOnly date, Guid? registrationId)
    {
        var purchases = c.Ledgers.First(l => l.Name == "Purchases 3B");
        var supplier = c.Ledgers.First(l => l.Name == "Scoped Supplier");

        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(value), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(value + tax.TotalTax.Amount), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var v = new Voucher(
            Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            date, lines, partyId: supplier.Id);
        if (registrationId is { } r) v.GstRegistrationId = r;
        new LedgerService(c).Post(v);
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
