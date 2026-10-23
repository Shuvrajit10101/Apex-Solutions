using System;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 12.6, the multi-VOUCHER half — a RANGE of vouchers printed as one collated job.</b>
///
/// <para>The row reads "Multi-account printing / multi-voucher (range) printing". The account half landed in
/// PR #56; the cell has said ever since that <i>"the multi-voucher (range) half of this row is not addressed —
/// the range knob selects sheets within a job, not a range of vouchers to print"</i>. These tests pin the
/// range.</para>
///
/// <para>🔴 <b>EVERY ASSERTION THAT MATTERS READS THE EMITTED PDF BYTES</b>, not the projector's return value
/// and never an internal the test itself set. The printing track has been proved this way throughout, for the
/// measured reason recorded on <c>MatrixSecondBandEgressTests</c>: a report whose rows are wider than its
/// column band is silently amputated by <c>ReportPdf</c>'s <c>Math.Min(Columns.Count, row.Cells.Count)</c>
/// clamp, and an assertion on the view model passes happily over the amputation. A filed annual return went out
/// with its money deleted that way.</para>
///
/// <para>🔴 <b>PDF string literals escape <c>(</c> and <c>)</c></b>, so no assertion here looks for a
/// parenthesis — a reviewer on this project once reported clipping that did not exist by forgetting it.</para>
/// </summary>
public sealed class MultiVoucherPrintTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>
    /// Three receipts from Northwind, two payments to Southgate, all inside the year. Distinct amounts, so a
    /// voucher that reached (or failed to reach) the paper can be identified by its own figure.
    /// </summary>
    private static (Company C, Guid Northwind, Guid Southgate, Guid ReceiptType, Guid PaymentType) Books()
    {
        var c = CompanyFactory.CreateSeeded("Range Print Co", FyStart);

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(500_000m);
        cash.OpeningIsDebit = true;

        var capital = new Domain.Ledger(Guid.NewGuid(), "Capital A/c",
            c.FindGroupByName("Capital Account")!.Id, Money.FromRupees(500_000m), openingIsDebit: false);
        c.AddLedger(capital);

        var northwind = new Domain.Ledger(Guid.NewGuid(), "Northwind Traders",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.FromRupees(300_000m), openingIsDebit: true);
        c.AddLedger(northwind);

        var southgate = new Domain.Ledger(Guid.NewGuid(), "Southgate Supplies",
            c.FindGroupByName("Sundry Creditors")!.Id, Money.FromRupees(200_000m), openingIsDebit: false);
        c.AddLedger(southgate);

        var receipt = c.FindVoucherTypeByName("Receipt")!;
        var payment = c.FindVoucherTypeByName("Payment")!;
        var svc = new LedgerService(c);

        // Northwind receipts: 11,111.11 / 22,222.22 / 33,333.33 on three different months.
        var receiptAmounts = new[] { 11_111.11m, 22_222.22m, 33_333.33m };
        var receiptDates = new[]
        {
            new DateOnly(2025, 4, 10), new DateOnly(2025, 5, 10), new DateOnly(2025, 6, 10),
        };
        for (int i = 0; i < 3; i++)
        {
            var v = new Voucher(Guid.NewGuid(), receipt.Id, receiptDates[i], new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(receiptAmounts[i]), DrCr.Debit),
                new EntryLine(northwind.Id, Money.FromRupees(receiptAmounts[i]), DrCr.Credit),
            })
            { PartyId = northwind.Id };
            svc.Post(v);
        }

        // Southgate payments: 44,444.44 / 55,555.55.
        var paymentAmounts = new[] { 44_444.44m, 55_555.55m };
        var paymentDates = new[] { new DateOnly(2025, 4, 20), new DateOnly(2025, 7, 20) };
        for (int i = 0; i < 2; i++)
        {
            var v = new Voucher(Guid.NewGuid(), payment.Id, paymentDates[i], new[]
            {
                new EntryLine(southgate.Id, Money.FromRupees(paymentAmounts[i]), DrCr.Debit),
                new EntryLine(cash.Id, Money.FromRupees(paymentAmounts[i]), DrCr.Credit),
            })
            { PartyId = southgate.Id };
            svc.Post(v);
        }

        return (c, northwind.Id, southgate.Id, receipt.Id, payment.Id);
    }

    private static string RenderRange(
        Company c, Guid? typeId, Guid? partyId, DateOnly from, DateOnly to)
    {
        var chosen = MultiVoucherPrintProjector.Select(c, typeId, partyId, from, to);
        var docs = MultiVoucherPrintProjector.Project(c, chosen);
        return AsLatin1(ReportPdf.Render(docs, new PageConfig()));
    }

    // ------------------------------------------------------------------ the range itself

    /// <summary>
    /// 🔴 <b>THE HEADLINE: a PERIOD selects a range of vouchers, and the vouchers outside it are not on the
    /// paper.</b> Both halves are asserted, because "the right ones printed" and "the wrong ones did not" are
    /// different claims and a projector that printed everything would satisfy only the first.
    /// </summary>
    [Fact]
    public void A_period_prints_every_voucher_inside_it_and_none_outside_it()
    {
        var (c, _, _, _, _) = Books();

        // April through June: the three Northwind receipts and the 20-Apr Southgate payment. NOT the 20-Jul one.
        string s = RenderRange(c, null, null, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));

        Assert.Contains("11,111.11", s, StringComparison.Ordinal);
        Assert.Contains("22,222.22", s, StringComparison.Ordinal);
        Assert.Contains("33,333.33", s, StringComparison.Ordinal);
        Assert.Contains("44,444.44", s, StringComparison.Ordinal);
        Assert.DoesNotContain("55,555.55", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// The range is INCLUSIVE at both ends. A one-day window on the first voucher's own date must print it —
    /// an exclusive upper bound would silently drop the last day of every month-end run, and nothing on the
    /// paper would say so.
    /// </summary>
    [Fact]
    public void The_period_bounds_are_inclusive_at_both_ends()
    {
        var (c, _, _, _, _) = Books();

        string justTheTenth = RenderRange(
            c, null, null, new DateOnly(2025, 4, 10), new DateOnly(2025, 4, 10));

        Assert.Contains("11,111.11", justTheTenth, StringComparison.Ordinal);
        Assert.DoesNotContain("22,222.22", justTheTenth, StringComparison.Ordinal);
        Assert.DoesNotContain("44,444.44", justTheTenth, StringComparison.Ordinal);
    }

    /// <summary>The VOUCHER TYPE axis — the vendor's own Configure field.</summary>
    [Fact]
    public void A_voucher_type_restricts_the_job_to_that_type()
    {
        var (c, _, _, receiptType, _) = Books();

        string s = RenderRange(c, receiptType, null, FyStart, FyEnd);

        Assert.Contains("11,111.11", s, StringComparison.Ordinal);
        Assert.Contains("33,333.33", s, StringComparison.Ordinal);
        // Both payments are a different type and must not be in a receipts job.
        Assert.DoesNotContain("44,444.44", s, StringComparison.Ordinal);
        Assert.DoesNotContain("55,555.55", s, StringComparison.Ordinal);
    }

    /// <summary>The PARTY axis — attested on the vendor's release notes alongside the period.</summary>
    [Fact]
    public void A_party_restricts_the_job_to_that_partys_vouchers()
    {
        var (c, _, southgate, _, _) = Books();

        string s = RenderRange(c, null, southgate, FyStart, FyEnd);

        Assert.Contains("44,444.44", s, StringComparison.Ordinal);
        Assert.Contains("55,555.55", s, StringComparison.Ordinal);
        Assert.DoesNotContain("11,111.11", s, StringComparison.Ordinal);
        Assert.DoesNotContain("22,222.22", s, StringComparison.Ordinal);
        Assert.DoesNotContain("33,333.33", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// The three axes COMPOSE: type AND party AND period, all at once, down to a single voucher.
    /// </summary>
    [Fact]
    public void The_three_axes_compose_down_to_one_voucher()
    {
        var (c, northwind, _, receiptType, _) = Books();

        string s = RenderRange(c, receiptType, northwind, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31));

        Assert.Contains("22,222.22", s, StringComparison.Ordinal);
        Assert.DoesNotContain("11,111.11", s, StringComparison.Ordinal);
        Assert.DoesNotContain("33,333.33", s, StringComparison.Ordinal);
        Assert.DoesNotContain("44,444.44", s, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ one job, many sheets

    /// <summary>
    /// 🔴 <b>IT IS ONE COLLATED JOB, NOT N SEPARATE DOCUMENTS — asserted on the PDF's own page objects.</b>
    /// Five vouchers in the range produce five sheets in ONE byte stream. A projector that returned the right
    /// documents but a renderer that emitted one page would satisfy every content assertion above.
    /// </summary>
    [Fact]
    public void Every_voucher_in_the_range_starts_its_own_sheet_in_one_pdf()
    {
        var (c, _, _, _, _) = Books();

        var chosen = MultiVoucherPrintProjector.Select(c, null, null, FyStart, FyEnd);
        Assert.Equal(5, chosen.Count);

        var docs = MultiVoucherPrintProjector.Project(c, chosen);
        Assert.Equal(5, docs.Count);

        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig()));

        // Read the page count off the emitted PDF structure, not off docs.Count.
        int pageObjects = CountOccurrences(s, "/Type /Page\n") + CountOccurrences(s, "/Type /Page ");
        Assert.True(pageObjects >= 5,
            $"five vouchers must occupy at least five sheets; the PDF declares {pageObjects}.");
        Assert.StartsWith("%PDF-", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// The vouchers reach the paper in DATE order, so a reprint of the same range matches the first copy.
    /// Asserted on the byte offsets of each figure in the emitted stream — the order the sheets really came out
    /// in, not the order of a list.
    /// </summary>
    [Fact]
    public void The_sheets_come_out_in_date_order()
    {
        var (c, northwind, _, receiptType, _) = Books();

        string s = RenderRange(c, receiptType, northwind, FyStart, FyEnd);

        int april = s.IndexOf("11,111.11", StringComparison.Ordinal);
        int may = s.IndexOf("22,222.22", StringComparison.Ordinal);
        int june = s.IndexOf("33,333.33", StringComparison.Ordinal);

        Assert.True(april >= 0 && may >= 0 && june >= 0, "all three receipts must be on the paper");
        Assert.True(april < may, "the April receipt must precede the May one in the emitted stream");
        Assert.True(may < june, "the May receipt must precede the June one in the emitted stream");
    }

    /// <summary>
    /// 🔴 <b>THE EGRESS BAND — the invariant <c>MatrixSecondBandEgressTests</c> exists to hold.</b> No row of
    /// any document this projector builds may be wider than the column band that carries it, because
    /// <c>ReportPdf</c> clamps with <c>Math.Min(Columns.Count, row.Cells.Count)</c> and the overflow is deleted
    /// in silence. Checked per document over the whole range, so a single odd voucher cannot slip through.
    /// </summary>
    [Fact]
    public void No_document_carries_a_row_wider_than_its_column_band()
    {
        var (c, _, _, _, _) = Books();

        var chosen = MultiVoucherPrintProjector.Select(c, null, null, FyStart, FyEnd);
        var docs = MultiVoucherPrintProjector.Project(c, chosen);
        Assert.NotEmpty(docs);

        foreach (var d in docs)
        {
            Assert.NotEmpty(d.Columns);
            foreach (var row in d.Rows)
                Assert.True(row.Cells.Count <= d.Columns.Count,
                    $"'{d.Title}' has a row of {row.Cells.Count} cells under a band of {d.Columns.Count} — "
                  + "ReportPdf would delete the overflow without a word.");
        }
    }

    /// <summary>
    /// A CANCELLED voucher stays in the range and is MARKED on the paper. It is not dropped — its number was
    /// spent — and it is not printed looking live, which would put a withdrawn document in a counterparty's
    /// hands. The marking is read off the emitted bytes.
    /// </summary>
    [Fact]
    public void A_cancelled_voucher_prints_marked_rather_than_silently_or_not_at_all()
    {
        var (c, northwind, _, receiptType, _) = Books();

        var target = c.Vouchers.Single(v => v.TypeId == receiptType && v.Date == new DateOnly(2025, 5, 10));
        new LedgerService(c).Cancel(target.Id);

        string s = RenderRange(c, receiptType, northwind, FyStart, FyEnd);

        Assert.Contains("CANCELLED", s, StringComparison.Ordinal);
        // The live ones are untouched by the cancellation.
        Assert.Contains("11,111.11", s, StringComparison.Ordinal);
        Assert.Contains("33,333.33", s, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the panel

    /// <summary>
    /// The panel offers the three axes as real, populated choices and its match list is what the job will
    /// print. "All Voucher Types" and "All Parties" are the opening state, so the panel opens on the widest
    /// range rather than on nothing.
    /// </summary>
    [Fact]
    public void The_panel_opens_on_the_whole_period_and_lists_what_it_would_print()
    {
        var (c, _, _, _, _) = Books();

        var vm = new MultiVoucherPrintViewModel(c, FyStart, FyEnd);

        Assert.Equal("All Voucher Types", vm.VoucherTypes[0].Name);
        Assert.Null(vm.VoucherTypes[0].Id);
        Assert.Equal("All Parties", vm.Parties[0].Name);
        Assert.Null(vm.Parties[0].Id);

        // Only PARTY accounts are offered as party filters — a filter row that can never match is a dead field.
        Assert.Contains(vm.Parties, p => p.Name == "Northwind Traders");
        Assert.Contains(vm.Parties, p => p.Name == "Southgate Supplies");
        Assert.DoesNotContain(vm.Parties, p => p.Name == "Capital A/c");

        Assert.Equal(5, vm.MatchCount);
        Assert.True(vm.HasMatches);
        Assert.Equal(5, vm.BuildJob().Count);
    }

    /// <summary>
    /// Narrowing the panel's period narrows the JOB — the panel's selection really drives the documents, which
    /// is the half a projector-only test cannot see.
    /// </summary>
    [Fact]
    public void Narrowing_the_panels_period_narrows_the_emitted_job()
    {
        var (c, _, _, _, _) = Books();

        var vm = new MultiVoucherPrintViewModel(c, FyStart, FyEnd);
        Assert.Equal(5, vm.MatchCount);

        vm.PeriodFromText = "01-05-2025";
        vm.PeriodToText = "31-05-2025";

        Assert.Equal(1, vm.MatchCount);
        string s = AsLatin1(ReportPdf.Render(vm.BuildJob(), new PageConfig()));
        Assert.Contains("22,222.22", s, StringComparison.Ordinal);
        Assert.DoesNotContain("11,111.11", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// An INVERTED range is refused with a message naming both dates, and builds NOTHING — it is not silently
    /// swapped, which would print a batch the operator did not ask for while telling him he got what he typed.
    /// </summary>
    [Fact]
    public void An_inverted_range_is_refused_and_prints_nothing()
    {
        var (c, _, _, _, _) = Books();

        var vm = new MultiVoucherPrintViewModel(c, FyStart, FyEnd);
        vm.PeriodFromText = "30-06-2025";
        vm.PeriodToText = "01-04-2025";

        Assert.Equal(0, vm.MatchCount);
        Assert.False(vm.HasMatches);
        Assert.Empty(vm.BuildJob());
        Assert.Contains("before", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An EMPTY range builds nothing and says so. The caller must not open a preview over a blank sheet —
    /// presenting a mistake as output is the shape this project keeps filing.
    /// </summary>
    [Fact]
    public void An_empty_range_builds_no_job_and_says_why()
    {
        var (c, _, _, _, _) = Books();

        var vm = new MultiVoucherPrintViewModel(c, FyStart, FyEnd);
        vm.PeriodFromText = "01-01-2030";
        vm.PeriodToText = "31-01-2030";

        Assert.Equal(0, vm.MatchCount);
        Assert.Empty(vm.BuildJob());
        Assert.Contains("No vouchers", vm.Status, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }
        return n;
    }
}
