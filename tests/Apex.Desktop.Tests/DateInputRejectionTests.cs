using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-5 — <b>an unparseable date is REJECTED, never silently discarded</b> (the second of the two live
/// correctness bugs this work item kills).
/// <para>
/// The old setters assigned <c>Date</c> only <i>inside</i> <c>if (TryParse(…))</c>, so on bad input nothing
/// happened at all. That is subtler — and worse — than "the typing vanishes": because <c>Date</c> never
/// changed, its property-changed notification never fired, so the two-way binding never re-read the getter.
/// <b>The rejected text STAYED ON SCREEN while the voucher kept — and posted — a different date.</b> Screen
/// and stored value silently disagreed.
/// </para>
/// <para>
/// So asserting only "the date is unchanged" would prove NOTHING (it is unchanged under the bug too). These
/// tests assert the two things that are actually new: the field is <b>re-notified</b> so the display snaps
/// back to the date really held, and the rejection is <b>surfaced</b> in the screen's message line.
/// </para>
/// </summary>
public sealed class DateInputRejectionTests
{
    private static (Company Company, CompanyStorage Storage, string TempDir) NewCompany()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexDateReject_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var shell = new MainWindowViewModel(storage);
        shell.NewCompanyName = "Date Reject Co";
        shell.CreateCompany();      // seeds the standard groups / ledgers / voucher types and persists
        return (shell.Company!, storage, tempDir);
    }

    private static VoucherEntryViewModel NewVoucherEntry(Company company, CompanyStorage storage)
    {
        var type = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);
        return new VoucherEntryViewModel(company, type, storage, onSaved: () => { }, onCancelled: () => { });
    }

    /// <summary>Records every PropertyChanged name raised by a view model.</summary>
    private static List<string> RecordNotifications(INotifyPropertyChanged vm)
    {
        var seen = new List<string>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName ?? string.Empty);
        return seen;
    }

    // ------------------------------------------------------------------ the reject-not-discard lock

    [Fact]
    public void An_unparseable_voucher_date_is_rejected_and_surfaced_not_silently_swallowed()
    {
        var (company, storage, tempDir) = NewCompany();
        try
        {
            var entry = NewVoucherEntry(company, storage);
            entry.DateText = "03/04/2024";
            var kept = entry.Date;
            Assert.Equal(new DateOnly(2024, 4, 3), kept);

            var notifications = RecordNotifications(entry);
            entry.DateText = "not-a-date";

            // 1. The last VALID date survives — the operator's good data is not destroyed.
            Assert.Equal(kept, entry.Date);

            // 2. THE FIX: the field is re-notified, so the two-way binding re-reads the getter and the rejected
            //    text on screen is replaced by the date actually held. Under the old code NO notification fired
            //    (Date never changed), which is exactly how the discard stayed silent.
            Assert.Contains(nameof(VoucherEntryViewModel.DateText), notifications);

            // 3. …and the rejection is SURFACED, naming the offending input and the canonical format.
            Assert.NotNull(entry.Message);
            Assert.Contains("not-a-date", entry.Message!);
            Assert.Contains(ApexDate.Canonical, entry.Message!);

            // 4. The displayed text is the canonical rendering of the kept date — never the rejected input.
            Assert.Equal("03-Apr-2024", entry.DateText);
        }
        finally { Cleanup(tempDir); }
    }

    /// <summary>
    /// The same contract on the inventory entry screen — which was worse still: it had no
    /// <c>OnDateChanged</c> at all, so even a SUCCESSFUL parse never echoed canonically.
    /// </summary>
    [Fact]
    public void An_unparseable_inventory_voucher_date_is_rejected_and_surfaced()
    {
        var (company, storage, tempDir) = NewCompany();
        try
        {
            var type = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PurchaseOrder && t.IsActive);
            var entry = new InventoryVoucherEntryViewModel(
                company, type, storage, onSaved: () => { }, onCancelled: () => { });

            entry.DateText = "03/04/2024";
            var kept = entry.Date;
            Assert.Equal(new DateOnly(2024, 4, 3), kept);

            var notifications = RecordNotifications(entry);
            entry.DateText = "rubbish";

            Assert.Equal(kept, entry.Date);
            Assert.Contains(nameof(InventoryVoucherEntryViewModel.DateText), notifications);
            Assert.NotNull(entry.Message);
            Assert.Contains("rubbish", entry.Message!);
            Assert.Equal("03-Apr-2024", entry.DateText);
        }
        finally { Cleanup(tempDir); }
    }

    /// <summary>
    /// A SUCCESSFUL parse also re-notifies, so shorthand the operator types is echoed back canonically even
    /// when it resolves to the date already held (where the date itself does not change, and therefore raises
    /// nothing of its own).
    /// </summary>
    [Fact]
    public void Typing_a_shorthand_for_the_date_already_held_still_echoes_canonically()
    {
        var (company, storage, tempDir) = NewCompany();
        try
        {
            var entry = NewVoucherEntry(company, storage);
            entry.DateText = "03-Apr-2024";
            Assert.Equal(new DateOnly(2024, 4, 3), entry.Date);

            var notifications = RecordNotifications(entry);
            entry.DateText = "3/4/2024";        // same date, different spelling ⇒ Date does NOT change

            Assert.Contains(nameof(VoucherEntryViewModel.DateText), notifications);
            Assert.Equal("03-Apr-2024", entry.DateText);
        }
        finally { Cleanup(tempDir); }
    }

    /// <summary>
    /// The whole point, end to end on the highest-value field in the app: a day-first date typed into the main
    /// accounting voucher posts as the day-first date.
    /// </summary>
    [Fact]
    public void The_main_voucher_date_reads_day_first_not_month_first()
    {
        var (company, storage, tempDir) = NewCompany();
        try
        {
            var entry = NewVoucherEntry(company, storage);
            entry.DateText = "03/04/2024";

            Assert.Equal(4, entry.Date.Month);                       // April
            Assert.Equal(3, entry.Date.Day);
            Assert.NotEqual(new DateOnly(2024, 3, 4), entry.Date);   // NOT the MM/dd misread
        }
        finally { Cleanup(tempDir); }
    }

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
