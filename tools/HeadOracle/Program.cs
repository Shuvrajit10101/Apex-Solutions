using System.Globalization;
using System.Text;
using HeadOracle;

// ---------------------------------------------------------------------------------------------------
// HEAD-ORACLE RUNNER — top-level statements, NO namespace.
//
// A namespace beginning with "Apex." would make the bare identifier `Ledger` ambiguous, because
// Apex.Ledger is a namespace AND Apex.Ledger.Domain.Ledger is a type. Global namespace sidesteps it.
//
// Modes:
//   emit    <out.tsv>
//   compare <head.tsv> <live.tsv> <report.txt> <headTreeSha> <liveTreeSha> <workTreeSha>
//
// Exit codes:  0 = clean
//              1 = ENGINE REJECTED   (the change under test is not acceptable)
//              2 = usage / IO error
//              3 = HARNESS BROKEN    (the oracle cannot be trusted; fix the harness, judge nothing)
// ---------------------------------------------------------------------------------------------------

if (args.Length == 0) return Usage();

switch (args[0])
{
    case "emit":
        if (args.Length < 2) return Usage();
        return Emit(args[1]);
    case "compare":
        if (args.Length < 7) return Usage();
        return Compare(args[1], args[2], args[3], args[4], args[5], args[6]);
    default:
        return Usage();
}

static int Usage()
{
    Console.Error.WriteLine("usage: OracleRunner emit <out.tsv>");
    Console.Error.WriteLine("       OracleRunner compare <head.tsv> <live.tsv> <report.txt> <headTreeSha> <liveTreeSha> <workTreeSha>");
    return 2;
}

// =============================================================== EMIT

static int Emit(string outPath)
{
    var rows = new List<string[]>();
    void Row(string scenario, string item, string method, string asOf, string measure, string value)
        => rows.Add([scenario, item, method, asOf, measure, value]);

    foreach (var s in Corpus.Scenarios)
    {
        // The SPEC-DERIVED never-negative predicate. Byte identity (check 1) and calibration (check 4) are
        // scoped by THIS, not by "the scenario id starts with the letter N" — which left E1, a genuinely
        // never-negative family, outside both.
        Row(s.Id, "-", "-", "-", "FactNeverNegative", Reference.NeverNegative(s) ? "1" : "0");

        // ---------- spec-derived, engine-INDEPENDENT facts, PER ITEM and PER AS-OF DATE.
        foreach (var asOf in s.AsOfDates)
        {
            var d = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            foreach (var item in s.Items)
            {
                EmitFacts(Row, s.Id, item.Name, d, Facts.For(s, item, asOf));
                // THE SPEC'S LOT TABLE — the other half of the value invariant's rate binding. Derived by
                // Facts' own walk of the spec, never by Reference.BuildStack (the arithmetic under audit).
                Row(s.Id, item.Name, "-", d, "FactInwardLots", Facts.InwardLots(s, item, asOf));
                // THE ORDERING FACT — the tokens a surviving layer may name, from a PURE QUANTITY walk.
                // "*" means the book never ran dry, so the rule constrains nothing. This is the assertion
                // audit #3 asked for and audit #4 recorded as not built: it is the only thing that kills a
                // poison which resurrects consumed units and binds every layer TRUTHFULLY to a real lot at
                // that lot's real rate — which passes the entire rate binding 0/0/0/0.
                Row(s.Id, item.Name, "-", d, "FactPostDryLots", Facts.PostDryLots(s, item, asOf));
                // ROUND 10 — the quantity the ITEM-LEVEL cost-layer replay reaches (layers - debt),
                // derived by Facts' own gated quantity walk. The reference's layer stack is measured
                // against THIS, not against the reported closing quantity, because the reported
                // quantity is replayed PER (item, godown, batch) and the two genuinely part company
                // on a multi-key book carrying a physical count. That desync is a real engine
                // property; it is reported below as a measured delta instead of being swallowed.
                Row(s.Id, item.Name, "-", d, "FactFlatNetMicro", Facts.FlatNetMicro(s, item, asOf).ToString(CultureInfo.InvariantCulture));

                // THE INVENTED POPULATION, DERIVED FROM THE SPEC (audit #5 finding [3]). CHECK 4c's
                // coverage half used to iterate RefProvenance — a tag the reference emits ABOUT ITSELF —
                // so a PARTIAL retag could quietly shrink the population the harness claims to have pinned.
                // This row is a pure QUANTITY walk; the comparator asserts the emitted tags equal it.
                Row(s.Id, item.Name, "-", d, "FactInventedByRule", Facts.InventedByRule(s, item, asOf) ? "1" : "0");

                // THE DEBT-SHAPE FACT (audit #6, LOW [1]). CHECK 4c's clause coverage was asserted from
                // the golden table's OWN Clause labels — a projection of the thing under audit — so a
                // right number under a wrong label reported coverage a clause never actually had. This
                // row is another pure QUANTITY walk; the comparator requires each golden's label to be
                // TRUE of its subject and fails the HARNESS when it is not.
                Row(s.Id, item.Name, "-", d, "FactDebtShape", Facts.DebtShape(s, item, asOf));

                // ROUND 11 — THE SINGLE-KEY PREDICATE, the debt rule's whole scope. Spec-derived and
                // IDENTITY-ONLY: does every event of this item's as-of-scoped stream sit on one and the
                // same (godown, batch) key? CHECK 1M scopes byte identity by its COMPLEMENT — and does so
                // ENGINE AGAINST ENGINE, so a mistake in this predicate cannot make CHECK 1M pass.
                Row(s.Id, item.Name, "-", d, "FactSingleKey", Reference.SingleKeyFact(s, item, asOf) ? "1" : "0");
            }

            // The company-wide aggregate the TotalClosingStockValue row is judged against (audit H3).
            EmitFacts(Row, s.Id, "-", d, Facts.Aggregate(s, asOf));
        }

        // ---------- THE POINT ORACLE. Computed from the spec, so it is identical on both arms; that is
        // itself asserted (corpus integrity), which is how a tampered reference announces itself.
        foreach (var method in Corpus.Methods)
        {
            var mn = method.ToString();
            foreach (var asOf in s.AsOfDates)
            {
                var d = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var total = 0m;
                foreach (var item in s.Items)
                {
                    var r = Reference.Value(s, item, mn, asOf);
                    Row(s.Id, item.Name, mn, d, "RefOnHandMicro", Micro(r.OnHandBase));
                    Row(s.Id, item.Name, mn, d, "RefClosingQtyMicro", Micro(r.ClosingQty));
                    Row(s.Id, item.Name, mn, d, "RefClosingValuePaisa", Paisa(r.ClosingValueRupees));

                    // How much of the reference's authority on THIS subject was ever validated:
                    // CALIBRATED (an N* path) / BRIEF (a rule the brief states) / INVENTED (neither).
                    // AverageCost is tagged from the SAME debt flags since 2026-07-27; the blanket
                    // ECHO-OF-HEAD tag was retired (audit #4 finding [3]) because it became false when
                    // Value()'s AverageCost arm moved to RunAverageDebtAware, and it was hiding that
                    // CHECK 2's conviction on G6-001 rests on the count-with-debt rule.
                    Row(s.Id, item.Name, mn, d, "RefProvenance", Reference.Provenance(s, item, mn, asOf));

                    if (mn is "Fifo" or "Lifo")
                    {
                        Row(s.Id, item.Name, mn, d, "RefLayerQtyMicro", Micro(Reference.LayerQty(s, item, mn, asOf)));
                        // The mechanical review artefact behind the VALUE invariant (PART A). Emitted, not
                        // argued: a value-only poison of the debt branch used to pass every integrity
                        // assertion because no invariant bound the reference's VALUE to the spec.
                        Row(s.Id, item.Name, mn, d, "RefLayerBreakdown", Reference.LayerBreakdown(s, item, mn, asOf));
                        Row(s.Id, item.Name, mn, d, "RefLayerRateSources", Reference.LayerRateSources(s, item, mn, asOf));
                        // WHICH LOT each layer's units came from. The comparator looks the rate up in
                        // FactInwardLots, so an admissible-but-WRONG rate no longer sails through.
                        Row(s.Id, item.Name, mn, d, "RefLayerOrigins", Reference.LayerOrigins(s, item, mn, asOf));
                        Row(s.Id, item.Name, mn, d, "RefAdmissibleRates", Reference.AdmissibleRates(s, item, asOf));
                    }

                    // THE DEBT-AWARE AverageCost COLUMN — the one CHECK 2 issues verdicts from. Since the
                    // 2026-07-27 scope decision RefClosingValuePaisa for AverageCost is computed from the
                    // SAME function, so the two columns are IDENTICAL BY CONSTRUCTION. That is a fact about
                    // the code, not a validated agreement, and PART A says so rather than printing a gate
                    // that cannot fail (audit #4 finding [2]). What actually anchors both columns is
                    // CHECK 4c — the hand-derived goldens in Goldens.cs.
                    if (mn == "AverageCost")
                        Row(s.Id, item.Name, mn, d, "RefClosingValueDebtAwarePaisa",
                            Paisa(Reference.DebtAwareAverageValue(s, item, asOf)));

                    total += r.ClosingValueRupees;

                    foreach (var probe in s.IssueProbes)
                        Row(s.Id, item.Name, mn, d, "RefIssueValue@" + Num(probe) + "Paisa",
                            Paisa(Reference.IssueValue(s, item, mn, probe, asOf)));
                }
                Row(s.Id, "-", mn, d, "RefTotalClosingPaisa", Paisa(total));
            }
        }

        // ---------- THE ENGINE UNDER TEST.
        foreach (var method in Corpus.Methods)
        {
            var mn = method.ToString();
            Corpus.Book book;
            try
            {
                book = Corpus.Build(s, method);
            }
            catch (Exception ex)
            {
                // A build that throws is recorded, never swallowed: a change that turns a posted book
                // into a rejection must appear as a diff, not as a silently missing row.
                Row(s.Id, "-", mn, "-", "BuildOutcome", "EXC:" + ex.GetType().FullName);
                continue;
            }
            Row(s.Id, "-", mn, "-", "BuildOutcome", "OK");

            foreach (var asOf in s.AsOfDates)
            {
                var d = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                foreach (var item in s.Items)
                {
                    var id = book.ItemIds[item.Name];

                    try
                    {
                        var v = book.Valuation.ClosingValue(id, asOf);
                        Row(s.Id, item.Name, mn, d, "ClosingQtyMicro", Micro(v.Quantity));
                        Row(s.Id, item.Name, mn, d, "ClosingValuePaisa", Paisa(v.Value.Amount));
                        // Money.IsPaisaExact is its own oracle column: a non-paisa-exact value is a defect
                        // regardless of what HEAD did.
                        Row(s.Id, item.Name, mn, d, "ClosingValueIsPaisaExact", v.Value.IsPaisaExact ? "1" : "0");
                    }
                    catch (Exception ex)
                    {
                        var e = "EXC:" + ex.GetType().FullName;
                        Row(s.Id, item.Name, mn, d, "ClosingQtyMicro", e);
                        Row(s.Id, item.Name, mn, d, "ClosingValuePaisa", e);
                        Row(s.Id, item.Name, mn, d, "ClosingValueIsPaisaExact", e);
                    }

                    try { Row(s.Id, item.Name, mn, d, "OnHandMicro", Micro(book.OnHand.OnHand(id, asOf))); }
                    catch (Exception ex) { Row(s.Id, item.Name, mn, d, "OnHandMicro", "EXC:" + ex.GetType().FullName); }

                    foreach (var probe in s.IssueProbes)
                    {
                        var measure = "IssueValue@" + Num(probe) + "Paisa";
                        try { Row(s.Id, item.Name, mn, d, measure, Paisa(book.Valuation.IssueValue(id, probe, asOf).Amount)); }
                        catch (Exception ex) { Row(s.Id, item.Name, mn, d, measure, "EXC:" + ex.GetType().FullName); }
                    }
                }

                try { Row(s.Id, "-", mn, d, "TotalClosingPaisa", Paisa(book.Valuation.TotalClosingStockValue(asOf).Amount)); }
                catch (Exception ex) { Row(s.Id, "-", mn, d, "TotalClosingPaisa", "EXC:" + ex.GetType().FullName); }
            }
        }
    }

    rows.Sort(static (a, b) =>
    {
        for (var i = 0; i < 5; i++)
        {
            var c = string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return 0;
    });

    var sb = new StringBuilder();
    sb.Append("scenario\titem\tmethod\tasOf\tmeasure\tvalue\n");
    foreach (var r in rows) sb.Append(string.Join('\t', r)).Append('\n');

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"emit: {rows.Count} rows -> {outPath}");
    return 0;
}

static void EmitFacts(Action<string, string, string, string, string, string> row,
                      string scenario, string item, string d, AsOfFacts f)
{
    void Fact(string measure, string value) => row(scenario, item, "-", d, measure, value);
    Fact("FactHasRatedInward", f.HasRatedInward ? "1" : "0");
    Fact("FactHasUnratedInward", f.HasUnratedInward ? "1" : "0");
    Fact("FactHasCount", f.HasCount ? "1" : "0");
    Fact("FactHasRatedOutward", f.HasRatedOutward ? "1" : "0");
    Fact("FactHasStandardCost", f.HasStandardCost ? "1" : "0");
    Fact("FactStandardCostPaisa", Int(f.StandardCostPaisa));
    Fact("FactMinInwardRateBandPaisa", Int(f.MinInwardRateBandPaisa));
    Fact("FactMaxInwardRateBandPaisa", Int(f.MaxInwardRateBandPaisa));
    Fact("FactMinOutwardRateBandPaisa", Int(f.MinOutwardRateBandPaisa));
    Fact("FactMaxOutwardRateBandPaisa", Int(f.MaxOutwardRateBandPaisa));
    Fact("FactRatedSpendPaisaCeil", Int(f.RatedSpendPaisaCeil));
    Fact("FactSpendCeilingPaisa", Int(f.SpendCeilingPaisa));
    Fact("FactImputedUnitsMicro", Int(f.ImputedUnitsMicro));
    Fact("FactTotalInwardMicro", Int(f.TotalInwardMicro));
    Fact("FactTotalOutwardMicro", Int(f.TotalOutwardMicro));
}

// Decimal only — never double. A float round-trip would manufacture diffs in a paisa-exact engine.
static string Num(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);
static string Int(long v) => v.ToString(CultureInfo.InvariantCulture);
static string Paisa(decimal rupees) => Num(rupees * 100m);
static string Micro(decimal qty) => Num(qty * 1_000_000m);

// =============================================================== COMPARE

