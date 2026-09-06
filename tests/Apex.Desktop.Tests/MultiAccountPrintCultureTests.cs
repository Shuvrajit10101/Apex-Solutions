using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W2-32 / census 12.6 — a printed statement must not depend on the operator's Windows locale.</b>
///
/// <para><b>🔴 THE DEFECT THIS FILE WAS WRITTEN AGAINST, and it was found by reading rather than by the gate.</b>
/// <c>MultiAccountPrintProjector</c> formatted every date it prints with a bare
/// <c>DateOnly.ToString("dd-MM-yyyy")</c> — ten sites: the statement's period subtitle, each posting's date, each
/// bill's date and due date, and the "as at" sentence in both letters. A bare custom format string reads
/// <see cref="CultureInfo.CurrentCulture"/>, and <b>CurrentCulture carries a CALENDAR</b>. Measured on the Robert
/// fixture, whose period is <c>01-04-2020 to 30-04-2020</c>: on a Thai machine (<c>th-TH</c>, Buddhist era — the
/// year moves by 543) it printed <c>01-04-2563 to 30-04-2563</c>, and on <c>ar-SA</c> (Umm al-Qura) it printed
/// <c>08-08-1441 to 07-09-1441</c>, moving the day and the month as well. The figures were right and the DATES
/// WERE WRONG — a confirmation of accounts asking a counterparty to confirm a balance "as at" a date that is not
/// the date of the balance.</para>
///
/// <para><b>Why no existing test could have caught it, on any of the three CI legs.</b> The three runners
/// (ubuntu, windows, macos) all resolve to an English/invariant ambient culture, so every one of the ten sites
/// renders identically to the fix there. The defect is invisible to a green gate by construction; only pinning
/// the culture makes it observable. This is the same class as the <c>SpecialFolder.MyDocuments</c>-returns-""
/// escape: a platform/environment assumption that the gate cannot see.</para>
///
/// <para><b>Why it mattered NOW and not before.</b> The projector shipped with <b>zero references</b> (filed as
/// <c>T2-40</c>) — nothing it formatted ever reached paper. This slice gives it a menu route, so this pass is
/// exactly when those ten sites become a document an operator is holding. Every other printed document in the
/// product already formats through <c>CultureInfo.InvariantCulture</c>
/// (<c>VoucherPrintProjector</c>, nine sites); this projector was the sole exception.</para>
///
/// <para><b>Asserted on the PDF BYTES, and negatively-plus-positively.</b> The equality across cultures is the
/// bite; the day/month/year check under <c>th-TH</c> names WHICH date is wrong when it fires, so the next reader
/// is not left comparing two byte arrays.</para>
/// </summary>
public sealed class MultiAccountPrintCultureTests
{
    /// <summary>
    /// Cultures whose default calendar or separators differ from the invariant one. <c>th-TH</c> is the sharp
    /// instrument (Buddhist era: the YEAR moves by 543); <c>ar-SA</c> moves the day and the month as well;
    /// <c>de-DE</c> and <c>en-US</c> keep the Gregorian calendar and are the control.
    /// </summary>
    public static TheoryData<string> ForeignCultures() => new() { "en-US", "de-DE", "th-TH", "ar-SA" };

    /// <summary>
    /// The Robert fixture's own period, in the invariant Gregorian form the books carry — measured, not assumed.
    /// Written out rather than derived from the shell so a failure states the expected DATE, which is the thing
    /// the defect got wrong.
    /// </summary>
    private const string RobertPeriod = "01-04-2020 to 30-04-2020";

