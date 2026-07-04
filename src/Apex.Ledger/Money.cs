using System.Globalization;

namespace Apex.Ledger;

/// <summary>
/// An exact monetary amount in Indian Rupees, stored as <see cref="decimal"/>
/// so ledger math reconciles to the paisa (NFR-3). Binary floating point is
/// deliberately avoided. This is a minimal Phase-0 stub that signals intent;
/// the full posting/valuation engine arrives in Phase 1.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    /// <summary>The amount, in rupees, as an exact decimal.</summary>
    public decimal Amount { get; }

    /// <summary>Zero rupees.</summary>
    public static readonly Money Zero = new(0m);

    public Money(decimal amount) => Amount = amount;

    /// <summary>Creates a <see cref="Money"/> from a rupee amount.</summary>
    public static Money FromRupees(decimal rupees) => new(rupees);

    /// <summary>
    /// This amount rounded to the paisa (2 decimal places), using normal (away-from-zero) rounding —
    /// the same convention the interest engine uses (<see cref="Domain.InterestParameters.ApplyRounding"/>).
    /// A base-currency amount must be paisa-exact to persist (INTEGER paisa, NFR-3); any amount derived from
    /// a non-round factor — notably a forex line's base = <c>forexAmount × rate</c> — must be snapped to the
    /// paisa here so it never carries a sub-paisa tail that the paisa store would reject.
    /// </summary>
    public Money RoundToPaisa() => new(Math.Round(Amount, 2, MidpointRounding.AwayFromZero));

    /// <summary>The paisa-exact base value of <paramref name="forexAmount"/> × <paramref name="rate"/>.</summary>
    public static Money ForexBase(Money forexAmount, decimal rate) =>
        new Money(forexAmount.Amount * rate).RoundToPaisa();

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);
    public static Money operator -(Money a) => new(-a.Amount);

    public bool Equals(Money other) => Amount == other.Amount;
    public override bool Equals(object? obj) => obj is Money other && Equals(other);
    public override int GetHashCode() => Amount.GetHashCode();
    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public static bool operator ==(Money a, Money b) => a.Equals(b);
    public static bool operator !=(Money a, Money b) => !a.Equals(b);
    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    /// <summary>Formats the amount to two decimal places (paisa), invariant culture.</summary>
    public override string ToString() => Amount.ToString("0.00", CultureInfo.InvariantCulture);
}