static int Compare(string headPath, string livePath, string reportPath,
                   string headTreeSha, string liveTreeSha, string workTreeSha)
{
    var head = ReadTsv(headPath);
    var live = ReadTsv(livePath);

    var report = new StringBuilder();
    void W(string line = "") => report.Append(line).Append('\n');

    var harnessFailures = new List<string>();
    var engineFailures = new List<string>();

    // ---- THE SANDBOX SENTINEL ------------------------------------------------------------------
    // A bite driver measures a DELIBERATELY MUTATED third engine. It used to pass that engine's own
    // digest into BOTH the live-arm and working-tree slots, so the equality below held and the report
    // printed "provenance assertion: live arm IS the working tree => PASS" — a self-certifying document,
    // formatted identically to a real verdict, stating that a mutated engine's provenance was asserted.
    // This project's costliest recorded failure was an agent reporting a mutation as the working fix.
    // The drivers now pass the literal sentinel below, and a report carrying it is stamped as such on its
    // FIRST LINE and can never exit 0.
    var sandbox = string.Equals(workTreeSha, "BITE-MUTATED", StringComparison.Ordinal);
    if (sandbox)
    {
        W("*** BITE TEST — MUTATED ENGINE — NOT A VERDICT ON THE WORKING TREE ***");
        W("*** This report measures a deliberately mutated engine under .oracle-work/. It says NOTHING");
        W("*** about src/. It CANNOT exit 0: a sandbox run exits 4 even when every check passes.");
        W();
    }

    W("================================================================================");
    W("HEAD-ORACLE REPORT — reworked harness (11 checks + calibrated point oracle)");
    W("================================================================================");
    W($"head tsv                : {Path.GetFullPath(headPath)}  ({head.Count} rows)");
    W($"live tsv                : {Path.GetFullPath(livePath)}  ({live.Count} rows)");
    W();
    W("--- PROVENANCE (ASSERTED, not merely printed — audit H4) ---------------------");
    W("  SHA-256 of the LF-normalised, sorted, whole-tree digest of each engine arm:");
    W($"  head arm  (must equal the pristine baseline) : {headTreeSha}");
    W($"  live arm  (must equal working src/ on disk)  : {liveTreeSha}");
    W($"  working src/Apex.Ledger on disk              : {workTreeSha}");
    if (sandbox)
    {
        W("  provenance assertion: NOT MADE — this is a SANDBOX run against a mutated engine.");
        W("  There is no claim here that any measured tree is the working tree. Exit will be 4.");
    }
    else if (!string.Equals(liveTreeSha, workTreeSha, StringComparison.OrdinalIgnoreCase))
    {
        harnessFailures.Add("PROVENANCE: the live arm is not the working src/Apex.Ledger tree.");
        W("  *** PROVENANCE ASSERTION FAILED: live arm != working src/ ***");
    }
    else
    {
        W("  provenance assertion: live arm IS the working tree  => PASS");
    }
    W();

    var keys = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var k in head.Keys) keys.Add(k);
    foreach (var k in live.Keys) keys.Add(k);

    // ---------------- raw diff + per-family divergence -------------------------------------------
    var diffs = new List<(string Key, string Head, string Live)>();
    var familyDiffCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var familyRowCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var familyMaxAbs = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
    var familyMaxRel = new SortedDictionary<string, decimal>(StringComparer.Ordinal);

    foreach (var key in keys)
    {
        var fam = Family(key);
        familyRowCount[fam] = familyRowCount.GetValueOrDefault(fam) + 1;
        familyDiffCount.TryAdd(fam, 0);
        familyMaxAbs.TryAdd(fam, 0m);
        familyMaxRel.TryAdd(fam, 0m);

        var h = head.GetValueOrDefault(key, "<MISSING>");
        var l = live.GetValueOrDefault(key, "<MISSING>");
        if (string.Equals(h, l, StringComparison.Ordinal)) continue;

        diffs.Add((key, h, l));
        familyDiffCount[fam]++;
        if (decimal.TryParse(h, NumberStyles.Any, CultureInfo.InvariantCulture, out var hv) &&
            decimal.TryParse(l, NumberStyles.Any, CultureInfo.InvariantCulture, out var lv))
        {
            var abs = Math.Abs(lv - hv);
            if (abs > familyMaxAbs[fam]) familyMaxAbs[fam] = abs;
            if (hv != 0m)
            {
                var rel = Math.Abs((lv - hv) / hv);
                if (rel > familyMaxRel[fam]) familyMaxRel[fam] = rel;
            }
        }
    }

    // =============================================================================================
    // HARNESS INTEGRITY — if any of this fails, the oracle is not fit to judge anything.
    // =============================================================================================
    W("================================================================================");
    W("PART A — HARNESS INTEGRITY  (if this fails, judge NOTHING; fix the harness)");
    W("================================================================================");
    W();

    // ---- ROW-SET SYMMETRY: a key on one arm and not the other is a FIRST-CLASS FAILURE.
    // The most realistic wrong-fix shape — the engine refuses the voucher at posting time — makes
    // Corpus.Build throw for every G*/E1 scenario, Emit `continue`s, and those rows are simply ABSENT
    // from the live arm. The point oracle iterates live.Keys, so absent rows were neither evaluated nor
    // counted as mismatches: check 3's subject count collapsed 332 -> 134 and it printed PASS.
    var missingOnLive = keys.Where(k => !live.ContainsKey(k)).ToList();
    var missingOnHead = keys.Where(k => !head.ContainsKey(k)).ToList();
    W("ROW-SET SYMMETRY — every emitted key must exist on BOTH arms");
    W($"  keys in the union          : {keys.Count}");
    W($"  present on head, MISSING on live : {missingOnLive.Count}");
    foreach (var k in missingOnLive.Take(40)) W($"    MISSING-ON-LIVE  {k}   head={head[k]}");
    W($"  present on live, MISSING on head : {missingOnHead.Count}");
    foreach (var k in missingOnHead.Take(40)) W($"    MISSING-ON-HEAD  {k}   live={live[k]}");
    W($"                             => {Verdict(missingOnLive.Count == 0 && missingOnHead.Count == 0)}");
    if (missingOnLive.Count > 0)
        engineFailures.Add($"ROW-SET: {missingOnLive.Count} keys the head arm produced are ABSENT from the live arm.");
    if (missingOnHead.Count > 0)
        engineFailures.Add($"ROW-SET: {missingOnHead.Count} keys the live arm produced are ABSENT from the head arm.");
    W();

    // ---- EMITTED-ROW ACCOUNTING: the parsed row count must equal the emitted row count.
    // The emitter printed 20030 and the report header printed 19970 for a year, because ReadTsv silently
    // dropped 60 duplicate keys. ReadTsv now throws on a duplicate, so the two can only agree; this line
    // states it so a reader never has to reconcile two numbers by hand again.
    var headLines = File.ReadAllLines(headPath).Count(l => l.Length > 0 && !l.StartsWith("scenario\t", StringComparison.Ordinal));
    var liveLines = File.ReadAllLines(livePath).Count(l => l.Length > 0 && !l.StartsWith("scenario\t", StringComparison.Ordinal));
    W("EMITTED-ROW ACCOUNTING — parsed rows == rows on disk (no silently dropped duplicates)");
    W($"  head: rows on disk {headLines}, parsed {head.Count}   => {Verdict(headLines == head.Count)}");
    W($"  live: rows on disk {liveLines}, parsed {live.Count}   => {Verdict(liveLines == live.Count)}");
    if (headLines != head.Count) harnessFailures.Add($"EMITTED-ROW ACCOUNTING: head arm emitted {headLines} rows but only {head.Count} parsed.");
    if (liveLines != live.Count) harnessFailures.Add($"EMITTED-ROW ACCOUNTING: live arm emitted {liveLines} rows but only {live.Count} parsed.");
    W();

    // ---- BUILD OUTCOME — every declared scenario must actually BUILD, on BOTH arms. -----------------
    var headBuild = BuildOutcomes(head);
    var liveBuild = BuildOutcomes(live);
    W("BUILD OUTCOME — every declared (scenario x method) must CONSTRUCT, on both arms");
    W("  AUDIT #3 FINDING [0] (HIGH). BuildOutcome was emitted and read by NOTHING. Scenario G11-002 — the");
    W("  PURCHASE-invoice half of the invoice seam — threw on BOTH arms for its whole life: Emit `continue`d,");
    W("  no engine row existed, the point oracle iterates LIVE keys so it evaluated 0 subjects there, CHECK 11");
    W("  saw a SYMMETRIC exception and passed, and the RECORDED census had been recorded FROM that state, so");
    W("  the census gate BLESSED the hole. 'G11-002' appeared ZERO times in the report. The mechanism was");
    W("  fully general: any scenario added to cover the negative-stock fix could vanish the same way.");
    W("  A head-arm failure is a BROKEN CORPUS (harness, exit 3). A live-arm failure where head built is an");
    W("  ENGINE that refuses to post what HEAD posted, which is an engine verdict.");
    W($"  declared cells (scenarios x methods) : {headBuild.Rows}   (head OK {headBuild.Ok} / live OK {liveBuild.Ok})");
    foreach (var b in headBuild.MissingCells.Take(20)) W($"    BUILD-MISSING  head  {b}");
    foreach (var b in headBuild.Bad.Take(20)) W($"    BUILD-FAILED   head  {b}");
    foreach (var b in liveBuild.MissingCells.Take(20)) W($"    BUILD-MISSING  live  {b}");
    foreach (var b in liveBuild.Bad.Take(20)) W($"    BUILD-FAILED   live  {b}");
    W($"                             => {Verdict(headBuild.Ok == headBuild.Rows && liveBuild.Ok == liveBuild.Rows)}");
    if (headBuild.Bad.Count > 0 || headBuild.MissingCells.Count > 0)
        harnessFailures.Add(
            $"BUILD OUTCOME: {headBuild.Bad.Count + headBuild.MissingCells.Count} of {headBuild.Rows} declared " +
            "(scenario x method) cells did NOT build on the HEAD arm. A scenario that cannot be constructed is " +
            "measured by nothing — fix the corpus, judge nothing until it builds.");
    if (liveBuild.Bad.Count > 0 || liveBuild.MissingCells.Count > 0)
        engineFailures.Add(
            $"BUILD OUTCOME: {liveBuild.Bad.Count + liveBuild.MissingCells.Count} declared cells did NOT build on " +
            "the LIVE arm — the engine refuses to construct a book HEAD constructs.");
    W();

    // ---- corpus integrity: the spec-derived rows must be identical on both arms.
    var specRows = keys.Where(IsSpecDerived).ToList();
    var specDiffs = diffs.Where(d => IsSpecDerived(d.Key)).ToList();
    W("CORPUS INTEGRITY — spec-derived Fact* / Ref* rows identical on both arms");
    W($"  spec-derived rows compared : {specRows.Count}");
    W($"  diffs                      : {specDiffs.Count}   => {Verdict(specDiffs.Count == 0)}");
    foreach (var d in specDiffs.Take(40)) W($"    DIFF {d.Key}   head={d.Head}   live={d.Live}");
    if (specRows.Count == 0) harnessFailures.Add("CORPUS INTEGRITY evaluated 0 rows.");
    if (specDiffs.Count > 0) harnessFailures.Add($"CORPUS INTEGRITY: {specDiffs.Count} spec-derived rows differ between arms.");
    W();

    // ---- reference self-consistency: its layer stack must hold exactly the quantity its OWN replay
    // reaches (FactFlatNetMicro, clamped at 0 — a debt is the negative part and holds no layers).
    var refSelf = new List<string>();
    var refDesync = new List<string>();
    var refSelfChecked = 0;
    foreach (var key in head.Keys.Where(k => Col(k, 4) == "RefLayerQtyMicro").OrderBy(k => k, StringComparer.Ordinal))
    {
        var p4 = key.Split('\t');
        var flatKey = string.Join('\t', [p4[0], p4[1], "-", p4[3], "FactFlatNetMicro"]);
        var qtyKey = string.Join('\t', p4[..4]) + "\tRefClosingQtyMicro";
        if (Dec(head, flatKey) is not { } flatNet || Dec(head, key) is not { } layerQty) continue;
        refSelfChecked++;
        var heldQty = Math.Max(flatNet, 0m);
        if (layerQty != heldQty)
            refSelf.Add($"{key}   layer qty={Num(layerQty)}   replay net={Num(flatNet)}   delta={Num(layerQty - heldQty)}");
        if (Dec(head, qtyKey) is { } closing && closing > 0m && heldQty != closing)
            refDesync.Add($"{key}   layers hold {Num(heldQty)}   reported closing qty {Num(closing)}   delta={Num(heldQty - closing)}");
    }
    W("REFERENCE SELF-CONSISTENCY — the reference's cost layers must hold exactly the quantity its replay reaches");
    W("  A reference whose value comes from a different number of units than its own replay reached is not");
    W("  an oracle, it is two answers. (An early draft of the debt rule topped a physical count up by");
    W("  (counted + debt) and valued 23 units while reporting 8; this invariant convicts that class.)");
    W("  MEASURED AGAINST FactFlatNetMicro — Facts' OWN gated quantity walk of the flattened ITEM-LEVEL");
    W("  stream — not against the reported closing quantity. The reported quantity is replayed PER");
    W("  (item, godown, batch); valuation replays ONE flattened stream. On every single-key book, and on");
    W("  every multi-key book with no physical count, the two are the same number and this is the same");
    W("  assertion it always was. Where a per-key COUNT meets a flattened stack they genuinely differ, and");
    W("  that DESYNC is a real pre-existing engine property, listed below rather than swallowed here.");
    W($"  subjects checked           : {refSelfChecked}");
    W($"  inconsistencies            : {refSelf.Count}   => {Verdict(refSelf.Count == 0)}");
    foreach (var m in refSelf.Take(40)) W($"    REF-INCONSISTENT  {m}");
    W($"  ITEM-LEVEL/PER-KEY DESYNC (reported, not failed) : {refDesync.Count}");
    foreach (var m in refDesync.Take(40)) W($"    DESYNC  {m}");
    if (refSelfChecked == 0) harnessFailures.Add("REFERENCE SELF-CONSISTENCY evaluated 0 subjects.");
    if (refSelf.Count > 0) harnessFailures.Add($"REFERENCE SELF-CONSISTENCY: {refSelf.Count} subjects where layer qty != the replay net.");
    W();

    // ---- REFERENCE VALUE INVARIANT — the one PART A did not have. ----------------------------------
    var val = ReferenceValueInvariant(head);
    W("REFERENCE VALUE INVARIANT — the reference's VALUE must decompose into SPEC rates");
    W("  The quantity invariant above is real, and a CRUDE poison (deleting the debt repayment outright)");
    W("  is caught by it. A VALUE-ONLY poison was not: setting the surviving remainder of a repaying lot");
    W("  to unit 0 leaves every quantity untouched, so self-consistency passes; N* books never carry a");
    W("  debt, so CHECK 4 CALIBRATION passes; and the report printed 'HARNESS INTEGRITY : SOUND' while the");
    W("  poisoned reference DEMANDED Rs 0 on the crux — i.e. it would have convicted a correct engine and");
    W("  acquitted one that wiped the asset. Calibration validates only the paths N* reaches, and the debt");
    W("  branch is BY CONSTRUCTION not one of them.");
    W("  AUDIT #3 FINDING [1] (HIGH) — SET MEMBERSHIP WAS NOT ENOUGH. The first version of this invariant");
    W("  asked only 'is this rate somewhere in the admissible set?'. That acquits the single most likely");
    W("  genuine mistake in the debt branch: RE-RATING the repayment surplus at the rate of the stock that");
    W("  ran out. The adversary demonstrated it end to end — a poisoned reference demanded 25@100.13 =");
    W("  Rs 2,503.25 on THE CRUX where the brief says 25@7.91 = Rs 197.75, and PART A still printed");
    W("  'INADMISSIBLE layer rates : 0 / HARNESS INTEGRITY : SOUND', because 100.13 IS in the set.");
    W("  AUDIT #3 FINDING [3] (MEDIUM) — and the one test that existed was WAIVED by a tag ('RunningAverage')");
    W("  that the audited code emits ABOUT ITSELF. Self-attestation is not evidence.");
    W("  Both are closed by binding each layer to A LOT IN THE SPEC (FactInwardLots, derived by Facts' own");
    W("  walk, never by Reference.BuildStack). So, for every Fifo/Lifo subject:");
    W("    (a) SUM(layer qty)        == max(FactFlatNetMicro, 0)   [the quantity its own replay reached]");
    W("    (b) SUM(layer qty x rate) == RefClosingValuePaisa   (paisa-snapped)");
    W("    (c) ORIGIN BINDING — every layer names the LOT its units came from, and:");
    W("        * the lot must EXIST in the spec's lot table, and must have had at least that many units;");
    W("        * if that lot carries an EXPLICIT rate, the layer MUST be priced at THAT rate. Not an");
    W("          admissible rate — THE rate. No tag can excuse it: the best-available-cost chain is");
    W("          unreachable for a rated lot, so a 'RunningAverage' tag on one is itself convicted.");
    W("        * only an UNRATED lot or a physical count-up can reach the chain. There the rate must be");
    W("          admissible outright, or lie inside the CONVEX HULL [min, max] of the admissible set — a");
    W("          weighted blend of admissible rates provably cannot leave it. Outside the hull FAILS.");
    W($"  subjects checked           : {val.Checked}");
    W($"  qty-decomposition failures : {val.QtyFailures.Count}");
    foreach (var m in val.QtyFailures.Take(30)) W($"    VALUE-INVARIANT(a)  {m}");
    W($"  value-decomposition failures: {val.ValueFailures.Count}");
    foreach (var m in val.ValueFailures.Take(30)) W($"    VALUE-INVARIANT(b)  {m}");
    W($"  layers BOUND to an explicitly-rated spec lot (rate checked against THAT lot) : {val.OriginBoundLayers}");
    W($"  ORIGIN / WRONG-RATE failures : {val.OriginFailures.Count}");
    foreach (var m in val.OriginFailures.Take(30)) W($"    VALUE-INVARIANT(c-lot)  {m}");
    if (val.OriginFailures.Count > 30) W($"    ... and {val.OriginFailures.Count - 30} more");
    W($"  INADMISSIBLE layer rates (outside the admissible hull) : {val.RateFailures.Count}");
    foreach (var m in val.RateFailures.Take(30)) W($"    VALUE-INVARIANT(c)  {m}");
    W("    (d) ORDERING — audit #3 asked for it, round 4 did not build it, audit #4 finding [1](2) again.");
    W("        A poison that RESURRECTS the drained lot's units after a repayment binds every layer");
    W("        TRUTHFULLY to a real lot at that lot's real spec rate, so (c) passes completely. The only");
    W("        thing that kills it is a fact about ORDER: the company-wide net quantity was <= 0 at the last");
    W("        dry point (FactPostDryLots, a PURE QUANTITY walk in Facts.cs), so the stack was empty there");
    W("        and NOTHING created at or before it can still be surviving.");
    W($"  subjects the ordering rule CONSTRAINS (the book ran dry) : {val.OrderingConstrainedSubjects}");
    W($"  layers tested against the post-dry lot set : {val.OrderingTestedLayers}");
    W($"  ORDERING failures (a layer surviving from before the stack ran dry) : {val.OrderingFailures.Count}");
    foreach (var m in val.OrderingFailures.Take(30)) W($"    VALUE-INVARIANT(d-order)  {m}");
    if (val.OrderingFailures.Count > 30) W($"    ... and {val.OrderingFailures.Count - 30} more");
    W("    (e) AGGREGATE PER-LOT BOUND — audit #4 finding [5] (LOW). `perLot` was accumulated and never");
    W("        read, so only a PER-LAYER bound existed and a reference that split an over-claim across");
    W("        several layers from one lot escaped with the counter still at 0.");
    W($"  (subject, lot) pairs whose AGGREGATE claim was bounded : {val.PerLotChecks}");
    W($"  AGGREGATE over-claim failures : {val.PerLotFailures.Count}");
    foreach (var m in val.PerLotFailures.Take(30)) W($"    VALUE-INVARIANT(e-agg)  {m}");
    W($"  chain-priced blend layers inside the hull (reported, not failed) : {val.BlendLayers}");
    foreach (var m in val.BlendExamples.Take(10)) W($"    HULL-BLEND  {m}");
    W($"  of which tagged RunningAverage : {val.RunningAverageLayers} on {val.RunningAverageSubjects} subject(s)");
    foreach (var m in val.RunningAverageExamples.Take(10)) W($"    RA-BLEND  {m}");
    W("  NOTE ON THE COUNT-UP EXEMPTION, STATED RATHER THAN BURIED. A count-up layer has no supplying lot,");
    W("  so it cannot be bound to one; it is tested only for admissibility/hull membership here. Audit #4");
    W("  finding [1](1) showed that is not enough — re-pricing a count-up taken WITH A DEBT OUTSTANDING moved");
    W("  the crux 10.25x with this whole block reading 0/0/0/0. That path is now pinned by an EXTERNAL");
    W("  constant instead: CHECK 4c golden GT-11/GT-11L/GT-12 fix G6-001 at 8 x Rs 9.77 = 7816p, and CHECK 4c");
    W("  further asserts that EVERY subject tagged INVENTED (which is exactly the count-with-debt and");
    W("  unrated-repayment population) carries such a golden. The exemption can no longer hide anything.");
    var valOk = val.QtyFailures.Count == 0 && val.ValueFailures.Count == 0
             && val.RateFailures.Count == 0 && val.OriginFailures.Count == 0
             && val.OrderingFailures.Count == 0 && val.PerLotFailures.Count == 0;
    W($"                             => {Verdict(valOk)}");
    if (val.Checked == 0) harnessFailures.Add("REFERENCE VALUE INVARIANT evaluated 0 subjects.");
    if (val.OriginBoundLayers == 0) harnessFailures.Add(
        "REFERENCE VALUE INVARIANT: NOT ONE layer was bound to an explicitly-rated spec lot. The rate " +
        "binding evaluated nothing, so finding [1] is open again — check RefLayerOrigins/FactInwardLots.");
    if (val.OrderingConstrainedSubjects == 0) harnessFailures.Add(
        "REFERENCE VALUE INVARIANT: the ORDERING rule constrained 0 subjects. On a corpus with G* families " +
        "that is impossible — FactPostDryLots is not reaching the comparator, so finding [1](2) is open again.");
    if (val.PerLotChecks == 0) harnessFailures.Add(
        "REFERENCE VALUE INVARIANT: the AGGREGATE per-lot bound evaluated 0 (subject, lot) pairs — " +
        "finding [5] is open again.");
    if (!valOk) harnessFailures.Add(
        $"REFERENCE VALUE INVARIANT: {val.QtyFailures.Count} qty, {val.ValueFailures.Count} value, " +
        $"{val.OriginFailures.Count} lot-origin/wrong-rate, {val.RateFailures.Count} inadmissible-rate, " +
        $"{val.OrderingFailures.Count} ordering, {val.PerLotFailures.Count} aggregate-over-claim " +
        "failures. The reference is wrong — fix the reference.");
    W();

    // ---- REFERENCE PROVENANCE CENSUS — how much of the oracle was ever validated. -------------------
    W("REFERENCE PROVENANCE — per family, how many subjects rest on WHAT");
    W("  CALIBRATED   = only paths an N* book reaches; CHECK 4 asserts they equal HEAD exactly.");
    W("  BRIEF        = a debt repaid by a RATED inward. Stated verbatim in the rework brief, but NO N*");
    W("                 book reaches it, so calibration cannot see it.");
    W("  INVENTED     = a rule NOT reachable by any calibrated path: a physical count taken with a debt");
    W("                 outstanding, or a debt settled by an inward that carries no purchase rate.");
    W("  (The ECHO-OF-HEAD tag was RETIRED on 2026-07-27 — audit #4 finding [3]. It was applied to all 187");
    W("   AverageCost subjects and became false the moment Reference.Value's AverageCost arm became");
    W("   debt-aware, at which point that column started issuing CHECK 2's engine verdicts. It also kept the");
    W("   AverageCost subjects resting on the settled rule below OUT of the INVENTED count. AverageCost is");
    W("   now tagged from the SAME debt flags as Fifo/Lifo.)");
    var prov = ProvenanceCensus(head);
    W("  family | method       | CALIBRATED | BRIEF | INVENTED");
    foreach (var row in prov) W("  " + row);
    var unknownProv = head.Keys.Count(k => Col(k, 4) == "RefProvenance"
        && head[k] is not (RefProvenance.Calibrated or RefProvenance.Brief or RefProvenance.Invented));
    if (unknownProv > 0)
        harnessFailures.Add($"REFERENCE PROVENANCE: {unknownProv} subjects carry a provenance tag this comparator " +
                            "does not recognise (ECHO-OF-HEAD was retired 2026-07-27). An unclassified subject is " +
                            "one whose validation status nobody can read off the census.");
    W();

    // ---- THE INVENTED POPULATION, DERIVED FROM THE SPEC. -------------------------------------------
    // AUDIT #5 FINDING [3] (LOW). CHECK 4c's coverage half is driven by RefProvenance, a tag the reference
    // emits ABOUT ITSELF, and no census cell pinned the population. The total collapse was already caught
    // (zero INVENTED subjects is a harness failure); a PARTIAL retag was not — if a refactor stopped setting
    // CountWithDebtOutstanding, G6-001's three subjects would drop to BRIEF, coverage would still pass on
    // G7's remaining six, and nothing would move except a table nobody diffs.
    var invPop = InventedPopulation(head);
    W("THE INVENTED POPULATION — DERIVED FROM THE SPEC, NOT READ OFF THE REFERENCE'S OWN TAG");
    W("  Facts.InventedByRule answers the same question by a PURE QUANTITY WALK: was a physical count taken,");
    W("  or an UNRATED inward received, at a point where the company-wide net quantity was already negative?");
    W("  It touches no rate, no cost and no layer arithmetic. The equivalence is exact: the reference's debt");
    W("  is positive exactly when that net is negative, because an inward always repays the debt before it");
    W("  adds a layer, so debt and layers are never both non-zero and net = layer quantity - debt throughout.");
    W($"  Fifo/Lifo/AverageCost subjects considered : {invPop.Compared}");
    W($"  INVENTED per the SPEC                     : {invPop.SpecSubjects}");
    W($"  INVENTED per the reference's own tag      : {invPop.EmittedSubjects}");
    W($"  tagged INVENTED but the spec says not     : {invPop.EmittedNotSpec.Count}");
    foreach (var m in invPop.EmittedNotSpec.Take(20)) W($"    INVENTED-POPULATION  {m}");
    W($"  spec says INVENTED but NOT tagged         : {invPop.SpecNotEmitted.Count}");
    foreach (var m in invPop.SpecNotEmitted.Take(20)) W($"    INVENTED-POPULATION  {m}");
    W($"  subjects with no FactInventedByRule row   : {invPop.MissingFact.Count}");
    foreach (var m in invPop.MissingFact.Take(20)) W($"    INVENTED-POPULATION  {m}");
    W($"                             => {Verdict(invPop.EmittedNotSpec.Count == 0 && invPop.SpecNotEmitted.Count == 0 && invPop.MissingFact.Count == 0)}");
    W("  The size of the SPEC population is pinned as census cell CHECK4c.inventedSubjects, so a rule that");
    W("  stops being reached announces itself as a changed cell rather than as a quieter table.");
    if (invPop.SpecSubjects == 0)
        harnessFailures.Add("INVENTED POPULATION: the SPEC-derived population is EMPTY. The corpus previously had " +
                            "count-with-debt and unrated-repayment subjects, so either Facts.InventedByRule or the " +
                            "corpus stopped reaching them — and the settled rule would be measured by nothing.");
    if (invPop.MissingFact.Count > 0)
        harnessFailures.Add($"INVENTED POPULATION: {invPop.MissingFact.Count} subjects have no FactInventedByRule row, " +
                            "so the coverage assertion is standing on the reference's own tag again.");
    if (invPop.EmittedNotSpec.Count > 0 || invPop.SpecNotEmitted.Count > 0)
        harnessFailures.Add($"INVENTED POPULATION MISMATCH: {invPop.EmittedNotSpec.Count} subjects tagged INVENTED " +
                            $"the spec does not justify, {invPop.SpecNotEmitted.Count} the spec demands and the " +
                            "reference does not tag. The population CHECK 4c pins is not the population that exists.");
    W();

    // ---- SETTLED POLICY — the ruling that used to be a ">>> USER DECISION REQUIRED" block. ----------
    // AUDIT #4 FINDING [4] (MEDIUM). The old block stated its numeric consequence as HARD-CODED PROSE
    // ("DEMANDS ... = 8 x Rs 9.77"). The adversary poisoned the reference so it actually demanded
    // 8 x Rs 100.13 and the report kept printing "8 x Rs 9.77" in the same document — the one sentence
    // asking the user to ratify a number was decoupled from the oracle it described. Every figure below is
    // now READ FROM THE EMITTED ROWS, and an INVENTED subject that fails to appear here is a HARNESS failure.
    var inventedSubjects = head.Keys
        .Where(k => Col(k, 4) == "RefProvenance" && head[k] == RefProvenance.Invented)
        .Select(k => string.Join('\t', k.Split('\t')[..4]))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToList();
    W("SETTLED POLICY — the rule that prices a debt settled by something carrying no purchase rate");
    W("  DECIDED BY THE USER ON 2026-07-27, and recorded in the repository in parallel. It was an open");
    W("  '>>> USER DECISION REQUIRED' escalation in every previous round; it is settled now and this block");
    W("  records the ruling, not a request.");
    W("  THE RULE ITSELF, STATED SO A READER CAN EVALUATE IT WITHOUT NEEDING THE RULING (audit #5 finding");
    W("  [2]): a debt settled by a movement carrying NO PURCHASE RATE — an unrated inward, or a physical");
    W("  count taken while a debt is outstanding — is valued through the engine's EXISTING best-available-");
    W("  cost chain, CostContext.NoRateInwardCost:");
    W("      running average -> strictly-positive StandardCost -> last rated inward -> 0");
    W("  ('strictly-positive' is load-bearing: an item whose standard cost is an EXPLICIT Rs 0.00 SKIPS that");
    W("  link and falls through to the last rated inward. G10-002's Gadget is exactly that case, and GT-21");
    W("  pins it.) Nothing new is invented; the reference applies the rule the engine already applies to an");
    W("  unrated inward. HEAD's divergence is that it uses the running average ALONE, which is 0 immediately");
    W("  after an over-draw, so HEAD values genuinely-held units at Rs 0.00. Judge the rule on that statement;");
    W("  the attribution says who settled it, not why it is right.");
    W("  THE CONSEQUENCE IS COMPUTED, NOT ASSERTED. Every line below is derived from the emitted");
    W("  RefClosingValuePaisa / RefClosingQtyMicro rows of THIS run, so it moves if the reference moves:");
    var inventedUnnamed = new List<string>();
    foreach (var stem in inventedSubjects)
    {
        var v = Dec(head, stem + "\tRefClosingValuePaisa");
        var qm = Dec(head, stem + "\tRefClosingQtyMicro");
        if (v is not { } paisaV || qm is not { } qMicro || qMicro <= 0m)
        {
            inventedUnnamed.Add($"{stem}   is INVENTED but its value/quantity rows are missing or non-positive.");
            continue;
        }
        var qty = qMicro / 1_000_000m;
        var perUnit = Math.Round(paisaV / 100m / qty, 4, MidpointRounding.AwayFromZero);
        W($"    SETTLED  {stem}   demands {Num(qty)} x Rs {Num(perUnit)} = Rs {Num(paisaV / 100m)}   " +
          $"({Num(paisaV)}p, pinned by CHECK 4c)");
    }
    W($"  INVENTED subjects named above : {inventedSubjects.Count - inventedUnnamed.Count} of {inventedSubjects.Count}");
    foreach (var m in inventedUnnamed) W($"    SETTLED-UNNAMED  {m}");
    if (inventedSubjects.Count == 0)
        harnessFailures.Add("SETTLED POLICY: 0 subjects are tagged INVENTED. The corpus previously had them, so " +
                            "either the provenance tagging stopped working or the count-with-debt / unrated-repayment " +
                            "paths are no longer reached — either way the settled rule is now measured by nothing.");
    if (inventedUnnamed.Count > 0)
        harnessFailures.Add($"SETTLED POLICY: {inventedUnnamed.Count} INVENTED subjects could not be named with a " +
                            "computed figure. A subject that is INVENTED and unnamed is itself a failure.");
    W();

    // ---- CHECK 4 — THE CALIBRATION GATE.
    // Scoped by the SPEC-DERIVED never-negative predicate, not by the letter N. E1 never goes negative
    // (10 + 20 - 15 = 15) and was excluded from calibration by the old letter test, so the reference was
    // used as an oracle on E1 without ever having been calibrated there.
    var neverNeg = NeverNegativeScenarios(head);
    var cal = Calibration(head, neverNeg);
    W("CHECK 4 — REFERENCE CALIBRATION (the hard gate)");
    W("  On every NEVER-NEGATIVE scenario x all 6 costing methods x every as-of date, the reference must");
    W("  equal HEAD EXACTLY. HEAD is trusted on never-negative books. A disagreement here means the");
    W("  REFERENCE is wrong — fix the reference, NEVER the engine.");
    W($"  scope: scenarios the SPEC says never go negative (FactNeverNegative=1), not 'the id starts with N'");
    W($"  never-negative scenarios   : {string.Join(", ", neverNeg.OrderBy(x => x, StringComparer.Ordinal))}");
    W($"  subjects calibrated        : {cal.Subjects}  (scenario x item x method x asOf x measure)");
    W($"  scenarios                  : {cal.Scenarios}");
    W($"  methods covered            : {string.Join(", ", cal.Methods)}");
    W($"  mismatches                 : {cal.Mismatches.Count}   => {Verdict(cal.Mismatches.Count == 0)}");
    foreach (var m in cal.Mismatches.Take(60)) W($"    CALIBRATION MISMATCH  {m}");
    if (cal.Mismatches.Count > 60) W($"    ... and {cal.Mismatches.Count - 60} more");
    if (cal.Subjects == 0) harnessFailures.Add("CHECK 4 evaluated 0 subjects — calibration never ran.");
    if (cal.Methods.Count != 6) harnessFailures.Add($"CHECK 4 covered {cal.Methods.Count} methods, expected 6.");
    if (cal.Mismatches.Count > 0) harnessFailures.Add($"CHECK 4 CALIBRATION FAILED: {cal.Mismatches.Count} mismatches. The reference is wrong.");
    W();

    // ---- CHECK 4b — THE DEBT-AWARE AverageCost CALIBRATION GATE. -----------------------------------
    var avgCal = DebtAwareAverageCalibration(head, neverNeg);
    W("CHECK 4b — DEBT-AWARE AverageCost CALIBRATION (the gate CHECK 4 could not reach)");
    W("  AUDIT #3 FINDING [2] (HIGH). CHECK 4 derives its engine twin by STRIPPING THE 'Ref' PREFIX, so");
    W("  RefClosingValueDebtAwarePaisa mapped to ClosingValueDebtAwarePaisa — which NO ENGINE EMITS — and the");
    W("  lookup silently dropped it with `continue`. The debt-aware AverageCost oracle was validated by");
    W("  NOTHING, and it is now the oracle CHECK 2 convicts HEAD with. The adversary poisoned it: 148 of 184");
    W("  magnitudes were rewritten, defects were INVENTED on books that never go negative (N1-002, N5-001,");
    W("  E1-001) and the headline G2-004 figure moved, while PART A still printed HARNESS INTEGRITY : SOUND.");
    W("  THE CALIBRATION IS FORCED BY THE SEMANTICS, not chosen: a never-negative book NEVER CARRIES A DEBT,");
    W("  so every clause that distinguishes RunAverageDebtAware from RunAverage is dead code there and the");
    W("  debt-aware value MUST equal HEAD's AverageCost EXACTLY. A disagreement means the DEBT-AWARE ORACLE");
    W("  is wrong — fix the oracle, never the engine — so this is a HARNESS failure (exit 3).");
    W("  A never-negative AverageCost subject with no engine twin counts as MISSING and fails too: a silent");
    W("  skip is exactly how the hole existed.");
    W($"  scope: FactNeverNegative=1 x AverageCost x every as-of date");
    W($"  subjects calibrated        : {avgCal.Subjects}   (scenarios {avgCal.Scenarios})");
    W($"  subjects with NO engine twin: {avgCal.Missing.Count}");
    foreach (var m in avgCal.Missing.Take(30)) W($"    AVG-CAL MISSING  {m}");
    W($"  mismatches                 : {avgCal.Mismatches.Count}   => {Verdict(avgCal.Mismatches.Count == 0 && avgCal.Missing.Count == 0)}");
    foreach (var m in avgCal.Mismatches.Take(60)) W($"    AVG-CAL MISMATCH  {m}");
    if (avgCal.Mismatches.Count > 60) W($"    ... and {avgCal.Mismatches.Count - 60} more");
    if (avgCal.Subjects == 0)
        harnessFailures.Add("CHECK 4b evaluated 0 subjects — the debt-aware AverageCost oracle is uncalibrated, " +
                            "so CHECK 2's convictions rest on nothing.");
    if (avgCal.Missing.Count > 0)
        harnessFailures.Add($"CHECK 4b: {avgCal.Missing.Count} never-negative AverageCost subjects have no engine twin.");
    if (avgCal.Mismatches.Count > 0)
        harnessFailures.Add($"CHECK 4b DEBT-AWARE CALIBRATION FAILED: {avgCal.Mismatches.Count} mismatches on books " +
                            "that never carry a debt. RunAverageDebtAware is wrong — fix the reference.");

    // The reference must not ask for two different AverageCost answers on one subject. CHECK 2 compares
    // against RefClosingValueDebtAwarePaisa, while CHECK 10 (issue value) and CHECK 9(b) (the company
    // total) are DERIVED from RefClosingValuePaisa. If those two columns ever diverged, the harness would
    // convict the very engine CHECK 2 prescribes — audit #2 finding [0] in a new costume.
    var avgSelf = new List<string>();
    var avgSelfChecked = 0;
    foreach (var key in head.Keys.Where(k => Col(k, 4) == "RefClosingValueDebtAwarePaisa")
                                 .OrderBy(k => k, StringComparer.Ordinal))
    {
        var plain = string.Join('\t', key.Split('\t')[..4]) + "\tRefClosingValuePaisa";
        if (!head.TryGetValue(plain, out var pv)) { avgSelf.Add($"{plain}   MISSING"); continue; }
        avgSelfChecked++;
        if (!string.Equals(pv, head[key], StringComparison.Ordinal))
            avgSelf.Add($"{plain}   RefClosingValuePaisa={pv}   RefClosingValueDebtAwarePaisa={head[key]}");
    }
    W("  THE TWO AverageCost REFERENCE COLUMNS — IDENTITY BY CONSTRUCTION, NOT A GATE");
    W("    AUDIT #4 FINDING [2] (HIGH) RETRACTS WHAT THIS BLOCK USED TO CLAIM. Round 4 printed");
    W("    'REFERENCE INTERNAL CONSISTENCY on AverageCost ... 187 subjects, 0 divergences => PASS' in the");
    W("    section whose whole purpose is harness-integrity EVIDENCE. It was a TAUTOLOGY: Reference.Value's");
    W("    AverageCost arm is Paisa(RunAverageDebtAware(events, chain).Average * closingQty) and");
    W("    Reference.DebtAwareAverageValue is the same call with the same arguments. It cannot disagree, and");
    W("    the adversary confirmed it empirically — poisoning RunAverageDebtAware moved BOTH columns together");
    W("    and the gate still printed PASS. A gate that cannot fail is worse than no gate, so it no longer");
    W("    carries a verdict, no longer carries a census cell, and is stated here as what it is.");
    W($"    subjects where the two columns are the same number : {avgSelfChecked} of {avgSelfChecked + avgSelf.Count}");
    W("    (this is a REGRESSION TRIPWIRE against the two being un-linked, and nothing more. What actually");
    W("     anchors BOTH columns is CHECK 4c below, which asserts each against an EXTERNAL hand-derived");
    W("     constant — a comparison that CAN fail, and does if either column moves.)");
    foreach (var m in avgSelf.Take(30)) W($"    AVG-REF-SPLIT  {m}");
    if (avgSelf.Count > 0)
        harnessFailures.Add($"AverageCost REFERENCE SPLIT: {avgSelf.Count} subjects where the reference's two " +
                            "AverageCost columns disagree. The harness would convict a conformant engine.");
    W();

    // ---- CHECK 4c — THE HAND-DERIVED DEBT-BRANCH GOLDENS. ------------------------------------------
    var gold = HandDerivedGoldens(head, live);
    var issueStruct = IssueStructure(head);
    W("CHECK 4c — HAND-DERIVED DEBT-BRANCH GOLDENS (the gate CHECK 4b CANNOT reach, by construction)");
    W("  AUDIT #4 FINDING [0] (CRITICAL) — AND WHY IT IS A RECURSION. CHECK 2 issues ENGINE VERDICTS from");
    W("  the debt-aware reference. CHECK 4b calibrates that reference ONLY on FactNeverNegative=1 books —");
    W("  and a never-negative book NEVER CARRIES A DEBT, so on exactly those books every clause that");
    W("  distinguishes RunAverageDebtAware from RunAverage is DEAD CODE. The clauses deciding all six of");
    W("  CHECK 2's convictions were therefore validated by NOTHING. More calibration cannot close that:");
    W("  HEAD HAS NO CORRECT DEBT BEHAVIOUR TO CALIBRATE AGAINST. That is the recursion.");
    W("  IT TERMINATES HERE. The constants below are LITERAL expected paisa values for subjects where the");
    W("  debt clauses actually FIRE. Each was (a) DERIVED BY HAND, movement by movement, and written up so a");
    W("  reviewer can check the arithmetic WITHOUT trusting any code in this repository; (b) CROSS-CHECKED by");
    W("  an out-of-band Python replay written from the corpus movement lists alone, sharing no line of code");
    W("  with Reference.cs or Program.cs; (c) compared against the C# reference, with any disagreement to be");
    W("  resolved by HAND ARITHMETIC and never by picking a side.");
    W("  WHAT THIS BUYS, HONESTLY: it does NOT make the reference provably right. It makes the reference");
    W("  wrong ONLY IF a human derivation and two independent implementations are all wrong THE SAME WAY.");
    W("  That is the terminal state of this argument, and it is the honest one.");
    W("  A golden the reference does not reproduce is a HARNESS failure (exit 3) — these constants judge the");
    W("  ORACLE, never src/. For an AverageCost golden BOTH reference columns are asserted against the same");
    W("  constant, which is what makes the tautology above unnecessary.");
    W("  ROUND 6 — AUDIT #5 FINDING [0] (HIGH) CLOSED THE OTHER HALF. Round 5's table pinned CLOSING VALUES");
    W("  ONLY. CHECK 10 is judged from RefIssueValue, whose Fifo/Lifo arm is a SEPARATE consume loop that");
    W("  CHECK 4 also calibrates only on never-negative books — so the reference's ISSUE arm was the one");
    W("  verdict-issuing output with NO external anchor on the debt branch. The adversary proved it: a poison");
    W("  that issued at the debt-aware pool average whenever the book had ever carried a debt rewrote 68 of");
    W("  the 120 reported CHECK 10 demands (Rs 197.75 -> Rs 7,910.00 on the crux, 40x, silently dropping the");
    W("  stock cap) while CHECK 4/4b/4c all printed PASS and PART A printed SOUND. A builder with a correct");
    W("  Balance Sheet and a wrong P&L would have been certified. There are now TWO tables, and a structural");
    W("  assertion that needs no constants at all.");
    W($"  CLOSING-value goldens in the table : {Goldens.All.Count}");
    W($"  CLOSING-value goldens evaluated    : {gold.Evaluated}");
    W($"  ISSUE-value goldens in the table   : {Goldens.Issue.Count}");
    W($"  ISSUE-value goldens evaluated      : {gold.IssueEvaluated}");
    W($"  goldens with NO reference row      : {gold.Missing.Count}");
    foreach (var m in gold.Missing.Take(30)) W($"    GOLDEN MISSING  {m}");
    W($"  mismatches                  : {gold.Mismatches.Count}   => " +
      Verdict(gold.Mismatches.Count == 0 && gold.Missing.Count == 0));
    foreach (var m in gold.Mismatches.Take(40)) W($"    GOLDEN MISMATCH  {m}");
    W("  --- the CLOSING-value table, with the hand derivation printed beside every constant -------------");
    foreach (var l in gold.Lines) W("    " + l);
    W("  --- the ISSUE-value table (audit #5 finding [0]) ------------------------------------------------");
    foreach (var l in gold.IssueLines) W("    " + l);

    // ---- THE DERIVATION MUST AGREE WITH THE CONSTANT (audit #5 finding [1], MEDIUM).
    W("  --- THE PROSE IS TIED TO THE CONSTANT ----------------------------------------------------------");
    W("  The one shortcut Goldens.cs forbids — 'edit the constant to match the code' — was the one thing no");
    W("  gate detected: the census pinned the NUMBER of goldens, never their VALUES. Two things now stop it.");
    W("  (a) the LAST rupee figure of each printed derivation is parsed and must equal the constant / 100, so");
    W("      an edited constant with an unedited derivation fails MECHANICALLY rather than being noticed;");
    W("  (b) CHECK4c.goldenDigest below is a census cell computed over the constants themselves, so editing");
    W("      BOTH the constant and its prose still changes a recorded number that has to be justified.");
    W($"  constants whose derivation does not end in them : {gold.WorkingMismatches.Count}");
    foreach (var m in gold.WorkingMismatches.Take(30)) W($"    GOLDEN-PROSE SPLIT  {m}");
    W($"  CHECK4c.goldenDigest (recorded in Census.cs)     : {Goldens.Digest()}");

    // ---- THE STRUCTURAL ISSUE ASSERTION — no constants involved.
    W("  --- STRUCTURAL: an issue can never reach past the surviving stack (Fifo/Lifo) -------------------");
    W("  A probe at or above the closing QUANTITY must cost EXACTLY the closing VALUE, because the walk runs");
    W("  out of layers — the units a debt repayment settled went to COGS when it was settled and are not");
    W("  there to be sold again. This alone convicts every one of the adversary's 68 fabricated rows, and it");
    W("  needs no golden. It deliberately does NOT cover AverageCost, whose issue arm prices at the closing");
    W("  unit rate and is UNCAPPED by design; that arm is pinned by constants (GI-05/06/11/18/21) instead.");
    W($"  Fifo/Lifo subjects checked                   : {issueStruct.Subjects}");
    W($"  (subject, probe) pairs at or above on-hand   : {issueStruct.AtOrAbovePairs}");
    W($"  (subject, probe) pairs below on-hand         : {issueStruct.BelowPairs}");
    W($"  probe >= on-hand but issue != closing value  : {issueStruct.AtOrAboveFailures.Count}");
    foreach (var m in issueStruct.AtOrAboveFailures.Take(30)) W($"    ISSUE-STRUCTURE  {m}");
    W($"  issue value exceeding the whole stack        : {issueStruct.OverStackFailures.Count}");
    foreach (var m in issueStruct.OverStackFailures.Take(20)) W($"    ISSUE-STRUCTURE  {m}");
    W($"  non-monotonic in the probe                   : {issueStruct.MonotonicFailures.Count}");
    foreach (var m in issueStruct.MonotonicFailures.Take(20)) W($"    ISSUE-STRUCTURE  {m}");

    W("  --- COVERAGE, ASSERTED (an unpinned INVENTED subject is the exposure this table closes) ---------");
    W($"  INVENTED subjects with no golden          : {gold.UncoveredInvented.Count}");
    foreach (var m in gold.UncoveredInvented.Take(30)) W($"    GOLDEN UNCOVERED  {m}");
    W($"  MULTI-KEY INVENTED subjects with no golden (INFORMATIONAL, NO VERDICT) : {gold.UncoveredInventedInfo.Count}");
    foreach (var m in gold.UncoveredInventedInfo.Take(30)) W($"    INFO-UNCOVERED  {m}");
    if (gold.UncoveredInventedInfo.Count > 30) W($"    ... and {gold.UncoveredInventedInfo.Count - 30} more");
    W($"  debt families with no golden              : {gold.UncoveredFamilies.Count}");
    foreach (var m in gold.UncoveredFamilies) W($"    GOLDEN UNCOVERED  {m}");
    W($"  required debt clauses not exercised       : {gold.UnexercisedClauses.Count}");
    foreach (var m in gold.UnexercisedClauses) W($"    GOLDEN UNCOVERED  {m}");

    // ---- THE LABELS ARE VERIFIED, NOT BELIEVED (audit #6, LOW [1]).
    W("  --- THE CLAUSE LABELS ARE VERIFIED AGAINST THE SPEC, NOT TAKEN AT THEIR WORD -------------------");
    W("  AUDIT #6 [1] (LOW). The coverage assertion above compares Goldens.RequiredClauses against");
    W("  Goldens.All.Concat(Goldens.Issue).Select(g => g.Clause) — a projection of the TABLE UNDER AUDIT.");
    W("  It proved that every required tag APPEARS, never that any of them is TRUE: nothing asked whether a");
    W("  golden tagged 'issue:debt-outstanding' is actually taken with a debt outstanding. A table with the");
    W("  right numbers under the wrong labels therefore reported FULL clause coverage while leaving a clause");
    W("  genuinely unexercised, and re-tagging a single golden manufactured coverage out of nothing.");
    W("  Self-attestation is the finding that has recurred in every audit of this harness; this is the last");
    W("  instance of it. Each label is now required to be TRUE of its own subject, judged from FactDebtShape");
    W("  — a PURE QUANTITY WALK in Facts.cs that reads no rate, no cost and no layer, so it cannot share a");
    W("  mistake with the debt VALUE branch whose labels it is auditing. A false label is a HARNESS failure");
    W("  (exit 3): it says the ORACLE's coverage claim is wrong, never that src/ is.");
    W($"  goldens whose label was verified          : {gold.ClauseChecked} of {Goldens.All.Count + Goldens.Issue.Count}");
    W($"  goldens with no FactDebtShape row        : {gold.ClauseNoFact.Count}");
    foreach (var m in gold.ClauseNoFact.Take(30)) W($"    CLAUSE-UNVERIFIED  {m}");
    W($"  labels that are FALSE of their subject   : {gold.ClauseViolations.Count}   => " +
      Verdict(gold.ClauseViolations.Count == 0 && gold.ClauseNoFact.Count == 0));
    foreach (var m in gold.ClauseViolations.Take(40)) W($"    CLAUSE-LABEL  {m}");
    W("  per clause, how many goldens carry it (every one of them checked, not sampled):");
    foreach (var m in gold.ClauseTally) W($"    {m}");

    // ---- HOW MUCH OF WHAT IS ACTUALLY CONVICTED IS PINNED (audit #5 finding [4], LOW).
    W("  --- THE RATIO, COMPUTED FROM THIS RUN'S ROWS (not a claim the reader has to interpret) ----------");
    W("  Audit #5 finding [4]: 32 constants stood behind 219 debt-dependent subjects and only 19 of CHECK 3's");
    W("  70 convictions were directly pinned. The numbers a reader will quote as evidence are the CONVICTIONS,");
    W("  so those are what round 6 pinned — all of them.");
    W($"  subjects whose value depends on a debt clause (RefProvenance BRIEF or INVENTED) : {gold.DebtDependentSubjects}");
    W($"  CHECK  2 convictions this run : {gold.Check2Convictions,4}   directly pinned by a golden : {gold.Check2Pinned,4}");
    W($"  CHECK  3 convictions this run : {gold.Check3Convictions,4}   directly pinned by a golden : {gold.Check3Pinned,4}");
    W($"  CHECK 10 convictions this run : {gold.Check10Convictions,4}   directly pinned by a golden : {gold.Check10Pinned,4}");
    W("  (CHECK 10 is pinned representatively, not exhaustively: 48 distinct probes x 3366 subjects is a");
    W("   different order of magnitude, and the STRUCTURAL assertion above bounds the Fifo/Lifo arm globally.");
    W("   What is asserted is that every issue-side debt CLAUSE carries a constant, and the ratio is printed");
    W("   rather than described so nobody has to take that on trust.)");

    if (gold.Evaluated == 0)
        harnessFailures.Add("CHECK 4c evaluated 0 goldens — the reference's debt branch is validated by NOTHING again.");
    if (gold.IssueEvaluated == 0)
        harnessFailures.Add("CHECK 4c evaluated 0 ISSUE goldens — the reference's issue arm is unanchored again, " +
                            "which is audit #5 finding [0] reopened.");
    if (gold.WorkingMismatches.Count > 0)
        harnessFailures.Add($"CHECK 4c PROSE/CONSTANT SPLIT: {gold.WorkingMismatches.Count} constants whose printed " +
                            "hand derivation does not end in the constant. One of the two was edited without the " +
                            "other, and the derivation is what a reviewer adjudicates from.");
    if (issueStruct.Subjects == 0)
        harnessFailures.Add("CHECK 4c STRUCTURAL: the issue-value structural assertion evaluated 0 Fifo/Lifo subjects.");
    if (issueStruct.AtOrAbovePairs == 0)
        harnessFailures.Add("CHECK 4c STRUCTURAL: not one (subject, probe) pair sat at or above on-hand, so the " +
                            "assertion that kills a fabricated issue value evaluated nothing.");
    if (issueStruct.AtOrAboveFailures.Count > 0 || issueStruct.OverStackFailures.Count > 0
        || issueStruct.MonotonicFailures.Count > 0)
        harnessFailures.Add(
            $"CHECK 4c STRUCTURAL ISSUE ASSERTION FAILED: {issueStruct.AtOrAboveFailures.Count} probes at or above " +
            $"on-hand that do not cost the closing value, {issueStruct.OverStackFailures.Count} issues exceeding the " +
            $"whole stack, {issueStruct.MonotonicFailures.Count} non-monotonic. The reference's ISSUE arm is claiming " +
            "units the debt repayment already sent to COGS — fix the reference.");
    if (gold.Missing.Count > 0)
        harnessFailures.Add($"CHECK 4c: {gold.Missing.Count} goldens name a subject the reference does not emit.");
    if (gold.Mismatches.Count > 0)
        harnessFailures.Add($"CHECK 4c HAND-DERIVED GOLDENS FAILED: {gold.Mismatches.Count} literal constants the " +
                            "reference does not reproduce. The reference is wrong — fix the reference, never the constant.");
    if (gold.UncoveredInvented.Count > 0)
        harnessFailures.Add($"CHECK 4c COVERAGE: {gold.UncoveredInvented.Count} INVENTED subjects carry no hand-derived " +
                            "golden. An INVENTED subject rests on a rule nothing calibrates, so an unpinned one is " +
                            "precisely the hole finding [0] is about.");
    if (gold.UncoveredFamilies.Count > 0)
        harnessFailures.Add($"CHECK 4c COVERAGE: {gold.UncoveredFamilies.Count} families carry debt subjects but no golden.");
    if (gold.UnexercisedClauses.Count > 0)
        harnessFailures.Add($"CHECK 4c COVERAGE: {gold.UnexercisedClauses.Count} required debt clauses are unexercised.");
    if (gold.ClauseChecked == 0)
        harnessFailures.Add("CHECK 4c CLAUSE LABELS: not one golden's clause label was verified against the spec, " +
                            "so clause coverage is once again asserted purely from the table's own tags.");
    if (gold.ClauseNoFact.Count > 0)
        harnessFailures.Add($"CHECK 4c CLAUSE LABELS: {gold.ClauseNoFact.Count} goldens have no FactDebtShape row, so " +
                            "their labels were verified against nothing.");
    if (gold.ClauseViolations.Count > 0)
        harnessFailures.Add($"CHECK 4c CLAUSE LABELS ARE FALSE: {gold.ClauseViolations.Count} goldens carry a clause tag " +
                            "that is NOT true of their subject, judged by a pure quantity walk over the spec. The " +
                            "clause-coverage claim built on those tags is worthless until they are corrected.");
    W();

    // =============================================================================================
    // ENGINE CHECKS
    // =============================================================================================
    W("================================================================================");
    W("PART B — ENGINE CHECKS (1,2,3,5,6,7,8,9,10,11)");
    W("================================================================================");
    W();

    // ---- CHECK 11 first: an exception asymmetry poisons every other comparison.
    var exc11 = new List<string>();
    var headExc = 0;
    var liveExc = 0;
    foreach (var key in keys)
    {
        var h = head.GetValueOrDefault(key, "<MISSING>");
        var l = live.GetValueOrDefault(key, "<MISSING>");
        var hE = h.StartsWith("EXC:", StringComparison.Ordinal);
        var lE = l.StartsWith("EXC:", StringComparison.Ordinal);
        if (hE) headExc++;
        if (lE) liveExc++;
        if (hE != lE) exc11.Add($"{key}   head={h}   live={l}");
    }
    W("CHECK 11 — EXCEPTION ASYMMETRY (an EXC: on one arm where the other has a value)");
    W("  An engine that THROWS on every negative-stock valuation was previously certified CLEAN and");
    W("  credited with 16 'resolved' violations. An EXC: row can never resolve anything.");
    W($"  rows compared              : {keys.Count}");
    W($"  EXC: rows on head          : {headExc}");
    W($"  EXC: rows on live          : {liveExc}");
    W($"  asymmetric rows            : {exc11.Count}   => {Verdict(exc11.Count == 0)}");
    foreach (var e in exc11.Take(60)) W($"    ASYMMETRY  {e}");
    if (exc11.Count > 60) W($"    ... and {exc11.Count - 60} more");
    if (keys.Count == 0) harnessFailures.Add("CHECK 11 evaluated 0 rows.");
    if (exc11.Count > 0) engineFailures.Add($"CHECK 11: {exc11.Count} exception-asymmetric rows.");
    W();

    // ---- CHECK 1 — byte identity on every scenario the SPEC says never goes negative.
    // Scoped by FactNeverNegative, not by the letter N: E1-001/E1-002 never go negative and are equally
    // entitled to byte identity, but the letter test excluded them, so a change that perturbed the
    // equal-date tie-break IDENTICALLY in both E1 scenarios would have left E1Mismatches at 0 and would
    // not have tripped check 1 either.
    var check1Rows = keys.Where(k => neverNeg.Contains(Col(k, 0))).ToList();
    var check1 = diffs.Where(d => neverNeg.Contains(Col(d.Key, 0))).ToList();
    W("CHECK 1 — BYTE IDENTITY ON EVERY NEVER-NEGATIVE BOOK (spec-derived scope, not the letter 'N')");
    W($"  scenarios in scope         : {neverNeg.Count}");
    W($"  rows compared              : {check1Rows.Count}");
    W($"  diffs                      : {check1.Count}   => {Verdict(check1.Count == 0)}");
    var n1Rows = check1Rows.Count;
    foreach (var d in check1.Take(80)) W($"    DIFF {d.Key}   head={d.Head}   live={d.Live}");
    if (check1.Count > 80) W($"    ... and {check1.Count - 80} more");
    if (n1Rows == 0) harnessFailures.Add("CHECK 1 evaluated 0 rows.");
    if (check1.Count > 0) engineFailures.Add($"CHECK 1: {check1.Count} diffs on never-negative books.");
    W();

    // ---- CHECK 1M — BYTE IDENTITY ON EVERY MULTI-KEY SUBJECT, NEGATIVE OR NOT.
    // ROUND 11. The debt rule now applies ONLY where it is proven — to an item whose whole as-of-scoped
    // movement history sits on ONE (godown, batch) key. The claim that buys is INERTNESS: on a multi-key
    // item every debt clause is unreachable and the engine reduces to HEAD exactly. This check measures
    // that claim DIRECTLY — head.tsv against live.tsv, two engines, no reference anywhere in the
    // comparison. That matters: the round-10 review's finding [1] was that the byte-identity claim had
    // been measured against the REFERENCE's own predicate, so the reference and the engine could agree
    // about the wrong thing and no check could see it. Here the reference supplies only the SCOPE, and a
    // scope that is wrong in the direction that matters (calling a multi-key subject single-key) SHRINKS
    // this check rather than satisfying it — which CHECK 1M's population floor below then catches.
    var mkSubjects = new HashSet<string>(StringComparer.Ordinal);   // scenario \t item \t asOf
    var mkByScenarioDate = new Dictionary<string, bool>(StringComparer.Ordinal);
    foreach (var k in head.Keys.Where(k => Col(k, 4) == "FactSingleKey"))
    {
        var sc = Col(k, 0); var it = Col(k, 1); var ao = Col(k, 3);
        var multi = head[k] == "0";
        if (multi) mkSubjects.Add(sc + "\t" + it + "\t" + ao);
        var sd = sc + "\t" + ao;
        mkByScenarioDate[sd] = mkByScenarioDate.TryGetValue(sd, out var prev) ? prev && multi : multi;
    }
    bool MultiKeyRow(string key)
    {
        var sc = Col(key, 0); var it = Col(key, 1); var ao = Col(key, 3);
        if (ao == "-") return false;                        // scenario-level fact rows carry no as-of
        if (it != "-") return mkSubjects.Contains(sc + "\t" + it + "\t" + ao);
        // an aggregate row (TotalClosingStockValue and friends) qualifies only when EVERY item of the
        // scenario is multi-key at that date, so a single-key item can never hide inside it.
        return mkByScenarioDate.TryGetValue(sc + "\t" + ao, out var all) && all;
    }
    // ================================================================================================
    // THE SCOPE OF THE REFERENCE'S AUTHORITY — WHICH SUBJECTS IT MAY CONVICT ON. (2026-07-29.)
    // ================================================================================================
    // The reference is a VALIDATED ORACLE ON SINGLE-KEY BOOKS AND NOWHERE ELSE. On a single-key item the
    // cost replay and the quantity register walk the same key, so `Σ layers − debt == on-hand` holds and
    // the debt rule is arithmetically sound; that scope carries an exhaustive 6,144-row sweep, 214
    // hand-derived goldens, and two independent re-derivations.
    //
    // On a MULTI-KEY book it is NOT AN ORACLE, and that is proven rather than suspected. The per-key
    // review showed the item-level model gives WRONG ANSWERS for transfers — re-deriving each key's pool
    // independently stops cost flowing across a Stock Journal and prices transferred units off an EMPTY
    // pool (Rs 5,000,002.37 of stock on Rs 1,000,003.73 ever spent), while flattening to item level loses
    // the key distinction the quantity register keeps. Neither model is right there, so the reference has
    // no standing to sentence those subjects.
    //
    // THEREFORE: the reference-backed checks (2, 3, 3b, 10, 9(b)) CONVICT ONLY on subjects PROVEN
    // single-key. Multi-key subjects are still replayed, still compared and still PRINTED — as
    // INFORMATIONAL lines carrying no verdict. Anything the predicate cannot classify (no FactSingleKey
    // row, or an aggregate row mixing scopes) is treated as NOT JUDGED, the conservative direction.
    // A harness that demands a number it cannot justify is the failure mode this exercise exists to
    // prevent; the engine-vs-engine checks (1, 1M, 11, 6/7/8) and the spec-derived quantity oracle
    // (CHECK 5) are unaffected, because none of them consults the reference's cost arithmetic.
    var skSubjects = new HashSet<string>(StringComparer.Ordinal);
    var skByScenarioDate = new Dictionary<string, bool>(StringComparer.Ordinal);
    foreach (var k in head.Keys.Where(k => Col(k, 4) == "FactSingleKey"))
    {
        var sc = Col(k, 0); var it = Col(k, 1); var ao = Col(k, 3);
        var single = head[k] == "1";
        if (single) skSubjects.Add(sc + "\t" + it + "\t" + ao);
        var sd = sc + "\t" + ao;
        skByScenarioDate[sd] = skByScenarioDate.TryGetValue(sd, out var prevS) ? prevS && single : single;
    }
    // TRUE only where the reference is entitled to issue a verdict. An aggregate row (item "-") qualifies
    // only when EVERY item of that scenario is single-key at that date, so a multi-key item can never
    // hide inside a company total that gets convicted.
    bool ReferenceMayJudge(string key)
    {
        var sc = Col(key, 0); var it = Col(key, 1); var ao = Col(key, 3);
        if (ao == "-") return false;
        if (it != "-") return skSubjects.Contains(sc + "\t" + it + "\t" + ao);
        return skByScenarioDate.TryGetValue(sc + "\t" + ao, out var all) && all;
    }
    (List<string> Judged, List<string> Info) ScopeSplit(IEnumerable<string> lines)
    {
        var judged = new List<string>();
        var info = new List<string>();
        foreach (var l in lines) (ReferenceMayJudge(l) ? judged : info).Add(l);
        return (judged, info);
    }
    // Prints a reference-backed check's outcome in TWO parts and fails the run on the judged part only.
    void ScopedVerdict(string check, string tag, List<string> mismatches, string what, int show)
    {
        var (judged, info) = ScopeSplit(mismatches);
        W($"  SINGLE-KEY mismatches (JUDGED) : {judged.Count}   => {Verdict(judged.Count == 0)}");
        foreach (var m in judged.Take(show)) W($"    {tag}  {m}");
        if (judged.Count > show) W($"    ... and {judged.Count - show} more");
        W($"  MULTI-KEY mismatches (INFORMATIONAL, NO VERDICT) : {info.Count}");
        W("    the reference is not a validated oracle for these books — measured and printed, never judged.");
        foreach (var m in info.Take(show)) W($"    INFO-{tag}  {m}");
        if (info.Count > show) W($"    ... and {info.Count - show} more");
        if (judged.Count > 0) engineFailures.Add($"{check}: {judged.Count} {what}");
    }

    var check1mRows = keys.Where(MultiKeyRow).ToList();
    var check1m = diffs.Where(d => MultiKeyRow(d.Key)).ToList();
    var mkScenarios = mkSubjects.Select(x => x.Split('\t')[0]).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
    W("CHECK 1M — BYTE IDENTITY ON EVERY MULTI-KEY SUBJECT (the debt rule must be INERT there)");
    W("  scope: FactSingleKey=0 — an item whose as-of-scoped movements touch more than one (godown, batch)");
    W("  key. NOT scoped by negativity: a multi-key book that genuinely went short must ALSO equal HEAD.");
    W("  measured ENGINE AGAINST ENGINE (head.tsv vs live.tsv); the reference supplies only the scope.");
    W($"  scenarios with multi-key subjects : {string.Join(", ", mkScenarios)}");
    W($"  multi-key subjects (scenario x item x asOf) : {mkSubjects.Count}");
    W($"  rows compared              : {check1mRows.Count}");
    W($"  diffs                      : {check1m.Count}   => {Verdict(check1m.Count == 0)}");
    foreach (var d in check1m.Take(80)) W($"    DIFF {d.Key}   head={d.Head}   live={d.Live}");
    if (check1m.Count > 80) W($"    ... and {check1m.Count - 80} more");
    // POPULATION FLOOR. A predicate that quietly stopped classifying anything as multi-key would empty
    // this check and it would pass on nothing. The corpus's multi-key families are N8, N9, N10, N11, G9,
    // G12 and G16; the floor is asserted so shrinking the scope is a HARNESS failure, not a silent PASS.
    var mkFamiliesExpected = new[] { "N8", "N9", "N10", "N11", "G9", "G12", "G16" };
    var mkFamiliesSeen = mkScenarios.Select(x => x.Split('-')[0]).Distinct().ToHashSet(StringComparer.Ordinal);
    foreach (var fam in mkFamiliesExpected)
        if (!mkFamiliesSeen.Contains(fam))
            harnessFailures.Add($"CHECK 1M: family {fam} has NO multi-key subject — the single-key predicate " +
                                "has stopped seeing a corpus axis it must see.");
    if (check1mRows.Count == 0) harnessFailures.Add("CHECK 1M evaluated 0 rows.");
    if (check1m.Count > 0)
        engineFailures.Add($"CHECK 1M: {check1m.Count} diffs on MULTI-KEY subjects. The debt rule is not inert " +
                           "there, so the engine is not byte-identical to HEAD where it promises to be.");
    W();

    // ---- CHECK 2 — THE AverageCost POINT ORACLE (INVERTED — see below).
    var avgRows = keys.Count(k => Col(k, 2) == "AverageCost");
    var avgRawDiffs = diffs.Where(d => Col(d.Key, 2) == "AverageCost").ToList();
    var check2Pt = PointOracle(live, head, "ClosingValuePaisa", "RefClosingValueDebtAwarePaisa",
                               m => m == "AverageCost", itemRowsOnly: true);
    W("CHECK 2 — POINT ORACLE ON AverageCost: live ClosingValue == the DEBT-AWARE reference");
    W("  *** THIS CHECK WAS INVERTED ON 2026-07-27, BY A USER SCOPE DECISION. ***");
    W("  It used to assert AverageCost BYTE IDENTITY to HEAD. That was defensible only while AverageCost was");
    W("  deferred: it did not detect an AverageCost defect, it ENFORCED that one stayed, and the report said");
    W("  so out loud. The user has now decided AverageCost IS to be fixed — at which point a byte-lock to");
    W("  HEAD FORBIDS THE AUTHORISED FIX and would convict the very engine that repairs it.");
    W("  AverageCost is therefore now a FIRST-CLASS POINT-ORACLE SUBJECT, exactly as FIFO/LIFO are under");
    W("  CHECK 3, compared against RefClosingValueDebtAwarePaisa — a genuinely independent debt-aware moving");
    W("  average that shares no code path with RunAverage, and which CHECK 4b now CALIBRATES against HEAD on");
    W("  every never-negative book (finding [2]). CORRECTED IN ROUND 7 (audit #6): RefClosingValuePaisa for");
    W("  AverageCost is NOT an echo of HEAD either. Reference.Value's AverageCost arm is the SAME");
    W("  RunAverageDebtAware call, so the two columns are identical BY CONSTRUCTION — CHECK 2 names the");
    W("  debt-aware one, CHECK 9(b) and CHECK 10 derive from the plain one, and they cannot disagree.");
    W("  CONSEQUENCE, STATED PLAINLY: on a clean run HEAD is CONVICTED here. That is the point — the");
    W("  Rs 11,996.40 phantom asset on G2-004 and the Rs 0.00 valuation of 8 physically counted units on");
    W("  G6-001 are now FAILURES, not a footnote.");
    W($"  subjects evaluated         : {check2Pt.Evaluated}");
    W($"  live != debt-aware reference : {check2Pt.Mismatches.Count} (before scoping)");
    ScopedVerdict("CHECK 2", "POINT-ORACLE-AVG", check2Pt.Mismatches,
                  "AverageCost closing values disagree with the debt-aware reference on single-key books.", 120);
    W($"  (reporting only, no verdict) raw AverageCost head-vs-live row diffs : {avgRawDiffs.Count} of {avgRows} rows");
    foreach (var d in avgRawDiffs.Take(40)) W($"    AVG-DIFF {d.Key}   head={d.Head}   live={d.Live}");
    if (check2Pt.Evaluated == 0) harnessFailures.Add("CHECK 2 evaluated 0 subjects.");
    if (avgRows == 0) harnessFailures.Add("CHECK 2 saw 0 AverageCost rows.");

    W();

    // ---- THE SECOND AverageCost OPINION — the MAGNITUDE behind CHECK 2. -----------------------------
    // This block issues no verdict OF ITS OWN; it is not "never failed on" any more, because CHECK 2 above
    // fails the run against the very same column. The header said "reported, never failed on" until round
    // 7 — a leftover from before the 2026-07-27 inversion, contradicted by this block's own printed text.
    var avgDefect = DebtAwareAverageDefect(head);
    W("DEBT-AWARE AverageCost — the MAGNITUDE behind CHECK 2's convictions (CALIBRATED by CHECK 4b)");
    W("  RefClosingValueDebtAwarePaisa applies the SAME debt semantics as the cost-layer reference to the");
    W("  moving average: an over-draw is a debt; a later inward repays it AT ITS OWN RATE and only the");
    W("  surplus joins the pool; an existing debt is never re-rated; a count writes the debt off. HEAD");
    W("  instead RESETS the pool at the over-draw and re-averages every later inward, so the SIGN of its");
    W("  error is the sign of the rate trend across the recovery lots — cheap-then-dear UNDERSTATES,");
    W("  dear-then-cheap OVERSTATES, and the overstatement grows without bound with the rate spread.");
    W("  THESE ROWS NOW FAIL THE RUN. Until 2026-07-27 they were reported and never failed on, because the");
    W("  decision of record was that AverageCost would not be touched. The user has since decided it IS to");
    W("  be fixed, so CHECK 2 above compares live against this column and convicts. This block remains as");
    W("  the MAGNITUDE statement — how many rupees, on which subject — behind those convictions.");
    W("  The column is no longer unvalidated: CHECK 4b calibrates it against HEAD on every never-negative");
    W("  book, where the debt clauses are dead code and the two MUST agree.");
    W($"  AverageCost subjects        : {avgDefect.Subjects}");
    W($"  HEAD != debt-aware          : {avgDefect.Disagreeing}");
    W("  family | subjects | disagreeing | max |head-debtaware| paisa | worst subject");
    foreach (var row in avgDefect.Rows) W("  " + row);
    foreach (var d in avgDefect.Detail.Take(40)) W($"    AVG-DEBT-AWARE  {d}");
    W("  (this block issues no verdict of its own — CHECK 2 does, against the same column)");
    W();

    // ---- CHECK 3 — THE POINT ORACLE on closing value (FIFO/LIFO, all families).
    var pt = PointOracle(live, head, "ClosingValuePaisa", "RefClosingValuePaisa",
                         m => m is "Fifo" or "Lifo", itemRowsOnly: true);
    W("CHECK 3 — POINT ORACLE: live ClosingValue == the calibrated reference (FIFO/LIFO, ALL families)");
    W("  This is the check the absolute bands cannot replace. THE CRUX (G1-001) is overstated by 60% at");
    W("  HEAD and sits comfortably inside a 12.7x-wide rate band, so only a point comparison convicts it.");
    W($"  subjects evaluated         : {pt.Evaluated}");
    W($"  live != reference          : {pt.Mismatches.Count} (before scoping)");
    ScopedVerdict("CHECK 3", "POINT-ORACLE", pt.Mismatches,
                  "closing values disagree with the reference on single-key books.", 200);
    if (pt.Evaluated == 0) harnessFailures.Add("CHECK 3 evaluated 0 subjects.");
    W();

    // ---- CHECK 3b — THE POINT ORACLE ON THE FLAT METHODS.
    // Check 3 is scoped to Fifo/Lifo. That left per-item closing value under StandardCost /
    // LastPurchaseCost / LastSaleCost pinned only INDIRECTLY — via check 9(b) on the company total and
    // via check 10, whose flat-method path compares a value ROUNDED to a unit rate — so a per-item error
    // below half a paisa per unit, or one that cancelled between items in a multi-item scenario, was
    // invisible. The reference IS genuinely independent for these three (a rate chain, not an echo, and
    // calibrated on N6/N5/N3), so there is no reason not to compare them point-wise. AverageCost stays out
    // because it ALREADY HAS a point oracle of its own — CHECK 2, against RefClosingValueDebtAwarePaisa.
    // ROUND 7 (audit #6): the reason recorded here used to be "its reference is an ECHO OF HEAD and
    // comparing it to itself would prove nothing". That has been false since round 4 moved Reference.Value's
    // AverageCost arm onto RunAverageDebtAware. AverageCost is COVERED here, not excused.
    var ptFlat = PointOracle(live, head, "ClosingValuePaisa", "RefClosingValuePaisa",
                             m => m is "StandardCost" or "LastPurchaseCost" or "LastSaleCost", itemRowsOnly: true);
    W("CHECK 3b — POINT ORACLE ON THE FLAT METHODS (StandardCost / LastPurchaseCost / LastSaleCost)");
    W("  These three ARE genuinely independent in the reference — a rate chain, not an echo — and they are");
    W("  calibrated on N3/N5/N6. AverageCost is not in THIS block because CHECK 2 already point-oracles it");
    W("  against the debt-aware column: it is COVERED, not excused. (This line said 'its reference echoes");
    W("  HEAD' until round 7 — false since round 4 made Reference.Value's AverageCost arm debt-aware.)");
    W($"  subjects evaluated         : {ptFlat.Evaluated}");
    W($"  live != reference          : {ptFlat.Mismatches.Count} (before scoping)");
    ScopedVerdict("CHECK 3b", "POINT-ORACLE-FLAT", ptFlat.Mismatches,
                  "flat-method closing values disagree with the reference on single-key books.", 120);
    if (ptFlat.Evaluated == 0) harnessFailures.Add("CHECK 3b evaluated 0 subjects.");
    W();

    // ---- CHECK 5 — QUANTITY ORACLE.
    var q1 = PointOracle(live, head, "ClosingQtyMicro", "RefClosingQtyMicro", _ => true, itemRowsOnly: true);
    var q2 = PointOracle(live, head, "OnHandMicro", "RefOnHandMicro", _ => true, itemRowsOnly: true);
    var q = q1.Mismatches.Count + q2.Mismatches.Count;
    W("CHECK 5 — QUANTITY ORACLE: ClosingQty and OnHand == spec-computed on-hand (ALL methods/families)");
    W("  Without this, a 'fix' that returns quantity 0 and value 0 passes every value check while a real");
    W("  asset silently leaves the Balance Sheet.");
    W($"  ClosingQty subjects        : {q1.Evaluated}   mismatches: {q1.Mismatches.Count}");
    W($"  OnHand subjects            : {q2.Evaluated}   mismatches: {q2.Mismatches.Count}");
    W($"                             => {Verdict(q == 0)}");
    foreach (var m in q1.Mismatches.Concat(q2.Mismatches).Take(80)) W($"    QTY-ORACLE  {m}");
    if (q1.Evaluated == 0 || q2.Evaluated == 0) harnessFailures.Add("CHECK 5 evaluated 0 subjects.");
    if (q > 0) engineFailures.Add($"CHECK 5: {q} quantity mismatches against the spec.");
    W();

    // ---- CHECK 10 — ISSUE VALUE against the reference.
    var issueMeasures = keys.Select(k => Col(k, 4))
        .Where(m => m.StartsWith("IssueValue@", StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToList();
    var issueEvaluated = 0;
    var issueMismatch = new List<string>();
    foreach (var measure in issueMeasures)
    {
        var r = PointOracle(live, head, measure, "Ref" + measure, _ => true, itemRowsOnly: true);
        issueEvaluated += r.Evaluated;
        issueMismatch.AddRange(r.Mismatches);
    }
    W("CHECK 10 — ISSUE VALUE == the reference issue value (ALL methods/families)");
    W("  Audited by nothing in v1: a change with a correct Balance Sheet and a wrong P&L shipped clean.");
    W($"  distinct issue probes      : {issueMeasures.Count}");
    W($"  subjects evaluated         : {issueEvaluated}");
    W($"  mismatches                 : {issueMismatch.Count} (before scoping)");
    ScopedVerdict("CHECK 10", "ISSUE-ORACLE", issueMismatch,
                  "issue values disagree with the reference on single-key books.", 120);
    if (issueEvaluated == 0) harnessFailures.Add("CHECK 10 evaluated 0 subjects.");
    W();

    // ---- CHECK 9 — TOTAL CLOSING STOCK VALUE (the actual Balance-Sheet figure).
    var totalSum = TotalConsistency(live);
    var totalHeadSum = TotalConsistency(head);
    var totalPoint = PointOracle(live, head, "TotalClosingPaisa", "RefTotalClosingPaisa", _ => true, itemRowsOnly: false);
    W("CHECK 9 — TotalClosingStockValue (audit H3: emitted by v1 and excluded from every check)");
    W("  (a) it must equal the sum of the per-item closing values on the SAME arm;");
    W("  (b) it must equal the reference total;");
    W("  (c) it is subject to the absolute checks 6-8 using the company-wide aggregate facts (below).");
    // ROUND 7, AUDIT #6 — THE SCOPE NOTE MUST DESCRIBE THIS ROUND'S CODE. What stood here was a "SCOPE
    // WARNING, STILL TRUE AFTER THE CHECK-2 INVERSION" saying the AverageCost contribution to (b) was an
    // ECHO OF HEAD and that an AverageCost result on (b) "says only that live matched head". It stopped
    // being true in round 4, when Reference.Value's AverageCost arm moved to RunAverageDebtAware:
    // RefTotalClosingPaisa is Paisa(sum of Reference.Value(...).ClosingValueRupees), so that term became
    // the DEBT-AWARE number and (b) started issuing real AverageCost convictions. The paragraph was
    // instructing readers to discount every one of them. The counts below are now DERIVED FROM THIS RUN
    // rather than asserted in prose, so this passage cannot rot the same way twice.
    var totalByMethod = totalPoint.Mismatches
        .GroupBy(m => m.Split('\t')[2], StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    var avgTotalMismatches = totalByMethod.GetValueOrDefault("AverageCost", 0);
    W("  SCOPE OF (b) — NO METHOD IS EXCLUDED FROM IT, AverageCost INCLUDED.");
    W("  RefTotalClosingPaisa is the paisa-snapped SUM of Reference.Value(...).ClosingValueRupees across the");
    W("  scenario's items, and since the 2026-07-27 scope decision Reference.Value's AverageCost arm IS");
    W("  RunAverageDebtAware. The AverageCost term is the debt-aware number, NOT HEAD's own, so (b) convicts");
    W($"  on AverageCost like any other method: {avgTotalMismatches} of this run's {totalPoint.Mismatches.Count} (b) mismatches are AverageCost rows,");
    W("  and they include the G2-004 phantom asset and the G6-001 stock wiped to nil — both printed below.");
    W("  (Until round 7 a SCOPE WARNING here called that term an ECHO OF HEAD and told the reader an");
    W("  AverageCost result on (b) 'says only that live matched head'. That described round-3 code and would");
    W($"  have had a reader discount all {avgTotalMismatches} of those convictions. It was removed, not softened.)");
    W("  THE CAVEAT THAT IS STILL REAL: an AverageCost conviction on (b) is not INDEPENDENT evidence from");
    W("  CHECK 2's. For AverageCost, RefClosingValuePaisa and RefClosingValueDebtAwarePaisa are the same pure");
    W("  function of the same arguments, so (b) RESTATES CHECK 2 at company level rather than corroborating");
    W("  it, and both rest on one external anchor — CHECK 4c's hand-derived goldens. What (b) adds that no");
    W("  per-item check can is THE SUM ITSELF: an engine right on every item and wrong in the aggregate is");
    W("  convicted here, by (a) and (b), and nowhere else.");
    W($"  (a) live totals checked    : {totalSum.Evaluated}   mismatches: {totalSum.Mismatches.Count}");
    W($"      head totals checked    : {totalHeadSum.Evaluated}   mismatches: {totalHeadSum.Mismatches.Count}   (reported, informational)");
    foreach (var m in totalSum.Mismatches.Take(40)) W($"    TOTAL-SUM  live  {m}");
    foreach (var m in totalHeadSum.Mismatches.Take(40)) W($"    TOTAL-SUM  head  {m}");
    W($"  (b) live vs reference      : {totalPoint.Evaluated} subjects, {totalPoint.Mismatches.Count} mismatches");
    W("      (b) mismatches BY METHOD : " + (totalByMethod.Count == 0 ? "(none)"
        : string.Join("   ", totalByMethod.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                          .Select(kv => $"{kv.Key}={kv.Value}"))));
    var multiItem = Corpus.Scenarios.Count(s => s.Items.Count > 1);
    W($"  multi-item scenarios in the corpus (so this is a real sum): {multiItem}");
    // (a) is arm-internal — the total against the sum of ITS OWN items — so it needs no reference and is
    // judged on every scenario. (b) compares against the reference and is therefore SCOPED: a company
    // total qualifies only where every item in that scenario is single-key at that date.
    W($"  (a) => {Verdict(totalSum.Mismatches.Count == 0)}");
    ScopedVerdict("CHECK 9(b)", "TOTAL-ORACLE", totalPoint.Mismatches,
                  "company totals disagree with the reference on all-single-key scenarios.", 120);
    if (totalSum.Evaluated == 0 || totalPoint.Evaluated == 0) harnessFailures.Add("CHECK 9 evaluated 0 subjects.");
    if (multiItem == 0) harnessFailures.Add("CHECK 9 has no multi-item scenario — the total is a tautology.");
    if (totalSum.Mismatches.Count > 0) engineFailures.Add($"CHECK 9(a): {totalSum.Mismatches.Count} totals != sum of items.");
    W();

    // ---- CHECKS 6/7/8 — the absolute, HEAD-independent oracles, with MAGNITUDE comparison (audit C2).
    var headAudit = Audit(head);
    var liveAudit = Audit(live);
    var checkNames = new[] { "6 closing-rate band", "7 total-spend containment", "8 COGS conservation" };

    W("CHECKS 6/7/8 — ABSOLUTE ORACLES computed from the SPEC, evaluated on BOTH arms");
    W("  AUDIT FIX C2: a violation is keyed by (check, subject) AND compared BY MAGNITUDE. v1 discarded a");
    W("  live violation whose key already existed at head regardless of size, and certified a mutation that");
    W("  produced Rs 100,000 of phantom stock as CLEAN. Now: a live violation that is WORSE than head's at");
    W("  the same key FAILS, and every violation prints head and live side by side.");
    W("  AUDIT FIX H1: checks 7/8 no longer stand down on physical-count / unrated-inward scenarios; the");
    W("  spend ceiling imputes the dearest available rate for units nobody bought, which is still a hard");
    W("  upper bound. Subjects marked [imputed] carry that looser bound.");
    W();
    W("  SUBJECTS EVALUATED PER CHECK (a check that evaluated nothing FAILS — audit H1)");
    foreach (var check in checkNames)
    {
        var he = headAudit.Census.Where(kv => kv.Key.Check == check).Sum(kv => kv.Value);
        var le = liveAudit.Census.Where(kv => kv.Key.Check == check).Sum(kv => kv.Value);
        var fams = liveAudit.Census.Where(kv => kv.Key.Check == check).Select(kv => kv.Key.Family).Distinct().Count();
        var meths = liveAudit.Census.Where(kv => kv.Key.Check == check).Select(kv => kv.Key.Method).Distinct().Count();
        W($"    {check,-28} head {he,6}   live {le,6}   families {fams,3}   methods {meths,3}");
        if (le == 0) harnessFailures.Add($"{check} evaluated 0 subjects on the live arm.");
        if (he == 0) harnessFailures.Add($"{check} evaluated 0 subjects on the head arm.");
    }
    W();

    W("  PER FAMILY x CHECK   'violations / subjects evaluated'   ('-' = not applicable here)");
    var allFamilies = new SortedSet<string>(familyRowCount.Keys, StringComparer.Ordinal);
    W("  family | " + string.Join(" | ", checkNames.Select(c => $"{c,-32}")));
    foreach (var fam in allFamilies)
    {
        var cells = new List<string>();
        var any = false;
        foreach (var check in checkNames)
        {
            var he = headAudit.Census.Where(kv => kv.Key.Check == check && kv.Key.Family == fam).Sum(kv => kv.Value);
            var le = liveAudit.Census.Where(kv => kv.Key.Check == check && kv.Key.Family == fam).Sum(kv => kv.Value);
            var hv = headAudit.Findings.Count(kv => kv.Key.Check == check && kv.Key.Family == fam);
            var lv = liveAudit.Findings.Count(kv => kv.Key.Check == check && kv.Key.Family == fam);
            if (he > 0 || le > 0) any = true;
            cells.Add(he == 0 && le == 0 ? "-" : $"head {hv}/{he}  live {lv}/{le}");
        }
        if (!any) continue;
        W($"  {fam,-6} | " + string.Join(" | ", cells.Select(c => $"{c,-32}")));
    }
    W();

    // ---- STRUCTURALLY-UNSATISFIABLE SUBJECTS — their own named bucket, numbers still visible. -------
    W("  STRUCTURALLY-UNSATISFIABLE SUBJECTS (excluded from the classification below; NOT a pass, NOT a fail)");
    W("    A check whose PREMISE cannot be met by ANY value has no discriminating power on that subject.");
    W("    Check 8 asks 'could the money spent have covered the units issued at a rate this item has");
    W("    actually seen?'. On a book where MORE UNITS WERE ISSUED THAN WERE EVER BOUGHT the answer is no");
    W("    for every possible closing value, and the magnitude then INCREASES with the closing value — so");
    W("    moving G6-001 from HEAD's Rs 0 (8 counted units valued at nothing: a WIPED ASSET, the actual");
    W("    defect) to the point oracle's demanded Rs 78.16 scored as 'WORSENED ... FAIL'. The harness");
    W("    rejected the exact engine its own check 3 prescribes, and the obvious way to make it green");
    W("    again was to wipe the asset a second time. These subjects are therefore reported here, in full,");
    W("    and are neither counted as introduced nor as worsened.");
    W($"    structurally-unsatisfiable premises (head arm) : {headAudit.Structural.Count}");
    W($"    structurally-unsatisfiable premises (live arm) : {liveAudit.Structural.Count}");
    W("    per family (head arm):");
    foreach (var g in headAudit.Structural
                 .GroupBy(t => Family(t.Split(" | ")[1].Replace('/', '\t')))
                 .OrderBy(g => g.Key, StringComparer.Ordinal))
        W($"      {g.Key,-6} {g.Count(),4} subject(s)");
    W("    THE CRUX CASE, in full (this is the row that made the harness contradict itself):");
    foreach (var t in headAudit.Structural.Where(t => t.Contains("G6-001", StringComparison.Ordinal)))
        W($"      UNSATISFIABLE  head  {t}");
    foreach (var t in liveAudit.Structural.Where(t => t.Contains("G6-001", StringComparison.Ordinal)))
        W($"      UNSATISFIABLE  live  {t}");
    W("    every unsatisfiable premise, head arm:");
    foreach (var t in headAudit.Structural) W($"      UNSATISFIABLE  head  {t}");
    W("    every unsatisfiable premise, live arm:");
    foreach (var t in liveAudit.Structural) W($"      UNSATISFIABLE  live  {t}");
    W();

    // ---- WHAT STILL COVERS THEM — audit #3 finding [4] (MEDIUM). ------------------------------------
    // The exclusion predicate itself was independently verified SOUND and must NOT be narrowed. What was
    // missing is that NOTHING in the report said "on these subjects checks 6/7/8 have stood down and the
    // point oracle is the sole defence" — and a 5x inflation of the crux passed all three (only CHECK 3 and
    // CHECK 9(b) convicted it). So: state the cover PER SUBJECT, and ASSERT that every excluded subject has
    // some point oracle left, so the day a family lands here with no cover the harness says so instead of
    // printing three PASSes.
    W("  WHAT STILL COVERS THE EXCLUDED SUBJECTS (checks 6/7/8 have STOOD DOWN on every subject below)");
    W("    The exclusion is SOUND — the predicate is computed only from Fact* rows, which corpus integrity");
    W("    pins identical on both arms, so an ENGINE CANNOT PUT ITSELF INTO THIS BUCKET. But it does");
    W("    concentrate all remaining risk on the point oracles, and an engine wrong ONLY here (value x5 on");
    W("    the structurally-unsatisfiable subjects) passed 6, 7 and 8 together. CHECK 3 and CHECK 9(b) are");
    W("    what convicted it. The cover is therefore enumerated, and its EXISTENCE is asserted.");
    var structCover = StructuralCover(headAudit, liveAudit, live);
    var uncovered = structCover.Where(c => c.Value.Count == 0).Select(c => c.Key).ToList();
    W($"    distinct structurally-unsatisfiable subjects : {structCover.Count}");
    var coverTally = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var kv in structCover)
    {
        var label = kv.Value.Count == 0 ? "*** NO POINT ORACLE ***" : string.Join(" + ", kv.Value);
        coverTally[label] = coverTally.GetValueOrDefault(label) + 1;
    }
    foreach (var kv in coverTally) W($"      cover: {kv.Key,-46} {kv.Value,4} subject(s)");
    foreach (var kv in structCover)
        W($"      COVER  {kv.Key}  ::  {(kv.Value.Count == 0 ? "*** NO POINT ORACLE — UNMEASURED ***" : string.Join(" + ", kv.Value))}");
    W($"    subjects with NO point-oracle cover : {uncovered.Count}   => {Verdict(uncovered.Count == 0)}");
    foreach (var u in uncovered.Take(40)) W($"      UNCOVERED  {u}");
    if (structCover.Count > 0 && uncovered.Count > 0)
        harnessFailures.Add(
            $"STRUCTURAL COVER: {uncovered.Count} structurally-unsatisfiable subject(s) are covered by NO point " +
            "oracle. Checks 6/7/8 stand down there by design, so those subjects are measured by NOTHING — the " +
            "harness would print three PASSes over an unmeasured value. Add a point oracle before judging.");
    W();

    // Magnitude-aware classification.
    var introduced = new List<string>();
    var worsened = new List<string>();
    var preExisting = new List<string>();
    var resolved = new List<string>();
    var unsatisfiable = new List<string>();

    foreach (var kv in liveAudit.Findings.OrderBy(kv => kv.Key.Check + "|" + kv.Key.Subject, StringComparer.Ordinal))
    {
        var headHas = headAudit.Findings.TryGetValue(kv.Key, out var hf);

        // THE GATE. On a structurally-unsatisfiable subject, EVERY value violates, so a magnitude
        // comparison between the two arms is meaningless and — because the magnitude rises with the
        // closing value — actively points the wrong way. Bucket it, never classify it.
        if (kv.Value.Structural || (headHas && hf!.Structural))
        {
            unsatisfiable.Add(
                $"{kv.Key.Check} | {kv.Key.Subject} | head magnitude " +
                $"{(headHas ? Num(hf!.Magnitude) + "p" : "NO VIOLATION")} | live magnitude " +
                $"{Num(kv.Value.Magnitude)}p | {kv.Value.Text}");
            continue;
        }

        if (!headHas)
        {
            introduced.Add($"{kv.Key.Check} | {kv.Key.Subject} | head: NO VIOLATION | live: {kv.Value.Text} [magnitude {Num(kv.Value.Magnitude)}p]");
            continue;
        }
        if (kv.Value.Magnitude > hf!.Magnitude)
            worsened.Add($"{kv.Key.Check} | {kv.Key.Subject} | head magnitude {Num(hf.Magnitude)}p ({hf.Text}) | live magnitude {Num(kv.Value.Magnitude)}p ({kv.Value.Text})");
        else
            preExisting.Add($"{kv.Key.Check} | {kv.Key.Subject} | head {Num(hf.Magnitude)}p | live {Num(kv.Value.Magnitude)}p");
    }

    foreach (var kv in headAudit.Findings.OrderBy(kv => kv.Key.Check + "|" + kv.Key.Subject, StringComparer.Ordinal))
    {
        if (liveAudit.Findings.ContainsKey(kv.Key)) continue;
        if (kv.Value.Structural)
        {
            unsatisfiable.Add($"{kv.Key.Check} | {kv.Key.Subject} | head magnitude {Num(kv.Value.Magnitude)}p | live: NO VIOLATION (premise unsatisfiable at head)");
            continue;
        }
        // AUDIT FIX C3: a violation is only "resolved" if the live arm produced a REAL VALUE there.
        // An engine that throws must never be credited with resolving anything.
        var liveValue = live.GetValueOrDefault(kv.Key.RowKey, "<MISSING>");
        if (liveValue.StartsWith("EXC:", StringComparison.Ordinal) || liveValue == "<MISSING>")
        {
            engineFailures.Add($"CHECK 6/7/8: head violation at {kv.Key.Subject} vanished because live produced '{liveValue}'.");
            W($"    NOT-RESOLVED (live produced '{liveValue}')  {kv.Key.Check} | {kv.Key.Subject}");
            continue;
        }
        resolved.Add($"{kv.Key.Check} | {kv.Key.Subject} | head {Num(kv.Value.Magnitude)}p ({kv.Value.Text}) | live value {liveValue}p — IN BAND");
    }

    W($"  LIVE-INTRODUCED violations (FAIL)          : {introduced.Count}");
    foreach (var v in introduced.Take(80)) W($"    INTRODUCED  {v}");
    if (introduced.Count > 80) W($"    ... and {introduced.Count - 80} more");
    W($"  WORSENED violations, same key (FAIL)       : {worsened.Count}");
    foreach (var v in worsened.Take(80)) W($"    WORSENED    {v}");
    if (worsened.Count > 80) W($"    ... and {worsened.Count - 80} more");
    W($"  PRE-EXISTING, not worse (reported only)    : {preExisting.Count}");
    foreach (var v in preExisting.Take(80)) W($"    PRE-EXISTING  {v}");
    if (preExisting.Count > 80) W($"    ... and {preExisting.Count - 80} more");
    W($"  RESOLVED by live, with a real value        : {resolved.Count}");
    foreach (var v in resolved.Take(80)) W($"    RESOLVED    {v}");
    if (resolved.Count > 80) W($"    ... and {resolved.Count - 80} more");
    W($"  STRUCTURALLY-UNSATISFIABLE (no verdict)    : {unsatisfiable.Count}");
    // Printed IN FULL, deliberately: the audit's requirement is that these are excluded from the
    // classification but that THEIR NUMBERS STAY VISIBLE. A truncated bucket would hide the one row
    // (G6-001) whose head-vs-live magnitudes are the whole reason the bucket exists.
    foreach (var v in unsatisfiable) W($"    UNSATISFIABLE  {v}");
    W($"                             => {Verdict(introduced.Count == 0 && worsened.Count == 0)}");
    if (introduced.Count > 0) engineFailures.Add($"CHECKS 6/7/8: {introduced.Count} live-introduced violations.");
    if (worsened.Count > 0) engineFailures.Add($"CHECKS 6/7/8: {worsened.Count} violations worse on live than head.");
    W();

    // =============================================================================================
    // THE CENSUS GATE — "evaluated > 0" was never enough.
    // =============================================================================================
    W("================================================================================");
    W("CENSUS GATE — every check must evaluate the SAME subjects it evaluated before");
    W("================================================================================");
    W("  Every check asserted only 'evaluated > 0'. The most realistic wrong-fix shape — the engine");
    W("  refuses the voucher at posting time — makes Corpus.Build throw for every G*/E1 scenario, so those");
    W("  rows are simply ABSENT from the live arm; the point oracle iterates live.Keys, so absent rows are");
    W("  neither evaluated nor counted as mismatches. Check 3 went from 332 subjects to 134 AND PRINTED");
    W("  PASS. Checks 5, 9, 10 passed. Checks 6/7/8 printed 'live 0/0' for E1 and every G family and STILL");
    W("  printed PASS, because the assertion only fired when the WHOLE-ARM sum was zero. Nothing in the");
    W("  exit code or the verdict block said 'I measured 40% of what I measured last time'.");
    W();
    W("  TWO independent pins, both hard failures (exit 3 — the oracle has lost coverage, so judge NOTHING):");
    W("   (1) RECORDED: the HEAD arm's counts must equal the census recorded in Census.cs. That catches a");
    W("       corpus or emitter regression that shrinks BOTH arms identically, which a head-vs-live");
    W("       comparison cannot see. Re-recording is a deliberate edit to a source file, never a side effect.");
    W("   (2) LIVE vs HEAD: the live arm's count must equal the head arm's, cell by cell.");
    W("  A correct fix does NOT trip this. Checks 6/7/8 only evaluate a subject whose closing QUANTITY is");
    W("  positive, and closing quantity is pinned independently against the spec by CHECK 5 — so an engine");
    W("  that leaves quantities alone (which a conformant one must) leaves the census alone. Verified: the");
    W("  reference-conformant engine built by bite/accept-probe.sh shrinks 0 cells and grows 0 cells. An");
    W("  engine that DOES move the census has changed what there is to measure, and that is exactly the");
    W("  event this gate exists to refuse to judge.");
    W();

    var headPt = PointOracle(head, head, "ClosingValuePaisa", "RefClosingValuePaisa", m => m is "Fifo" or "Lifo", true);
    var headPtAvg = PointOracle(head, head, "ClosingValuePaisa", "RefClosingValueDebtAwarePaisa", m => m == "AverageCost", true);
    var headPtFlat = PointOracle(head, head, "ClosingValuePaisa", "RefClosingValuePaisa",
                                 m => m is "StandardCost" or "LastPurchaseCost" or "LastSaleCost", true);
    var headQ1 = PointOracle(head, head, "ClosingQtyMicro", "RefClosingQtyMicro", _ => true, true);
    var headQ2 = PointOracle(head, head, "OnHandMicro", "RefOnHandMicro", _ => true, true);
    var headTotalPt = PointOracle(head, head, "TotalClosingPaisa", "RefTotalClosingPaisa", _ => true, false);
    var headIssueEvaluated = issueMeasures.Sum(measure =>
        PointOracle(head, head, measure, "Ref" + measure, _ => true, true).Evaluated);

    var liveCensus = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        ["BUILD.ok"] = liveBuild.Ok,
        ["CHECK1.rows"] = keys.Count(k => neverNeg.Contains(Col(k, 0)) && live.ContainsKey(k)),
        ["CHECK2.rows"] = live.Keys.Count(k => Col(k, 2) == "AverageCost"),
        ["CHECK2.subjects"] = check2Pt.Evaluated,
        ["CHECK3.subjects"] = pt.Evaluated,
        ["CHECK3b.subjects"] = ptFlat.Evaluated,
        ["CHECK4.subjects"] = cal.Subjects,
        ["CHECK4b.subjects"] = avgCal.Subjects,
        // CHECK4b.selfConsistency was REMOVED on 2026-07-27 (audit #4 finding [2]): it pinned the size of a
        // tautological comparison, which made a gate that cannot fail look like measured coverage.
        ["CHECK4c.goldens"] = gold.Evaluated,
        // ROUND 6 (audit #5). issueGoldens pins the table that anchors the ISSUE arm — finding [0];
        // goldenDigest pins the CONSTANTS THEMSELVES, not merely how many there are — finding [1];
        // inventedSubjects pins the SPEC-derived population the coverage assertion iterates — finding [3];
        // issueStructurePairs pins that the constant-free structural assertion evaluated something.
        ["CHECK4c.issueGoldens"] = gold.IssueEvaluated,
        ["CHECK4c.goldenDigest"] = Goldens.Digest(),
        ["CHECK4c.inventedSubjects"] = invPop.SpecSubjects,
        ["CHECK4c.issueStructurePairs"] = issueStruct.AtOrAbovePairs,
        // ROUND 7 (audit #6 finding [1]). clauseVerified pins the number of goldens whose SELF-DECLARED
        // clause label was checked against the spec-derived FactDebtShape. Without it the label check
        // could quietly stop evaluating anything and clause coverage would silently revert to being
        // asserted from the golden table's own tags — the exact self-attestation it exists to end.
        ["CHECK4c.clauseVerified"] = gold.ClauseChecked,
        ["VALUE-INVARIANT.originBoundLayers"] = val.OriginBoundLayers,
        ["VALUE-INVARIANT.hullBlendLayers"] = val.BlendLayers,
        ["VALUE-INVARIANT.orderingSubjects"] = val.OrderingConstrainedSubjects,
        ["VALUE-INVARIANT.orderingLayers"] = val.OrderingTestedLayers,
        ["VALUE-INVARIANT.perLotChecks"] = val.PerLotChecks,
        ["CHECK5.closingQty"] = q1.Evaluated,
        ["CHECK5.onHand"] = q2.Evaluated,
        ["CHECK9.totalSum"] = totalSum.Evaluated,
        ["CHECK9.totalOracle"] = totalPoint.Evaluated,
        ["CHECK10.subjects"] = issueEvaluated,
        ["CHECK11.rows"] = live.Count,
        ["VALUE-INVARIANT.subjects"] = val.Checked,
        ["SELF-CONSISTENCY.subjects"] = refSelfChecked,
    };
    var headCensus = new SortedDictionary<string, int>(StringComparer.Ordinal)
    {
        ["BUILD.ok"] = headBuild.Ok,
        ["CHECK1.rows"] = keys.Count(k => neverNeg.Contains(Col(k, 0)) && head.ContainsKey(k)),
        ["CHECK2.rows"] = head.Keys.Count(k => Col(k, 2) == "AverageCost"),
        ["CHECK2.subjects"] = headPtAvg.Evaluated,
        ["CHECK3.subjects"] = headPt.Evaluated,
        ["CHECK3b.subjects"] = headPtFlat.Evaluated,
        ["CHECK4.subjects"] = cal.Subjects,
        ["CHECK4b.subjects"] = avgCal.Subjects,
        // CHECK4b.selfConsistency was REMOVED on 2026-07-27 (audit #4 finding [2]): it pinned the size of a
        // tautological comparison, which made a gate that cannot fail look like measured coverage.
        ["CHECK4c.goldens"] = gold.Evaluated,
        // ROUND 6 (audit #5). issueGoldens pins the table that anchors the ISSUE arm — finding [0];
        // goldenDigest pins the CONSTANTS THEMSELVES, not merely how many there are — finding [1];
        // inventedSubjects pins the SPEC-derived population the coverage assertion iterates — finding [3];
        // issueStructurePairs pins that the constant-free structural assertion evaluated something.
        ["CHECK4c.issueGoldens"] = gold.IssueEvaluated,
        ["CHECK4c.goldenDigest"] = Goldens.Digest(),
        ["CHECK4c.inventedSubjects"] = invPop.SpecSubjects,
        ["CHECK4c.issueStructurePairs"] = issueStruct.AtOrAbovePairs,
        // ROUND 7 (audit #6 finding [1]). clauseVerified pins the number of goldens whose SELF-DECLARED
        // clause label was checked against the spec-derived FactDebtShape. Without it the label check
        // could quietly stop evaluating anything and clause coverage would silently revert to being
        // asserted from the golden table's own tags — the exact self-attestation it exists to end.
        ["CHECK4c.clauseVerified"] = gold.ClauseChecked,
        ["VALUE-INVARIANT.originBoundLayers"] = val.OriginBoundLayers,
        ["VALUE-INVARIANT.hullBlendLayers"] = val.BlendLayers,
        ["VALUE-INVARIANT.orderingSubjects"] = val.OrderingConstrainedSubjects,
        ["VALUE-INVARIANT.orderingLayers"] = val.OrderingTestedLayers,
        ["VALUE-INVARIANT.perLotChecks"] = val.PerLotChecks,
        ["CHECK5.closingQty"] = headQ1.Evaluated,
        ["CHECK5.onHand"] = headQ2.Evaluated,
        ["CHECK9.totalSum"] = totalHeadSum.Evaluated,
        ["CHECK9.totalOracle"] = headTotalPt.Evaluated,
        ["CHECK10.subjects"] = headIssueEvaluated,
        ["CHECK11.rows"] = head.Count,
        ["VALUE-INVARIANT.subjects"] = val.Checked,
        ["SELF-CONSISTENCY.subjects"] = refSelfChecked,
    };
    // Checks 6/7/8 are pinned per (check, family, method) cell, not just per check: the collapse showed
    // 'live 0/0' in EVERY G cell while the whole-arm sum stayed non-zero.
    foreach (var kv in headAudit.Census)
        headCensus[$"{kv.Key.Check}|{kv.Key.Family}|{kv.Key.Method}"] = kv.Value;
    foreach (var kv in liveAudit.Census)
        liveCensus[$"{kv.Key.Check}|{kv.Key.Family}|{kv.Key.Method}"] = kv.Value;

    // ---- (1) RECORDED expected census, asserted against the HEAD arm.
    var expected = ExpectedCensus.Parse();
    W($"  (1) RECORDED census: {expected.Count} cells recorded in Census.cs, {headCensus.Count} produced by the head arm");
    if (expected.Count == 0)
    {
        harnessFailures.Add("CENSUS GATE: no expected census is recorded in Census.cs — the harness cannot detect coverage shrinkage.");
        W("      *** NO EXPECTED CENSUS RECORDED — the harness cannot detect coverage shrinkage. ***");
    }
    else
    {
        var recordedBad = new List<string>();
        foreach (var k in expected.Keys.Concat(headCensus.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            var e = expected.GetValueOrDefault(k, -1);
            var a = headCensus.GetValueOrDefault(k, -1);
            if (e != a) recordedBad.Add($"{k}: recorded {(e < 0 ? "ABSENT" : e.ToString(CultureInfo.InvariantCulture))}, head arm {(a < 0 ? "ABSENT" : a.ToString(CultureInfo.InvariantCulture))}");
        }
        W($"      cells disagreeing with the recording : {recordedBad.Count}   => {Verdict(recordedBad.Count == 0)}");
        foreach (var b in recordedBad.Take(60)) W($"      CENSUS-RECORDED  {b}");
        if (recordedBad.Count > 60) W($"      ... and {recordedBad.Count - 60} more");
        if (recordedBad.Count > 0)
            harnessFailures.Add(
                $"CENSUS GATE (recorded): {recordedBad.Count} cells differ from the census recorded in Census.cs. " +
                "Coverage changed. If the change is intended, RE-RECORD Census.cs deliberately and say why.");
    }
    W();

    // ---- (2) LIVE vs HEAD, cell by cell. Any shrinkage is fatal.
    var shrunk = new List<string>();
    var grew = new List<string>();
    foreach (var k in headCensus.Keys.Concat(liveCensus.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
    {
        var h = headCensus.GetValueOrDefault(k, 0);
        var l = liveCensus.GetValueOrDefault(k, 0);
        if (l < h) shrunk.Add($"{k}: head {h}, live {l}   (LOST {h - l})");
        else if (l > h) grew.Add($"{k}: head {h}, live {l}   (GAINED {l - h})");
    }
    W($"  (2) LIVE vs HEAD: {headCensus.Count} head cells, {liveCensus.Count} live cells");
    W($"      cells that SHRANK on live : {shrunk.Count}   => {Verdict(shrunk.Count == 0)}");
    foreach (var b in shrunk.Take(60)) W($"      CENSUS-SHRANK  {b}");
    if (shrunk.Count > 60) W($"      ... and {shrunk.Count - 60} more");
    W($"      cells that GREW on live   : {grew.Count}   => {Verdict(grew.Count == 0)}");
    foreach (var b in grew.Take(60)) W($"      CENSUS-GREW    {b}");
    if (shrunk.Count > 0)
        harnessFailures.Add($"CENSUS GATE (live vs head): {shrunk.Count} cells evaluate FEWER subjects on the live arm. The oracle has lost coverage — judge nothing.");
    if (grew.Count > 0)
        harnessFailures.Add($"CENSUS GATE (live vs head): {grew.Count} cells evaluate MORE subjects on the live arm — the two arms are not measuring the same corpus.");
    W();
    W("  FULL CENSUS (head arm) — this is what Census.cs must record");
    foreach (var kv in headCensus) W($"    CENSUS  {kv.Key}\t{kv.Value}");
    W();

    // ---------------- HEAD SELF-CHARACTERISATION -------------------------------------------------
    W("--------------------------------------------------------------------------------");
    W("HEAD SELF-CHARACTERISATION — every absolute violation in the HEAD arm, worked out.");
    W("Computed from the SPEC, so it stands whether or not live == head. This is the yardstick.");
    W("--------------------------------------------------------------------------------");
    foreach (var check in checkNames)
    {
        var hits = headAudit.Findings.Where(kv => kv.Key.Check == check)
            .OrderBy(kv => kv.Key.Subject, StringComparer.Ordinal).ToList();
        W($"  {check}: {hits.Count} violation(s) at HEAD");
        foreach (var kv in hits.Take(60)) W($"    HEAD  {kv.Key.Subject}  ::  {kv.Value.Text}");
        if (hits.Count > 60) W($"    ... and {hits.Count - 60} more");
        W();
    }

    W("--------------------------------------------------------------------------------");
    W("HEAD vs REFERENCE — THE QUANTITATIVE STATEMENT OF THE DEFECT (per family, FIFO/LIFO)");
    W("The reference is calibrated on N*, so on G* it is the oracle. These are the rupees the");
    W("Balance Sheet is wrong by TODAY; a fix must drive every one of them to zero.");
    W("--------------------------------------------------------------------------------");
    var defect = HeadVsReference(head);
    W("  family | subjects | disagreeing | max |head-ref| paisa | worst subject");
    foreach (var row in defect) W("  " + row);
    W();

    // ---------------- reporting-only signals — surfaced, NEVER clamped ----------------------------
    W("REPORTING-ONLY SIGNALS (surfaced, never clamped, never failed on)");
    foreach (var (label, rows) in new[] { ("head", head), ("live", live) })
    {
        var neg = ValueSignal(rows, negative: true);
        W($"  {label}: NEGATIVE value on positive quantity : {neg.Count}");
        foreach (var n in neg.Take(30)) W($"      {n}");
        var zero = ValueSignal(rows, negative: false);
        W($"  {label}: ZERO value on positive quantity     : {zero.Count}");
        foreach (var n in zero.Take(30)) W($"      {n}");
        var inexact = rows.Count(kv => kv.Key.EndsWith("\tClosingValueIsPaisaExact", StringComparison.Ordinal) && kv.Value == "0");
        W($"  {label}: NON-paisa-exact closing values      : {inexact}");
    }
    W();

    // ---------------- E1 ordering determinism ----------------------------------------------------
    var headE1 = E1Mismatches(head);
    var liveE1 = E1Mismatches(live);
    W("E1 ORDERING DETERMINISM — E1-001 vs E1-002 (identical vouchers, opposite insertion order)");
    W($"  head mismatches            : {headE1.Count}");
    foreach (var m in headE1.Take(20)) W($"      {m}");
    W($"  live mismatches            : {liveE1.Count}   => {Verdict(liveE1.Count <= headE1.Count)}");
    foreach (var m in liveE1.Take(20)) W($"      {m}");
    if (liveE1.Count > headE1.Count) engineFailures.Add("E1: live made ordering insertion-dependent.");
    W();

    W("  divergence from head, per family (max absolute, max relative)");
    W("  family | rows  | diffs | max |live-head|      | max relative");
    W("  -------+-------+-------+----------------------+-------------");
    foreach (var fam in familyRowCount.Keys)
        W($"  {fam,-6} | {familyRowCount[fam],5} | {familyDiffCount[fam],5} | {Num(familyMaxAbs[fam]),20} | {Num(familyMaxRel[fam])}");
    W();

    // ---------------- verdict --------------------------------------------------------------------
    W("================================================================================");
    W($"TOTAL DIFFS (all families)                 : {diffs.Count}");
    W();
    W("  CHECK  1  never-negative byte identity   : " + Verdict(check1.Count == 0));
    W("  CHECK  1M multi-key byte identity        : " + Verdict(check1m.Count == 0)
      + "   [the INERTNESS claim, measured engine-vs-engine]");
    W("  CHECK  2  point oracle (AverageCost)     : " + Verdict(ScopeSplit(check2Pt.Mismatches).Judged.Count == 0) + "   [single-key scope; vs the CALIBRATED debt-aware reference]");
    W("  CHECK  3  point oracle (FIFO/LIFO)       : " + Verdict(ScopeSplit(pt.Mismatches).Judged.Count == 0) + "   [single-key scope]");
    W("  CHECK  3b point oracle (flat methods)    : " + Verdict(ScopeSplit(ptFlat.Mismatches).Judged.Count == 0) + "   [single-key scope]");
    W("  CHECK  4  reference calibration          : " + Verdict(cal.Mismatches.Count == 0) + "   [HARNESS]");
    W("  CHECK  4b debt-aware AVG calibration     : " + Verdict(avgCal.Mismatches.Count == 0 && avgCal.Missing.Count == 0)
      + "   [HARNESS — never-negative books only; the debt clauses are DEAD CODE there]");
    W("  CHECK  4c hand-derived debt goldens      : "
      + Verdict(gold.Mismatches.Count == 0 && gold.Missing.Count == 0 && gold.UncoveredInvented.Count == 0
                && gold.UncoveredFamilies.Count == 0 && gold.UnexercisedClauses.Count == 0
                && gold.WorkingMismatches.Count == 0
                && issueStruct.AtOrAboveFailures.Count == 0 && issueStruct.OverStackFailures.Count == 0
                && issueStruct.MonotonicFailures.Count == 0
                && invPop.EmittedNotSpec.Count == 0 && invPop.SpecNotEmitted.Count == 0
                && invPop.MissingFact.Count == 0)
      + "   [HARNESS — the ONLY validation the debt branch has: CLOSING and ISSUE]");
    W("  CHECK  5  quantity oracle                : " + Verdict(q == 0));
    W("  CHECK  6  closing-rate band              : " + Verdict(!introduced.Any(v => v.StartsWith("6 ", StringComparison.Ordinal)) && !worsened.Any(v => v.StartsWith("6 ", StringComparison.Ordinal))));
    W("  CHECK  7  total-spend containment        : " + Verdict(!introduced.Any(v => v.StartsWith("7 ", StringComparison.Ordinal)) && !worsened.Any(v => v.StartsWith("7 ", StringComparison.Ordinal))));
    W("  CHECK  8  COGS conservation              : " + Verdict(!introduced.Any(v => v.StartsWith("8 ", StringComparison.Ordinal)) && !worsened.Any(v => v.StartsWith("8 ", StringComparison.Ordinal))));
    W("  CHECK  9  TotalClosingStockValue         : " + Verdict(totalSum.Mismatches.Count == 0 && ScopeSplit(totalPoint.Mismatches).Judged.Count == 0));
    W("  CHECK 10  issue value                    : " + Verdict(ScopeSplit(issueMismatch).Judged.Count == 0) + "   [single-key scope]");
    W("  CHECK 11  exception asymmetry            : " + Verdict(exc11.Count == 0));
    W("  corpus integrity (spec rows identical)   : " + Verdict(specDiffs.Count == 0) + "   [HARNESS]");
    W();
    W($"HARNESS INTEGRITY : {(harnessFailures.Count == 0 ? "SOUND" : "BROKEN")}");
    foreach (var f in harnessFailures) W($"    HARNESS  {f}");
    W($"ENGINE VERDICT    : {(engineFailures.Count == 0 ? "ACCEPTED" : "REJECTED")}");
    foreach (var f in engineFailures) W($"    ENGINE   {f}");

    var verdictExit = harnessFailures.Count > 0 ? 3 : engineFailures.Count > 0 ? 1 : 0;
    if (sandbox)
    {
        W();
        W("*** BITE TEST — MUTATED ENGINE — NOT A VERDICT ON THE WORKING TREE ***");
        W($"    the verdict above, taken alone, would exit {verdictExit}" +
          $"{(verdictExit == 0 ? "  (harness sound AND engine accepted)" : "")}");
        W("    a SANDBOX run exits 4 regardless, so it can never be pasted as a certification of src/");
    }
    W("================================================================================");

    if (diffs.Count > 0)
    {
        W();
        W("FULL DIFF LIST (scenario/item/method/asOf/measure):");
        foreach (var d in diffs.Take(400)) W($"  {d.Key}   head={d.Head}   live={d.Live}");
        if (diffs.Count > 400) W($"  ... and {diffs.Count - 400} more");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
    Console.Write(report.ToString());

    // A sandbox run NEVER exits 0. A real failure still surfaces as itself (3 beats 1 beats 4), so a bite
    // test that convicts still reads as a conviction; only "clean" is denied to a mutated engine.
    if (sandbox) return verdictExit == 0 ? 4 : verdictExit;
    if (harnessFailures.Count > 0) return 3;
    return engineFailures.Count > 0 ? 1 : 0;
}

// =============================================================== helpers

static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

static string Col(string key, int i)
{
    var p = key.Split('\t');
    return i < p.Length ? p[i] : string.Empty;
}

static string Family(string key)
{
    var scenario = key.Split('\t')[0];
    var dash = scenario.IndexOf('-');
    return dash < 0 ? scenario : scenario[..dash];
}

/// <summary>A row whose value is produced by the SPEC, not the engine — identical on both arms by design.</summary>
static bool IsSpecDerived(string key)
{
    var m = Col(key, 4);
    return m.StartsWith("Fact", StringComparison.Ordinal) || m.StartsWith("Ref", StringComparison.Ordinal);
}

/// <summary>
/// Reads an emitted TSV into (key -> value). A DUPLICATE KEY IS FATAL.
/// <para>It used to be silent: <c>map[key] = value</c> kept the last one. Corpus.cs listed the probe 1.25
/// TWICE for G2-002, so Emit wrote 'IssueValue@1.25Paisa' and 'RefIssueValue@1.25Paisa' twice per (method,
/// as-of) and 60 emitted rows vanished on EVERY run — the emitter said 20030 rows and the report header
/// said 19970, with no explanation, which trains a reader to ignore the number. Worse, a future corpus
/// edit that accidentally collided two keys would drop rows from the comparison with no diagnostic at all,
/// shrinking coverage invisibly. Now it throws, and the two counts are asserted equal.</para>
/// </summary>
static Dictionary<string, string> ReadTsv(string path)
{
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    var lineNo = 0;
    foreach (var line in File.ReadAllLines(path))
    {
        lineNo++;
        if (line.Length == 0) continue;
        if (line.StartsWith("scenario\t", StringComparison.Ordinal)) continue;
        var parts = line.Split('\t');
        if (parts.Length != 6) throw new InvalidDataException($"malformed row in {path}: {line}");
        var key = string.Join('\t', parts[..5]);
        if (map.TryGetValue(key, out var already))
            throw new InvalidDataException(
                $"DUPLICATE KEY in {path} at line {lineNo}: '{key}' — already present with value '{already}', " +
                $"now '{parts[5]}'. Two emitted rows share a key, so one of them would vanish from every " +
                "comparison. Fix the corpus (a repeated IssueProbe is the usual cause); never let this pass.");
        map[key] = parts[5];
    }
    return map;
}

static decimal? Dec(Dictionary<string, string> rows, string key)
    => rows.TryGetValue(key, out var s) &&
       decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

// ---------------- the spec-derived never-negative scope -----------------------------------------

/// <summary>
/// The scenarios the SPEC says never drive any (item, godown, batch) on-hand negative and never drain the
/// company-wide cost-layer stack into a debt. This — not "the id starts with the letter N" — is the scope
/// of byte identity (check 1) and calibration (check 4). E1 is never-negative and the letter test excluded
/// it from both, so the reference was used as an oracle on E1 having never been calibrated there.
/// </summary>
static HashSet<string> NeverNegativeScenarios(Dictionary<string, string> rows)
{
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (var kv in rows)
        if (Col(kv.Key, 4) == "FactNeverNegative" && kv.Value == "1")
            set.Add(Col(kv.Key, 0));
    return set;
}

// ---------------- the reference VALUE invariant --------------------------------------------------

/// <summary>
/// Asserts that the reference's VALUE decomposes into rates the SPEC contains, from the emitted layer
/// breakdown alone. Lives in the COMPARATOR, deliberately: the arithmetic it audits lives in Reference.cs,
/// and an invariant that shared a body with the thing it validates would validate nothing.
/// </summary>
static ValueInvariantResult ReferenceValueInvariant(Dictionary<string, string> rows)
{
    var qtyFail = new List<string>();
    var valFail = new List<string>();
    var rateFail = new List<string>();
    var originFail = new List<string>();
    var raExamples = new List<string>();
    var blendExamples = new List<string>();
    var raLayers = 0;
    var raSubjects = 0;
    var originBound = 0;
    var blendLayers = 0;
    var checkedCount = 0;
    // ---- audit #4 finding [1](2): the ORDERING assertion, and finding [5]: the AGGREGATE per-lot bound.
    var orderFail = new List<string>();
    var perLotFail = new List<string>();
    var orderSubjects = 0;
    var orderLayers = 0;
    var perLotChecks = 0;

    foreach (var key in rows.Keys.Where(k => Col(k, 4) == "RefLayerBreakdown").OrderBy(k => k, StringComparer.Ordinal))
    {
        var stem = string.Join('\t', key.Split('\t')[..4]);
        if (!rows.TryGetValue(stem + "\tRefLayerRateSources", out var srcRaw)) continue;
        if (!rows.TryGetValue(stem + "\tRefAdmissibleRates", out var admRaw)) continue;
        // ROUND 10 — the layer stack is measured against the quantity the ITEM-LEVEL replay reached
        // (Facts' own gated quantity walk), not against the PER-KEY reported closing quantity. Same
        // number on every single-key book; see REFERENCE SELF-CONSISTENCY above for why they differ.
        var pq = key.Split('\t');
        if (Dec(rows, string.Join('\t', [pq[0], pq[1], "-", pq[3], "FactFlatNetMicro"])) is not { } flatNetMicro)
            continue;
        var closingMicro = Math.Max(flatNetMicro, 0m);
        if (Dec(rows, stem + "\tRefClosingValuePaisa") is not { } closingPaisa) continue;

        // The SPEC's lot table for this (scenario, item, as-of). It lives on the method-less "-" row
        // because it is a property of the BOOK, not of a costing method.
        var p = key.Split('\t');
        var lotKey = string.Join('\t', [p[0], p[1], "-", p[3], "FactInwardLots"]);
        if (!rows.TryGetValue(lotKey, out var lotRaw)) continue;
        if (!rows.TryGetValue(stem + "\tRefLayerOrigins", out var originRaw)) continue;

        // THE ORDERING FACT (audit #4 finding [1](2) — the assertion audit #3 asked for and round 4 did
        // not build). "*" = the book never ran dry, so nothing is constrained. Anything else is the
        // EXHAUSTIVE list of tokens a surviving layer may name: the company-wide net quantity was <= 0 at
        // the last dry point, so the stack was empty there and NOTHING created at or before it can have
        // survived. A MISSING fact is a harness failure, never a skip — a silent `continue` is exactly how
        // the CHECK 4b hole existed.
        if (!rows.TryGetValue(string.Join('\t', [p[0], p[1], "-", p[3], "FactPostDryLots"]), out var postDryRaw))
        {
            orderFail.Add($"{stem}   FactPostDryLots is MISSING, so the ordering rule evaluated nothing here.");
            postDryRaw = "*";
        }
        var orderingConstrains = postDryRaw != "*";
        var postDry = orderingConstrains
            ? new HashSet<string>(postDryRaw.Length == 0 ? [] : postDryRaw.Split(';'), StringComparer.Ordinal)
            : null;
        if (orderingConstrains) orderSubjects++;

        var breakdown = rows[key];
        if (breakdown == "-") continue;
        checkedCount++;

        var pairs = breakdown.Length == 0 ? [] : breakdown.Split(';');
        var srcs = srcRaw.Length == 0 ? [] : srcRaw.Split(';');
        var origins = originRaw.Length == 0 ? [] : originRaw.Split(';');
        if (pairs.Length != srcs.Length)
        {
            rateFail.Add($"{stem}   breakdown has {pairs.Length} layers but {srcs.Length} rate sources");
            continue;
        }
        if (pairs.Length != origins.Length)
        {
            originFail.Add($"{stem}   breakdown has {pairs.Length} layers but {origins.Length} lot origins");
            continue;
        }
        var admissible = new List<decimal>();
        foreach (var a in admRaw.Split(';'))
            if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var av)) admissible.Add(av);
        var hullLo = admissible.Count > 0 ? admissible.Min() : 0m;
        var hullHi = admissible.Count > 0 ? admissible.Max() : 0m;

        // THE SPEC'S LOTS: token -> (base qty, base rate or null when the lot is unrated).
        var lots = new Dictionary<string, (decimal Qty, decimal? Rate)>(StringComparer.Ordinal);
        foreach (var lot in lotRaw.Length == 0 ? [] : lotRaw.Split(';'))
        {
            var colon = lot.IndexOf(':');
            var at2 = lot.LastIndexOf('@');
            if (colon < 0 || at2 < colon) continue;
            var token = lot[..colon];
            if (!decimal.TryParse(lot[(colon + 1)..at2], NumberStyles.Any, CultureInfo.InvariantCulture, out var lotQty)) continue;
            var rateText = lot[(at2 + 1)..];
            lots[token] = (lotQty,
                decimal.TryParse(rateText, NumberStyles.Any, CultureInfo.InvariantCulture, out var lotRate) ? lotRate : null);
        }

        var sumQty = 0m;
        var sumVal = 0m;
        var raHere = 0;
        var perLot = new Dictionary<string, decimal>(StringComparer.Ordinal);
        for (var i = 0; i < pairs.Length; i++)
        {
            var at = pairs[i].IndexOf('@');
            if (at < 0 ||
                !decimal.TryParse(pairs[i][..at], NumberStyles.Any, CultureInfo.InvariantCulture, out var lq) ||
                !decimal.TryParse(pairs[i][(at + 1)..], NumberStyles.Any, CultureInfo.InvariantCulture, out var lr))
            {
                rateFail.Add($"{stem}   unparseable layer '{pairs[i]}'");
                continue;
            }
            sumQty += lq;
            sumVal += lq * lr;

            var origin = origins[i];
            perLot[origin] = perLot.GetValueOrDefault(origin) + lq;

            // ------------------------------------------------------------ (d) THE ORDERING ASSERTION
            // A PURE ORDERING FACT that value arithmetic cannot fake. It is the ONLY thing that convicts
            // the poison audit #4 demonstrated at a10r4-h2-survivors: resurrect the drained lot's units
            // after the repayment, so every layer is TRUTHFULLY bound to a real lot at that lot's real
            // spec rate. The whole rate binding passes 0/0/0/0 on it; only "those units were provably
            // consumed before this point" kills it.
            if (postDry is not null)
            {
                orderLayers++;
                if (!postDry.Contains(origin))
                    orderFail.Add(
                        $"{stem}   layer {Num(lq)}@{Num(lr)} names lot '{origin}', but the company-wide stack ran DRY " +
                        $"after it (spec quantity walk), so those units were provably consumed. Only these lots can " +
                        $"still be on the stack: [{postDryRaw}]. A layer surviving from before the dry point is the " +
                        "resurrect-the-drained-lot shape, which every rate test acquits because the rate is genuine.");
            }

            // ------------------------------------------------------------------ (c) THE RATE BINDING
            // AUDIT #3 FINDING [1] (HIGH) and FINDING [3] (MEDIUM). The old test asked only whether the
            // rate was a MEMBER of the admissible set, and WAIVED even that whenever the layer's own
            // emitted tag said "RunningAverage" — a tag produced by the very file the invariant audits.
            // Both holes are closed here by asking the SPEC instead:
            //   * a layer whose ORIGIN LOT carries an explicit rate MUST be priced at THAT lot's rate.
            //     No set membership, no tag, no exemption: the best-available-cost chain is unreachable
            //     for a rated lot, so a "RunningAverage" tag on one is itself a defect.
            //   * a layer from an UNRATED lot, or a count-up layer (no lot at all), is the only place the
            //     chain can fire. There the rate must either be admissible outright, or — if the chain
            //     answered with a weighted BLEND — lie inside the convex hull [min, max] of the admissible
            //     set, which a weighted average of admissible rates provably cannot leave.
            var isCountUp = origin.StartsWith("CNT", StringComparison.Ordinal);
            if (!isCountUp)
            {
                if (!lots.TryGetValue(origin, out var lot))
                {
                    originFail.Add(
                        $"{stem}   layer {Num(lq)}@{Num(lr)} claims to come from lot '{origin}', which the SPEC's lot " +
                        $"table DOES NOT CONTAIN. Spec lots: [{lotRaw}]");
                    continue;
                }

                if (lq > lot.Qty)
                    originFail.Add(
                        $"{stem}   layer claims {Num(lq)} surviving units from lot '{origin}', which only ever " +
                        $"supplied {Num(lot.Qty)}. Spec lots: [{lotRaw}]");

                if (lot.Rate is { } specRate)
                {
                    originBound++;
                    if (srcs[i] == "RunningAverage")
                        originFail.Add(
                            $"{stem}   layer {Num(lq)}@{Num(lr)} from lot '{origin}' is TAGGED 'RunningAverage', but " +
                            $"that lot carries an EXPLICIT rate of {Num(specRate)} in the spec, so the " +
                            "best-available-cost chain is unreachable for it. A tag cannot excuse a rate.");
                    if (lr != specRate)
                        originFail.Add(
                            $"{stem}   layer {Num(lq)}@{Num(lr)} comes from lot '{origin}', which the SPEC prices at " +
                            $"{Num(specRate)}. THE WRONG ADMISSIBLE RATE IS STILL THE WRONG RATE — this is the " +
                            "re-rating shape (pricing a repayment surplus at the rate of the stock that ran out). " +
                            $"Spec lots: [{lotRaw}]");
                    continue;   // fully bound to the spec; nothing weaker to add
                }
            }

            // Unrated lot, or a count-up: the chain legitimately answers here.
            if (admissible.Contains(lr)) continue;

            if (lr >= hullLo && lr <= hullHi)
            {
                blendLayers++;
                if (srcs[i] == "RunningAverage") raHere++;
                if (blendExamples.Count < 10)
                    blendExamples.Add(
                        $"{stem}   layer {Num(lq)}@{Num(lr)} from {(isCountUp ? "a physical count-up" : "unrated lot '" + origin + "'")} " +
                        $"is a BLEND inside the admissible hull [{Num(hullLo)}, {Num(hullHi)}] (tag {srcs[i]})");
                continue;
            }

            rateFail.Add(
                $"{stem}   layer {Num(lq)}@{Num(lr)} (source {srcs[i]}, origin {origin}) is priced at a rate the " +
                $"SPEC DOES NOT CONTAIN and OUTSIDE the admissible hull [{Num(hullLo)}, {Num(hullHi)}]. " +
                $"Admissible: [{admRaw}]. A weighted blend of admissible rates cannot leave their convex hull.");
        }
        if (raHere > 0)
        {
            raLayers += raHere;
            raSubjects++;
            if (raExamples.Count < 10) raExamples.Add($"{stem}   {raHere} layer(s) priced from a running-average blend: {breakdown}");
        }

        // ---------------------------------------------------- (e) THE AGGREGATE PER-LOT BOUND
        // AUDIT #4 FINDING [5] (LOW). `perLot` was accumulated and NEVER READ — the third instance of that
        // shape in this file. The only quantity constraint was PER LAYER (`lq > lot.Qty`), so a reference
        // that SPLIT an over-claim across several layers from the same lot escaped while the counter still
        // read 0: two layers of 8 units each from a lot that only ever supplied 10 passed. The aggregate is
        // now asserted, and the number of (subject, lot) pairs it bounds is a census cell so it cannot
        // quietly stop evaluating.
        foreach (var (token, claimed) in perLot.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (token.StartsWith("CNT", StringComparison.Ordinal)) continue;   // no lot supplied a count-up
            if (!lots.TryGetValue(token, out var lot)) continue;               // already convicted above
            perLotChecks++;
            if (claimed > lot.Qty)
                perLotFail.Add(
                    $"{stem}   the surviving layers claim {Num(claimed)} units IN AGGREGATE from lot '{token}', which " +
                    $"only ever supplied {Num(lot.Qty)}. Every layer is individually under the lot size, which is why " +
                    $"the per-layer test acquits this. Breakdown: {breakdown} / origins: {originRaw}");
        }

        if (Math.Round(sumQty * 1_000_000m, 0, MidpointRounding.AwayFromZero) != closingMicro)
            qtyFail.Add($"{stem}   layer qty {Num(sumQty)} != replay net {Num(closingMicro / 1_000_000m)}");

        var decomposed = Math.Round(Math.Round(sumVal, 2, MidpointRounding.AwayFromZero) * 100m, 4);
        if (decomposed != Math.Round(closingPaisa, 4))
            valFail.Add($"{stem}   layers decompose to {Num(decomposed)}p but RefClosingValuePaisa is {Num(closingPaisa)}p");
    }

    return new ValueInvariantResult(checkedCount, qtyFail, valFail, rateFail, raLayers, raSubjects, raExamples,
                                    originBound, originFail, blendLayers, blendExamples,
                                    orderSubjects, orderLayers, orderFail, perLotChecks, perLotFail);
}

// ---------------- reference provenance census ----------------------------------------------------

static List<string> ProvenanceCensus(Dictionary<string, string> rows)
{
    var tally = new SortedDictionary<(string Family, string Method), int[]>();
    foreach (var kv in rows)
    {
        if (Col(kv.Key, 4) != "RefProvenance") continue;
        var k = (Family(kv.Key), Col(kv.Key, 2));
        if (!tally.TryGetValue(k, out var cells)) { cells = new int[4]; tally[k] = cells; }
        // Slot 3 is the UNRECOGNISED bucket. It exists so a tag nobody knows about cannot vanish into an
        // existing column: ECHO-OF-HEAD was retired on 2026-07-27 and any reappearance must be loud.
        var slot = kv.Value switch
        {
            RefProvenance.Calibrated => 0,
            RefProvenance.Brief => 1,
            RefProvenance.Invented => 2,
            _ => 3,
        };
        cells[slot]++;
    }
    return tally.Select(kv => $"{kv.Key.Family,-6} | {kv.Key.Method,-12} | {kv.Value[0],10} | {kv.Value[1],5} | {kv.Value[2],8}"
                             + (kv.Value[3] > 0 ? $"   *** {kv.Value[3]} UNRECOGNISED PROVENANCE TAG(S) ***" : ""))
                .ToList();
}

// ---------------- the debt-aware AverageCost divergence ------------------------------------------

/// <summary>
/// HEAD's AverageCost against the debt-aware moving average, per family — the MAGNITUDE, in rupees and by
/// subject, behind the convictions CHECK 2 issues against that same column. This block issues no verdict of
/// its own; CHECK 2 does.
/// <para>ROUND 7 (audit #6) corrected this summary. It used to read "reported as a magnitude and never
/// failed on — check 2 forbids changing AverageCost", and to describe the pre-2026-07-27 state as one where
/// "the only AverageCost number in the report was a tautological zero produced by a reference that echoes
/// HEAD". CHECK 2 was INVERTED on 2026-07-27 and now convicts rather than forbidding, and the echoing
/// reference it named stopped existing when Reference.Value's AverageCost arm moved to
/// RunAverageDebtAware.</para>
/// </summary>
static AvgDefectResult DebtAwareAverageDefect(Dictionary<string, string> head)
{
    var perFamily = new SortedDictionary<string, DefectRow>(StringComparer.Ordinal);
    var detail = new List<string>();
    var subjects = 0;
    var disagreeing = 0;

    foreach (var key in head.Keys.Where(k => Col(k, 4) == "ClosingValuePaisa" && Col(k, 1) != "-" && Col(k, 2) == "AverageCost")
                                 .OrderBy(k => k, StringComparer.Ordinal))
    {
        var refKey = string.Join('\t', key.Split('\t')[..4]) + "\tRefClosingValueDebtAwarePaisa";
        if (Dec(head, key) is not { } hv || Dec(head, refKey) is not { } dv) continue;

        subjects++;
        var fam = Family(key);
        if (!perFamily.TryGetValue(fam, out var row)) { row = new DefectRow(); perFamily[fam] = row; }
        row.Subjects++;
        var delta = Math.Abs(hv - dv);
        if (hv != dv)
        {
            row.Bad++;
            disagreeing++;
            var pct = dv != 0m ? Num(Math.Round((hv - dv) / dv * 100m, 2)) + "%" : "n/a";
            detail.Add($"{key.Replace('\t', '/')}   head={Num(hv)}p   debt-aware={Num(dv)}p   gap={Num(hv - dv)}p ({pct})");
        }
        if (delta > row.Max)
        {
            row.Max = delta;
            row.Worst = $"{key.Replace('\t', '/')}  head={Num(hv)}p  debt-aware={Num(dv)}p";
        }
    }

    var rows = perFamily.Select(kv =>
        $"{kv.Key,-6} | {kv.Value.Subjects,8} | {kv.Value.Bad,11} | {Num(kv.Value.Max),20} | {kv.Value.Worst}").ToList();
    return new AvgDefectResult(subjects, disagreeing, rows, detail);
}

// ---------------- structural-exclusion COVER --------------------------------------------------------

/// <summary>
/// AUDIT #3 FINDING [4] (MEDIUM). For every subject checks 6/7/8 have excluded as structurally
/// unsatisfiable, which POINT ORACLE still holds it — determined from the emitted rows, not asserted in
/// prose. A subject is covered when the LIVE arm actually carries both the engine measure and the
/// reference measure the corresponding point oracle compares, so a missing row shows up as no cover
/// rather than as a silent pass.
/// </summary>
static SortedDictionary<string, List<string>> StructuralCover(
    AuditResult headAudit, AuditResult liveAudit, Dictionary<string, string> live)
{
    var subjects = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var t in headAudit.Structural.Concat(liveAudit.Structural))
    {
        var parts = t.Split(" | ");
        if (parts.Length >= 2) subjects.Add(parts[1].Trim());
    }

    var result = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
    foreach (var subject in subjects)
    {
        // subject == scenario/item/method/asOf/(item|TOTAL)
        var f = subject.Split('/');
        var cover = new List<string>();
        if (f.Length == 5)
        {
            var (scenario, item, method, asOf, kind) = (f[0], f[1], f[2], f[3], f[4]);
            bool Has(string it, string measure) =>
                live.ContainsKey(string.Join('\t', [scenario, it, method, asOf, measure]));

            if (kind == "TOTAL")
            {
                if (Has("-", "TotalClosingPaisa") && Has("-", "RefTotalClosingPaisa"))
                    cover.Add("CHECK 9(b) total point oracle");
            }
            else if (Has(item, "ClosingValuePaisa"))
            {
                if (method is "Fifo" or "Lifo" && Has(item, "RefClosingValuePaisa"))
                    cover.Add("CHECK 3 point oracle");
                else if (method is "StandardCost" or "LastPurchaseCost" or "LastSaleCost" && Has(item, "RefClosingValuePaisa"))
                    cover.Add("CHECK 3b point oracle");
                else if (method == "AverageCost" && Has(item, "RefClosingValueDebtAwarePaisa"))
                    cover.Add("CHECK 2 debt-aware point oracle");
            }
        }
        result[subject] = cover;
    }
    return result;
}

// ---------------- the BUILD OUTCOME gate ----------------------------------------------------------

/// <summary>
/// AUDIT #3 FINDING [0] (HIGH). <c>BuildOutcome</c> was emitted at Emit() and READ BY NO CHECK AT ALL.
/// G11-002 threw on BOTH arms for its whole life, so no engine row existed; the point oracle iterates LIVE
/// keys, so it evaluated 0 subjects there; CHECK 11 saw a SYMMETRIC exception and passed; and the recorded
/// census had been recorded FROM that state, so the census gate actively BLESSED the hole. The string
/// "G11-002" appeared ZERO times in the report while G11 was presented as covering the invoice seam.
/// <para>This asserts, per arm, that EVERY declared (scenario x method) cell exists and reads "OK". The
/// mechanism is fully general: any future scenario — including one added specifically to cover the
/// negative-stock fix — that fails to construct now stops the run instead of disappearing.</para>
/// </summary>
static BuildOutcomeResult BuildOutcomes(Dictionary<string, string> rows)
{
    var bad = new List<string>();
    var missing = new List<string>();
    var ok = 0;
    var total = 0;

    foreach (var s in Corpus.Scenarios)
    {
        foreach (var method in Corpus.Methods)
        {
            var key = string.Join('\t', [s.Id, "-", method.ToString(), "-", "BuildOutcome"]);
            total++;
            if (!rows.TryGetValue(key, out var v))
            {
                missing.Add($"{s.Id} / {method} — NO BuildOutcome row at all (the scenario was never attempted).");
                continue;
            }
            if (string.Equals(v, "OK", StringComparison.Ordinal)) { ok++; continue; }
            bad.Add($"{s.Id} / {method} — BuildOutcome = {v}. A scenario that cannot be CONSTRUCTED is a broken " +
                    "corpus, not an engine verdict: nothing downstream measures it, and the census would " +
                    "make its absence permanent.");
        }
    }

    return new BuildOutcomeResult(total, ok, bad, missing);
}

// ---------------- the debt-aware AverageCost CALIBRATION GATE ------------------------------------

/// <summary>
/// AUDIT #3 FINDING [2] (HIGH), and the PREREQUISITE for making AverageCost a first-class oracle.
/// <para>CHECK 4 derives its engine twin by stripping the "Ref" prefix, so
/// <c>RefClosingValueDebtAwarePaisa</c> maps to <c>ClosingValueDebtAwarePaisa</c> — which NO engine emits —
/// and the <c>TryGetValue ... continue</c> silently dropped it. The debt-aware AverageCost oracle, the
/// entire evidential basis for the AverageCost scope decision, was therefore validated by NOTHING.
/// Poisoning it rewrote 148 of 184 magnitudes, invented defects on books that never go negative
/// (N1-002, N5-001, E1-001) and moved the headline G2-004 figure, while PART A printed SOUND.</para>
/// <para>THE CALIBRATION IS FORCED BY THE SEMANTICS: a never-negative book NEVER CARRIES A DEBT, so every
/// clause that distinguishes <c>RunAverageDebtAware</c> from <c>RunAverage</c> is dead code there, and the
/// debt-aware value MUST equal HEAD's AverageCost exactly. A disagreement means the debt-aware oracle is
/// wrong, so this is a HARNESS failure (exit 3), never an engine verdict.</para>
/// <para>A never-negative AverageCost subject with NO engine twin is counted as MISSING and fails too —
/// a silent skip is precisely how the hole existed.</para>
/// </summary>
static AvgCalibrationResult DebtAwareAverageCalibration(Dictionary<string, string> head, HashSet<string> neverNegative)
{
    var mismatches = new List<string>();
    var missing = new List<string>();
    var scenarios = new SortedSet<string>(StringComparer.Ordinal);
    var subjects = 0;

    foreach (var key in head.Keys.Where(k => Col(k, 4) == "RefClosingValueDebtAwarePaisa")
                                 .OrderBy(k => k, StringComparer.Ordinal))
    {
        if (!neverNegative.Contains(Col(key, 0))) continue;
        var engineKey = string.Join('\t', key.Split('\t')[..4]) + "\tClosingValuePaisa";

        if (!head.TryGetValue(engineKey, out var engineValue) ||
            engineValue.StartsWith("EXC:", StringComparison.Ordinal))
        {
            missing.Add($"{engineKey}   engine value {(engineValue is null ? "<MISSING>" : engineValue)}   " +
                        $"debt-aware={head[key]}");
            continue;
        }

        subjects++;
        scenarios.Add(Col(key, 0));
        if (!string.Equals(engineValue, head[key], StringComparison.Ordinal))
            mismatches.Add($"{engineKey}   head={engineValue}   debt-aware={head[key]}   " +
                           "— a never-negative book carries NO DEBT, so these MUST be equal.");
    }

    return new AvgCalibrationResult(subjects, scenarios.Count, missing, mismatches);
}

// ---------------- CHECK 4c — THE HAND-DERIVED DEBT-BRANCH GOLDENS --------------------------------

/// <summary>
/// Asserts the reference reproduces every LITERAL paisa constant in <see cref="Goldens.All"/>, and that
/// the table's COVERAGE is complete. See Goldens.cs for why this exists — it is the only thing standing
/// behind the reference's debt clauses, which CHECK 4b cannot reach by construction.
/// <para>For an AverageCost golden BOTH reference columns are asserted against the same constant. That is
/// deliberate: the two columns are the same computation by construction today, so comparing them to each
/// other is a tautology (audit #4 finding [2]) — but comparing each to an EXTERNAL constant is not, and it
/// is what would convict them if they were ever un-linked.</para>
/// </summary>
static GoldenResult HandDerivedGoldens(Dictionary<string, string> rows, Dictionary<string, string> live)
{
    var lines = new List<string>();
    var issueLines = new List<string>();
    var mismatches = new List<string>();
    var missing = new List<string>();
    var working = new List<string>();
    var evaluated = 0;
    var issueEvaluated = 0;

    // The reference columns one golden is asserted against. A CLOSING golden on AverageCost is asserted
    // against BOTH columns — CHECK 2 convicts from the debt-aware one, CHECK 10 and CHECK 9(b) derive from
    // the plain one — so the tautology of comparing them to each other is unnecessary.
    static string[] ColumnsFor(Golden g)
        => g.IsIssue ? [$"RefIssueValue@{g.Probe}Paisa"]
         : g.Method == "AverageCost" ? ["RefClosingValueDebtAwarePaisa", "RefClosingValuePaisa"]
         : ["RefClosingValuePaisa"];

    foreach (var g in Goldens.All.Concat(Goldens.Issue))
    {
        var stem = g.Stem;
        var bad = false;
        var found = false;
        foreach (var col in ColumnsFor(g))
        {
            if (Dec(rows, stem + "\t" + col) is not { } actual)
            {
                missing.Add($"{g.Id}  {stem}\t{col}   the reference emitted NO SUCH ROW — the golden measured nothing.");
                bad = true;
                continue;
            }
            found = true;
            if (actual != g.Paisa)
            {
                bad = true;
                mismatches.Add(
                    $"{g.Id}  {stem}\t{col}   HAND-DERIVED GOLDEN = {g.Paisa}p   reference = {Num(actual)}p   " +
                    $"delta = {Num(actual - g.Paisa)}p" + Environment.NewLine +
                    $"        clause: {g.Clause}" + Environment.NewLine +
                    $"        derivation: {g.Working}" + Environment.NewLine +
                    "        THE REFERENCE IS WRONG, or the golden is. Resolve it by HAND ARITHMETIC — never by " +
                    "editing the constant to match the code, which is the whole failure this gate exists to prevent.");
            }
        }
        if (found) { if (g.IsIssue) issueEvaluated++; else evaluated++; }

        // ---- THE DERIVATION MUST AGREE WITH THE CONSTANT (audit #5 finding [1], MEDIUM).
        // The prose beside a constant was the ONLY thing that made an edited constant noticeable, and
        // noticing is a reviewer's attention, not a gate. The last rupee figure in the derivation is now
        // parsed and asserted, so a constant edited to match the code fails MECHANICALLY unless its
        // derivation is rewritten too — and rewriting it changes the digest census cell.
        if (LastRupeeFigure(g.Working) is not { } stated)
            working.Add($"{g.Id}  the derivation contains no rupee figure at all, so nothing ties the prose to " +
                        $"the constant {g.Paisa}p.");
        else if (decimal.Round(stated * 100m, 0, MidpointRounding.AwayFromZero) != g.Paisa)
            working.Add($"{g.Id}  the derivation ends at Rs {Num(stated)} (= {Num(decimal.Round(stated * 100m, 0, MidpointRounding.AwayFromZero))}p) " +
                        $"but the constant is {g.Paisa}p. One of them was edited without the other.");

        var probe = g.IsIssue ? "@" + g.Probe : "";
        var line = $"{(bad ? "FAIL" : "ok  ")}  {g.Id,-7} {g.Scenario,-9} {g.Item,-8} {g.Method,-12} {g.AsOf}{probe,-8}  " +
                   $"= {g.Paisa,9}p  [{g.Clause}]";
        (g.IsIssue ? issueLines : lines).Add(line);
        (g.IsIssue ? issueLines : lines).Add($"           {g.Working}");
    }

    // ---- COVERAGE, ASSERTED. An unpinned INVENTED subject is exactly the exposure this table closes.
    // ROUND 6: BOTH sides are required. A rule nothing calibrates anchored on closing value alone is
    // precisely the shape audit #5 finding [0] demonstrated — a right Balance Sheet with a wrong P&L.
    var pinnedClosing = new HashSet<string>(Goldens.All.Select(g => g.Stem), StringComparer.Ordinal);
    var pinnedIssue = new HashSet<string>(Goldens.Issue.Select(g => g.Stem), StringComparer.Ordinal);

    // THE COVERAGE DEMAND IS SCOPED TO SINGLE-KEY SUBJECTS. (2026-07-29.)
    // A golden is a HAND-DERIVED TRUTH the reference must reproduce, so DEMANDING one asserts that a
    // correct answer for that subject is knowable. On a multi-key book where a debt fires, it is not:
    // the item-level model the reference uses is known wrong there, and its per-key alternative broke
    // ordinary godown transfers. Requiring a constant for such a subject would force someone to invent
    // one and call it truth — the exact failure this harness exists to prevent. So multi-key INVENTED
    // subjects are listed as INFORMATIONAL and do not fail the run; single-key ones still MUST be pinned,
    // and that is where every debt clause the reference actually ships is exercised.
    bool StemIsSingleKey(string scenario, string item, string asOf)
        => rows.TryGetValue($"{scenario}\t{item}\t-\t{asOf}\tFactSingleKey", out var v) && v == "1";

    var uncoveredInvented = new List<string>();
    var uncoveredInventedInfo = new List<string>();
    var debtFamilies = new SortedSet<string>(StringComparer.Ordinal);
    var debtDependent = 0;
    foreach (var kv in rows)
    {
        if (Col(kv.Key, 4) != "RefProvenance") continue;
        var single = StemIsSingleKey(Col(kv.Key, 0), Col(kv.Key, 1), Col(kv.Key, 3));
        if (kv.Value is RefProvenance.Brief or RefProvenance.Invented)
        {
            debtDependent++;
            if (single) debtFamilies.Add(Family(kv.Key));   // only a JUDGEABLE subject obliges its family
        }
        if (kv.Value != RefProvenance.Invented) continue;
        var stem = string.Join('\t', kv.Key.Split('\t')[..4]);
        var sink = single ? uncoveredInvented : uncoveredInventedInfo;
        var note = single ? "" : "  [MULTI-KEY: informational — the reference is not an oracle here, so no " +
                                 "constant can be honestly derived; NOT a failure]";
        if (!pinnedClosing.Contains(stem))
            sink.Add($"{stem}   is tagged INVENTED (a rule NOTHING calibrates) and carries NO " +
                     "hand-derived CLOSING-value golden." + note);
        if (!pinnedIssue.Contains(stem))
            sink.Add($"{stem}   is tagged INVENTED (a rule NOTHING calibrates) and carries NO " +
                     "hand-derived ISSUE-value golden. A rule nothing calibrates must be pinned on " +
                     "the P&L as well as the Balance Sheet." + note);
    }
    uncoveredInvented.Sort(StringComparer.Ordinal);
    uncoveredInventedInfo.Sort(StringComparer.Ordinal);

    var goldenFamilies = new HashSet<string>(
        Goldens.All.Concat(Goldens.Issue).Select(g => Family(g.Scenario + "\t")), StringComparer.Ordinal);
    var uncoveredFamilies = debtFamilies.Where(f => !goldenFamilies.Contains(f))
        .Select(f => $"family {f} has SINGLE-KEY BRIEF/INVENTED subjects but NO hand-derived golden anywhere in it.")
        .ToList();

    var exercised = new HashSet<string>(Goldens.All.Concat(Goldens.Issue).Select(g => g.Clause), StringComparer.Ordinal);
    var unexercised = Goldens.RequiredClauses.Where(c => !exercised.Contains(c))
        .Select(c => $"debt clause '{c}' is required but no golden exercises it.")
        .ToList();

    // ---- AUDIT #6, LOW [1] — THE LABELS THEMSELVES ARE NOW VERIFIED AGAINST THE SPEC.
    // Everything above this line takes `g.Clause` at its word: `exercised` is a projection of the very
    // table under audit, so "every required clause is exercised" was a statement the table made ABOUT
    // ITSELF. Nothing asked whether a golden tagged issue:debt-outstanding is taken with a debt actually
    // outstanding. A table with the RIGHT numbers under the WRONG labels therefore reported full clause
    // coverage while leaving a clause genuinely unexercised, and re-tagging one golden manufactured
    // coverage out of nothing. Each label is now required to be TRUE of its subject, judged from
    // FactDebtShape — a pure quantity walk in Facts.cs that shares no code with the debt VALUE branch.
    // A label that is false is a HARNESS failure (exit 3), like every other CHECK 4c failure: it says the
    // ORACLE's coverage claim is wrong, never that src/ is.
    var clauseChecked = 0;
    var clauseViolations = new List<string>();
    var clauseNoFact = new List<string>();
    var clauseTally = new SortedDictionary<string, int>(StringComparer.Ordinal);

    foreach (var g in Goldens.All.Concat(Goldens.Issue))
    {
        var p = g.Stem.Split('\t');
        var shapeKey = string.Join('\t', [p[0], p[1], "-", p[3], "FactDebtShape"]);
        if (!rows.TryGetValue(shapeKey, out var shapeRaw))
        {
            clauseNoFact.Add($"{g.Id}  {p[0]}/{p[1]}/{p[3]}   no FactDebtShape row, so its clause label " +
                             $"'{g.Clause}' was verified against NOTHING.");
            continue;
        }

        var shape = new HashSet<string>(
            shapeRaw.Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var everDebt = shape.Contains("everDebt");
        var debtNow = shape.Contains("debtAtAsOf");

        // For the two probe-sensitive issue clauses, "reaches past the surviving stack" vs "stays inside
        // it" is decided against the closing QUANTITY of this golden's own method.
        decimal? probeMicro = g.IsIssue && decimal.TryParse(g.Probe, NumberStyles.Any,
                                  CultureInfo.InvariantCulture, out var pq) ? pq * 1_000_000m : null;
        var closingMicro = Dec(rows, g.Stem + "\tRefClosingQtyMicro");

        // Each entry answers ONE question: is the property this tag NAMES true of this subject?
        var (holds, needs) = g.Clause switch
        {
            DebtClause.RepayRated => (shape.Contains("repaidByRatedInward"),
                "a RATED inward arriving while the net quantity was negative"),
            DebtClause.RepayMultiLot => (shape.Contains("repaidAcrossMultipleInwards"),
                "an inward swallowed WHOLE by the debt, so the surviving stock came from a later lot"),
            DebtClause.RepayUnrated => (shape.Contains("repaidByUnratedInward"),
                "an UNRATED inward arriving while the net quantity was negative"),
            DebtClause.CountWithDebt => (shape.Contains("countWithDebt"),
                "a physical count taken while the net quantity was negative"),
            DebtClause.CountAfterRepay => (shape.Contains("countAfterRepay"),
                "a physical count taken after a debt had been repaid"),
            DebtClause.DebtOutstanding => (debtNow,
                "a debt STILL outstanding at the as-of date"),
            DebtClause.DebtFromEmpty => (shape.Contains("debtFromEmptyStack"),
                "an outward taken against a net of exactly zero"),
            DebtClause.DebtAccumulated => (shape.Contains("twoSuccessiveOverdraws"),
                "an outward taken while the net quantity was ALREADY negative"),
            DebtClause.AverageDebt => (g.Method == "AverageCost" && everDebt,
                "method AverageCost on a book that carried a debt"),
            DebtClause.NoDebtControl => (!everDebt,
                "a book that NEVER carried a company-wide debt"),

            DebtClause.IssueUnderDebt => (g.IsIssue && debtNow,
                "an ISSUE golden on a book with a debt still outstanding at the as-of date"),
            DebtClause.IssuePostRecovery => (g.IsIssue && everDebt && !debtNow
                    && probeMicro is { } a && closingMicro is { } b && a <= b,
                "an ISSUE golden, probe at or below the closing quantity, on a recovered book"),
            DebtClause.IssueAcrossRepaid => (g.IsIssue && everDebt && !debtNow
                    && probeMicro is { } c && closingMicro is { } d && c >= d,
                "an ISSUE golden whose probe reaches the whole surviving stack, on a recovered book"),
            DebtClause.IssueAverage => (g.IsIssue && g.Method == "AverageCost" && everDebt,
                "an ISSUE golden on the AverageCost arm of a book that carried a debt"),
            DebtClause.IssueControl => (g.IsIssue && !everDebt,
                "an ISSUE golden on a book that NEVER carried a company-wide debt"),

            _ => (false, "a clause tag this comparator does not recognise at all"),
        };

        clauseChecked++;
        clauseTally.TryGetValue(g.Clause, out var n);
        clauseTally[g.Clause] = n + 1;
        if (!holds)
            clauseViolations.Add(
                $"{g.Id}  {p[0]}/{p[1]}/{g.Method}/{p[3]}" + (g.IsIssue ? $"@{g.Probe}" : "") +
                $"   LABEL '{g.Clause}' IS NOT TRUE OF THIS SUBJECT." + Environment.NewLine +
                $"        the tag asserts : {needs}" + Environment.NewLine +
                $"        the SPEC says   : FactDebtShape = {shapeRaw}" +
                (probeMicro is not null ? $" ; probe {g.Probe}, closing qty " +
                    (closingMicro is { } cq ? Num(cq / 1_000_000m) : "<no RefClosingQtyMicro row>") : "") +
                Environment.NewLine +
                "        The constant may still be right — what is wrong is the COVERAGE CLAIM built on " +
                "this label. Re-tag the golden, or add one that genuinely exercises the clause.");
    }
    clauseViolations.Sort(StringComparer.Ordinal);

    // A clause whose only goldens are mislabelled is not exercised at all, whatever `exercised` says.
    var verifiedClauses = new HashSet<string>(
        Goldens.All.Concat(Goldens.Issue)
            .Where(g => !clauseViolations.Any(v => v.StartsWith(g.Id + "  ", StringComparison.Ordinal)))
            .Select(g => g.Clause), StringComparer.Ordinal);
    unexercised.AddRange(Goldens.RequiredClauses
        .Where(c => exercised.Contains(c) && !verifiedClauses.Contains(c))
        .Select(c => $"debt clause '{c}' is claimed by at least one golden but NOT ONE of them is " +
                     "actually taken on a subject where that clause fires — the coverage was a label."));

    // ---- HOW MUCH OF WHAT THE HARNESS ACTUALLY CONVICTS ON IS DIRECTLY PINNED (audit #5 finding [4]).
    // Computed from the emitted rows of THIS run, so it moves with the corpus instead of being a claim a
    // reader has to interpret. A conviction is a subject where the LIVE arm disagrees with the reference
    // column that check judges from.
    var closingPinned = new HashSet<string>(Goldens.All.Select(g => g.Stem), StringComparer.Ordinal);
    var issuePinned = new HashSet<string>(
        Goldens.Issue.Select(g => g.Stem + "\tRefIssueValue@" + g.Probe + "Paisa"), StringComparer.Ordinal);
    int c2 = 0, c2p = 0, c3 = 0, c3p = 0, c10 = 0, c10p = 0;
    foreach (var key in live.Keys)
    {
        var measure = Col(key, 4);
        var method = Col(key, 2);
        var stem = string.Join('\t', key.Split('\t')[..4]);
        if (measure == "ClosingValuePaisa")
        {
            var refCol = method == "AverageCost" ? "RefClosingValueDebtAwarePaisa" : "RefClosingValuePaisa";
            if (method is not ("AverageCost" or "Fifo" or "Lifo")) continue;
            if (Dec(rows, stem + "\t" + refCol) is not { } r || Dec(live, key) is not { } l || r == l) continue;
            if (method == "AverageCost") { c2++; if (closingPinned.Contains(stem)) c2p++; }
            else { c3++; if (closingPinned.Contains(stem)) c3p++; }
        }
        else if (measure.StartsWith("IssueValue@", StringComparison.Ordinal))
        {
            if (Dec(rows, stem + "\tRef" + measure) is not { } r || Dec(live, key) is not { } l || r == l) continue;
            c10++;
            if (issuePinned.Contains(stem + "\tRef" + measure)) c10p++;
        }
    }

    return new GoldenResult(evaluated, lines, mismatches, missing, uncoveredInvented, uncoveredInventedInfo, uncoveredFamilies,
                            unexercised, issueEvaluated, issueLines, working,
                            c2, c2p, c3, c3p, c10, c10p, debtDependent,
                            clauseChecked, clauseViolations, clauseNoFact,
                            clauseTally.Select(kv => $"{kv.Key,-32} {kv.Value,3} golden(s), all verified against " +
                                                     "FactDebtShape").ToList());
}

/// <summary>The LAST rupee figure written in a hand derivation — what the constant must equal x100.</summary>
static decimal? LastRupeeFigure(string working)
{
    decimal? last = null;
    for (var i = 0; i < working.Length; i++)
    {
        if (!char.IsAsciiDigit(working[i])) continue;
        var start = i;
        while (i < working.Length && (char.IsAsciiDigit(working[i]) || working[i] == '.')) i++;
        var text = working[start..i].TrimEnd('.');
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) last = v;
    }
    return last;
}

// ---------------- CHECK 4c(s) — THE STRUCTURAL ISSUE-VALUE ASSERTION ------------------------------

/// <summary>
/// Asserts, WITHOUT any constant, the two properties the Fifo/Lifo issue walk cannot violate:
/// <list type="number">
///   <item>a probe at or above the closing QUANTITY must yield EXACTLY the closing VALUE — the walk runs
///     out of layers, and the units a debt repayment consumed are COGS, not stock;</item>
///   <item>any probe must yield at most the closing value, and issue value must not DECREASE as the probe
///     grows.</item>
/// </list>
/// AUDIT #5 FINDING [0]. The adversary's poison — issue at the debt-aware pool average whenever the book
/// had ever carried a debt — rewrote 68 of the 120 reported CHECK 10 demands and violated property (1) on
/// every one of them (G1-001 Fifo @1000 became Rs 7,910.00 against a Rs 197.75 stack). This costs three
/// lines and kills all 68 without needing a golden for each.
/// <para>It cannot reach AverageCost: that arm issues at the closing unit rate and is deliberately NOT
/// capped by the stack, which is why Goldens.Issue carries constants for it instead.</para>
/// </summary>
static IssueStructureResult IssueStructure(Dictionary<string, string> rows)
{
    var atOrAbove = new List<string>();
    var overStack = new List<string>();
    var monotonic = new List<string>();
    var subjects = new HashSet<string>(StringComparer.Ordinal);
    int atPairs = 0, belowPairs = 0;

    var byStem = new SortedDictionary<string, List<(decimal Probe, decimal Value)>>(StringComparer.Ordinal);
    foreach (var kv in rows)
    {
        var measure = Col(kv.Key, 4);
        if (!measure.StartsWith("RefIssueValue@", StringComparison.Ordinal)) continue;
        if (Col(kv.Key, 2) is not ("Fifo" or "Lifo")) continue;
        var probeText = measure["RefIssueValue@".Length..^"Paisa".Length];
        if (!decimal.TryParse(probeText, NumberStyles.Number, CultureInfo.InvariantCulture, out var probe)) continue;
        if (Dec(rows, kv.Key) is not { } v) continue;
        var stem = string.Join('\t', kv.Key.Split('\t')[..4]);
        if (!byStem.TryGetValue(stem, out var list)) byStem[stem] = list = [];
        list.Add((probe, v));
    }

    foreach (var (stem, probes) in byStem)
    {
        if (Dec(rows, stem + "\tRefClosingValuePaisa") is not { } closingValue) continue;
        // ROUND 10 — THE BOUNDARY IS THE SURVIVING STACK, NOT THE REPORTED ON-HAND. The assertion is
        // "a probe that reaches the whole stack must cost exactly what the stack is worth", and the
        // stack's quantity is FactFlatNetMicro (Facts' own gated quantity walk of the flattened
        // item-level stream), clamped at 0. On every single-key book that IS the reported closing
        // quantity and this is the same assertion it always was; on a multi-key book carrying a
        // physical count the two genuinely differ (the ITEM-LEVEL/PER-KEY DESYNC), and comparing a
        // stack-walk against a register that counted different units convicts the reference for a
        // divergence that lives in the engine's quantity model, not in its issue arm.
        var p4 = stem.Split('\t');
        if (Dec(rows, string.Join('\t', [p4[0], p4[1], "-", p4[3], "FactFlatNetMicro"])) is not { } flatNet)
            continue;
        var closingQty = Math.Max(flatNet, 0m) / 1_000_000m;
        subjects.Add(stem);

        foreach (var (probe, value) in probes)
        {
            if (probe >= closingQty)
            {
                atPairs++;
                if (value != closingValue)
                    atOrAbove.Add($"{stem}   issue@{Num(probe)} = {Num(value)}p but the closing stock is " +
                                  $"{Num(closingQty)} units worth {Num(closingValue)}p. A probe at or above on-hand " +
                                  "must consume EXACTLY the surviving stack — anything more is stock that a debt " +
                                  "repayment already sent to COGS being sold a second time.");
            }
            else
            {
                belowPairs++;
                if (value > closingValue)
                    overStack.Add($"{stem}   issue@{Num(probe)} = {Num(value)}p exceeds the whole closing stock " +
                                  $"({Num(closingValue)}p) while asking for LESS than the closing quantity " +
                                  $"({Num(closingQty)}).");
                if (value < 0m)
                    overStack.Add($"{stem}   issue@{Num(probe)} = {Num(value)}p is NEGATIVE.");
            }
        }

        var ordered = probes.OrderBy(p => p.Probe).ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].Value < ordered[i - 1].Value)
                monotonic.Add($"{stem}   issue@{Num(ordered[i].Probe)} = {Num(ordered[i].Value)}p is LESS than " +
                              $"issue@{Num(ordered[i - 1].Probe)} = {Num(ordered[i - 1].Value)}p — issuing more " +
                              "cannot cost less.");
    }

    return new IssueStructureResult(subjects.Count, atPairs, belowPairs, atOrAbove, overStack, monotonic);
}

