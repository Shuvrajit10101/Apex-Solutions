using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>GST payment</b> engine (Phase 9 slice 7; RQ-22; A14-CONFIRMED §11.2/§11.3): PMT-06 challan deposits into the
/// electronic cash ledger, the cash discharge of output tax, and DRC-03 voluntary / self-ascertained payments. Every
/// posting flows through the single guarded entry-point <see cref="LedgerService.Post"/> (so the balance invariant
/// runs). Two HARD invariants the poster rejects fail-fast (never silent-fix):
/// <list type="bullet">
///   <item><b>Cash minor-head isolation</b> — a deposit under one (major, minor) cell is drawable only within that
///     cell (cross-cell movement needs PMT-09, out of scope); a draw against an unfunded cell is refused.</item>
///   <item><b>Credit tax-only</b> — the electronic credit ledger (PMT-02) settles <b>only</b> the Tax minor head;
///     interest / penalty / fee / late-fee are cash-only (§49(4) / Rule 86(2)).</item>
/// </list>
/// §50 interest is captured flag/field-only (DP-34; 18% p.a. if ever surfaced — §11.6), never auto-computed.
/// </summary>
public sealed class GstDepositService
{
    private readonly Company _company;

    public GstDepositService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>The auto-created GST Stat-Payment (Ctrl+F) Payment voucher-type name.</summary>
    public const string StatPaymentTypeName = "GST Stat Payment";

    /// <summary>How a DRC-03 (or cash discharge) is funded.</summary>
    public enum PaymentMethod
    {
        /// <summary>From the electronic cash ledger (Cr Electronic Cash Ledger).</summary>
        Cash,

        /// <summary>Directly from the bank (Cr Bank) — the Ctrl+F autofill single-step shape.</summary>
        Bank,

        /// <summary>From the electronic credit ledger (Cr Input {head}) — TAX ONLY (§49(4)); interest/penalty/fee rejected.</summary>
        Credit,
    }

    /// <summary>
    /// The HARD credit tax-only rule (§11.2; §49(4) / Rule 86(2)): the electronic credit ledger settles ONLY the Tax
    /// minor head. Throws if a caller tries to discharge interest / penalty / fee / late-fee from credit. Used by the
    /// DRC-03 poster and the set-off engine; exposed so the rule is directly assertable.
    /// </summary>
    public static void EnsureCreditCanSettle(GstMinorHead minor)
    {
        if (minor != GstMinorHead.Tax)
            throw new InvalidOperationException(
                $"The electronic credit ledger settles TAX only — {minor} is cash-only (§49(4) / Rule 86(2)); pay it in cash.");
    }

    /// <summary>
    /// Finds — or creates — the GST Stat-Payment (Ctrl+F) Payment voucher type (idempotent). Reuses the Payment base
    /// type; only <see cref="VoucherType.IsStatPayment"/> marks it — so <c>DirectionOf</c> (Payment ⇒ null) keeps the
    /// deposit / discharge vouchers OUT of the Table 3.1 / 4(A) sums. A GST stat-payment is kept distinct (by name)
    /// from the TDS one so the two never collide.
    /// </summary>
    public VoucherType EnsureStatPaymentType()
    {
        var existing = _company.VoucherTypes.FirstOrDefault(
            t => t.IsStatPaymentType && string.Equals(t.Name, StatPaymentTypeName, StringComparison.Ordinal));
        if (existing is not null) return existing;

        var type = new VoucherType(
            Guid.NewGuid(), StatPaymentTypeName, VoucherBaseType.Payment,
            NumberingMethod.Automatic, defaultShortcut: "Ctrl+F", abbreviation: "GStat",
            isActive: true, isPredefined: false, isStatPayment: true);
        _company.AddVoucherType(type);
        return type;
    }

    // ==============================================================================================================
    //  PMT-06 — deposit into the electronic cash ledger
    // ==============================================================================================================

