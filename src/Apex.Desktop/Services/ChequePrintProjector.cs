using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;

namespace Apex.Desktop.Services;

/// <summary>
/// Projects a posted Payment voucher (with its <see cref="Company"/> context) into the framework-agnostic
/// <see cref="ChequePrintData"/> that <c>ChequePdf</c> inks onto the bank's pre-printed leaf (census row 8.4).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/print-cheques/</c>, "Print Cheque from Payment
/// Voucher": the cheque number is the voucher's <i>Inst. no.</i>, its date the <i>Inst. date</i>, and the cheque
/// is printed straight off the voucher.</para>
///
/// <para><b>🔴 WHAT MAKES A VOUCHER A PRINTABLE CHEQUE — all four, and each one refuses rather than degrades.</b>
/// (1) a CREDIT to a bank ledger, because a cheque you print is money leaving your account; (2) that ledger's
/// <see cref="Ledger.EnableChequePrinting"/> is on; (3) the line's bank allocation is a
/// <see cref="BankTransactionType.ChequeOrDD"/> — you cannot print a cheque for an RTGS; and (4) it carries an
/// instrument number, because the leaf is pre-numbered and a cheque with no number cannot be matched to one.
/// A voucher failing any of these is simply not a cheque, and prints as the ordinary Dr/Cr voucher it is.</para>
///
/// <para>Pure and Avalonia-free: it resolves masters and formats nothing else. It never touches disk, the clock
/// or OS-print, and it introduces no brand text.</para>
/// </summary>
public static class ChequePrintProjector
{
    /// <summary>
    /// The bank line this voucher would draw a cheque on, or <c>null</c> when it is not a cheque payment at all.
    /// </summary>
    public static EntryLine? FindChequeLine(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        foreach (var line in voucher.Lines)
        {
            if (line.Side != DrCr.Credit) continue;
            if (line.BankAllocation is not { } alloc) continue;
            if (alloc.TransactionType != BankTransactionType.ChequeOrDD) continue;
            if (string.IsNullOrWhiteSpace(alloc.InstrumentNumber)) continue;
            if (company.FindLedger(line.LedgerId) is not { EnableChequePrinting: true }) continue;
            return line;
        }
        return null;
    }

    /// <summary>
    /// Projects the cheque, or returns <c>null</c> when this voucher draws none. The caller still has to ask
    /// <c>ChequePdf.Validate</c> whether the bank's dimensions permit printing it — a projectable cheque on an
    /// uncalibrated bank is a legitimate state, and its refusal message is what the operator needs to see.
    /// </summary>
    public static ChequePrintData? Project(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        if (FindChequeLine(company, voucher) is not { } line) return null;
        var alloc = line.BankAllocation!;
        var bank = company.FindLedger(line.LedgerId)!;

        // "Print Currency Formal Name" (help.tallysolutions.com/.../Creation_Mode.htm) names the company's base
        // currency. Company.BaseCurrencyName holds the ISO-style code ("INR"); the CURRENCY master holds the
        // formal name the words should read. Prefer the master, fall back to the fixed wording — never to a code,
        // because "INR One Hundred Only" is not a sentence a bank will honour.
        var baseCurrency = company.BaseCurrency;
        var formalName = baseCurrency?.FormalName;
        if (string.IsNullOrWhiteSpace(formalName) || IsCodeLike(formalName)) formalName = "Rupees";

        return new ChequePrintData
        {
            PayeeName = Apex.Ledger.Reports.ChequePrinting.FavouringName(company, voucher, bank.Id),
            Amount = line.Amount,
            // "Inst. date" is the cheque date. A payment entered without one falls back to the voucher date,
            // which is the date the payment was made — never left blank, because an undated cheque is void.
            ChequeDate = alloc.InstrumentDate ?? voucher.Date,
            InstrumentNumber = alloc.InstrumentNumber,
            BankName = bank.Name,
            CompanyName = company.Name,
            PrintCompanyName = bank.PrintCompanyNameOnCheque,
            CurrencyFormalName = formalName,
            CurrencyMinorName = "Paise",
            // The PDF text encoding is WinAnsi, which has no rupee sign, so the symbol is folded to the ASCII
            // form the renderer can actually lay down rather than silently dropping a glyph.
            CurrencySymbol = AsciiSymbol(company.BaseCurrencySymbol),
            NudgeTopTmm = bank.ChequeAdjustTopTmm,
            NudgeLeftTmm = bank.ChequeAdjustLeftTmm,
        };
    }

    /// <summary>True for an ISO-style currency CODE ("INR", "USD") as opposed to a formal name ("Rupees").</summary>
    private static bool IsCodeLike(string value) =>
        value.Trim().Length <= 3 && value.Trim().All(char.IsLetter);

    /// <summary>The currency symbol in a form the WinAnsi PDF font can render; "₹" folds to "Rs.".</summary>
    private static string AsciiSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return "Rs.";
        var s = symbol.Trim();
        return s.All(c => c < 128) ? s : "Rs.";
    }
}