// ---------------- THE SPEC-DERIVED INVENTED POPULATION --------------------------------------------

/// <summary>
/// Compares the RefProvenance=INVENTED population against <see cref="Facts.InventedByRule"/>, the
/// SPEC-derived pure-quantity answer to the same question (audit #5 finding [3]).
/// <para>Both directions fail. An emitted INVENTED tag the spec does not justify means the reference is
/// applying an uncalibrated rule somewhere the spec says it should not; a spec-INVENTED subject the
/// reference tags BRIEF or CALIBRATED means the population CHECK 4c claims to have pinned has silently
/// shrunk, which is the partial-retag case the total-collapse guard could not see.</para>
/// </summary>
static InventedPopulationResult InventedPopulation(Dictionary<string, string> rows)
{
    var emitted = new SortedSet<string>(StringComparer.Ordinal);
    var considered = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var kv in rows)
    {
        if (Col(kv.Key, 4) != "RefProvenance") continue;
        if (Col(kv.Key, 2) is not ("Fifo" or "Lifo" or "AverageCost")) continue;   // flat methods never reach the rule
        var stem = string.Join('\t', kv.Key.Split('\t')[..4]);
        considered.Add(stem);
        if (kv.Value == RefProvenance.Invented) emitted.Add(stem);
    }

    var spec = new SortedSet<string>(StringComparer.Ordinal);
    var missingFact = new List<string>();
    foreach (var stem in considered)
    {
        var parts = stem.Split('\t');
        var factKey = string.Join('\t', [parts[0], parts[1], "-", parts[3], "FactInventedByRule"]);
        if (!rows.TryGetValue(factKey, out var f))
        {
            missingFact.Add($"{stem}   has no FactInventedByRule row — the spec-derived population cannot be " +
                            "computed, so the coverage assertion is standing on the reference's own tag again.");
            continue;
        }
        if (f == "1") spec.Add(stem);
    }

    var emittedNotSpec = emitted.Where(s => !spec.Contains(s))
        .Select(s => $"{s}   the reference tags INVENTED but the SPEC's quantity walk says no count-with-debt and " +
                     "no unrated repayment ever happened here.")
        .ToList();
    var specNotEmitted = spec.Where(s => !emitted.Contains(s))
        .Select(s => $"{s}   the SPEC says a debt was settled by a movement carrying no purchase rate, but the " +
                     "reference does NOT tag it INVENTED. The population CHECK 4c pins has shrunk.")
        .ToList();

    return new InventedPopulationResult(spec.Count, emitted.Count, considered.Count,
                                        emittedNotSpec, specNotEmitted, missingFact);
}