    /// <summary>
    /// Posts a <b>PMT-06 deposit</b> into the electronic cash ledger — a two-leg Payment voucher <c>Dr Electronic Cash
    /// Ledger</c> / <c>Cr <paramref name="bank"/></c> — and records the <see cref="GstChallan"/> (CPIN → CIN/BRN,
    /// major + minor head), linking it to the deposit voucher via the challan's own <see cref="GstChallan.VoucherId"/>.
    /// The cash ledger is credited <b>only on CIN</b> (§11.3), so a non-blank <paramref name="cin"/> is required.
    /// Posted through <see cref="LedgerService.Post"/> (balanced by construction). §50 interest is flag-only
    /// (<paramref name="interestFlag"/>).
    /// </summary>
    public (Voucher Voucher, GstChallan Challan) PostPmt06(
        GstTaxHead majorHead, GstMinorHead minorHead, Money amount, Domain.Ledger bank, DateOnly date,
        string cpin, string cin, string? brn = null, bool interestFlag = false)
    {
        ArgumentNullException.ThrowIfNull(bank);
        if (amount.Amount <= 0m)
            throw new ArgumentException("PMT-06 deposit amount must be > 0.", nameof(amount));
        if (!amount.IsPaisaExact)
            throw new InvalidOperationException($"PMT-06 deposit amount {amount} must be paisa-exact.");
        if (string.IsNullOrWhiteSpace(cin))
            throw new InvalidOperationException(
                "The electronic cash ledger is credited only on CIN (the bank-credit reference) — supply the CIN to post the deposit.");

        var cash = new GstService(_company).EnsureElectronicCashLedger();
        var type = EnsureStatPaymentType();

        var voucher = new LedgerService(_company).Post(new Voucher(
            Guid.NewGuid(), type.Id, date, new[]
            {
                new EntryLine(cash.Id, amount, DrCr.Debit),  // deposit into the electronic cash ledger
                new EntryLine(bank.Id, amount, DrCr.Credit), // paid from the bank
            },
            narration: $"GST cash deposit (PMT-06) — {GstService.TaxLedgerName(majorHead, GstTaxDirection.Output)} / {minorHead}"));

        var challan = new GstChallan(
            Guid.NewGuid(), cpin, cin, brn, date, majorHead, minorHead, amount, voucher.Id, interestFlag);
        _company.AddGstChallan(challan);
        return (voucher, challan);
    }

    /// <summary>
    /// Posts a <b>cash discharge of output tax</b> for <paramref name="head"/> — a Payment voucher <c>Dr Output
    /// {head}</c> / <c>Cr Electronic Cash Ledger</c>, tagged <see cref="GstAdjustmentKind.CashPayment"/> — drawing the
    /// deposited cash down within its own (major, Tax) cell. Enforces cash minor-head isolation: the (head, Tax) cell
    /// must have enough deposited-and-unutilised balance, else the draw is refused. Output tax is a Tax minor-head
    /// liability, so the discharge always draws the Tax cell.
    /// </summary>
    public Voucher PostCashDischarge(GstTaxHead head, Money amount, DateOnly date)
    {
        if (amount.Amount <= 0m)
            throw new ArgumentException("Cash discharge amount must be > 0.", nameof(amount));
        if (!amount.IsPaisaExact)
            throw new InvalidOperationException($"Cash discharge amount {amount} must be paisa-exact.");

        EnsureCashAvailable(head, GstMinorHead.Tax, amount);

        var gst = new GstService(_company);
        var cash = gst.EnsureElectronicCashLedger();
        if (head == GstTaxHead.Cess) gst.EnsureCessLedgers();
        var output = gst.FindTaxLedger(head, GstTaxDirection.Output)
            ?? throw new InvalidOperationException(
                $"Output {head} ledger not found — enable GST first (EnableGst auto-creates it).");
        var type = EnsureStatPaymentType();

        return new LedgerService(_company).Post(new Voucher(
            Guid.NewGuid(), type.Id, date, new[]
            {
                new EntryLine(output.Id, amount, DrCr.Debit,
                    gst: new GstLineTax(head, 0, Money.Zero, adjustment: GstAdjustmentKind.CashPayment)),
                new EntryLine(cash.Id, amount, DrCr.Credit,
                    gst: CashDrawTag(head, GstMinorHead.Tax)),
            },
            narration: $"GST cash payment — {GstService.TaxLedgerName(head, GstTaxDirection.Output)}"));
    }

    // ==============================================================================================================
    //  DRC-03 — voluntary / self-ascertained payment
    // ==============================================================================================================

