using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Two defects that shipped BEHIND a confident false comment — pinned so they cannot come back.</b>
///
/// <para>🔴 <b>Why a comment gets a test at all.</b> Both defects here reached <c>main</c> past a fully green
/// four-project gate, and the review that found them named the mechanism: a reader who trusts a confident comment
/// stops checking the code under it. One comment described an unreadable-cell guard in a class that has no such
/// guard; the other was inserted BETWEEN an existing summary and the member it documented, so a drift-locked
/// invariant's explanation came to name the wrong member. Neither is visible to any behavioural test, which is
/// precisely why they are asserted against the SOURCE.</para>
///
/// <para><b>These scanners read source text, so they must find the repository.</b> They walk up from the test
/// binary for <c>Apex.slnx</c>, the same locator <c>ExportFolderDefaultTests</c> uses. 🔴 Building with
/// <c>--artifacts-path</c> outside the repository breaks that walk and fails these tests spuriously, along with
/// every other document scanner in the solution; build in place.</para>
/// </summary>
public sealed class FalseDocCommentTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// 🔴 <b>F5 — a class may not describe an unreadable-cash-cell mechanism it does not have.</b>
    ///
    /// <para><b>The deleted claim.</b> <c>ElectronicLedgersReportViewModel</c>'s snapshot doc asserted that its
    /// cash rows "carry the constant <c>not read</c> when a cell could not be read" and that the string rides
    /// through the <c>Closing / Balance</c> column unchanged. Both halves were false of that class: the mechanism
    /// belongs to <c>Drc03PaymentViewModel</c>, which owns the constant, the reader seam and a
    /// <c>CashReadFailed</c> flag. The electronic-ledgers view model reads
    /// <c>ElectronicLedgersView.CashCells</c>, whose every value is a <c>Money</c> that
    /// <c>IndianFormat.AmountAlways</c> always formats — there is no failure path to describe, and the ONLY
    /// occurrence of the string anywhere in the file was inside the comment itself.</para>
    ///
    /// <para><b>The rule, stated generally rather than as one file's grudge.</b> Any view model that NAMES the
    /// unreadable-cash constant must actually own the machinery: the constant and a read-failure flag. That makes
    /// the assertion survive the comment being reworded, moved, or re-added to a third class.</para>
    /// </summary>
    [Fact]
    public void No_view_model_claims_the_unreadable_cash_constant_without_owning_the_machinery()
    {
        string dir = Path.Combine(RepoRoot(), "src", "Apex.Desktop", "ViewModels");
        Assert.True(Directory.Exists(dir), dir);

        var offenders = new List<string>();
        foreach (string path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(path);

            // The claim: this file talks about the unreadable-cash constant, by name or by its literal value.
            bool claims = text.Contains("CashUnreadable", StringComparison.Ordinal)
                       || text.Contains("\"not read\"", StringComparison.Ordinal)
                       || text.Contains("<c>not read</c>", StringComparison.Ordinal);
            if (!claims) continue;

            // The machinery the claim requires: the constant declared here, and a read-failure signal.
            bool owns = text.Contains("CashUnreadable =", StringComparison.Ordinal)
                     && text.Contains("CashReadFailed", StringComparison.Ordinal);
            if (!owns) offenders.Add(Path.GetFileName(path));
        }

        Assert.True(offenders.Count == 0,
            "These view models describe the unreadable-cash-cell mechanism but do not own it (no CashUnreadable "
            + "constant and/or no CashReadFailed flag). A comment describing machinery that does not exist is the "
            + "mechanism two defects of wave 33 shipped behind — DELETE the claim, do not soften it:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 🔴 <b>F8 — a doc comment may not be orphaned from its member by a new one inserted above it.</b>
    ///
    /// <para><b>The defect.</b> <c>ItcSetOffReportViewModel</c>'s new census-6.19 XML block was inserted BETWEEN
    /// the existing <c>&lt;summary&gt;</c> for <c>private static long P(Money)</c> and <c>P</c> itself. The result
    /// is two consecutive <c>&lt;summary&gt;</c> elements on one member: the compiler keeps the last, so
    /// <c>ToMasterListSnapshot</c> carried two and <c>P</c> silently lost its doc. <c>P</c> is the rupees-to-paisa
    /// rule, a DRIFT-LOCKED invariant — its explanation came to name the wrong member, which is the worst place
    /// for this to happen because the next reader looks for exactly that sentence.</para>
    ///
    /// <para>🔴 <b>THE SIGNATURE IS MECHANICAL, WHICH IS WHY IT IS WORTH A SCANNER.</b> A doc line that CLOSES a
    /// summary, immediately followed by a doc line that OPENS one, is always this shape and never intentional. It
    /// is easy to introduce and invisible at review: it was introduced ONCE MORE while fixing F8 on this branch,
    /// and an earlier draft of this test is what caught that.</para>
    ///
    /// <para>🔴 <b>AND THE FIRST DRAFT OF THIS SCANNER MISSED THE REAL DEFECT — recorded because it is the trap.</b>
    /// It required <c>&lt;/summary&gt;</c> to be ALONE on its line. F8's actual shape is a one-line or two-line
    /// summary whose closing tag sits at the END of a prose line, so the original defect would have walked straight
    /// past it: a mutation re-introducing F8 verbatim SURVIVED. The test now asks whether the previous doc line
    /// CONTAINS the closing tag, wherever on the line it falls, and that mutation is killed. A scanner that cannot
    /// see the defect it was written for is worse than none, because it reads as coverage.</para>
    ///
    /// <para><b>The allow-list is DEBT, recorded rather than hidden.</b> <b>18</b> sites of this exact shape
    /// already existed on <c>main</c> at 284f588 — the corrected detector finds six more than the first draft did,
    /// across eight files and all three test projects. Re-homing eighteen doc comments is real work with a real
    /// risk of attaching the wrong prose to the wrong member, and it is outside this remediation's named findings —
    /// so it is REPORTED as a follow-up and listed here. What the allow-list buys is that no NINETEENTH site can
    /// appear. Removing an entry is always correct; adding one needs a reason.</para>
    /// </summary>
    [Fact]
    public void No_new_doc_comment_is_orphaned_from_its_member_by_a_doubled_summary()
    {
        // Pre-existing on main at 284f588, outside this remediation's findings. See the remarks: this is debt.
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/Apex.Desktop/ViewModels/GatewayColumn.cs:488",
            "src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:2190",
            "src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:2497",
            "src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:6370",
            "src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:6513",
            "src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:6546",
            "src/Apex.Desktop/ViewModels/MainWindowViewModel.cs:8849",
            "src/Apex.Desktop/ViewModels/PosBillingViewModel.cs:724",
            "src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:4699",
            "src/Apex.Desktop/ViewModels/VoucherEntryViewModel.cs:6550",
            "src/Apex.Desktop/ViewModels/VoucherTypeMasterViewModel.cs:15",
            "src/Apex.Ledger/Services/GroupService.cs:32",
            "src/Apex.Ledger/Services/InventoryPostingService.cs:297",
            "src/Apex.Ledger/Services/LedgerService.cs:516",
            "tests/Apex.Ledger.Tests/CompanyImportRoundTripTests.cs:669",
            "tests/Apex.Ledger.Tests/MasterVerbsW29DeletionGuardTests.cs:367",
            "tests/Apex.Ledger.Tests/PayrollStatutoryFormsTests.cs:758",
            "tests/Apex.Persistence.Sqlite.Tests/GstSetOffSchemaTests.cs:272",
        };

        string root = RepoRoot();
        var offenders = new List<string>();

        foreach (string area in new[] { "src", "tests" })
        {
            string areaDir = Path.Combine(root, area);
            if (!Directory.Exists(areaDir)) continue;

            foreach (string path in Directory.EnumerateFiles(areaDir, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal))
                    continue;

                var lines = File.ReadAllLines(path);
                for (int i = 1; i < lines.Length; i++)
                {
                    if (!OpensSummary(lines[i])) continue;
                    if (!ClosesSummary(lines[i - 1])) continue;

                    string site = $"{rel}:{i + 1}";      // 1-indexed, naming the ORPHANING <summary> line
                    if (!allowed.Contains(site)) offenders.Add(site);
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A '/// <summary>' immediately follows a '/// </summary>', so the doc block ABOVE it has been orphaned "
            + "from the member it describes — the compiler keeps only the last summary. Move the new block above "
            + "the existing one, or put it on its own member (this is finding F8 of wave 33):\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>A doc-comment line that OPENS a summary element — the tag must start the line's content, since a
    /// <c>&lt;summary&gt;</c> further in would be prose about the tag rather than a new block.</summary>
    private static bool OpensSummary(string line)
        => line.Trim() is { } t
           && t.StartsWith("///", StringComparison.Ordinal)
           && t[3..].TrimStart().StartsWith("<summary>", StringComparison.Ordinal);

    /// <summary>A doc-comment line that CLOSES a summary element ANYWHERE on the line. 🔴 The "anywhere" is the
    /// whole correction: F8's real shape ended its summary at the tail of a prose line, and a version of this
    /// helper that demanded the tag be alone on its line let a verbatim re-introduction of F8 pass.</summary>
    private static bool ClosesSummary(string line)
        => line.Trim() is { } t
           && t.StartsWith("///", StringComparison.Ordinal)
           && t.Contains("</summary>", StringComparison.Ordinal);

    /// <summary>
    /// 🔴 <b>F8, the other half: the drift-locked rupees-to-paisa rule is documented ON its own member.</b> The
    /// scanner above proves no doc block is orphaned; this proves the specific sentence that was orphaned is back
    /// where it belongs. It is asserted separately because the general shape could be satisfied by DELETING the
    /// explanation instead of re-homing it, and this invariant's explanation is the thing worth keeping.
    /// </summary>
    [Fact]
    public void The_paisa_conversion_helper_carries_its_drift_lock_explanation()
    {
        string path = Path.Combine(
            RepoRoot(), "src", "Apex.Desktop", "ViewModels", "ItcSetOffReportViewModel.cs");
        var lines = File.ReadAllLines(path);

        int p = Array.FindIndex(lines, l => l.Contains("private static long P(Money m)", StringComparison.Ordinal));
        Assert.True(p > 0, "ItcSetOffReportViewModel must still declare the paisa helper P(Money).");

        // Walk the contiguous doc-comment block immediately above the declaration.
        var doc = new List<string>();
        for (int i = p - 1; i >= 0 && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal); i--)
            doc.Add(lines[i]);
        string block = string.Join("\n", doc);

        Assert.Contains("drift lock D3", block, StringComparison.Ordinal);
        Assert.Contains("ToPaisaRounded", block, StringComparison.Ordinal);
    }
}