// ---------------- CHECK 4 — calibration -------------------------------------------------------

static CalibrationResult Calibration(Dictionary<string, string> head, HashSet<string> neverNegative)
{
    var mismatches = new List<string>();
    var methods = new SortedSet<string>(StringComparer.Ordinal);
    var scenarios = new SortedSet<string>(StringComparer.Ordinal);
    var subjects = 0;

    foreach (var key in head.Keys.OrderBy(k => k, StringComparer.Ordinal))
    {
        var measure = Col(key, 4);
        if (!measure.StartsWith("Ref", StringComparison.Ordinal)) continue;
        if (!neverNegative.Contains(Col(key, 0))) continue;   // calibrate ONLY where HEAD is trusted

        var engineMeasure = measure[3..];                      // RefClosingValuePaisa -> ClosingValuePaisa
        var engineKey = string.Join('\t', key.Split('\t')[..4]) + "\t" + engineMeasure;
        if (!head.TryGetValue(engineKey, out var engineValue)) continue;

        subjects++;
        methods.Add(Col(key, 2));
        scenarios.Add(Col(key, 0));

        var refValue = head[key];
        if (!string.Equals(engineValue, refValue, StringComparison.Ordinal))
            mismatches.Add($"{engineKey}   head={engineValue}   reference={refValue}");
    }

    return new CalibrationResult(subjects, scenarios.Count, methods, mismatches);
}