    /// <summary>
    /// Posts a <b>DRC-03 voluntary / self-ascertained payment</b> (Rule 142(2)/(3)): a payment voucher discharging the
    /// per-head tax (+ optional §50 interest, flag-only) and a <see cref="GstDrc03"/> record capturing the cause,
    /// period, per-head/minor amounts and the optional DRC-03A demand link. Funded per <paramref name="method"/>:
    /// <b>Cash</b> (Cr Electronic Cash Ledger) / <b>Bank</b> (Cr Bank) / <b>Credit</b> (Cr Input {head} — TAX ONLY;
    /// any non-zero interest is rejected, §11.2). The debit lands in the ITC-reversal cost ledger. Interest is a
    /// passed field, never auto-computed (DP-34; 18% if surfaced — §11.6).
    /// </summary>
    public (Voucher Voucher, GstDrc03 Record) PostDrc03(
        string cause, string period, DateOnly date,
        long cgstPaisa, long sgstPaisa, long igstPaisa, long cessPaisa, long interestPaisa,
        PaymentMethod method, Domain.Ledger? bank = null,
        string? drc03Ref = null, string? drc03aDemandRef = null, DateTimeOffset? createdAt = null)
    {
        if (cgstPaisa < 0 || sgstPaisa < 0 || igstPaisa < 0 || cessPaisa < 0 || interestPaisa < 0)
            throw new ArgumentException("DRC-03 amounts must be ≥ 0 paisa.");
        var totalTax = cgstPaisa + sgstPaisa + igstPaisa + cessPaisa;
        if (totalTax + interestPaisa <= 0)
            throw new ArgumentException("A DRC-03 must discharge a positive amount.");

        // Credit tax-only (§11.2): interest / penalty / fee can never be discharged from the credit ledger.
        if (method == PaymentMethod.Credit && interestPaisa > 0)
            EnsureCreditCanSettle(GstMinorHead.Interest);

        var gst = new GstService(_company);
        var cost = gst.EnsureItcReversalCostLedger();
        var type = EnsureStatPaymentType();
        var lines = new List<EntryLine>();

        // Debit the cost of the discharge (the tax + interest becomes a cost / suspense on a voluntary payment).
        var totalMoney = new Money((totalTax + interestPaisa) / 100m);
        lines.Add(new EntryLine(cost.Id, totalMoney, DrCr.Debit));

        if (method == PaymentMethod.Credit)
        {
            // TAX ONLY — reduce the Input {head} credit pools per head (Cr Input {head}). This is a credit-ledger
            // UTILISATION (not a cash payment), so it carries the same set-off tag the Rule-88A poster puts on its
            // credit-utilisation leg (GstSetOffService.PostSetOff) — that keeps the ElectronicLedgersView credit-pool
            // movement decomposition (additions − utilised − reversed) footing to the closing Input balance.
            void Credit(GstTaxHead head, long paisa)
            {
                if (paisa <= 0) return;
                if (head == GstTaxHead.Cess) gst.EnsureCessLedgers();
                var input = gst.FindTaxLedger(head, GstTaxDirection.Input)
                    ?? throw new InvalidOperationException($"Input {head} ledger not found — enable GST first.");
                lines.Add(new EntryLine(input.Id, new Money(paisa / 100m), DrCr.Credit,
                    gst: new GstLineTax(head, 0, Money.Zero, adjustment: GstAdjustmentKind.SetOff)));
            }
            Credit(GstTaxHead.Central, cgstPaisa);
            Credit(GstTaxHead.State, sgstPaisa);
            Credit(GstTaxHead.Integrated, igstPaisa);
            Credit(GstTaxHead.Cess, cessPaisa);
        }
        else if (method == PaymentMethod.Bank)
        {
            // Bank-funded: one Cr Bank leg (the bank ledger is not part of the cash-cell projection, so no tag).
            var bankLedger = bank ?? throw new ArgumentException("A bank ledger is required for a bank-funded DRC-03.", nameof(bank));
            lines.Add(new EntryLine(bankLedger.Id, totalMoney, DrCr.Credit));
        }
        else // Cash
        {
            // Cash minor-head isolation: verify EVERY (major, minor) cell has enough deposited-and-unutilised cash
            // BEFORE posting any leg (so a multi-head DRC-03 never self-blocks on its own not-yet-posted draws).
            if (cgstPaisa > 0) EnsureCashAvailable(GstTaxHead.Central, GstMinorHead.Tax, new Money(cgstPaisa / 100m));
            if (sgstPaisa > 0) EnsureCashAvailable(GstTaxHead.State, GstMinorHead.Tax, new Money(sgstPaisa / 100m));
            if (igstPaisa > 0) EnsureCashAvailable(GstTaxHead.Integrated, GstMinorHead.Tax, new Money(igstPaisa / 100m));
            if (cessPaisa > 0) EnsureCashAvailable(GstTaxHead.Cess, GstMinorHead.Tax, new Money(cessPaisa / 100m));
            if (interestPaisa > 0) EnsureCashAvailable(GstTaxHead.Integrated, GstMinorHead.Interest, new Money(interestPaisa / 100m));

            // Draw each (major, minor) cell as its OWN tagged Cr Electronic Cash Ledger leg (mirror PostCashDischarge)
            // so AvailableCash nets the draw against exactly its cell — the SAME deposit can never be drawn twice, and
            // the cash ledger can never be overdrawn to a credit balance.
            var cash = gst.EnsureElectronicCashLedger();
            void Draw(GstTaxHead major, GstMinorHead minor, long paisa)
            {
                if (paisa <= 0) return;
                lines.Add(new EntryLine(cash.Id, new Money(paisa / 100m), DrCr.Credit,
                    gst: CashDrawTag(major, minor)));
            }
            Draw(GstTaxHead.Central, GstMinorHead.Tax, cgstPaisa);
            Draw(GstTaxHead.State, GstMinorHead.Tax, sgstPaisa);
            Draw(GstTaxHead.Integrated, GstMinorHead.Tax, igstPaisa);
            Draw(GstTaxHead.Cess, GstMinorHead.Tax, cessPaisa);
            Draw(GstTaxHead.Integrated, GstMinorHead.Interest, interestPaisa);
        }

        var voucher = new LedgerService(_company).Post(new Voucher(
            Guid.NewGuid(), type.Id, date, lines, narration: $"DRC-03 — {cause} — {period}"));

        var record = new GstDrc03(
            Guid.NewGuid(), drc03Ref, cause, period, cgstPaisa, sgstPaisa, igstPaisa, cessPaisa, interestPaisa,
            drc03aDemandRef, voucher.Id, createdAt ?? DateTimeOffset.UnixEpoch);
        _company.AddGstDrc03(record);
        return (voucher, record);
    }

