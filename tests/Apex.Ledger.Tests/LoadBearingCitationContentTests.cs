using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Content-matching for the handful of citations that carry an argument.</b>
///
/// <para><b>Why this exists.</b> <c>DocumentCodeAgreementTests</c> proves a <c>file.ext:NN</c> citation is not
/// dangling and not past EOF. It states its own limit outright: it "CANNOT tell you that <c>Foo.cs:172</c> still
/// points at the method the sentence is about". That gap is not theoretical — the W0-2a review found <b>a dozen
/// drifted print-path citations in <c>docs/w0-2-company-screen-grounding.md</c> and one inside the R12 user gate
/// in <c>plan.md</c></b>, all of them green under the reach check, because the files are long enough that a wrong
/// line is still a valid line. One of them was the single citation demonstrating that the printer ignores
/// <c>Company.State</c> — the evidence the user is being asked to rule on.</para>
///
/// <para><b>What this adds, and deliberately no more.</b> A small table of citations that are <i>load-bearing</i>
/// — each one is the sole evidence for a design ruling or a user gate. For each, the test locates the citation in
/// the document <b>by an adjacent context phrase</b> (never by a hard-coded line number, which would itself
/// drift), reads the line it points at in the CODE, and asserts that line contains a required token. Re-anchor a
/// citation correctly and this test follows it; re-anchor it to the wrong place and this test goes red.</para>
///
/// <para><b>Scope honesty.</b> This covers <see cref="Anchors"/> and nothing else — a few citations out of
/// thousands. It is a targeted guard for the claims that decide behaviour, not general coverage. Citations
/// outside the table remain protected only by the reach check.</para>
///
/// <para>⚠️ <b>And a scope lesson, recorded 2026-08-16 (owed review of WF-1, lens 3 finding 11).</b> This table
/// shipped in <c>e49b88e</c> with seven anchors, <b>every one of them W0-2a</b> — the half of that commit that
/// HAD been reviewed. The other half, WF-1, got none, and it was WF-1's register row that had silently gone
/// false. <b>A tool built because a check has a blind spot must be pointed at the code nobody has read yet, not
/// at the code that was just reviewed.</b> Four WF-1 anchors were added on that basis.</para>
///
/// <para>🔴 <b>The same lesson, a third time — T0-11 / Phase 10.13, 2026-08-20.</b> That chain ADDED LINES to
/// <c>VoucherPrintProjector.cs</c> and <c>GstReportSupport.cs</c>, and its own slice-S2 pass reported that roughly
/// <b>sixteen of the twenty citations it remapped had already gone silently false</b> — every one of them green
/// under the reach check, because a file long enough makes a wrong line a valid line. Fourteen T0-11 anchors were
/// added on that basis, spanning the census gap-register row, ADR-0002 and RQ-11/11a/11b. <b>This does not fix the
/// mechanism</b>, which is a design item: adding a line to either of those files still falsifies roughly thirty
/// citations with no signal at all. It content-checks the claims <i>this</i> chain made load-bearing, and leaves
/// the rest reach-only — which is the honest description of the guard, not an apology for it.</para>
/// </summary>
public sealed class LoadBearingCitationContentTests
{
    /// <param name="Document">Repo-relative document carrying the citation.</param>
    /// <param name="ContextPhrase">Text on the SAME document line as the citation — how the citation is found.</param>
    /// <param name="CitedFile">The file name the citation must name (e.g. <c>VoucherPrintProjector.cs</c>).</param>
    /// <param name="RequiredToken">Text that must appear within the cited line range in that file.</param>
    private sealed record Anchor(string Document, string ContextPhrase, string CitedFile, string RequiredToken);