// ---------------- point-oracle comparison ------------------------------------------------------

/// <summary>
/// Compares one engine measure on the LIVE arm against its <c>Ref*</c> twin. HEAD's value is printed
/// alongside so the report states, for every disagreement, what HEAD said, what live said, and what the
/// reference says is right.
/// </summary>
static OracleResult PointOracle(Dictionary<string, string> live, Dictionary<string, string> head,
                                string engineMeasure, string refMeasure,
                                Func<string, bool> methodFilter, bool itemRowsOnly)
{
    var mismatches = new List<string>();
    var evaluated = 0;

    foreach (var key in live.Keys.Where(k => Col(k, 4) == engineMeasure).OrderBy(k => k, StringComparer.Ordinal))
    {
        var method = Col(key, 2);
        if (!methodFilter(method)) continue;
        if (itemRowsOnly && Col(key, 1) == "-") continue;
        if (!itemRowsOnly && Col(key, 1) != "-") continue;

        var refKey = string.Join('\t', key.Split('\t')[..4]) + "\t" + refMeasure;
        if (!live.TryGetValue(refKey, out var refValue)) continue;

        evaluated++;
        var liveValue = live[key];
        if (string.Equals(liveValue, refValue, StringComparison.Ordinal)) continue;

        var headValue = head.GetValueOrDefault(key, "<MISSING>");
        var delta = "";
        if (decimal.TryParse(liveValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var lv) &&
            decimal.TryParse(refValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var rv))
            delta = $"   live-ref={Num(lv - rv)}" +
                    (rv != 0m ? $"  ({Num(Math.Round((lv - rv) / rv * 100m, 2))}%)" : "");
        mismatches.Add($"{key}   head={headValue}   live={liveValue}   REFERENCE={refValue}{delta}");
    }

    return new OracleResult(evaluated, mismatches);
}

