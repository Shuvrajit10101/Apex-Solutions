using System;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// 🔴 <b>SECURITY — spreadsheet formula injection (OWASP "CSV injection") in the STATUTORY FLAT-FILE writers.</b>
///
/// <para><b>What was wrong.</b> <see cref="EsiContributionWriter"/>, <see cref="EcrWriter"/> and
/// <see cref="FvuWriter"/> each encode their free-text fields by REPLACING the delimiter and CR/LF with a space —
/// which protects the record FRAMING and nothing else. None of them neutralised a formula trigger, so a party or
/// employee named <c>=cmd|'/c calc'!A1</c> reached the produced file raw. The ESI file is the sharp one: it is
/// written with a <c>.csv</c> extension (<c>&lt;employer code&gt;_yyyy_MM.csv</c>) and an operator double-clicks it
/// to check it before uploading to ESIC, so it executes on open exactly like the two Desktop CSV exporters that
/// were just fixed. The ECR (<c>#~#</c>) and FVU (<c>^</c>) files are <c>.txt</c> and will not auto-split on a
/// double-click — a genuinely lower rank — but they are routinely opened through Excel's Text Import Wizard, where
/// a cell beginning <c>= + - @</c> is evaluated just the same, and the guard costs one call.</para>
///
/// <para>🔴 <b>The ordering here is the OPPOSITE of the quoting exporters', and each writer has a test for it.</b>
/// Where a field is quoted, the guard must run FIRST so the <c>'</c> lands inside the quotes. Where the field is
/// made safe by REPLACING the delimiter — as all three of these do — the guard must run LAST, because the
/// replacement can itself expose a trigger that was not first before: <c>",=cmd…"</c> becomes <c>" =cmd…"</c>,
/// which the guard's leading-space skip catches only if it runs afterwards. Swapping the two steps in any of these
/// three writers reddens its "…only the delimiter sanitisation exposes" test.</para>
///
/// <para><b>Numbers are deliberately not routed through the guard.</b> Each writer keeps separate
/// <c>Int</c>/<c>Money</c>/<c>Rate</c>/<c>Date</c> encoders, so a negative figure can never collect an apostrophe
/// and stop being a number to the portal that ingests it. <c>A_negative_figure_is_never_prefixed</c> pins that.</para>
///
/// <para>The ESI file's own end-to-end proof — through <c>EsiContributionReportViewModel</c>, the screen's real
/// Ctrl+A export — lives in <c>Apex.Desktop.Tests.DelimitedExportFormulaInjectionTests</c>, because that is where
/// the <c>.csv</c> file name is decided.</para>
/// </summary>
public sealed class FlatFileFormulaInjectionTests
{
    /// <summary>The realistic attack payload: a DDE command a spreadsheet executes when the file is opened.</summary>
    private const string Payload = "=cmd|'/c calc'!A1";

    // ────────────────────────────────────────────────────────── EPFO ECR 2.0 (#~# delimited, .txt)

    private static readonly DateOnly EcrMonth = new(2025, 4, 30);

    private static PfEcrReturn EcrWith(string memberName) =>
        new("MHBAN0000000000", EcrMonth,
            new[]
            {
                new PfEcrMember(
                    Uan: "100123456789", Name: memberName,
                    GrossWages: 20000, EpfWages: 15000, EpsWages: 15000, EdliWages: 15000,
                    EmployeeShareEpf: 1800, EpsContribution: 1250, EmployerShareEpf: 550,
                    NcpDays: 0, RefundOfAdvances: 0),
            },
            new PfChallanTotals(Account1: 2350, Account2: 500, Account10: 1250, Account21: 75, Account22: 0));

    private static string[] SingleEcrLineFields(PfEcrReturn ecr)
    {
        var text = Encoding.UTF8.GetString(EcrWriter.Write(ecr));
        var line = Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        return line.Split("#~#", StringSplitOptions.None);
    }

    [Fact]
    public void Ecr_member_name_carrying_a_formula_is_neutralised()
    {
        var fields = SingleEcrLineFields(EcrWith(Payload));

        Assert.Equal(11, fields.Length);            // the payload invented no field
        Assert.Equal("'" + Payload, fields[1]);     // guarded, and the member's own name verbatim after the '
    }

    /// <summary>The ordering proof: the trigger is not first until the <c>#~#</c> has been replaced with a space.
    /// Neutralising BEFORE the replacement returns this field untouched and ships the formula.</summary>
    [Fact]
    public void Ecr_guards_a_formula_that_only_the_delimiter_sanitisation_exposes()
    {
        var fields = SingleEcrLineFields(EcrWith("#~#" + Payload));

        Assert.Equal(11, fields.Length);
        Assert.Equal("' " + Payload, fields[1]);
    }

