using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// Implemented by every ENTRY screen that owns a working/voucher date (WI-5, part 4c). It is what makes
/// <b>F2 — Date</b> work "in whatever window": the shell asks the active page for its working-date field and
/// routes F2 to it, instead of the old stub that merely printed the (never-updated) financial-year start to
/// the status line.
/// <para>
/// The contract is deliberately TEXT, not <c>DateOnly</c>: F2 is a keyboard action that puts the caret in the
/// date field so the operator types the date — the app has <b>zero DatePicker controls by design</b> and F2
/// must not open one. The typed text is read by the one shared day-first parser
/// (<see cref="ApexDate.TryParse(string?, System.DateOnly, out System.DateOnly)"/>) and echoed back in the one
/// canonical <see cref="ApexDate.Canonical"/> spelling.
/// </para>
/// </summary>
public interface ISetsWorkingDate
{
    /// <summary>
    /// The screen's working-date field as text. Reading gives the canonical rendering; assigning routes
    /// through the shared lenient day-first parser, so an unparseable value is rejected (and surfaced)
    /// rather than silently discarded.
    /// </summary>
    string WorkingDateText { get; set; }
}