    /// <summary>The "as at" bound of that period — the date both letters put in their opening sentence.</summary>
    private const string RobertAsOf = "30-04-2020";

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>
    /// Drives the whole shipped route — load the fixture, open the panel from the shell, choose the kind, Select
    /// All, Print — with <see cref="Thread.CurrentCulture"/> pinned, and returns the bytes the operator would be
    /// holding. The shell is rebuilt inside the culture rather than outside it so that anything the construction
    /// path formats is covered too.
    /// </summary>
    private static byte[] PrintUnder(string cultureName, MultiAccountDocumentKind kind, out string periodText)
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
        string tempDir = Path.Combine(Path.GetTempPath(), "ApexMultiCulture_" + Guid.NewGuid().ToString("N"));
        try
        {
            var culture = cultureName.Length == 0
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(cultureName);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
            vm.LoadRobertDemo();
            vm.OpenMultiAccountPrint();

            var panel = vm.MultiAccountPrint!;
            panel.DocumentKind = kind;
            panel.SelectAll();
            periodText = panel.PeriodText;

            vm.PrintMultiAccountJob();
            return vm.PrintPreview!.PdfBytes;
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Non-vacuity. If the byte comparison below could not tell two renders apart it would pass for the wrong
    /// reason, so first prove the SAME culture twice is byte-identical (the job is deterministic and clock-free)
    /// and that the job is a real, multi-sheet document rather than an empty one.
    /// </summary>
    [Fact]
    public void The_job_is_deterministic_and_non_empty_so_the_comparison_below_means_something()
    {
        var first = PrintUnder("", MultiAccountDocumentKind.LedgerAccount, out string period);
        var second = PrintUnder("", MultiAccountDocumentKind.LedgerAccount, out _);

        Assert.True(first.SequenceEqual(second),
            "two renders under the same culture differ — the job is not deterministic, so the cross-culture "
          + "comparison below would be meaningless.");
        Assert.True(first.Length > 2000, $"the job rendered {first.Length} bytes; it must be a real document");
        Assert.Equal(RobertPeriod, period);
    }

    /// <summary>
    /// 🔴 <b>THE BITE.</b> The printed job must be byte-identical whatever locale the operator's machine is set
    /// to. Run over all three document kinds, because the two letters format their "as at" sentence and their
    /// bill dates through different code paths from the statement's postings.
    /// </summary>
    [Theory]
    [MemberData(nameof(ForeignCultures))]
    public void The_printed_job_is_byte_identical_whatever_locale_the_machine_is_set_to(string cultureName)
    {
        foreach (var kind in new[]
                 {
                     MultiAccountDocumentKind.LedgerAccount,
                     MultiAccountDocumentKind.ReminderLetter,
                     MultiAccountDocumentKind.ConfirmationOfAccounts,
                 })
        {
            var invariant = PrintUnder("", kind, out string invariantPeriod);
            var foreign = PrintUnder(cultureName, kind, out string foreignPeriod);

            Assert.Equal(invariantPeriod, foreignPeriod);
            Assert.True(invariant.SequenceEqual(foreign),
                $"the {kind} job rendered {foreign.Length} bytes under {cultureName} against "
              + $"{invariant.Length} under the invariant culture. A printed document must not depend on the "
              + "operator's locale: format every date through CultureInfo.InvariantCulture.");
        }
    }

    /// <summary>
    /// The same defect stated as the operator would see it, so a failure names the wrong DATE rather than a byte
    /// count. Under <c>th-TH</c> the Buddhist era adds 543 to the year: the Robert fixture's books-begin
    /// <c>01-04-2020</c> printed as <c>01-04-2563</c> before the fix.
    /// </summary>
    [Fact]
    public void A_thai_locale_never_prints_a_buddhist_era_year_on_a_statement()
    {
        var bytes = PrintUnder("th-TH", MultiAccountDocumentKind.LedgerAccount, out string period);
        string text = AsLatin1(bytes);

        Assert.Equal(RobertPeriod, period);
        Assert.DoesNotContain("2563", text, StringComparison.Ordinal);
        Assert.Contains("2020", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The "as at" sentence both letters carry is built in its own place, so it gets its own named assertion:
    /// a confirmation of accounts must not ask a counterparty to confirm a balance as at a date the books do not
    /// carry.
    /// </summary>
    [Fact]
    public void A_confirmation_never_asks_a_counterparty_to_confirm_a_balance_as_at_a_foreign_calendar_date()
    {
        string invariant = AsLatin1(PrintUnder("", MultiAccountDocumentKind.ConfirmationOfAccounts, out _));
        string thai = AsLatin1(PrintUnder("th-TH", MultiAccountDocumentKind.ConfirmationOfAccounts, out _));

        Assert.Contains("as at " + RobertAsOf, invariant, StringComparison.Ordinal);
        Assert.Contains("as at " + RobertAsOf, thai, StringComparison.Ordinal);
    }
}
