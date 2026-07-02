namespace Apex.Ledger.Services;

/// <summary>Base for all posting-time rejections (fail-fast; §6).</summary>
public class InvalidVoucherException : Exception
{
    public InvalidVoucherException(string message) : base(message) { }
}

/// <summary>Thrown when Σ Debit ≠ Σ Credit on a voucher (the golden invariant; §6.1).</summary>
public sealed class UnbalancedVoucherException : InvalidVoucherException
{
    public Money TotalDebit { get; }
    public Money TotalCredit { get; }

    public UnbalancedVoucherException(Money totalDebit, Money totalCredit)
        : base($"Unbalanced voucher: Σ Dr {totalDebit} ≠ Σ Cr {totalCredit}.")
    {
        TotalDebit = totalDebit;
        TotalCredit = totalCredit;
    }
}
