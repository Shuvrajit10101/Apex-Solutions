using System;
using System.Data.Common;
using System.IO;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The ONE report-vs-crash decision a master screen makes when persisting the shared <c>Company</c> aggregate
/// fails, and the one place its list of recognised failures is written (W0-13, slice S2b).
///
/// <para><b>Why a shared home rather than a fourth private copy.</b> <c>GstConfigViewModel</c> earned this list
/// on the F11 statutory screen; the Budget, Salary-Details and Pay-Head masters need exactly the same decision.
/// Four copies of a six-type list is precisely the divergence this campaign exists to remove — the same argument
/// <see cref="StorableAmount"/> makes for the sub-paisa predicate — so the list lives here and
/// <c>GstConfigViewModel.IsReportableSaveFailure</c> now delegates to it, keeping its eighteen call sites and its
/// own doc comment intact.</para>
///
/// <para><b>Why this exists at all rather than an inline <c>when (ex is InvalidOperationException or
/// ArgumentException)</c> filter</b> — the shape all three of this slice's screens shipped. That filter decided
/// two unrelated things at once: whether to show a message AND whether the rollback ran. Ordinary operational
/// failures fall outside it and so silently skipped the rollback, or crashed the app out of a Ctrl+A keystroke.
/// The two concerns must stay separated: <b>the caller restores unconditionally, and only the message-vs-crash
/// decision consults this predicate.</b></para>
/// </summary>
internal static class SaveFailure
{
    /// <summary>
    /// Whether a failure raised while mutating-and-persisting the company is one a screen reports as a message
    /// rather than letting it crash the app.
    ///
    /// <para>The three types beyond the domain's own refusals are the ones the narrow inline filter missed:
    /// <see cref="DbException"/> (<c>SqliteException</c> — SQLITE_BUSY from a second instance holding the write
    /// lock, SQLITE_READONLY, SQLITE_FULL; <c>SqliteCompanyStore</c> contains no catch blocks of its own, so
    /// these propagate raw), <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> from opening
    /// the company file, and <see cref="OverflowException"/> from an amount past what INTEGER paisa can carry —
    /// an <see cref="ArithmeticException"/>, not an <see cref="ArgumentException"/>.</para>
    ///
    /// <para>Anything NOT on this list — a <see cref="NullReferenceException"/> from a corrupted aggregate, say —
    /// is a defect, not an operational failure, and must reach the caller rather than be dressed up as a
    /// friendly message. The rollback still runs first.</para>
    /// </summary>
    public static bool IsReportable(Exception ex) =>
        ex is InvalidOperationException      // the domain's own refusals, and the paisa store's sub-paisa throw
           or ArgumentException              // incl. ArgumentOutOfRangeException from a domain constructor
           or OverflowException              // an amount beyond long paisa / decimal range
           or IOException                    // the company file could not be opened / written
           or UnauthorizedAccessException    // …or is read-only for this user
           or DbException;                   // SqliteException: BUSY (another instance), READONLY, FULL
}
