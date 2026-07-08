using Apex.Ledger.Io;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Tests for <see cref="IndianAmountInWords"/> — the paisa-accurate Indian lakh/crore number-to-words used on
/// vouchers and the GST tax invoice. Deterministic golden values.
/// </summary>
public sealed class IndianAmountInWordsTests
{
    [Theory]
    [InlineData(0, "Rupees Zero Only")]
    [InlineData(1, "Rupees One Only")]
    [InlineData(5.00, "Rupees Five Only")]
    [InlineData(19, "Rupees Nineteen Only")]
    [InlineData(20, "Rupees Twenty Only")]
    [InlineData(21, "Rupees Twenty One Only")]
    [InlineData(100, "Rupees One Hundred Only")]
    [InlineData(105, "Rupees One Hundred Five Only")]
    [InlineData(999, "Rupees Nine Hundred Ninety Nine Only")]
    [InlineData(1000, "Rupees One Thousand Only")]
    [InlineData(1234, "Rupees One Thousand Two Hundred Thirty Four Only")]
    [InlineData(100000, "Rupees One Lakh Only")]
    [InlineData(123450, "Rupees One Lakh Twenty Three Thousand Four Hundred Fifty Only")]
    [InlineData(10000000, "Rupees One Crore Only")]
    [InlineData(12345678, "Rupees One Crore Twenty Three Lakh Forty Five Thousand Six Hundred Seventy Eight Only")]
    public void Whole_rupee_amounts_convert(decimal amount, string expected)
    {
        Assert.Equal(expected, IndianAmountInWords.Convert(amount));
    }

    [Fact]
    public void The_canonical_lakh_example_with_paise()
    {
        Assert.Equal(
            "Rupees One Lakh Twenty Three Thousand Four Hundred Fifty and Sixty Paise Only",
            IndianAmountInWords.Convert(123450.60m));
    }

    [Fact]
    public void Sub_rupee_amount_omits_the_rupee_clause()
    {
        Assert.Equal("Seventy Five Paise Only", IndianAmountInWords.Convert(0.75m));
    }

    [Fact]
    public void One_paise_only()
    {
        Assert.Equal("One Paise Only", IndianAmountInWords.Convert(0.01m));
    }

    [Fact]
    public void Rupees_and_single_paise()
    {
        Assert.Equal("Rupees Five and One Paise Only", IndianAmountInWords.Convert(5.01m));
    }

    [Fact]
    public void Rounds_to_the_paisa_away_from_zero()
    {
        // 99.999 -> 100.00 (whole rupee), 0.005 -> 0.01 (one paise).
        Assert.Equal("Rupees One Hundred Only", IndianAmountInWords.Convert(99.999m));
        Assert.Equal("One Paise Only", IndianAmountInWords.Convert(0.005m));
    }

    [Fact]
    public void Negative_amount_is_prefixed_minus()
    {
        Assert.Equal("Minus Rupees Five and Fifty Paise Only", IndianAmountInWords.Convert(-5.50m));
    }

    [Fact]
    public void Deterministic_across_calls()
    {
        Assert.Equal(IndianAmountInWords.Convert(987654.32m), IndianAmountInWords.Convert(987654.32m));
    }
}