    // ==============================================================================================================
    //  Cash-cell availability (projection over challans − posted cash draws)
    // ==============================================================================================================

    /// <summary>
    /// Enforces cash minor-head isolation: the (<paramref name="major"/>, <paramref name="minor"/>) cell must have
    /// enough deposited-and-unutilised cash to cover <paramref name="amount"/>. Available = Σ challan deposits into
    /// that cell − Σ posted cash draws attributed to it. Refuses a draw against an unfunded cell (a deposit under one
    /// minor head can never discharge another).
    /// </summary>
    public void EnsureCashAvailable(GstTaxHead major, GstMinorHead minor, Money amount)
    {
        var available = AvailableCash(major, minor);
        if (amount.Amount > available.Amount)
            throw new InvalidOperationException(
                $"Electronic cash ledger has {available} in the ({major}, {minor}) cell — cannot draw {amount}. " +
                "A deposit is drawable only within its own (major, minor) cell (cross-cell movement needs PMT-09).");
    }

    /// <summary>
    /// The GST tag on a <c>Cr Electronic Cash Ledger</c> draw leg. The cash ledger is a 2-D (major, minor) matrix, but a
    /// cash-discharge line carries no real tax rate — so the draw is keyed to its cell using the two free slots on
    /// <see cref="GstLineTax"/>: <see cref="GstLineTax.TaxHead"/> carries the MAJOR head and
    /// <see cref="GstLineTax.RateBasisPoints"/> carries the MINOR head ordinal (Tax = 0). <see cref="AvailableCash"/>
    /// keys draws by BOTH, so a (major, Tax) draw never decrements that major's Interest / Penalty / Fee cell (and
    /// vice-versa) — the per-cell projection foots exactly with no new schema.
    /// </summary>
    private static GstLineTax CashDrawTag(GstTaxHead major, GstMinorHead minor)
        => new(major, (int)minor, Money.Zero, adjustment: GstAdjustmentKind.CashPayment);

    /// <summary>The unutilised cash in a (major, minor) cell = Σ challan deposits − Σ posted cash draws attributed to
    /// it (each draw is keyed to its cell by <see cref="CashDrawTag"/>; a paisa-exact projection). </summary>
    public Money AvailableCash(GstTaxHead major, GstMinorHead minor)
    {
        var deposited = 0m;
        foreach (var ch in _company.GstChallans)
            if (ch.MajorHead == major && ch.MinorHead == minor)
                deposited += ch.Amount.Amount;

        var drawn = 0m;
        var cashLedger = _company.FindLedgerByName(GstService.ElectronicCashLedgerName);
        if (cashLedger is not null)
        {
            // Every cash draw (Cr Electronic Cash Ledger, tagged CashPayment) is keyed to its (major, minor) cell by
            // CashDrawTag: TaxHead == major AND RateBasisPoints == (int)minor. Keying by BOTH nets the draw against
            // exactly the cell it discharged — a Tax draw never bleeds into the Interest cell of the same major head.
            foreach (var v in _company.Vouchers)
            {
                if (v.Cancelled) continue;
                foreach (var line in v.Lines)
                    if (line.LedgerId == cashLedger.Id && line.Side == DrCr.Credit
                        && line.Gst is { Adjustment: GstAdjustmentKind.CashPayment } g
                        && g.TaxHead == major && g.RateBasisPoints == (int)minor)
                        drawn += line.Amount.Amount;
            }
        }
        return new Money(deposited - drawn);
    }
}
