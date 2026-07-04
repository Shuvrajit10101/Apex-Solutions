using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// Seeds the company <b>base currency</b> as a first-class <see cref="Currency"/> (catalog §2/§20
/// Multi-currency; plan.md §10 C-1). The base currency is defined by the existing
/// <see cref="Company.BaseCurrencySymbol"/> / <see cref="Company.BaseCurrencyName"/> /
/// <see cref="Company.DecimalPlaces"/> fields (₹/INR, 2-dp), so seeding just projects those into a
/// <see cref="Currency"/> row flagged <see cref="Currency.IsBaseCurrency"/>. Additional currencies
/// (USD, EUR, …) are created by the user, not seeded.
/// </summary>
public static class SeedCurrencies
{
    /// <summary>Builds the base-currency <see cref="Currency"/> from a company's base-currency fields.</summary>
    public static Currency BuildBaseCurrency(Company company) =>
        new(Guid.NewGuid(), company.BaseCurrencySymbol, company.BaseCurrencyName,
            decimalPlaces: company.DecimalPlaces, isBaseCurrency: true);
}
