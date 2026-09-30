using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>The presentation unit of a <see cref="PrincipalRatioLine"/> (drives the suffix a UI appends).</summary>
public enum RatioUnit
{
    /// <summary>A pure ratio, e.g. Current Ratio 3.71 — rendered as a bare number (often as "3.71 : 1").</summary>
    Ratio,
    /// <summary>A percentage already multiplied by 100, e.g. Gross Profit 20.55 — rendered with a "%" suffix.</summary>
    Percent,
    /// <summary>A count of days, e.g. Receivables Turnover 62 — rendered with a " days" suffix.</summary>
    Days,
}

/// <summary>One <b>Principal Group</b> figure (left column of the reference product's Ratio Analysis): a label and a Money amount.</summary>
public sealed record PrincipalGroupLine(string Label, Money Value);

/// <summary>
/// One <b>Principal Ratio</b> (right column of the reference product's Ratio Analysis): a label, a nullable value
/// (<c>null</c> = unavailable), and the <see cref="RatioUnit"/> that fixes how a UI renders it.
/// <para>🔴 <paramref name="UnavailableReason"/> exists because a bare "N/A" cannot say WHY. A <c>null</c> value
/// with no reason is the ordinary guarded zero denominator and renders "N/A"; a <c>null</c> value WITH a reason
/// renders that reason, so an operator can tell "this book records no bills" apart from "there were no sales".
/// Two withheld ratios that carry different reasons must render different strings — that is the contract, and
/// it is asserted at the rendered cell, not here.</para>
/// </summary>
public sealed record PrincipalRatioLine(string Label, decimal? Value, RatioUnit Unit, string? UnavailableReason = null);

