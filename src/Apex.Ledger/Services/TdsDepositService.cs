using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The TDS <b>deposit</b> engine (catalog §13; Phase 7 slice 3): pays the accrued "TDS Payable" liability into the
/// bank via a <b>Stat Payment</b> and records the ITNS-281 <see cref="TdsChallan"/> it produced. Framework-, DB-,
/// clock- and RNG-free — a pure, deterministic mutation over the <see cref="Company"/> aggregate. This mirrors how
/// a GSTR-3B net liability is discharged by a real Payment voucher against the payable ledger plus a challan
/// record; here it is the deductor's monthly TDS deposit (Dr "TDS Payable" / Cr Bank).
/// <para>
/// A <b>Stat Payment</b> is an ordinary Payment voucher (Tally "Ctrl+F"): it reuses the Payment
/// <see cref="VoucherBaseType"/> unchanged and is only <i>marked</i> by <see cref="VoucherType.IsStatPayment"/>
/// (never a new base type — so <c>GstReportSupport.DirectionOf</c> and every exhaustive base-type switch are
/// untouched). It debits the "TDS Payable" liability and credits Bank/Cash, driving the payable balance back
/// toward zero exactly like a normal payment reduces a creditor.
/// </para>
/// </summary>
public sealed class TdsDepositService
{
    private readonly Company _company;

    public TdsDepositService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>The auto-created Stat-Payment Payment voucher-type name.</summary>
    public const string StatPaymentTypeName = "TDS Stat Payment";

    /// <summary>
    /// Finds — or creates — the Stat-Payment Payment voucher type (Ctrl+F). Idempotent: an existing Payment type
    /// flagged <see cref="VoucherType.IsStatPayment"/> is reused; otherwise a new active "TDS Stat Payment" Payment
    /// type is added (reusing the Payment base type; only the flag distinguishes it). Never duplicates.
    /// </summary>
    public VoucherType EnsureStatPaymentType()
    {
        var existing = _company.VoucherTypes.FirstOrDefault(t => t.IsStatPaymentType);
        if (existing is not null) return existing;

        var statType = new VoucherType(
            Guid.NewGuid(), StatPaymentTypeName, VoucherBaseType.Payment,
            NumberingMethod.Automatic, defaultShortcut: "Ctrl+F", abbreviation: "Stat",
            isActive: true, isPredefined: false, isStatPayment: true);
        _company.AddVoucherType(statType);
        return statType;
    }

    /// <summary>
    /// Builds (does not post) the Stat-Payment voucher that deposits <paramref name="amount"/> of accrued TDS: a
    /// two-line Payment — <c>Dr "TDS Payable" = amount</c> / <c>Cr <paramref name="bank"/> = amount</c> — of the
    /// Stat-Payment type. The caller posts it through <c>LedgerService.Post</c> (so the balance invariant runs) and
    /// then records + links a challan. Requires TDS to be enabled (the auto-created "TDS Payable" ledger).
    /// </summary>
    public Voucher BuildStatPayment(Money amount, Domain.Ledger bank, DateOnly date, VoucherType statType)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(statType);
        if (amount.Amount <= 0m)
            throw new ArgumentException("Stat-Payment amount must be > 0.", nameof(amount));
        if (!amount.IsPaisaExact)
            throw new InvalidOperationException($"Stat-Payment amount {amount} must be paisa-exact.");
        if (!statType.IsStatPaymentType)
            throw new ArgumentException("The voucher type is not a Stat-Payment Payment type.", nameof(statType));

        var payable = new TdsService(_company).RequirePayableLedger();
        return new Voucher(Guid.NewGuid(), statType.Id, date, new[]
        {
            new EntryLine(payable.Id, amount, DrCr.Debit),   // discharge the liability
            new EntryLine(bank.Id, amount, DrCr.Credit),     // pay from the bank
        });
    }

    /// <summary>
    /// Records an ITNS-281 <see cref="TdsChallan"/> on the company and links it to the Stat-Payment voucher that
    /// booked the deposit (Phase 7 slice 3). Returns the created challan.
    /// </summary>
    public TdsChallan RecordChallan(
        string challanNo, string bsrCode, DateOnly depositDate, Money amount, string section, string minorHead,
        Voucher statPaymentVoucher)
    {
        ArgumentNullException.ThrowIfNull(statPaymentVoucher);
        var challan = new TdsChallan(Guid.NewGuid(), challanNo, bsrCode, depositDate, amount, section, minorHead);
        _company.AddTdsChallan(challan);
        _company.LinkChallanToVoucher(challan.Id, statPaymentVoucher.Id);
        return challan;
    }

    /// <summary>The current outstanding "TDS Payable" liability as of <paramref name="asOf"/> — a positive rupee
    /// figure (credit balance) means tax deducted but not yet deposited. Reads posted ledger balances (pure
    /// projection); zero once every deduction is deposited.</summary>
    public Money OutstandingPayable(DateOnly asOf)
    {
        var payable = new TdsService(_company).RequirePayableLedger();
        var signed = LedgerBalances.SignedClosing(_company, payable, asOf);
        // A liability sits on the credit side (negative signed) → the outstanding magnitude is −signed when negative.
        return new Money(signed < 0m ? -signed : 0m);
    }
}