/// <summary>CHECK 9(a): the company total must equal the sum of the per-item closing values on that arm.</summary>
static OracleResult TotalConsistency(Dictionary<string, string> rows)
{
    var mismatches = new List<string>();
    var evaluated = 0;

    foreach (var key in rows.Keys.Where(k => Col(k, 4) == "TotalClosingPaisa").OrderBy(k => k, StringComparer.Ordinal))
    {
        var scenario = Col(key, 0);
        var method = Col(key, 2);
        var asOf = Col(key, 3);
        var total = Dec(rows, key);
        if (total is null) continue;   // an EXC row — handled by check 11

        var sum = 0m;
        var found = 0;
        foreach (var itemKey in rows.Keys.Where(k =>
                     Col(k, 0) == scenario && Col(k, 2) == method && Col(k, 3) == asOf &&
                     Col(k, 4) == "ClosingValuePaisa" && Col(k, 1) != "-"))
        {
            if (Dec(rows, itemKey) is not { } v) { found = -1; break; }
            sum += v;
            found++;
        }
        if (found <= 0) continue;

        evaluated++;
        if (sum != total.Value)
            mismatches.Add($"{scenario}/{method}/{asOf}  total={Num(total.Value)}p  sum-of-items={Num(sum)}p  delta={Num(total.Value - sum)}p");
    }
    return new OracleResult(evaluated, mismatches);
}