    private static readonly Anchor[] Anchors =
    {
        // ---- the R12 user gate's evidence: the printer reads the GST State, never the postal one ----
        // 🔴 The context phrase used to CONTAIN THE CITED LINE NUMBER, which broke this table's own rule ("never
        // by a hard-coded line number, which would itself drift"). Phase 10.11 S3 added three lines to
        // VoucherPrintProjector above this citation; re-pointing the citation — the correct fix — then made the
        // anchor unfindable, and the guard went dark on the very gate it exists to protect. The phrase is now
        // number-free, so a future re-point moves the citation and this anchor still follows it.
        new("plan.md",
            "GST one** — `src/Apex.Desktop/Services/VoucherPrintProjector",
            "VoucherPrintProjector.cs",
            "StateText(company.Gst?.HomeStateCode)"),

        // ---- the correction that overturned "Company.State goes nowhere": the canonical round-trip carries it ----
        new("plan.md",
            "export–import round-trip**",
            "CanonicalMapper.cs",
            "State = c.State"),
        new("plan.md",
            "(read), `ImportPlan.cs:1198-1199` (assign)",
            "ImportPlan.cs",
            "t.State = c.State"),

        // ---- W0-2a's ER-13 guard: the reason every existing book still prints a blank supplier block ----
        new("plan.md",
            "**▶ The load-bearing guard — `SupplierPostalAddressText`",
            "VoucherPrintProjector.cs",
            "IsNullOrWhiteSpace(company.Address)"),

        // ---- the grounding doc's headline finding, after W0-2a made half of it false ----
        new("docs/w0-2-company-screen-grounding.md",
            "**changed by W0-2a.**",
            "VoucherPrintProjector.cs",
            "SplitAddress(SupplierPostalAddressText(company))"),
        new("docs/w0-2-company-screen-grounding.md",
            "**unchanged; still never `company.State`.**",
            "VoucherPrintProjector.cs",
            "StateText(company.Gst?.HomeStateCode)"),
        new("docs/w0-2-company-screen-grounding.md",
            "the party State rides on",
            "CanonicalXml.cs",
            "No <c>state</c> attribute"),

        // ---- WF-1 (slice S4): the three citations the owed review's findings rest on ----
        // Added 2026-08-16. The table shipped with SEVEN anchors, ALL of them W0-2a: the tool built BECAUSE
        // reach-only checks have a blind spot was pointed exclusively at the REVIEWED half of the commit, while
        // the UNREVIEWED half was the one whose register row had gone false (lens 3 finding 11).

        // The migration's back-fill — the statement the whole fresh/upgraded split and R12 decision 1 rest on.
        new("plan.md",
            "the back-fill `UPDATE` is",
            "Schema.cs",
            "UPDATE companies SET gst_source_of_hsn_sac = 1, gst_source_of_rate = 1;"),

        // The fix for the defect that erased that back-fill: the writer must PRESERVE, never default.
        new("plan.md",
            "The fix is the writer's three-way fallback",
            "SqliteCompanyStore.cs",
            "storedSourceOrders?.Hsn"),

        // The downgrade's index replay — without it every migration fixture runs against a database missing
        // two indexes a real book of that age would have.
        new("plan.md",
            "The index replay is",
            "SchemaDowngrade.cs",
            "foreach (var sql in indexes)"),

        // The schema version itself. memory.md's WF-1 entry is the record a new session reads first, and it was
        // absent altogether until the review (lens 3 finding 6); this keeps its headline number honest.
        new("memory.md",
            "SCHEMA v50 → 51, AND NOTHING READS IT",
            "Schema.cs",
            "CurrentVersion = 51"),

        // ---- W0-2b (the company profile screen): the two claims its register row rests on ----
        // Added 2026-08-16, following this file's own lesson: point the tool at the code nobody has read yet.
        // Both of these are statements the census makes about behaviour, and both are one edit away from
        // becoming false without any other test noticing.

        // The desktop layer's single validation floor. The census row claims the write half is "safe" BECAUSE
        // this call exists; delete it and the claim is false while the reach check stays green.
        new("docs/full-clone-census.md",
            "**The floor that made the write half safe - `CompanyStorage.cs:128`**",
            "CompanyStorage.cs",
            "company.EnsureValid()"),

        // The inheritance is a DISPLAY default, and the `??=` is what makes it one. Turning it into `=` would
        // let a postal field silently overwrite a GST registration — the wrong-tax-head class — and the census
        // sentence describing it would quietly become a lie.
        new("docs/full-clone-census.md",
            "not a stamp - `GstConfigViewModel.cs:583`",
            "GstConfigViewModel.cs",
            "HomeState ??="),

        // ---- T0-11 / Phase 10.13 (the printed-document three-axis split): the claims this chain made load-bearing ----
        // Added 2026-08-20, following this file's own lesson a third time: point the tool at the code nobody has
        // read yet. This chain ADDED LINES to `VoucherPrintProjector.cs` and `GstReportSupport.cs`, and slice S2
        // reported that roughly SIXTEEN OF TWENTY citations it remapped had gone silently false first — every one of
        // them green under the reach check, because a long file makes a wrong line a valid line. The rows below are
        // the T0-11 citations that are the sole evidence for a design ruling: the diagnosis that overturned the
        // census row's stated cause, the three-consumer hazard that makes the naive fix DANGEROUS rather than merely
        // wrong (it moves a docType we file with a government portal), and the two facts that decouple the Rule-53
        // note from T0-10. Each was read at the cited line before being entered here.

        // ---- census gap-register row T0-11 (the re-caused row itself) ----
        // One document line carries all three of these citations; the regex takes the FIRST occurrence per file
        // name, which is why `VoucherPrintProjector.cs` is deliberately NOT anchored on this row: its first
        // occurrence there is the STALE `:48` the row exists to disown.
        new("docs/full-clone-census.md",
            "Three consumers move together",
            "GstReportSupport.cs",
            "if (type?.BaseType != VoucherBaseType.Sales) return false;"),
        new("docs/full-clone-census.md",
            "Three consumers move together",
            "EWayBillService.cs",
            "IsBillOfSupplyForFiling"),
        new("docs/full-clone-census.md",
            "Three consumers move together",
            "VoucherDetailViewModel.cs",
            "BuildPrintPreview"),

        // ---- ADR-0002: the diagnosis, the hazard and the two decoupling facts ----
        // The rule is Sales-only and it lives HERE, not at the wrapper — the correction that re-caused the row.
        new("docs/adr/0002-printed-document-three-axis-split.md",
            "returns false for anything whose base type is not Sales",
            "GstReportSupport.cs",
            "if (type?.BaseType != VoucherBaseType.Sales) return false;"),
        // The defect is the CALL SITE. If this citation ever stops landing on `BuildPrintPreview`, the ADR's whole
        // "predicate is right, call site is wrong" argument loses the only line it rests on.
        new("docs/adr/0002-printed-document-three-axis-split.md",
            "takes the else branch",
            "VoucherDetailViewModel.cs",
            "BuildPrintPreview"),
        // Consumer 2 of the three-consumer hazard — the ADR quotes this line VERBATIM, so the quote is the token.
        new("docs/adr/0002-printed-document-three-axis-split.md",
            "(`if (!IsTaxInvoice(company, voucher)) return false;`);",
            "GstReportSupport.cs",
            "if (!IsTaxInvoice(company, voucher)) return false;"),
        // Consumer 3 — the NIC e-Way `docType`. This is the citation that turns "wrong" into "dangerous": it is the
        // evidence that widening the predicate would move a code filed with a government portal.
        new("docs/adr/0002-printed-document-three-axis-split.md",
            "feeds `EWayBillService.PartACodesFor` at",
            "GstReportSupport.cs",
            "IsBillOfSupplyForFiling"),
        // The ruling that our voucher number may not be captioned "Invoice No." rests on the supplier's number
        // already having somewhere to go. Delete that branch and the ruling silently loses its premise.
        new("docs/adr/0002-printed-document-three-axis-split.md",
            @"**already returns *""Supplier Invoice",
            "VoucherPrintProjector.cs",
            @"""Supplier Invoice No."""),
        // The two facts that decouple the Rule-53 note from census T0-10 — i.e. that let a legally complete note
        // ship without the stock wall moving. Both were re-measured first-hand before the re-attribution.
        new("docs/adr/0002-printed-document-three-axis-split.md",
            "throws on every post (reached from",
            "VoucherValidator.cs",
            "only valid on a Purchase or Sales voucher"),
        new("docs/adr/0002-printed-document-three-axis-split.md",
            "makes the item-invoice chord inert. Because",
            "VoucherEntryViewModel.cs",
            "VoucherBaseType.Purchase or VoucherBaseType.Sales"),

        // ---- RQ-11 / RQ-11a / RQ-11b (the requirement this chain amended IN PLACE) ----
        // RQ-11a's caption ruling, and the amendment record that says the CODE was right and the REQUIREMENT wrong.
        new("docs/phase5-reports-io-requirements.md",
            "**our** voucher number SHALL carry a caption",
            "VoucherPrintProjector.cs",
            @"""Supplier Invoice No."""),
        new("docs/phase5-reports-io-requirements.md",
            "returns false unless the base type is Sales, and",
            "GstReportSupport.cs",
            "if (type?.BaseType != VoucherBaseType.Sales) return false;"),
        new("docs/phase5-reports-io-requirements.md",
            "A purchase item-invoice",
            "VoucherDetailViewModel.cs",
            "BuildPrintPreview"),
        // RQ-11b's no-dependency-on-T0-10 clause quotes the throw verbatim; the quote is the token.
        new("docs/phase5-reports-io-requirements.md",
            @"throws *""Item-invoice stock lines are only valid on a",
            "VoucherValidator.cs",
            "only valid on a Purchase or Sales voucher"),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string[] ReadLines(string relative) =>
        File.ReadAllLines(Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string ResolveCodePath(string fileName)
    {
        // Filter on the REPO-RELATIVE path. Filtering the absolute one excluded every file in this checkout,
        // because the worktree itself lives under a dot-directory (`…/.claude/worktrees/…`).
        var root = RepoRoot();
        var matches = Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(root, p).Replace('\\', '/');
                return !rel.Split('/').Any(seg => seg is "bin" or "obj" or "node_modules" || seg.StartsWith('.'));
            })
            .ToList();

        Assert.True(matches.Count == 1,
            $"'{fileName}' resolves to {matches.Count} files; the anchor table needs an unambiguous name.");
        return matches[0];
    }