/// <summary>
/// The <b>Ratio Analysis</b> report (catalog §16), modelled on the reference product's actual report which is split into
/// two columns: <see cref="PrincipalGroups"/> (the key figures) on the left and <see cref="PrincipalRatios"/>
/// (the ratios that relate those figures) on the right. Every ratio guards against a zero denominator: its
/// value is <c>null</c> ("N/A") when the denominator is zero, never a divide-by-zero throw.
/// <para>Working-capital figures come from the closing Balance Sheet asset/liability lines classified by their
/// ledger's actual <b>group id</b> (Current Assets / Current Liabilities / Loans / Capital / Stock-in-Hand /
/// Sundry Debtors / Sundry Creditors). Profitability figures (gross/net profit, sales) come from the Trading &amp;
/// P&amp;L. All money is exact decimal rupees; ratios are exact decimals (percentages are ×100).</para>
/// <para><b>Verified against the reference product's official help documentation</b>
/// (<c>https://help.tallysolutions.com/tally-prime/accounting-financial-reports/ratio-analysis-tally/</c> — the
/// single source for every vendor claim in this file; opened and checked by content, not quoted from memory.
/// 🔴 <b>What that page states and what it does NOT:</b> it gives the ratio's SHAPE — "<i>This is the balance
/// outstanding in relation to the total sales made, multiplied by the total number of days in the period</i>" —
/// and captions the row "Sundry Debtors (due till today)" as "<i>the list of all the debtors and total debts due
/// as of the date of the statement</i>". It nowhere states that the RATIO's numerator is that due-till-today
/// amount rather than the closing balance; it says only that payment performance is measured "<i>irrespective of
/// the outstanding balance on the statement date</i>", and it separates the two figures explicitly. So the shape
/// is vendor-stated, and <b>reading "the balance outstanding" as the bill-wise due-till-today amount is OURS, a
/// documented divergence — see the note on <see cref="ReceivablesTurnoverDays"/>. Do not cite this URL for it.</b>)
/// (Principal Ratios): Current Ratio
/// (CA:CL), Quick Ratio ((CA−Stock):CL), Debt/Equity (Loans:(Capital+NettProfit)), Gross Profit % (GP/Turnover),
/// Nett Profit % (NP/Turnover), Operating Cost % (100 − NettProfit %, i.e. operating cost as a % of Sales),
/// Receivables Turnover in days (Debtors <b>due till today</b> ÷ Sales × days-in-period), Return on Investment %
/// (NettProfit ÷ (Capital + NettProfit) × 100), Return on Working Capital % (NettProfit ÷ WorkingCapital × 100),
/// Inventory Turnover (Turnover ÷ Stock), Working Capital Turnover (Sales ÷ Working Capital).</para>
/// <para>🔴 <b>The two Sundry figures are the exception to the sentence above and are deliberately NOT Balance-Sheet
/// closing balances (defect T0-27).</b> The vendor captions them "Sundry Debtors (due till today)" and "Sundry
/// Creditors (due till today)" and defines Receivables Turnover as the average time customers take to pay their
/// bills <i>irrespective of the outstanding balance on the statement date</i>. Both are therefore taken from the
/// bill-wise <see cref="Outstandings"/> projection, and the closing balances remain available in their own right as
/// <see cref="SundryDebtorsClosing"/> / <see cref="SundryCreditorsClosing"/>. See the note inside
/// <see cref="Build(Company, DateOnly, ReportOptions)"/> for the limitation this carries.</para>
/// <para>🔴 <b><see cref="ReceivablesTurnoverDays"/> is WITHHELD (<c>null</c>) — and the cell says why — whenever
/// the bills cannot account for the Sundry-Debtors money, or can but none of them has fallen due.</b> On either
/// shape the bill-wise numerator is 0, and "0 days" would state as fact that customers pay instantly while the
/// Balance Sheet on the same report shows real debtors. The two reasons render as the two different sentences in
/// <see cref="ReceivablesNoBillWiseDetails"/> / <see cref="ReceivablesNoBillsDueYet"/>.</para>
/// <para>🔴 <b>AND THIS IS A DELIBERATE DIVERGENCE FROM THE REFERENCE PRODUCT, LABELLED AS OURS (R7) — NOT a
/// vendor-verified behaviour.</b> Because <c>MaintainBillByBill</c> defaults to <c>false</c>, withholding applies
/// on the MAJORITY of books, both study fixtures among them, where the vendor — by the only formula its page
/// actually states ("the balance outstanding in relation to the total sales made, multiplied by the total number
/// of days in the period") — would render a NUMBER computed from the outstanding balance. We withhold instead
/// because this build's numerator is the bill-wise due-till-today amount, and on those books that amount measures
/// the absence of bills rather than the speed of payment. The user decision that settled this is recorded against
/// T1-35; the vendor page is not authority for it.</para>
/// </summary>
public sealed record RatioAnalysis(
    // ---- Principal-group figures (typed, for tests / direct access) ----
    Money CurrentAssets,
    Money CurrentLiabilities,
    Money WorkingCapital,
    Money Inventory,
    Money Sales,
    Money GrossProfit,
    Money NetProfit,
    Money ProprietorsFunds,
    Money LongTermDebt,
    Money SundryDebtorsDueTillToday,
    Money SundryCreditorsDueTillToday,
    Money SundryDebtorsClosing,
    Money SundryCreditorsClosing,
    Money CapitalAccount,
    // ---- Principal ratios (typed, for tests / direct access) ----
    decimal? CurrentRatio,
    decimal? QuickRatio,
    decimal? DebtEquityRatio,
    decimal? GrossProfitPercent,
    decimal? NetProfitPercent,
    decimal? OperatingCostPercent,
    decimal? ReceivablesTurnoverDays,
    decimal? ReturnOnInvestmentPercent,
    decimal? ReturnOnWorkingCapitalPercent,
    decimal? InventoryTurnover,
    decimal? WorkingCapitalTurnover,
    // ---- The two render-ready columns (reference-product layout) ----
    IReadOnlyList<PrincipalGroupLine> PrincipalGroups,
    IReadOnlyList<PrincipalRatioLine> PrincipalRatios)
{
    /// <summary>
    /// 🔴 The two reasons Receivables Turnover is withheld, as the operator reads them (user ruling 27). They are
    /// public so a test can assert the RENDERED cell against the shipping string rather than a copy of it, and
    /// they are deliberately DIFFERENT sentences: an operator must be able to tell "this book records no bills"
    /// from "the bills exist but none is due yet", and both from the bare "N/A" of a zero denominator.
    /// </summary>
    public const string ReceivablesNoBillWiseDetails = "N/A — no bill-wise details";

    /// <inheritdoc cref="ReceivablesNoBillWiseDetails"/>
    public const string ReceivablesNoBillsDueYet = "N/A — no bills due yet";

    /// <summary>Divides guarding a zero denominator (returns <c>null</c> = N/A).</summary>
    private static decimal? Ratio(decimal numerator, decimal denominator) =>
        denominator == 0m ? (decimal?)null : numerator / denominator;

    /// <summary>Builds the Ratio-Analysis report as-at <paramref name="asOf"/> under default report options.</summary>
    public static RatioAnalysis Build(Company company, DateOnly asOf)
        => Build(company, asOf, ReportOptions.AsOf(asOf));

    /// <summary>
    /// Builds the Ratio-Analysis report as-at <paramref name="asOf"/>. The P&amp;L / sales window follows
    /// <paramref name="options"/> (RQ-1 period); the balance figures are as-at <paramref name="asOf"/>.
    /// </summary>
    public static RatioAnalysis Build(Company company, DateOnly asOf, ReportOptions options)
    {
        var bs = BalanceSheet.Build(company, asOf, options);
        var pl = ProfitAndLoss.Build(company, asOf, options);

        // Classify each Balance-Sheet asset/liability line by its ledger's ACTUAL group id (not a name match:
        // two distinct groups may share a display name). The synthetic derived Stock-in-Hand and folded
        // Net-Profit heads have a null group id — handled explicitly below.
        var currentAssets = 0m;
        var inventory = 0m;
        var sundryDebtorsClosing = 0m;
        foreach (var line in bs.Assets)
        {
            var isStock = ClassificationRules.GroupIsUnder(line.GroupId, "Stock-in-Hand", company)
                          || (line.GroupId is null && line.GroupName == "Stock-in-Hand"); // derived Stock-in-Hand head
            if (isStock)
            {
                inventory += line.Amount.Amount;
                currentAssets += line.Amount.Amount; // stock is a current asset
            }
            else if (ClassificationRules.GroupIsUnder(line.GroupId, "Current Assets", company))
            {
                currentAssets += line.Amount.Amount;
                if (ClassificationRules.GroupIsUnder(line.GroupId, "Sundry Debtors", company))
                    sundryDebtorsClosing += line.Amount.Amount;
            }
        }

        var currentLiabilities = 0m;
        var sundryCreditorsClosing = 0m;
        var longTermDebt = 0m;
        var capitalAccount = 0m;   // Capital Account ledgers only (excludes folded Net Profit)
        var proprietorsFunds = 0m; // Capital Account + folded period Net Profit (= Capital + Nett Profit)
        foreach (var line in bs.Liabilities)
        {
            if (ClassificationRules.GroupIsUnder(line.GroupId, "Current Liabilities", company))
            {
                currentLiabilities += line.Amount.Amount;
                if (ClassificationRules.GroupIsUnder(line.GroupId, "Sundry Creditors", company))
                    sundryCreditorsClosing += line.Amount.Amount;
            }
            else if (ClassificationRules.GroupIsUnder(line.GroupId, "Loans (Liability)", company))
            {
                longTermDebt += line.Amount.Amount;
            }
            else if (ClassificationRules.GroupIsUnder(line.GroupId, "Capital Account", company))
            {
                capitalAccount += line.Amount.Amount;
                proprietorsFunds += line.Amount.Amount;
            }
            else if (line.GroupId is null && line.GroupName == "Profit & Loss A/c")
            {
                // The folded period Net Profit head: part of proprietor's funds, but NOT the Capital Account.
                proprietorsFunds += line.Amount.Amount;
            }
        }

        var workingCapital = currentAssets - currentLiabilities;
        var quickAssets = currentAssets - inventory;

        // ---------------------------------------------------------------- T0-27: "due till today"
        // 🔴 THE TWO SUNDRY FIGURES THIS REPORT PUBLISHES ARE BILL-WISE, NOT BALANCE-SHEET CLOSING BALANCES.
        // The reference product's Ratio Analysis labels them "Sundry Debtors (due till today)" and "Sundry
        // Creditors (due till today)", and defines Receivables Turnover as the average time customers take to
        // pay their bills "irrespective of the outstanding balance on the statement date" — a phrase that names
        // the closing balance as the WRONG input. This report used to accumulate both from bs.Assets/bs.Liabilities
        // and feed the first into receivablesTurnoverDays, so THREE published figures were wrong at once.
        //
        // 🔴 REUSED, NOT RE-DERIVED. The due-till-today amount comes from the SAME bill-wise projection the
        // Outstandings reports bind to (Outstandings.Build → OutstandingsReport.Receivable/PayableDueTillToday).
        // Computing ageing a second way here would let Ratio Analysis and Outstandings disagree about the same
        // book, which would be a new defect wearing the old one's clothes.
        //
        // A bill counts once its due date has ARRIVED (DueDate <= asOf), which is one day wider than the ageing
        // "Not due" bucket — see OutstandingBill.IsDueBy. A bill carrying no credit period is due on its own
        // voucher date (BillAllocation.EffectiveDueDate), so a book with no credit terms lands back on the full
        // pending amount; the two figures separate exactly when real credit periods exist, which is the point.
        //
        // ⚠️ KNOWN LIMITATION, stated rather than hidden: money with no BILL behind it contributes NOTHING to
        // these two totals even when it sits in a closing balance — and that is NOT only the ledgers whose
        // MaintainBillByBill flag is false. A bill-wise party's balance can equally arrive as an opening balance
        // (this build has no opening bill-wise breakdown), on a line posted with no allocations, or against an
        // On-Account allocation. That is the literal consequence of the vendor's definition; falling back to the
        // closing balance would reinstate T0-27 for every non-bill-wise book. The two Principal-Group rows carry
        // the "(due till today)" qualifier in their captions, so a 0 there is self-describing — but the RATIO
        // cannot say that in a number, which is what receivablesTurnoverDays guards on below.
        //
        // 🔴 THE BASIS THE NUMERATOR IS BUILT ON MUST BE THE ONE THE DENOMINATOR IS BUILT ON. SalesOf below
        // honours options.Scenario ONLY on the as-at path: under an explicit period window it falls back to
        // un-scenarioed movement, because LedgerBalances has no signed-movement overload that also applies a
        // scenario (that limitation is stated on SalesOf itself and is still true). Passing the scenario in here
        // unconditionally therefore produced a NEW mixed-basis figure whenever a scenario and a period were both
        // selected — reachable from the UI, which sets both from a saved view — dividing a scenario numerator by
        // an actual-books denominator. So the numerator follows the same rule the denominator actually
        // implements: scenario on the as-at path, actual books under a window. Both sides are then always one
        // basis, and the day the movement primitive lands, both sides move together.
        var basisScenario = options.Period is null ? options.Scenario : null;

        var outstandings = Outstandings.Build(company, asOf, basisScenario);
        var sundryDebtorsDueTillToday = outstandings.ReceivableDueTillToday.Amount;
        var sundryCreditorsDueTillToday = outstandings.PayableDueTillToday.Amount;

        // 🔴 CAN THIS BOOK ANSWER THE RECEIVABLES-TURNOVER QUESTION AT ALL? A numerator of 0 does NOT mean
        // "nothing has fallen due" unless the bills can account for all the debtor money in the first place.
        // This measures the MONEY the projection cannot account for — Σ (|closing| − its own pending bills) over
        // the Sundry Debtors ledgers — and NOT the MaintainBillByBill flag, which only claims to imply it. The
        // flag test let "0 days" through on a bill-wise party whose balance came in as an opening balance, on a
        // line posted with no allocations, and on an On-Account allocation. See ClosingNotCoveredByBills.
        var debtorsNotCoveredByBills = Outstandings.ClosingNotCoveredByBills(
            company, asOf, "Sundry Debtors", basisScenario).Amount;

        var sales = SalesOf(company, asOf, options);  // net turnover: ledgers under the Sales Accounts primary
        var grossProfit = pl.GrossProfit.Amount;
        var netProfit = pl.NetProfit.Amount;

        // Return-on-Investment denominator = Capital Account + Nett Profit (NOT capital-employed incl. loans).
        // proprietorsFunds already equals (Capital Account + folded Net Profit), which is exactly that figure.
        var roiDenominator = proprietorsFunds;

        var netProfitPercent = Ratio(netProfit * 100m, sales);
        // Operating Cost % = operating cost as a % of Sales = 100 − Nett Profit % (reference-product definition). N/A when
        // there are no sales (Nett Profit % itself is N/A).
        var operatingCostPercent = netProfitPercent is { } np ? 100m - np : (decimal?)null;

        // Receivables Turnover in days = (Sundry Debtors DUE TILL TODAY ÷ Sales) × days-in-period (inclusive
        // window). 🔴 The numerator is the bill-wise figure, NOT the closing balance — see the T0-27 note above.
        //
        // 🔴 TWO REASONS TO WITHHOLD IT, AND THEY ARE DIFFERENT SENTENCES ON SCREEN (user ruling 27: the cell
        // must say WHY, and must not be a bare "N/A" an operator cannot tell from a zero denominator).
        //   (a) Debtor money the bills cannot account for ⇒ the numerator is not a measurement of this book at
        //       all. This is the DEFAULT book shape (MaintainBillByBill defaults to false; both study fixtures
        //       are such books), and it also catches opening balances, allocation-less lines and On Account.
        //   (b) Every rupee IS carried by a bill, but none of those bills has fallen due ⇒ the numerator is a
        //       true 0 while real receivables exist, and "0 days" would state that customers pay instantly. A
        //       fully bill-wise book whose invoices are simply inside their credit period is the ordinary steady
        //       state of a healthy debtors ledger, so this is not an edge case either.
        // Order matters: (a) is checked first because on a book with no bills at all BOTH hold, and (a) is the
        // more specific truth. A book with no debtors at all trips neither and still publishes its honest 0.
        var window = options.EffectivePeriod(company);
        var daysInPeriod = window.To.DayNumber - window.From.DayNumber + 1;
        var receivablesUnavailableReason =
            debtorsNotCoveredByBills != 0m ? ReceivablesNoBillWiseDetails
            : sundryDebtorsDueTillToday == 0m && sundryDebtorsClosing != 0m ? ReceivablesNoBillsDueYet
            : null;
        var receivablesTurnoverDays = receivablesUnavailableReason is not null
            ? (decimal?)null
            : Ratio(sundryDebtorsDueTillToday * daysInPeriod, sales);

        var currentRatio = Ratio(currentAssets, currentLiabilities);
        var quickRatio = Ratio(quickAssets, currentLiabilities);
        var debtEquityRatio = Ratio(longTermDebt, proprietorsFunds);
        var grossProfitPercent = Ratio(grossProfit * 100m, sales);
        var returnOnInvestmentPercent = Ratio(netProfit * 100m, roiDenominator);
        var returnOnWorkingCapitalPercent = Ratio(netProfit * 100m, workingCapital);
        var inventoryTurnover = Ratio(sales, inventory);
        var workingCapitalTurnover = Ratio(sales, workingCapital);

        var principalGroups = new List<PrincipalGroupLine>
        {
            new("Working Capital", new Money(workingCapital)),
            new("Current Assets", new Money(currentAssets)),
            new("Current Liabilities", new Money(currentLiabilities)),
            // 🔴 The vendor's own captions carry the qualifier, and the qualifier is the whole of T0-27: a bare
            // "Sundry Debtors" invites the reader to reconcile it against the Balance Sheet, which is precisely
            // what it must NOT equal. The label states the basis so the two figures can differ in plain sight.
            new("Sundry Debtors (due till today)", new Money(sundryDebtorsDueTillToday)),
            new("Sundry Creditors (due till today)", new Money(sundryCreditorsDueTillToday)),
            new("Stock-in-Hand", new Money(inventory)),
            new("Sales Accounts", new Money(sales)),
            new("Capital Account", new Money(capitalAccount)),
            new("Nett Profit", new Money(netProfit)),
        };

        var principalRatios = new List<PrincipalRatioLine>
        {
            new("Current Ratio", currentRatio, RatioUnit.Ratio),
            new("Quick Ratio", quickRatio, RatioUnit.Ratio),
            new("Debt / Equity Ratio", debtEquityRatio, RatioUnit.Ratio),
            new("Gross Profit %", grossProfitPercent, RatioUnit.Percent),
            new("Nett Profit %", netProfitPercent, RatioUnit.Percent),
            new("Operating Cost %", operatingCostPercent, RatioUnit.Percent),
            new("Receivables Turnover (days)", receivablesTurnoverDays, RatioUnit.Days,
                receivablesUnavailableReason),
            new("Return on Investment %", returnOnInvestmentPercent, RatioUnit.Percent),
            new("Return on Working Capital %", returnOnWorkingCapitalPercent, RatioUnit.Percent),
            new("Inventory Turnover", inventoryTurnover, RatioUnit.Ratio),
            new("Working Capital Turnover", workingCapitalTurnover, RatioUnit.Ratio),
        };

        return new RatioAnalysis(
            new Money(currentAssets),
            new Money(currentLiabilities),
            new Money(workingCapital),
            new Money(inventory),
            new Money(sales),
            new Money(grossProfit),
            new Money(netProfit),
            new Money(proprietorsFunds),
            new Money(longTermDebt),
            new Money(sundryDebtorsDueTillToday),
            new Money(sundryCreditorsDueTillToday),
            new Money(sundryDebtorsClosing),
            new Money(sundryCreditorsClosing),
            new Money(capitalAccount),
            CurrentRatio: currentRatio,
            QuickRatio: quickRatio,
            DebtEquityRatio: debtEquityRatio,
            GrossProfitPercent: grossProfitPercent,
            NetProfitPercent: netProfitPercent,
            OperatingCostPercent: operatingCostPercent,
            ReceivablesTurnoverDays: receivablesTurnoverDays,
            ReturnOnInvestmentPercent: returnOnInvestmentPercent,
            ReturnOnWorkingCapitalPercent: returnOnWorkingCapitalPercent,
            InventoryTurnover: inventoryTurnover,
            WorkingCapitalTurnover: workingCapitalTurnover,
            PrincipalGroups: principalGroups,
            PrincipalRatios: principalRatios);
    }

    /// <summary>
    /// Net sales turnover: Σ credit-side movement of every ledger whose primary ancestor is <b>Sales Accounts</b>,
    /// over the report window (or as-at when no window). Excludes the closing-stock adjustment (a Direct Income),
    /// so it is the pure turnover the profitability ratios divide by.
    /// <para><b>Known limitation (same as P&amp;L):</b> when a scenario is selected together with an explicit
    /// period window, sales fall back to un-scenarioed movement — <see cref="LedgerBalances"/> has no
    /// signed-movement overload that also applies a scenario. Scenario-aware period sales is deferred to the
    /// engine slice that adds a scenario+window movement primitive; the as-at path already honours the scenario.</para>
    /// </summary>
    private static decimal SalesOf(Company company, DateOnly asOf, ReportOptions options)
    {
        var window = options.Period;
        var sales = 0m;
        foreach (var ledger in company.Ledgers)
        {
            var group = company.FindGroup(ledger.GroupId);
            if (group is null) continue;
            if (ClassificationRules.PrimaryAncestorOf(group, company).Name != "Sales Accounts") continue;

            var signed = window is { } w
                ? LedgerBalances.SignedMovement(company, ledger, w.From, asOf)
                : LedgerBalances.SignedClosing(company, ledger, asOf, options.Scenario);
            sales += -signed; // credit magnitude
        }
        return sales;
    }
}