/// <summary>The per-family HEAD-vs-reference divergence — the quantitative statement of today's defect.</summary>
static List<string> HeadVsReference(Dictionary<string, string> head)
{
    var perFamily = new SortedDictionary<string, DefectRow>(StringComparer.Ordinal);

    foreach (var key in head.Keys.Where(k => Col(k, 4) == "ClosingValuePaisa" && Col(k, 1) != "-")
                                 .OrderBy(k => k, StringComparer.Ordinal))
    {
        var method = Col(key, 2);
        if (method is not ("Fifo" or "Lifo")) continue;
        var refKey = string.Join('\t', key.Split('\t')[..4]) + "\tRefClosingValuePaisa";
        var hv = Dec(head, key);
        var rv = Dec(head, refKey);
        if (hv is null || rv is null) continue;

        var fam = Family(key);
        if (!perFamily.TryGetValue(fam, out var row)) { row = new DefectRow(); perFamily[fam] = row; }

        row.Subjects++;
        if (hv.Value != rv.Value) row.Bad++;
        var delta = Math.Abs(hv.Value - rv.Value);
        if (delta > row.Max)
        {
            row.Max = delta;
            row.Worst = $"{key.Replace('\t', '/')}  head={Num(hv.Value)}p  reference={Num(rv.Value)}p";
        }
    }

    return perFamily.Select(kv =>
        $"{kv.Key,-6} | {kv.Value.Subjects,8} | {kv.Value.Bad,11} | {Num(kv.Value.Max),20} | {kv.Value.Worst}").ToList();
}