    /// <summary>
    /// 🔴 The guard must never touch a NUMERIC field. A negative figure starts with <c>-</c>, which is a formula
    /// trigger — prefixing it would turn a number the EPFO portal parses into text it rejects. The numeric encoders
    /// are separate from <c>Text</c>/<c>Name</c> for exactly this reason, and this test fails the moment somebody
    /// "tidies up" by routing every field through one encoder.
    /// </summary>
    [Fact]
    public void Ecr_a_negative_figure_is_never_prefixed()
    {
        var ecr = EcrWith("SANJAY KUMAR") with
        {
            Members = new[]
            {
                new PfEcrMember(
                    Uan: "100123456789", Name: "SANJAY KUMAR",
                    GrossWages: 20000, EpfWages: 15000, EpsWages: 15000, EdliWages: 15000,
                    EmployeeShareEpf: 1800, EpsContribution: 1250, EmployerShareEpf: 550,
                    NcpDays: 0, RefundOfAdvances: -250),
            },
        };

        var fields = SingleEcrLineFields(ecr);
        Assert.Equal("-250", fields[10]);           // a plain number, no apostrophe
    }

    // ────────────────────────────────────────────────────── ESI monthly contribution (comma, .csv)

    private static string[] SingleEsiLineFields(string ipName)
    {
        var ret = new EsiContributionReturn(
            "31000123456789", new DateOnly(2025, 4, 30),
            new[] { new EsiContributionRow("3100123456", ipName, 30, 20000, null, null) });

        var text = Encoding.UTF8.GetString(EsiContributionWriter.Write(ret));
        var line = Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        return line.Split(',');
    }

    [Fact]
    public void Esi_ip_name_carrying_a_formula_is_neutralised()
    {
        var fields = SingleEsiLineFields(Payload);

        Assert.Equal(6, fields.Length);
        Assert.Equal("'" + Payload, fields[1]);
    }

    /// <summary>The ordering proof for the ESI writer: a leading comma is not a formula trigger, so neutralising
    /// first leaves the field alone; the comma then becomes a space and the formula is first after all.</summary>
    [Fact]
    public void Esi_guards_a_formula_that_only_the_delimiter_sanitisation_exposes()
    {
        var fields = SingleEsiLineFields("," + Payload);

        Assert.Equal(6, fields.Length);
        Assert.Equal("' " + Payload, fields[1]);
    }

    // ─────────────────────────────────────────────────── NSDL FVU 26Q (caret delimited, .txt)

    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>The golden 26Q books of <c>FvuWriterTests</c>, with the deductee's name under our control.</summary>
    private static Company TdsCompany(string vendorName)
    {
        var c = CompanyFactory.CreateSeeded("Return Co", FyStart);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = DeducteePan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });

        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var vendor = AddLedger(c, vendorName, "Sundry Creditors", false);
        vendor.TdsApplicable = true;
        vendor.TdsNatureOfPaymentId = nop.Id;
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = DeducteePan;

        var on = new DateOnly(2025, 5, 10);
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, on);
        var journal = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal, on,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));

        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var bank = AddLedger(c, "HDFC Bank", "Bank Accounts", true);
        var posted = new LedgerService(c).Post(
            dep.BuildStatPayment(Money.FromRupees(10_000m), bank, new DateOnly(2025, 6, 5), statType));
        dep.RecordChallan("00123", "0510308", new DateOnly(2025, 6, 5), Money.FromRupees(10_000m),
            "194J(b)", "200", posted);

        return c;
    }

    private static string[] FvuFields(string vendorName) =>
        Encoding.UTF8.GetString(FvuWriter.Write(Form26Q.Build(TdsCompany(vendorName), 2025, 1)))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => line.Split('^'))
            .ToArray();

    [Fact]
    public void Fvu_deductee_name_carrying_a_formula_is_neutralised()
    {
        var fields = FvuFields(Payload);

        // Exactly one field carries the name, and it carries it GUARDED — nothing anywhere in the file is the
        // raw payload, which is the field a Text Import Wizard would evaluate.
        Assert.Equal("'" + Payload, Assert.Single(fields, f => f.EndsWith(Payload, StringComparison.Ordinal)));
        Assert.DoesNotContain(fields, f => string.Equals(f, Payload, StringComparison.Ordinal));
    }

    /// <summary>The ordering proof for the FVU writer: a leading caret is not a formula trigger, so neutralising
    /// first leaves it; the caret then becomes a space and the formula is first after all.</summary>
    [Fact]
    public void Fvu_guards_a_formula_that_only_the_delimiter_sanitisation_exposes()
    {
        var fields = FvuFields("^" + Payload);

        Assert.Equal("' " + Payload, Assert.Single(fields, f => f.EndsWith(Payload, StringComparison.Ordinal)));
        Assert.DoesNotContain(fields, f => string.Equals(" " + Payload, f, StringComparison.Ordinal));
    }
}