    [Fact]
    public void Every_load_bearing_citation_points_at_a_line_that_still_says_what_the_document_claims()
    {
        var failures = new List<string>();

        foreach (var a in Anchors)
        {
            var docLines = ReadLines(a.Document);

            // 1. Find the document line carrying the context phrase.
            var hits = docLines
                .Select((text, i) => (text, lineNo: i + 1))
                .Where(x => x.text.Contains(a.ContextPhrase, StringComparison.Ordinal))
                .ToList();

            if (hits.Count == 0)
            {
                failures.Add($"{a.Document}: context phrase not found — \"{a.ContextPhrase}\". "
                           + "The prose was reworded; update the anchor table so the guard keeps biting.");
                continue;
            }

            // 2. Extract the citation to CitedFile from that line (or the line before, for wrapped prose).
            var escaped = Regex.Escape(a.CitedFile);
            var rx = new Regex(escaped + @":(\d+)(?:-(\d+))?");

            Match? m = null;
            foreach (var hit in hits)
            {
                foreach (var candidate in new[] { hit.text, hit.lineNo >= 2 ? docLines[hit.lineNo - 2] : "" })
                {
                    var found = rx.Match(candidate);
                    if (found.Success) { m = found; break; }
                }
                if (m is not null) break;
            }

            if (m is null)
            {
                failures.Add($"{a.Document}: found \"{a.ContextPhrase}\" but no `{a.CitedFile}:NN` citation on "
                           + "that line or the one above it. The citation was removed or renamed.");
                continue;
            }

            var start = int.Parse(m.Groups[1].Value);
            var end = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : start;

            // 3. The cited range in the CODE must contain the required token.
            var codeLines = File.ReadAllLines(ResolveCodePath(a.CitedFile));
            if (start < 1 || end > codeLines.Length)
            {
                failures.Add($"{a.Document} → {a.CitedFile}:{start}-{end} is outside the file (1-{codeLines.Length}).");
                continue;
            }

            var slice = string.Join("\n", codeLines[(start - 1)..end]);
            if (!slice.Contains(a.RequiredToken, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{a.Document} cites {a.CitedFile}:{start}"
                  + (end == start ? "" : $"-{end}")
                  + $" for \"{a.ContextPhrase}\", but that line does NOT contain \"{a.RequiredToken}\".\n"
                  + $"      It actually reads: {codeLines[start - 1].Trim()}");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} load-bearing citation(s) have drifted off the code they describe. These are the "
          + "citations that are the sole evidence for a design ruling or a user gate, so a wrong one misleads a "
          + "decision rather than merely annoying a reader:\n  - " + string.Join("\n  - ", failures));
    }

    /// <summary>
    /// Non-vacuity: the table is non-empty, every document in it exists, and every context phrase is actually
    /// present. Without this, deleting a row (or a typo in a phrase) would silently reduce the guard to nothing
    /// while staying green.
    /// </summary>
    [Fact]
    public void The_anchor_table_is_non_empty_and_every_row_resolves()
    {
        Assert.True(Anchors.Length >= 27, $"anchor table has shrunk to {Anchors.Length} rows");

        foreach (var a in Anchors)
        {
            var docPath = Path.Combine(RepoRoot(), a.Document.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(docPath), $"{a.Document} does not exist");
            Assert.Contains(a.ContextPhrase, File.ReadAllText(docPath), StringComparison.Ordinal);
            Assert.True(File.Exists(ResolveCodePath(a.CitedFile)), $"{a.CitedFile} does not resolve");
        }
    }
}
