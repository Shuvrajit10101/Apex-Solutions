using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase 7 slice 4 — the <see cref="FvuWriter"/> NSDL FVU-compatible flat-file for Form 26Q. Proves the file is
/// <b>deterministic + byte-stable</b> (byte-identical across two runs, no clock/RNG), <b>de-branded</b> (no
/// third-party accounting brand can leak, even from a party name typed with it), and structurally faithful (FH /
/// BH / CD / DD / FT records, caret-delimited) with the golden worked example's figures. ER-13: an empty return
/// still yields a valid header-only file (no corruption).
/// </summary>
public class FvuWriterTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company NewTdsCompany(DateOnly booksFrom, string responsible = "A. Sharma")
    {
        var c = CompanyFactory.CreateSeeded("Return Co", booksFrom);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = responsible, ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Vendor(Company c, string name = "Consultant")
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, name, "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = DeducteePan;
        return v;
    }

    private static Guid JournalTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    private static void BookDeduction(Company c, DateOnly on, Domain.Ledger vendor)
    {
        var fees = AddLedger(c, $"Professional Fees {on:yyyyMMdd}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, on);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), on,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
    }

    private static void Deposit(Company c, Money amount, DateOnly on, string challanNo)
    {
        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = c.FindLedgerByName("HDFC Bank") ?? AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(amount, bank, on, statType));
        dep.RecordChallan(challanNo, "0510308", on, amount, "194J(b)", "200", posted);
    }

    private static Company GoldenCompany(string vendorName = "Consultant")
    {
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c, vendorName);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 6, 5), "00123");
        return c;
    }

    [Fact]
    public void File_is_byte_identical_across_two_runs()
    {
        var q1 = Form26Q.Build(GoldenCompany(), 2025, 1);
        var a = FvuWriter.Write(q1);
        var b = FvuWriter.Write(q1);
        Assert.Equal(a, b);           // deterministic — no clock/RNG
        Assert.NotEmpty(a);
    }

    [Fact]
    public void File_never_contains_the_third_party_brand_even_from_a_party_name()
    {
        // A user types the forbidden brand into a party name — it must be scrubbed out of the produced file (ER-11).
        var q1 = Form26Q.Build(GoldenCompany("Tally Consultants"), 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void File_has_the_expected_fvu_record_structure_and_figures()
    {
        var q1 = Form26Q.Build(GoldenCompany(), 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // FH ^ 26Q ^ version ^ TAN ^ FY ^ quarter ^ deductorType ^ recordCount
        Assert.StartsWith($"FH^26Q^{FvuWriter.FvuVersion}^{ValidTan}^2025-26^Q1^Company^", lines[0]);
        // BH carries the responsible person.
        Assert.StartsWith("BH^", lines[1]);
        Assert.Contains("A. Sharma", lines[1]);

        // One challan detail then one deductee detail, then the trailer.
        var cd = Assert.Single(lines, l => l.StartsWith("CD^"));
        Assert.Contains("^00123^0510308^05062025^10000.00^194J(b)^200^", cd);
        var dd = Assert.Single(lines, l => l.StartsWith("DD^"));
        Assert.Contains(DeducteePan, dd);
        Assert.Contains("^94J-B^10052025^100000.00^10000.00^10.00^Y^", dd);

        // File Trailer: deductee count, challan count, total TDS, total paid, total deposited.
        var ft = Assert.Single(lines, l => l.StartsWith("FT^"));
        Assert.Equal("FT^1^1^10000.00^100000.00^10000.00", ft);
    }

    [Fact]
    public void Empty_return_still_yields_a_valid_header_only_file()
    {
        var c = NewTdsCompany(FyStart);
        var q1 = Form26Q.Build(c, 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("FH^26Q^", lines[0]);
        Assert.StartsWith("BH^", lines[1]);
        Assert.DoesNotContain(lines, l => l.StartsWith("CD^"));
        Assert.DoesNotContain(lines, l => l.StartsWith("DD^"));
        Assert.Equal("FT^0^0^0.00^0.00^0.00", lines[^1]);
    }

    [Fact]
    public void Undeposited_deduction_does_not_overstate_the_file_deductee_count()
    {
        // An in-quarter ₹10,000 deduction with NO deposit: the file has no challan and therefore no DD (deductee)
        // record. The BH/FT deductee counts and the FT money totals must describe the file's actual DD rows (zero),
        // never the full-quarter projection — otherwise the trailer claims a deductee record the file doesn't hold.
        var c = NewTdsCompany(FyStart);
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 5, 10), vendor);

        var q1 = Form26Q.Build(c, 2025, 1);
        var text = Encoding.UTF8.GetString(FvuWriter.Write(q1));
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.DoesNotContain(lines, l => l.StartsWith("CD^"));
        var ddCount = lines.Count(l => l.StartsWith("DD^"));
        Assert.Equal(0, ddCount);

        // File Trailer must equal zero deductee records / zero money — not FT^1^0^10000.00^100000.00^0.00.
        var ft = Assert.Single(lines, l => l.StartsWith("FT^"));
        Assert.Equal("FT^0^0^0.00^0.00^0.00", ft);

        // Batch Header's trailing deductee count must equal the DD lines actually written.
        var bh = Assert.Single(lines, l => l.StartsWith("BH^"));
        Assert.Equal(ddCount.ToString(), bh.Split('^')[^1]);
    }

    [Fact]
    public void Cross_fy_challan_is_written_into_the_deduction_quarter_file()
    {
        var c = NewTdsCompany(new DateOnly(2024, 4, 1));
        var vendor = Vendor(c);
        BookDeduction(c, new DateOnly(2025, 3, 20), vendor);
        Deposit(c, Money.FromRupees(10_000m), new DateOnly(2025, 4, 7), "00777");

        // Q4 FY2024-25 file has the April challan; the Q1 FY2025-26 file does not.
        var q4Text = Encoding.UTF8.GetString(FvuWriter.Write(Form26Q.Build(c, 2024, 4)));
        Assert.Contains("00777", q4Text);
        Assert.Contains("07042025", q4Text);  // deposit date printed, but in the Q4 file

        var q1Text = Encoding.UTF8.GetString(FvuWriter.Write(Form26Q.Build(c, 2025, 1)));
        Assert.DoesNotContain("00777", q1Text);
        Assert.EndsWith("FT^0^0^0.00^0.00^0.00\n", q1Text);
    }
}