// ---------------- CHECKS 6/7/8 — the absolute audit ---------------------------------------------

/// <summary>
/// Applies checks 6, 7 and 8 to ONE arm's rows using only the spec-derived Fact* rows — so the oracle is
/// engine-independent on both arms. No clamping, no floors: every violation is surfaced, each carrying a
/// MAGNITUDE in paisa so a live violation can be told apart from a worse live violation (audit C2). Also
/// returns the census of subjects each check actually EVALUATED, so a reported 0 can never be confused
/// with "the check never ran" (audit H1).
/// </summary>
static AuditResult Audit(Dictionary<string, string> rows)
{
    var findings = new Dictionary<FindingKey, Finding>();
    var census = new Dictionary<(string Check, string Family, string Method), int>();
    var structural = new List<string>();

    void Evaluated(string check, string family, string method)
    {
        var k = (check, family, method);
        census[k] = census.GetValueOrDefault(k, 0) + 1;
    }

    decimal? Val(string scenario, string item, string method, string asOf, string measure)
        => Dec(rows, $"{scenario}\t{item}\t{method}\t{asOf}\t{measure}");

    // ---------------------------------------------------------------------------------------------
    // THE ITEM-LEVEL / PER-KEY DESYNC, AS A SPEC-DERIVED PREDICATE (round 10).
    //
    // The engine replays cost layers over ONE flattened stream and replays QUANTITY per (item, godown,
    // batch). A physical count is a per-key statement, so on a multi-key book carrying one the two walks
    // legitimately reach DIFFERENT numbers of units: FactFlatNetMicro (the quantity the layer replay
    // reached) can differ from ClosingQtyMicro (the quantity the register reports). Both figures are
    // separately defensible; the PAIR is what does not describe one set of units. That divergence
    // PRE-DATES the debt rule and is not repaired by it — it needs a per-key cost replay in which cost
    // FLOWS ACROSS A TRANSFER, which is a separate piece of work.
    //
    // CHECK 6 divides the closing VALUE by the closing QUANTITY to get an implied unit rate. Where those
    // two describe different unit counts that quotient is not a rate the item could have paid under ANY
    // valuation, so the check has no discriminating power there — exactly the situation the
    // STRUCTURALLY-UNSATISFIABLE bucket exists for. Such subjects are listed BY NAME with their measured
    // desync and are neither passed nor failed. The predicate is derived from two SPEC walks (Facts'
    // gated flattened walk and the quantity oracle) and touches no value, so a valuation defect cannot
    // widen it: it is 0 subjects on every single-key book and on every multi-key book with no count.
    decimal DesyncMicro(string scenario, string item, string method, string asOf)
    {
        var flat = Dec(rows, $"{scenario}\t{item}\t-\t{asOf}\tFactFlatNetMicro");
        var qty = Val(scenario, item, method, asOf, "ClosingQtyMicro");
        if (flat is not { } f || qty is not { } q) return 0m;
        return Math.Max(f, 0m) - q;
    }

    decimal ScenarioDesyncMicro(string scenario, string method, string asOf)
    {
        var worst = 0m;
        foreach (var qk in rows.Keys.Where(k =>
                     Col(k, 0) == scenario && Col(k, 2) == method && Col(k, 3) == asOf &&
                     Col(k, 4) == "ClosingQtyMicro" && Col(k, 1) != "-"))
        {
            var d = DesyncMicro(scenario, Col(qk, 1), method, asOf);
            if (Math.Abs(d) > Math.Abs(worst)) worst = d;
        }
        return worst;
    }

    // Subjects: every per-item closing value AND every company total (the total is judged too — check 9c).
    var subjects = rows.Keys
        .Where(k => Col(k, 4) is "ClosingValuePaisa" or "TotalClosingPaisa")
        .Where(k => Col(k, 3) != "-")
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToList();

    foreach (var key in subjects)
    {
        var p = key.Split('\t');
        string scenario = p[0], item = p[1], method = p[2], asOf = p[3], measure = p[4];
        var isTotal = measure == "TotalClosingPaisa";
        if (isTotal && item != "-") continue;
        if (!isTotal && item == "-") continue;

        var family = scenario.Contains('-') ? scenario[..scenario.IndexOf('-')] : scenario;
        var subject = $"{scenario}/{item}/{method}/{asOf}/{(isTotal ? "TOTAL" : "item")}";

        var value = Dec(rows, key);
        if (value is null) continue;                       // an exception row — recorded, not audited

        // Quantity: for a per-item subject the item's own closing qty; for the total, the sum of them.
        decimal? qtyMicro;
        if (isTotal)
        {
            var sum = 0m;
            var any = false;
            foreach (var qk in rows.Keys.Where(k =>
                         Col(k, 0) == scenario && Col(k, 2) == method && Col(k, 3) == asOf &&
                         Col(k, 4) == "ClosingQtyMicro" && Col(k, 1) != "-"))
            {
                if (Dec(rows, qk) is not { } v) { any = false; break; }
                sum += v;
                any = true;
            }
            qtyMicro = any ? sum : null;
        }
        else
        {
            qtyMicro = Val(scenario, item, method, asOf, "ClosingQtyMicro");
        }
        if (qtyMicro is null) continue;

        // Facts row: per item, or the "-" aggregate row for the total.
        var factItem = isTotal ? "-" : item;
        var hasRatedInward = Val(scenario, factItem, "-", asOf, "FactHasRatedInward") == 1m;
        var hasUnratedInward = Val(scenario, factItem, "-", asOf, "FactHasUnratedInward") == 1m;
        var hasRatedOutward = Val(scenario, factItem, "-", asOf, "FactHasRatedOutward") == 1m;
        var hasStd = Val(scenario, factItem, "-", asOf, "FactHasStandardCost") == 1m;
        var stdPaisa = Val(scenario, factItem, "-", asOf, "FactStandardCostPaisa");
        var minIn = Val(scenario, factItem, "-", asOf, "FactMinInwardRateBandPaisa");
        var maxIn = Val(scenario, factItem, "-", asOf, "FactMaxInwardRateBandPaisa");
        var minOut = Val(scenario, factItem, "-", asOf, "FactMinOutwardRateBandPaisa");
        var maxOut = Val(scenario, factItem, "-", asOf, "FactMaxOutwardRateBandPaisa");
        var ceiling = Val(scenario, factItem, "-", asOf, "FactSpendCeilingPaisa");
        var floorSpend = Val(scenario, factItem, "-", asOf, "FactRatedSpendPaisaCeil");
        var imputedMicro = Val(scenario, factItem, "-", asOf, "FactImputedUnitsMicro") ?? 0m;
        var outMicro = Val(scenario, factItem, "-", asOf, "FactTotalOutwardMicro");

        var qty = qtyMicro.Value / 1_000_000m;
        var imputedTag = imputedMicro > 0m ? " [imputed]" : "";

        // ---------- CHECK 6 — implied closing unit rate inside the band the SPEC permits for this method.
        decimal? lo = null, hi = null;
        void Widen(decimal v) { if (lo is null || v < lo) lo = v; if (hi is null || v > hi) hi = v; }

        if (hasRatedInward && minIn is { } mi && maxIn is { } ma) { Widen(mi); Widen(ma); }
        if (hasUnratedInward && hasStd && stdPaisa is { } su) Widen(su);
        if (method == "StandardCost" && hasStd && stdPaisa is { } ss) Widen(ss);
        if (method == "LastSaleCost" && hasRatedOutward && minOut is { } mo && maxOut is { } xo) { Widen(mo); Widen(xo); }
        // A total mixes items; StandardCost/LastSale rates of ANY item may appear in it.
        if (isTotal && hasStd && stdPaisa is { } ts) Widen(ts);

        if (qty > 0m && lo is { } band0 && hi is { } band1)
        {
            Evaluated("6 closing-rate band", family, method);
            var implied = value.Value / qty;

            // TOLERANCE STATED ON THE VALUE, NEVER PER UNIT.
            // This was `tol = 1 + 1/qty` in paisa PER UNIT, which reads as an unbounded per-unit allowance
            // as the closing quantity approaches the engine's 6-dp floor (at 0.000001 units it prints as
            // 1,000,001p/unit). The allowance it actually granted was always the same thing — (qty + 1)
            // paisa of VALUE: 1 paisa for the closing value's own rounding, plus 1 paisa per unit for the
            // floored/ceilinged band edges — so it is now WRITTEN that way, with no division by a quantity
            // that can go to zero. Numerically identical to the old form on every subject; bounded by
            // construction on every subject that could ever exist.
            var valueTol = 1m + qty;                       // total paisa of VALUE, never per unit
            var lowValue = band0 * qty - valueTol;
            var highValue = band1 * qty + valueTol;
            var outsideValue = Math.Max(lowValue - value.Value, value.Value - highValue);

            // SATISFIABILITY: some non-negative closing value always lands inside [lowValue, highValue]
            // (highValue >= 0 whenever band1 >= 0), so check 6 always has a premise. Computed, not assumed.
            var sat6 = highValue >= 0m;
            if (!sat6) structural.Add($"6 closing-rate band | {subject} | band [{Num(band0)}, {Num(band1)}]p on qty {Num(qty)}");

            // THE DESYNC PREMISE (round 10) — see DesyncMicro. Where the layer replay and the quantity
            // register reached different unit counts, value / quantity is not an implied RATE at all and
            // this check cannot discriminate. Reported by name, scored neither way.
            var desync = isTotal ? ScenarioDesyncMicro(scenario, method, asOf)
                                 : DesyncMicro(scenario, item, method, asOf);
            if (desync != 0m)
            {
                sat6 = false;
                structural.Add(
                    $"6 closing-rate band | {subject} | the cost-layer replay holds " +
                    $"{Num((qtyMicro.Value + desync) / 1_000_000m)} units while the quantity register " +
                    $"reports {Num(qty)} (delta {Num(desync / 1_000_000m)}), so value / quantity is not " +
                    $"an implied rate — the ITEM-LEVEL/PER-KEY DESYNC, pre-existing and not repaired here");
            }

            if (outsideValue > 0m)
                findings[new FindingKey("6 closing-rate band", family, method, subject, key)] = new Finding(
                    // Rounded to 1/10,000 of a paisa: magnitudes are COMPARED between arms, and decimal
                    // tail noise must never make a legitimate improvement look like a regression.
                    Math.Round(outsideValue, 4), !sat6,
                    $"closing {Num(value.Value)}p on qty {Num(qty)} => implied {Num(Math.Round(implied, 4))}p/unit, " +
                    $"outside band [{Num(band0)}, {Num(band1)}]p; admissible VALUE range " +
                    $"[{Num(Math.Round(lowValue, 4))}, {Num(Math.Round(highValue, 4))}]p, " +
                    $"outside by {Num(Math.Round(outsideValue, 4))}p of value{imputedTag}");
        }

        // ---------- CHECK 7 — you cannot hold more asset than was ever bought (or could have been).
        // Cost-based methods only: a flat-rate method (Standard/LastPurchase/LastSale) values at a rate
        // that is not a purchase cost BY DESIGN and may legitimately exceed spend.
        var costBased = method is "AverageCost" or "Fifo" or "Lifo";
        if (costBased && hasRatedInward && ceiling is { } cap)
        {
            Evaluated("7 total-spend containment", family, method);
            // SATISFIABILITY: value 0 satisfies whenever cap >= -1, i.e. always. Computed, not assumed.
            var sat7 = cap + 1m >= 0m;
            if (!sat7) structural.Add($"7 total-spend containment | {subject} | ceiling {Num(cap)}p");
            if (value.Value > cap + 1m)
                findings[new FindingKey("7 total-spend containment", family, method, subject, key)] = new Finding(
                    Math.Round(value.Value - cap, 4), !sat7,
                    $"closing {Num(value.Value)}p > spend ceiling to date {Num(cap)}p " +
                    $"(excess {Num(value.Value - cap)}p, {Num(Math.Round(cap == 0m ? 0m : value.Value / cap, 4))}x){imputedTag}");
        }

        // ---------- CHECK 8 — implied COGS/unit inside the band of rates actually paid.
        //
        // The true spend S is only known to lie in [rated spend, spend ceiling]: the units nobody
        // bought (unrated inwards, count-ups) cost SOMETHING between nothing and the dearest rate the
        // item has ever seen. So implied COGS/unit lies in the INTERVAL
        //     [ (ratedSpend - closing) / outQty , (ceiling - closing) / outQty ]
        // and the check may only convict when that WHOLE interval falls outside the rate band. Using
        // the ceiling alone would convict N5 — a never-negative book the engine values correctly —
        // which is exactly the kind of false positive that trains a reader to ignore the report.
        // With no imputation the interval collapses to a point and this is the original check.
        if (costBased && ceiling is { } cap8 && floorSpend is { } flo8
            && outMicro is { } om && om > 0m
            && minIn is { } lo8 && maxIn is { } hi8 && hasRatedInward)
        {
            var outQty = om / 1_000_000m;
            Evaluated("8 COGS conservation", family, method);
            var band8Lo = lo8;
            var band8Hi = hasStd && stdPaisa is { } s8 ? Math.Max(hi8, s8) : hi8;

            // Tolerance on the VALUE, never per unit (same reasoning as check 6): 2 paisa for the two
            // spend endpoints' rounding plus 1 paisa per outward unit for the band edges. `1 + 2/outQty`
            // paisa PER UNIT was numerically the same thing and became unbounded as outQty -> 0.
            var spendTol = 2m + outQty;                             // total paisa of SPEND
            var lowSpend = band8Lo * outQty - spendTol;             // COGS floor, as total paisa
            var highSpend = band8Hi * outQty + spendTol;            // COGS ceiling, as total paisa
            var cogsMinTot = flo8 - value.Value;                    // total COGS at the stingiest spend
            var cogsMaxTot = cap8 - value.Value;                    // total COGS at the most generous spend

            // ============================================================================================
            // STRUCTURAL SATISFIABILITY — THE FIX FOR THE CHECK-3 / CHECK-8 CONTRADICTION.
            //
            // Check 8's premise is "SOME closing value makes implied COGS land inside the band of rates
            // actually paid". On a book where MORE UNITS WERE ISSUED THAN WERE EVER BOUGHT that premise is
            // structurally FALSE: no closing value whatsoever can put COGS/unit inside the band, because
            // the money that was spent is simply too little to cover the units that left at any rate the
            // item ever saw. And the magnitude is a MONOTONE INCREASING function of the closing value.
            //
            // G6-001 (In 10 @ 100.13 -> Out 25 -> Count 8) is exactly that book. HEAD closes it at Rs 0 —
            // 8 units physically counted on the shelf, valued at nothing: a WIPED ASSET, and the actual
            // defect. The calibrated point oracle DEMANDS Rs 78.16. Moving from Rs 0 to Rs 78.16 raises
            // check 8's magnitude from 70,064p to 77,880p, so the harness scored the correct fix as
            // "WORSENED ... FAIL" and REJECTED the engine its own check 3 prescribes. A builder who got
            // the fix exactly right saw REJECTED, and the obvious response is to make G6-001 close at
            // Rs 0 again — i.e. THE HARNESS WOULD HAVE STEERED THE FIX INTO THE VERY FAILURE MODE IT
            // EXISTS TO PREVENT. This project has already shipped that failure once: a positive-quantity
            // floor turned a diagnosable -Rs 120 into a plausible Rs 0 and wiped a real asset.
            //
            // So the premise is tested, per subject, BEFORE the magnitude is allowed to mean anything.
            // The COGS interval is [cogsMinTot, cogsMaxTot] = [flo8 - v, cap8 - v]; both endpoints fall as
            // v rises. It intersects [lowSpend, highSpend] iff
            //     cogsMaxTot >= lowSpend   <=>  v <= cap8 - lowSpend      (the binding one)
            //     cogsMinTot <= highSpend  <=>  v >= flo8 - highSpend
            // over the feasible closing values v in [0, cap8]. Non-empty iff
            //     max(0, flo8 - highSpend) <= min(cap8, cap8 - lowSpend).
            // When it is EMPTY the subject is STRUCTURALLY UNSATISFIABLE: check 8 has no discriminating
            // power there, in either direction, so the finding is excluded from the introduced/worsened
            // classification and reported in its own named bucket WITH ITS NUMBERS STILL VISIBLE.
            // ============================================================================================
            var satLo = Math.Max(0m, flo8 - highSpend);
            var satHi = Math.Min(cap8, cap8 - lowSpend);
            var sat8 = satLo <= satHi;
            var totalInMicro = Val(scenario, factItem, "-", asOf, "FactTotalInwardMicro") ?? 0m;
            var qtyNote = om > totalInMicro
                ? $"outward {Num(outQty)} EXCEEDS total inward {Num(totalInMicro / 1_000_000m)}"
                : $"outward {Num(outQty)} vs total inward {Num(totalInMicro / 1_000_000m)}";
            if (!sat8)
                structural.Add(
                    $"8 COGS conservation | {subject} | NO closing value in [0, {Num(cap8)}]p can put COGS/unit " +
                    $"inside rates paid [{Num(band8Lo)}, {Num(band8Hi)}]p ({qtyNote}); satisfying range would be " +
                    $"[{Num(Math.Round(satLo, 4))}, {Num(Math.Round(satHi, 4))}]p, which is empty");

            // too low: even the most generous spend leaves COGS below the cheapest rate ever paid.
            // too high: even the stingiest spend leaves COGS above the dearest rate ever paid.
            var outsideSpend = Math.Max(lowSpend - cogsMaxTot, cogsMinTot - highSpend);
            if (outsideSpend > 0m)
                findings[new FindingKey("8 COGS conservation", family, method, subject, key)] = new Finding(
                    Math.Round(outsideSpend, 4), !sat8,
                    $"spend in [{Num(flo8)}, {Num(cap8)}]p - closing {Num(value.Value)}p over outward " +
                    $"{Num(outQty)} => COGS in [{Num(Math.Round(cogsMinTot, 4))}, {Num(Math.Round(cogsMaxTot, 4))}]p, " +
                    $"wholly outside the admissible COGS range [{Num(Math.Round(lowSpend, 4))}, " +
                    $"{Num(Math.Round(highSpend, 4))}]p implied by rates paid [{Num(band8Lo)}, {Num(band8Hi)}]p, " +
                    $"by {Num(Math.Round(outsideSpend, 4))}p{(sat8 ? "" : "  [STRUCTURALLY-UNSATISFIABLE]")}{imputedTag}");
        }
    }

    return new AuditResult(findings, census, structural);
}

/// <summary>
/// Reporting-only: a closing value that is negative (or exactly zero) while the quantity is positive.
/// Surfaced, never clamped — a previously-tried positive-qty floor turned a diagnosable -Rs 120 into a
/// plausible Rs 0 and wiped a real asset.
/// </summary>
static List<string> ValueSignal(Dictionary<string, string> rows, bool negative)
{
    var hits = new List<string>();
    foreach (var k in rows.Keys.Where(k => Col(k, 4) == "ClosingValuePaisa" && Col(k, 1) != "-")
                              .OrderBy(k => k, StringComparer.Ordinal))
    {
        if (Dec(rows, k) is not { } v) continue;
        var p = k.Split('\t');
        if (Dec(rows, $"{p[0]}\t{p[1]}\t{p[2]}\t{p[3]}\tClosingQtyMicro") is not { } q) continue;
        if (q <= 0m) continue;
        var match = negative ? v < 0m : v == 0m;
        if (match) hits.Add($"{p[0]}/{p[1]}/{p[2]}/{p[3]}  value={Num(v)}p on qty={Num(q / 1_000_000m)}");
    }
    return hits;
}

/// <summary>
/// E1 probes the equal-date tie-break: E1-001 and E1-002 hold the SAME vouchers (same Guids, same rates,
/// same date, same number) inserted in opposite order, so every measure must agree. A mismatch means
/// ordering became insertion-dependent — the exact non-determinism the deterministic-Guid rule exists for.
/// </summary>
static List<string> E1Mismatches(Dictionary<string, string> rows)
{
    var hits = new List<string>();
    foreach (var k in rows.Keys.Where(k => k.StartsWith("E1-001\t", StringComparison.Ordinal))
                              .OrderBy(k => k, StringComparer.Ordinal))
    {
        if (IsSpecDerived(k)) continue;
        var twin = "E1-002\t" + k["E1-001\t".Length..];
        if (!rows.TryGetValue(twin, out var tv)) { hits.Add($"{k}  MISSING TWIN"); continue; }
        if (!string.Equals(rows[k], tv, StringComparison.Ordinal))
            hits.Add($"{k["E1-001\t".Length..]}  E1-001={rows[k]}  E1-002={tv}");
    }
    return hits;
}

// ---- type declarations must follow every top-level statement (incl. local functions) ----

/// <summary>A violation key: which check, family, method, subject — plus the TSV row it came from, so a
/// "resolved" claim can be checked against what the live arm actually produced there (audit C3).</summary>
record struct FindingKey(string Check, string Family, string Method, string Subject, string RowKey);

/// <summary>
/// A violation and HOW BAD it is, in paisa. Magnitude is what makes C2's fix possible.
/// <para><paramref name="Structural"/> marks a subject whose CHECK PREMISE cannot be satisfied by ANY
/// value. On such a subject every value violates and the magnitude moves monotonically with the closing
/// value, so comparing the two arms' magnitudes is not merely uninformative — it points the wrong way.
/// These are bucketed, never classified.</para>
/// </summary>
sealed record Finding(decimal Magnitude, bool Structural, string Text);

/// <summary>The findings, the census of subjects each check evaluated, and the unsatisfiable premises.</summary>
sealed record AuditResult(
    Dictionary<FindingKey, Finding> Findings,
    Dictionary<(string Check, string Family, string Method), int> Census,
    List<string> Structural);

/// <summary>The reference VALUE invariant's results — see the PART A block that prints them.</summary>
sealed record ValueInvariantResult(
    int Checked,
    List<string> QtyFailures,
    List<string> ValueFailures,
    List<string> RateFailures,
    int RunningAverageLayers,
    int RunningAverageSubjects,
    List<string> RunningAverageExamples,
    int OriginBoundLayers,
    List<string> OriginFailures,
    int BlendLayers,
    List<string> BlendExamples,
    // ---- added 2026-07-27 for audit #4 findings [1](2) and [5]
    /// <summary>Subjects whose book ran dry, so the ordering rule actually constrains their layers.</summary>
    int OrderingConstrainedSubjects,
    /// <summary>Layers whose origin token the ordering rule tested against the post-dry set.</summary>
    int OrderingTestedLayers,
    List<string> OrderingFailures,
    /// <summary>Distinct (subject, lot) pairs whose AGGREGATE claimed quantity was bounded by the spec lot.</summary>
    int PerLotChecks,
    List<string> PerLotFailures);

/// <summary>One hand-derived golden's verdict — see CHECK 4c and <see cref="Goldens"/>.</summary>
sealed record GoldenResult(
    int Evaluated,
    List<string> Lines,
    List<string> Mismatches,
    List<string> Missing,
    List<string> UncoveredInvented,
    /// <summary>MULTI-KEY INVENTED subjects with no golden — printed, never failed on: the reference is
    /// not a validated oracle there, so no constant for them could be honestly hand-derived.</summary>
    List<string> UncoveredInventedInfo,
    List<string> UncoveredFamilies,
    List<string> UnexercisedClauses,
    // ---- added 2026-07-27 for audit #5 findings [0], [1] and [4]
    /// <summary>ISSUE-value goldens that found their reference row (audit #5 finding [0]).</summary>
    int IssueEvaluated,
    List<string> IssueLines,
    /// <summary>Constants whose printed hand derivation does not end in the constant (audit #5 finding [1]).</summary>
    List<string> WorkingMismatches,
    /// <summary>Subjects the harness CONVICTS on, and how many of them a golden pins directly.</summary>
    int Check2Convictions, int Check2Pinned, int Check3Convictions, int Check3Pinned,
    int Check10Convictions, int Check10Pinned,
    /// <summary>Subjects whose value depends on a debt clause (RefProvenance BRIEF or INVENTED).</summary>
    int DebtDependentSubjects,
    // ---- added 2026-07-27 (round 7) for audit #6 finding [1]: the clause LABELS are verified from the spec.
    /// <summary>Goldens whose self-declared clause label was checked against FactDebtShape.</summary>
    int ClauseChecked,
    /// <summary>Goldens whose label is NOT true of their subject — a harness failure.</summary>
    List<string> ClauseViolations,
    /// <summary>Goldens whose subject emitted no FactDebtShape row, so the label was verified against nothing.</summary>
    List<string> ClauseNoFact,
    /// <summary>Per-clause tally of how many goldens carry each tag, printed as evidence of what was checked.</summary>
    List<string> ClauseTally);

/// <summary>
/// The STRUCTURAL issue-value assertion (audit #5 finding [0]) — no constants involved. On a Fifo/Lifo
/// subject the issue walk consumes the SURVIVING layers and stops, so an issue can never cost more than
/// the closing value and a probe at or above the closing quantity must cost EXACTLY it.
/// </summary>
sealed record IssueStructureResult(
    int Subjects, int AtOrAbovePairs, int BelowPairs,
    List<string> AtOrAboveFailures, List<string> OverStackFailures, List<string> MonotonicFailures);

/// <summary>
/// The SPEC-DERIVED INVENTED population versus the tags the reference emits about itself
/// (audit #5 finding [3]).
/// </summary>
sealed record InventedPopulationResult(
    int SpecSubjects, int EmittedSubjects, int Compared,
    List<string> EmittedNotSpec, List<string> SpecNotEmitted, List<string> MissingFact);

/// <summary>The debt-aware AverageCost divergence — the magnitude behind CHECK 2's convictions. The block
/// that prints it issues no verdict of its own; CHECK 2 fails the run against the same column.</summary>
sealed record AvgDefectResult(int Subjects, int Disagreeing, List<string> Rows, List<string> Detail);

sealed record CalibrationResult(int Subjects, int Scenarios, SortedSet<string> Methods, List<string> Mismatches);

/// <summary>The debt-aware AverageCost calibration gate — see <see cref="DebtAwareAverageCalibration"/>.</summary>
sealed record AvgCalibrationResult(int Subjects, int Scenarios, List<string> Missing, List<string> Mismatches);

/// <summary>Per-scenario/method build outcomes on one arm — see the BUILD OUTCOME gate in PART A.</summary>
sealed record BuildOutcomeResult(int Rows, int Ok, List<string> Bad, List<string> MissingCells);

sealed record OracleResult(int Evaluated, List<string> Mismatches);

/// <summary>One row of the per-family HEAD-vs-reference defect table.</summary>
sealed class DefectRow
{
    public int Subjects;
    public int Bad;
    public decimal Max;
    public string Worst = "-";
}
