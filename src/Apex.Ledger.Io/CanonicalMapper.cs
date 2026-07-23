using System.Globalization;
using Apex.Ledger;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Projects a <see cref="Company"/> aggregate into a <see cref="CanonicalModel"/> (masters + vouchers), with
/// <b>deterministic ordering</b> so the serialised bytes are stable across runs and machines (ER-8). Money is
/// captured as integer paisa via <see cref="MoneyCodec"/>; dates are ISO <c>yyyy-MM-dd</c>, culture-invariant;
/// enums are their member names. This is the shared, format-agnostic step both JSON and XML export call, so the
/// two formats carry the identical payload.
/// </summary>
public static class CanonicalMapper
{
    /// <summary>The canonical envelope format version — bump on any breaking shape change.</summary>
    public const int FormatVersion = 1;

    /// <summary>The persistence schema version this export targets (SQLite schema v38). Metadata only — the canonical
    /// round-trip is faithful regardless and this constant is not validated on import.</summary>
    public const int SchemaVersion = 38;

    /// <summary>The scale forex amounts and rates are captured at (× 1,000,000 = "micros"), mirroring the SQLite
    /// store, so a non-round rate round-trips exactly with no binary float.</summary>
    private const decimal MicroScale = 1_000_000m;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? Iso(DateOnly? d) => d is { } v ? Iso(v) : null;

    /// <summary>ISO-8601 round-trip (o) for a portal <see cref="DateTimeOffset"/> — preserves the offset so it round-trips
    /// byte-stably (Phase 9 slice 5 e-Way generation timestamp / validity).</summary>
    private static string? IsoDateTimeOffset(DateTimeOffset? dto) =>
        dto is { } v ? v.ToString("o", CultureInfo.InvariantCulture) : null;

    /// <summary>Exact rate → integer micros (rate × 1,000,000); throws if the rate carries a sub-micro tail.</summary>
    private static long ToMicro(decimal rate)
    {
        decimal micro = rate * MicroScale;
        if (micro != decimal.Truncate(micro))
            throw new InvalidOperationException($"Rate {rate} is finer than micros and cannot be serialised losslessly.");
        return (long)micro;
    }

    private static long? ToMicro(decimal? rate) => rate is { } r ? ToMicro(r) : null;

    public static CanonicalModel ToModel(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        return new CanonicalModel
        {
            FormatVersion = FormatVersion,
            SchemaVersion = SchemaVersion,
            Company = MapCompany(company),
            Payload = MapPayload(company),
        };
    }

    private static CompanyDto MapCompany(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        MailingName = c.MailingName,
        Address = c.Address,
        Country = c.Country,
        State = c.State,
        Pin = c.Pin,
        FinancialYearStart = Iso(c.FinancialYearStart),
        BooksBeginFrom = Iso(c.BooksBeginFrom),
        BaseCurrencySymbol = c.BaseCurrencySymbol,
        BaseCurrencyName = c.BaseCurrencyName,
        DecimalPlaces = c.DecimalPlaces,
        DecimalUnitName = c.DecimalUnitName,
        Gst = c.Gst is { } g ? MapGstConfig(g) : null,
        Tds = c.Tds is { } td ? MapTdsConfig(td) : null,
        Tcs = c.Tcs is { } tc ? MapTcsConfig(tc) : null,
        UseSeparateActualBilledQuantity = c.UseSeparateActualBilledQuantity,
        EnableMultiplePriceLevels = c.EnableMultiplePriceLevels,
        EnableJobOrderProcessing = c.EnableJobOrderProcessing,
        PayrollEnabled = c.PayrollEnabled,
        PayrollStatutoryEnabled = c.PayrollStatutoryEnabled,
        SalaryTdsEnabled = c.SalaryTdsEnabled,
        Pf = c.PfConfig is { } pf ? MapPfConfig(pf) : null,
        Esi = c.EsiConfig is { } esi ? MapEsiConfig(esi) : null,
        Pt = c.PtConfig is { } pt ? MapPtConfig(pt) : null,
        Gratuity = c.GratuityConfig is { } gr ? MapGratuityConfig(gr) : null,
        Bonus = c.BonusConfig is { } bo ? MapBonusConfig(bo) : null,
    };

    private static GratuityConfigDto MapGratuityConfig(GratuityConfig gr) => new()
    {
        CapPaisa = MoneyCodec.ToPaisa(gr.CapAmount),
        WageBasis = gr.WageBasis.ToString(),
        Population = gr.Population.ToString(),
    };

    private static BonusConfigDto MapBonusConfig(BonusConfig bo) => new()
    {
        RateBasisPoints = bo.RateBasisPoints,
        CalculationCeilingPaisa = MoneyCodec.ToPaisa(bo.CalculationCeiling),
        MinimumWagePaisa = MoneyCodec.ToPaisa(bo.MinimumWage),
        Prorate = bo.Prorate,
    };

    private static PtConfigDto MapPtConfig(PtConfig pt) => new()
    {
        StateCode = pt.StateCode,
        RegistrationNumber = pt.RegistrationNumber,
        WageBasis = pt.WageBasis.ToString(),
        SlabTables = pt.SlabTables.Select(MapPtSlab).ToList(),
    };

    private static PtSlabDto MapPtSlab(PtSlab s) => new()
    {
        Id = s.Id,
        StateCode = s.StateCode,
        GenderScope = s.GenderScope.ToString(),
        Bands = s.Bands.Select(MapPtSlabBand).ToList(),
    };

    private static PtSlabBandDto MapPtSlabBand(PtSlabBand b) => new()
    {
        FromWagePaisa = MoneyCodec.ToPaisa(b.FromWage),
        ToWagePaisa = b.ToWage is { } t ? MoneyCodec.ToPaisa(t) : null,
        MonthlyAmountPaisa = MoneyCodec.ToPaisa(b.MonthlyAmount),
        MonthOverrides = b.MonthOverrides
            .Select(o => new PtMonthOverrideDto { Month = o.Month, AmountPaisa = MoneyCodec.ToPaisa(o.Amount) })
            .ToList(),
    };

    private static PfConfigDto MapPfConfig(PfConfig pf) => new()
    {
        EpfRateBasisPoints = pf.EpfRateBasisPoints,
        EstablishmentCode = pf.EstablishmentCode,
        CapWagesAtCeiling = pf.CapWagesAtCeiling,
    };

    private static EsiConfigDto MapEsiConfig(EsiConfig esi) => new()
    {
        EmployeeRateBasisPoints = esi.EmployeeRateBasisPoints,
        EmployerRateBasisPoints = esi.EmployerRateBasisPoints,
        EmployerCode = esi.EmployerCode,
    };

    private static PayloadDto MapPayload(Company c) => new()
    {
        // Masters — ordered by name then id so the byte stream is stable regardless of insertion order.
        Groups = OrderById(c.Groups.Concat(c.ProfitAndLossHead is { } pl ? [pl] : Array.Empty<Group>()),
                    g => g.Name, g => g.Id)
                 .Select(MapGroup).ToList(),
        Ledgers = OrderById(c.Ledgers, l => l.Name, l => l.Id).Select(MapLedger).ToList(),
        VoucherTypes = OrderById(c.VoucherTypes, t => t.Name, t => t.Id).Select(MapVoucherType).ToList(),
        CostCategories = OrderById(c.CostCategories, x => x.Name, x => x.Id).Select(MapCostCategory).ToList(),
        CostCentres = OrderById(c.CostCentres, x => x.Name, x => x.Id).Select(MapCostCentre).ToList(),
        Currencies = OrderById(c.Currencies, x => x.FormalName, x => x.Id).Select(MapCurrency).ToList(),
        ExchangeRates = c.ExchangeRates
            .OrderBy(r => r.CurrencyId).ThenBy(r => r.Date).ThenBy(r => r.Id)
            .Select(MapExchangeRate).ToList(),
        Budgets = OrderById(c.Budgets, x => x.Name, x => x.Id).Select(MapBudget).ToList(),
        Scenarios = OrderById(c.Scenarios, x => x.Name, x => x.Id).Select(MapScenario).ToList(),
        Units = OrderById(c.Units, u => u.Symbol, u => u.Id).Select(MapUnit).ToList(),
        StockGroups = OrderById(c.StockGroups, g => g.Name, g => g.Id).Select(MapStockGroup).ToList(),
        StockCategories = OrderById(c.StockCategories, g => g.Name, g => g.Id).Select(MapStockCategory).ToList(),
        Godowns = OrderById(c.Godowns, g => g.Name, g => g.Id).Select(MapGodown).ToList(),
        StockItems = OrderById(c.StockItems, i => i.Name, i => i.Id).Select(MapStockItem).ToList(),
        // Batch masters — ordered by (item id, batch number, id) so the stream is stable and human-legible.
        BatchMasters = c.BatchMasters
            .OrderBy(b => b.StockItemId).ThenBy(b => b.BatchNumber, StringComparer.Ordinal).ThenBy(b => b.Id)
            .Select(MapBatchMaster).ToList(),
        // Bill-of-Materials masters — ordered by (item id, name, id) so the stream is stable and human-legible.
        // Line order within a BOM is preserved verbatim (it is load-bearing for the recipe).
        BillsOfMaterials = c.BillsOfMaterials
            .OrderBy(b => b.StockItemId).ThenBy(b => b.Name, StringComparer.Ordinal).ThenBy(b => b.Id)
            .Select(MapBom).ToList(),
        StockOpeningBalances = c.StockOpeningBalances.OrderBy(b => b.Id).Select(MapStockOpeningBalance).ToList(),
        // Price Levels — ordered by (name, id) so the stream is stable regardless of insertion order.
        PriceLevels = OrderById(c.PriceLevels, x => x.Name, x => x.Id).Select(MapPriceLevel).ToList(),
        // Price Lists — ordered by (level id, item id, applicable-from, id); the slab order within a list is the
        // list's own ascending slab order, preserved verbatim (it is load-bearing).
        PriceLists = c.PriceLists
            .OrderBy(p => p.PriceLevelId).ThenBy(p => p.StockItemId).ThenBy(p => p.ApplicableFrom).ThenBy(p => p.Id)
            .Select(MapPriceList).ToList(),
        // Reorder definitions — ordered by (scope, target id, id) so the stream is stable.
        ReorderDefinitions = c.ReorderDefinitions
            .OrderBy(d => d.Scope).ThenBy(d => d.TargetId).ThenBy(d => d.Id)
            .Select(MapReorderDefinition).ToList(),
        // Payroll masters (Phase 8 slice 1) — each ordered by name/symbol then id so the byte stream is stable.
        EmployeeCategories = OrderById(c.EmployeeCategories, x => x.Name, x => x.Id).Select(MapEmployeeCategory).ToList(),
        EmployeeGroups = OrderById(c.EmployeeGroups, x => x.Name, x => x.Id).Select(MapEmployeeGroup).ToList(),
        PayrollUnits = OrderById(c.PayrollUnits, x => x.Symbol, x => x.Id).Select(MapPayrollUnit).ToList(),
        AttendanceTypes = OrderById(c.AttendanceTypes, x => x.Name, x => x.Id).Select(MapAttendanceType).ToList(),
        Employees = OrderById(c.Employees, x => x.Name, x => x.Id).Select(MapEmployee).ToList(),
        // Pay heads ordered by name (byte-stable); salary structures (no name) by scope/scopeId/effective-from/id.
        PayHeads = OrderById(c.PayHeads, x => x.Name, x => x.Id).Select(MapPayHead).ToList(),
        SalaryStructures = c.SalaryStructures
            .OrderBy(x => (int)x.Scope).ThenBy(x => x.ScopeId).ThenBy(x => x.EffectiveFrom).ThenBy(x => x.Id)
            .Select(MapSalaryStructure).ToList(),
        // Attendance entries — ordered by (from date, employee, attendance type, id) so the stream is deterministic.
        AttendanceEntries = c.AttendanceEntries
            .OrderBy(a => a.FromDate).ThenBy(a => a.EmployeeId).ThenBy(a => a.AttendanceTypeId).ThenBy(a => a.Id)
            .Select(MapAttendanceEntry).ToList(),
        // §192 income-tax declarations — ordered by employee for a deterministic stream (Phase 8 slice 7).
        TaxDeclarations = c.TaxDeclarations
            .OrderBy(d => d.EmployeeId)
            .Select(MapTaxDeclaration).ToList(),
        // Vouchers — ordered by (date, number, id) so the stream is deterministic and human-legible.
        Vouchers = c.Vouchers
            .OrderBy(v => v.Date).ThenBy(v => v.Number).ThenBy(v => v.Id)
            .Select(MapVoucher).ToList(),
        InventoryVouchers = c.InventoryVouchers
            .OrderBy(v => v.Date).ThenBy(v => v.Number).ThenBy(v => v.Id)
            .Select(MapInventoryVoucher).ToList(),
        // TDS deposit challans — ordered by (deposit date, challan no, id) so the stream is stable and human-legible.
        TdsChallans = c.TdsChallans
            .OrderBy(ch => ch.DepositDate).ThenBy(ch => ch.ChallanNo, StringComparer.Ordinal).ThenBy(ch => ch.Id)
            .Select(MapTdsChallan).ToList(),
        // Challan-voucher links — ordered by (challan id, voucher id) so the stream is deterministic.
        ChallanVoucherLinks = c.ChallanVoucherLinks
            .OrderBy(l => l.ChallanId).ThenBy(l => l.VoucherId)
            .Select(l => new ChallanVoucherLinkDto { ChallanId = l.ChallanId, VoucherId = l.VoucherId }).ToList(),
        // TCS deposit challans — same deterministic ordering as the TDS ones (Phase 7 slice 6).
        TcsChallans = c.TcsChallans
            .OrderBy(ch => ch.DepositDate).ThenBy(ch => ch.ChallanNo, StringComparer.Ordinal).ThenBy(ch => ch.Id)
            .Select(MapTcsChallan).ToList(),
        TcsChallanVoucherLinks = c.TcsChallanVoucherLinks
            .OrderBy(l => l.ChallanId).ThenBy(l => l.VoucherId)
            .Select(l => new ChallanVoucherLinkDto { ChallanId = l.ChallanId, VoucherId = l.VoucherId }).ToList(),
        // Phase 9 slice 2: RCM generated documents + §34-CDN links + GST-on-advance receipts (ordered by id so stable).
        RcmDocuments = c.RcmDocuments.OrderBy(d => d.Id).Select(MapRcmDocument).ToList(),
        // Phase 9 slice 4a: e-invoice IRP artefacts (deterministic order: source voucher, then id).
        EInvoiceRecords = c.EInvoiceRecords.OrderBy(r => r.SourceVoucherId).ThenBy(r => r.Id).Select(MapEInvoiceRecord).ToList(),
        // Phase 9 slice 5: e-Way Bill artefacts (deterministic order: source voucher, then id).
        EWayBillRecords = c.EWayBillRecords.OrderBy(r => r.SourceVoucherId).ThenBy(r => r.Id).Select(MapEWayBillRecord).ToList(),
        CreditDebitNoteLinks = c.CreditDebitNoteLinks.OrderBy(l => l.Id).Select(MapCdnLink).ToList(),
        AdvanceReceipts = c.AdvanceReceipts.OrderBy(a => a.Id).Select(MapAdvanceReceipt).ToList(),
        // Phase 9 slice 6: imported GSTR-2B/2A snapshots (ordered by period, import instant, then id so stable) + the
        // persisted reconciliation results (ordered by line, then id).
        Gstr2bSnapshots = c.Gstr2bSnapshots
            .OrderBy(s => s.ReturnPeriod, StringComparer.Ordinal).ThenBy(s => s.ImportedAt).ThenBy(s => s.Id)
            .Select(MapGstr2bSnapshot).ToList(),
        Gstr2bReconResults = c.Gstr2bReconResults
            .OrderBy(r => r.LineId).ThenBy(r => r.Id).Select(MapGstr2bReconResult).ToList(),
        // Phase 9 slice 6b: offline IMS decisions (ordered by line, then id so stable).
        ImsActions = c.ImsActions
            .OrderBy(a => a.LineId).ThenBy(a => a.Id).Select(MapImsAction).ToList(),
        // Phase 9 slice 7: electronic-ledger set-off / reversal / challan / DRC-03 records, each deterministically
        // ordered so serialisation is byte-stable (setoff-lines by period/voucher/heads; reversals by rule/period;
        // challans by deposit date; DRC-03 by period).
        GstSetoffLines = c.GstSetoffLines
            .OrderBy(l => l.Period, StringComparer.Ordinal).ThenBy(l => l.VoucherId)
            .ThenBy(l => (int)l.CreditHead).ThenBy(l => (int)l.LiabilityHead).ThenBy(l => l.Id)
            .Select(MapGstSetoffLine).ToList(),
        ItcReversals = c.ItcReversals
            .OrderBy(r => (int)r.Rule).ThenBy(r => r.Period, StringComparer.Ordinal).ThenBy(r => r.Id)
            .Select(MapItcReversal).ToList(),
        GstChallans = c.GstChallans
            .OrderBy(ch => ch.DepositDate).ThenBy(ch => ch.Id).Select(MapGstChallan).ToList(),
        GstDrc03s = c.GstDrc03s
            .OrderBy(d => d.Period, StringComparer.Ordinal).ThenBy(d => d.Id).Select(MapGstDrc03).ToList(),
    };

    private static GstSetoffLineDto MapGstSetoffLine(GstSetoffLine l) => new()
    {
        Id = l.Id, VoucherId = l.VoucherId, Period = l.Period, CreditHead = l.CreditHead.ToString(),
        LiabilityHead = l.LiabilityHead.ToString(), IsCash = l.IsCash, AmountPaisa = l.AmountPaisa,
    };

    private static ItcReversalDto MapItcReversal(ItcReversal r) => new()
    {
        Id = r.Id, Rule = r.Rule.ToString(), Period = r.Period, CgstPaisa = r.CgstPaisa, SgstPaisa = r.SgstPaisa,
        IgstPaisa = r.IgstPaisa, CessPaisa = r.CessPaisa, D1BasisPaisa = r.D1BasisPaisa, D2BasisPaisa = r.D2BasisPaisa,
        SourceVoucherId = r.SourceVoucherId, SourceLineId = r.SourceLineId, ReversalVoucherId = r.ReversalVoucherId,
        ReclaimOfId = r.ReclaimOfId, Drc03Id = r.Drc03Id, Table4bBucket = r.Table4bBucket.ToString(),
        CreatedAt = r.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
    };

    private static GstChallanDto MapGstChallan(GstChallan ch) => new()
    {
        Id = ch.Id, Cpin = ch.Cpin, Cin = ch.Cin, Brn = ch.Brn, DepositDate = Iso(ch.DepositDate),
        MajorHead = ch.MajorHead.ToString(), MinorHead = ch.MinorHead.ToString(),
        AmountPaisa = MoneyCodec.ToPaisa(ch.Amount), VoucherId = ch.VoucherId, InterestFlag = ch.InterestFlag,
    };

    private static GstDrc03Dto MapGstDrc03(GstDrc03 d) => new()
    {
        Id = d.Id, Drc03Ref = d.Drc03Ref, Cause = d.Cause, Period = d.Period, CgstPaisa = d.CgstPaisa,
        SgstPaisa = d.SgstPaisa, IgstPaisa = d.IgstPaisa, CessPaisa = d.CessPaisa, InterestPaisa = d.InterestPaisa,
        Drc03aDemandRef = d.Drc03aDemandRef, VoucherId = d.VoucherId,
        CreatedAt = d.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
    };

    private static Gstr2bSnapshotDto MapGstr2bSnapshot(Gstr2bSnapshot s) => new()
    {
        Id = s.Id, StatementType = s.StatementType.ToString(), ReturnPeriod = s.ReturnPeriod,
        RecipientGstin = s.RecipientGstin, GeneratedOn = Iso(s.GeneratedOn), SourceFileHash = s.SourceFileHash,
        ImportedAt = s.ImportedAt.ToString("o", CultureInfo.InvariantCulture),
        SummaryIgstPaisa = s.SummaryIgstPaisa, SummaryCgstPaisa = s.SummaryCgstPaisa,
        SummarySgstPaisa = s.SummarySgstPaisa, SummaryCessPaisa = s.SummaryCessPaisa,
        Lines = s.Lines.OrderBy(l => l.Id).Select(MapGstr2bLine).ToList(),
    };

    private static Gstr2bLineDto MapGstr2bLine(Gstr2bLine l) => new()
    {
        Id = l.Id, SupplierGstin = l.SupplierGstin, SupplierTradeName = l.SupplierTradeName,
        DocType = l.DocType.ToString(), DocNumber = l.DocNumber, DocNumberNorm = l.DocNumberNorm,
        DocDate = Iso(l.DocDate), PosStateCode = l.PosStateCode, TaxableValuePaisa = l.TaxableValuePaisa,
        IgstPaisa = l.IgstPaisa, CgstPaisa = l.CgstPaisa, SgstPaisa = l.SgstPaisa, CessPaisa = l.CessPaisa,
        ItcAvailable = l.ItcAvailable, ItcUnavailableReason = l.ItcUnavailableReason, ReverseCharge = l.ReverseCharge,
    };

    private static Gstr2bReconResultDto MapGstr2bReconResult(Gstr2bReconResult r) => new()
    {
        Id = r.Id, LineId = r.LineId, Bucket = r.Bucket.ToString(), MatchedVoucherId = r.MatchedVoucherId,
        TaxableVariancePaisa = r.TaxableVariancePaisa, TaxVariancePaisa = r.TaxVariancePaisa,
        MatchPinned = r.MatchPinned, ReconciledAt = IsoDateTimeOffset(r.ReconciledAt),
    };

    private static ImsActionDto MapImsAction(ImsAction a) => new()
    {
        Id = a.Id, LineId = a.LineId, Status = a.Status.ToString(), Remarks = a.Remarks,
        DeclaredReversalPaisa = a.DeclaredReversalPaisa, NoReversalDeclared = a.NoReversalDeclared,
        ActedOn = Iso(a.ActedOn),
    };

    private static RcmDocumentDto MapRcmDocument(RcmDocument d) => new()
    {
        Id = d.Id, Kind = d.Kind.ToString(), SourceVoucherId = d.SourceVoucherId,
        SeriesNumber = d.SeriesNumber, DocDate = Iso(d.DocDate), SupplierLedgerId = d.SupplierLedgerId,
    };

    private static EInvoiceRecordDto MapEInvoiceRecord(EInvoiceRecord r) => new()
    {
        Id = r.Id, SourceVoucherId = r.SourceVoucherId, DocumentNumberUpper = r.DocumentNumberUpper,
        Status = r.Status.ToString(), Irn = r.Irn, AckNo = r.AckNo, AckDate = Iso(r.AckDate), SignedQr = r.SignedQr,
        SignedJsonBase64 = r.SignedJson is { } b ? Convert.ToBase64String(b) : null,
        CancelledOn = Iso(r.CancelledOn), CancelReasonCode = r.CancelReasonCode,
        ErrorCode = r.ErrorCode, ErrorMessage = r.ErrorMessage,
    };

    private static EWayBillRecordDto MapEWayBillRecord(EWayBillRecord r) => new()
    {
        Id = r.Id, SourceVoucherId = r.SourceVoucherId, DocumentNumberUpper = r.DocumentNumberUpper,
        Status = r.Status.ToString(), SupplyType = r.SupplyType, SubSupplyType = r.SubSupplyType, DocType = r.DocType,
        ConsignmentValuePaisa = r.ConsignmentValuePaisa, TransporterId = r.TransporterId,
        TransMode = r.Mode?.ToString(), VehicleNumber = r.VehicleNumber, DistanceKm = r.DistanceKm,
        TransportDocNo = r.TransportDocNo, ShipFromStateCode = r.ShipFromStateCode, ShipToStateCode = r.ShipToStateCode,
        IsOverDimensionalCargo = r.IsOverDimensionalCargo, ShipToGstin = r.ShipToGstin,
        ClosureRequested = r.ClosureRequested, ClosedOn = Iso(r.ClosedOn),
        EwbNumber = r.EwbNumber, GeneratedAt = IsoDateTimeOffset(r.GeneratedAt), ValidUpto = IsoDateTimeOffset(r.ValidUpto),
        CancelledOn = Iso(r.CancelledOn), CancelReasonCode = r.CancelReasonCode,
        ErrorCode = r.ErrorCode, ErrorMessage = r.ErrorMessage,
    };

    private static GstCdnLinkDto MapCdnLink(GstCreditDebitNoteLink l) => new()
    {
        Id = l.Id, CdnVoucherId = l.CdnVoucherId, CdnType = l.CdnType.ToString(),
        OriginalInvoiceVoucherId = l.OriginalInvoiceVoucherId, OriginalInvoiceNumber = l.OriginalInvoiceNumber,
        OriginalInvoiceDate = Iso(l.OriginalInvoiceDate), ReasonCode = l.ReasonCode, Is9BTarget = l.Is9BTarget,
    };

    private static GstAdvanceReceiptDto MapAdvanceReceipt(GstAdvanceReceipt a) => new()
    {
        Id = a.Id, ReceiptVoucherId = a.ReceiptVoucherId, IsService = a.IsService,
        AdvanceAmountPaisa = MoneyCodec.ToPaisa(a.AdvanceAmount), RateBasisPoints = a.RateBasisPoints,
        InterState = a.InterState, PlaceOfSupplyStateCode = a.PlaceOfSupplyStateCode,
        AdvanceTaxPaisa = MoneyCodec.ToPaisa(a.AdvanceTax),
        AdjustedAgainstInvoiceVoucherId = a.AdjustedAgainstInvoiceVoucherId, RefundVoucherId = a.RefundVoucherId,
    };

    private static TdsChallanDto MapTdsChallan(TdsChallan ch) => new()
    {
        Id = ch.Id,
        ChallanNo = ch.ChallanNo,
        BsrCode = ch.BsrCode,
        DepositDate = Iso(ch.DepositDate),
        AmountPaisa = MoneyCodec.ToPaisa(ch.Amount),
        Section = ch.Section,
        MinorHead = ch.MinorHead,
    };

    private static TcsChallanDto MapTcsChallan(TcsChallan ch) => new()
    {
        Id = ch.Id,
        ChallanNo = ch.ChallanNo,
        BsrCode = ch.BsrCode,
        DepositDate = Iso(ch.DepositDate),
        AmountPaisa = MoneyCodec.ToPaisa(ch.Amount),
        CollectionCode = ch.CollectionCode,
        MinorHead = ch.MinorHead,
    };

    private static IEnumerable<T> OrderById<T>(IEnumerable<T> src, Func<T, string> name, Func<T, Guid> id) =>
        src.OrderBy(name, StringComparer.Ordinal).ThenBy(id);

    // ------------------------------------------------------------- masters

    private static GroupDto MapGroup(Group g) => new()
    {
        Id = g.Id, Name = g.Name, Nature = g.Nature.ToString(),
        ParentId = g.ParentId, Alias = g.Alias, IsPredefined = g.IsPredefined,
    };

    private static LedgerDto MapLedger(Domain.Ledger l) => new()
    {
        Id = l.Id, Name = l.Name, GroupId = l.GroupId,
        OpeningBalancePaisa = MoneyCodec.ToPaisa(l.OpeningBalance),
        OpeningIsDebit = l.OpeningIsDebit, Alias = l.Alias, IsPredefined = l.IsPredefined,
        MaintainBillByBill = l.MaintainBillByBill,
        DefaultCreditPeriodDays = l.DefaultCreditPeriodDays,
        CostCentresApplicable = l.CostCentresApplicable,
        EnableChequePrinting = l.EnableChequePrinting,
        ChequePrintingBankName = l.ChequePrintingBankName,
        CurrencyId = l.CurrencyId,
        Interest = l.Interest is { } i ? MapInterest(i) : null,
        PartyGst = l.PartyGst is { } p ? MapPartyGst(p) : null,
        SalesPurchaseGst = l.SalesPurchaseGst is { } s ? MapStockItemGst(s) : null,
        GstClassification = l.GstClassification is { } gc ? MapGstClassification(gc) : null,
        MethodOfAppropriation = l.MethodOfAppropriation is { } m ? m.ToString() : null,
        DefaultPriceLevelId = l.DefaultPriceLevelId,
        TdsApplicable = l.TdsApplicable,
        TdsNatureOfPaymentId = l.TdsNatureOfPaymentId,
        DeducteeType = l.DeducteeType is { } dt ? dt.ToString() : null,
        PartyPan = l.PartyPan,
        DeductTdsInSameVoucher = l.DeductTdsInSameVoucher,
        TcsApplicable = l.TcsApplicable,
        TcsNatureOfGoodsId = l.TcsNatureOfGoodsId,
        CollecteeType = l.CollecteeType is { } ct ? ct.ToString() : null,
        TdsTcsClassification = l.TdsTcsClassification is { } k ? k.ToString() : null,
        // WI-4 (v45): the party Mailing Details block. Null (and therefore absent from the document entirely) for
        // every ledger that never captured one, so an unaffected company exports byte-identically (ER-13).
        Mailing = l.Mailing is { } ml ? MapPartyMailing(ml) : null,
    };

    private static PartyMailingDto MapPartyMailing(PartyMailingDetails m) => new()
    {
        MailingName = m.MailingName, Address = m.Address, Country = m.Country, Pincode = m.Pincode,
        // No State: the party State is PartyGst.StateCode (the place-of-supply driver), mapped by MapPartyGst.
    };

    private static InterestParametersDto MapInterest(InterestParameters i) => new()
    {
        Enabled = i.Enabled, RatePercent = i.RatePercent, Per = i.Per.ToString(),
        OnBalance = i.OnBalance.ToString(), Applicability = i.Applicability.ToString(),
        CalculateFrom = Iso(i.CalculateFrom), Style = i.Style.ToString(),
        RoundingMethod = i.RoundingMethod.ToString(), RoundingDecimals = i.RoundingDecimals,
    };

    private static CostCategoryDto MapCostCategory(CostCategory x) => new()
    {
        Id = x.Id, Name = x.Name, AllocateRevenueItems = x.AllocateRevenueItems,
        AllocateNonRevenueItems = x.AllocateNonRevenueItems, IsPredefined = x.IsPredefined,
    };

    private static CostCentreDto MapCostCentre(CostCentre x) => new()
    {
        Id = x.Id, Name = x.Name, CategoryId = x.CategoryId, ParentId = x.ParentId, Alias = x.Alias,
    };

    // ------------------------------------------------------------- payroll masters (Phase 8 slice 1)

    private static EmployeeCategoryDto MapEmployeeCategory(EmployeeCategory x) => new()
    {
        Id = x.Id, Name = x.Name, AllocateRevenueItems = x.AllocateRevenueItems,
        AllocateNonRevenueItems = x.AllocateNonRevenueItems, IsPredefined = x.IsPredefined,
    };

    private static EmployeeGroupDto MapEmployeeGroup(EmployeeGroup x) => new()
    {
        Id = x.Id, Name = x.Name, ParentId = x.ParentId, Alias = x.Alias, DefineSalaryDetails = x.DefineSalaryDetails,
    };

    private static PayrollUnitDto MapPayrollUnit(PayrollUnit u) => new()
    {
        Id = u.Id, Symbol = u.Symbol, FormalName = u.FormalName, IsCompound = u.IsCompound,
        DecimalPlaces = u.DecimalPlaces, FirstUnitId = u.FirstUnitId, TailUnitId = u.TailUnitId,
        ConversionNumerator = u.ConversionNumerator, ConversionDenominator = u.ConversionDenominator,
    };

    private static AttendanceTypeDto MapAttendanceType(AttendanceType a) => new()
    {
        Id = a.Id, Name = a.Name, ParentId = a.ParentId, Kind = a.Kind.ToString(), PayrollUnitId = a.PayrollUnitId,
    };

    private static EmployeeDto MapEmployee(Employee e) => new()
    {
        Id = e.Id, Name = e.Name, EmployeeGroupId = e.EmployeeGroupId, EmployeeCategoryId = e.EmployeeCategoryId,
        EmployeeNumber = e.EmployeeNumber, DateOfJoining = Iso(e.DateOfJoining), DateOfLeaving = Iso(e.DateOfLeaving),
        Designation = e.Designation, Function = e.Function, Location = e.Location, Gender = e.Gender,
        DateOfBirth = Iso(e.DateOfBirth), Pan = e.Pan, Aadhaar = e.Aadhaar, Uan = e.Uan,
        PfAccountNumber = e.PfAccountNumber, EsiNumber = e.EsiNumber, BankAccountNumber = e.BankAccountNumber,
        BankName = e.BankName, BankIfsc = e.BankIfsc, ApplicableTaxRegime = e.ApplicableTaxRegime.ToString(),
        PfApplicable = e.PfApplicable, PfContributeOnHigherWages = e.PfContributeOnHigherWages,
        PfJoinDate = Iso(e.PfJoinDate),
        EsiApplicable = e.EsiApplicable, IsPersonWithDisability = e.IsPersonWithDisability,
    };

    private static PayHeadDto MapPayHead(PayHead p) => new()
    {
        Id = p.Id, Name = p.Name, DisplayName = p.DisplayName, PayHeadType = p.Type.ToString(),
        CalculationType = p.CalculationType.ToString(), AffectsNetSalary = p.AffectsNetSalary,
        UnderGroupId = p.UnderGroupId, LedgerId = p.LedgerId, EmployerExpenseLedgerId = p.EmployerExpenseLedgerId,
        IncomeTaxComponent = p.IncomeTaxComponent.ToString(),
        UseForGratuity = p.UseForGratuity, RoundingMethod = p.RoundingMethod.ToString(),
        RoundingLimitPaisa = MoneyCodec.ToPaisa(p.RoundingLimit), CalculationPeriod = p.CalculationPeriod.ToString(),
        AttendanceTypeId = p.AttendanceTypeId, PerDayCalculationBasisDays = p.PerDayCalculationBasisDays,
        PfComponent = p.PfComponent.ToString(), PartOfPfWages = p.PartOfPfWages,
        EsiComponent = p.EsiComponent.ToString(), PartOfEsiWages = p.PartOfEsiWages, IsOvertime = p.IsOvertime,
        PtComponent = p.PtComponent.ToString(),
        ComputationComponents = p.Computation is { } c1
            ? c1.BasisComponents.Select(x => new PayHeadComputationComponentDto { PayHeadId = x.PayHeadId, IsSubtraction = x.IsSubtraction }).ToList()
            : [],
        ComputationSlabs = p.Computation is { } c2
            ? c2.Slabs.Select(MapPayHeadSlab).ToList()
            : [],
    };

    private static PayHeadComputationSlabDto MapPayHeadSlab(PayHeadComputationSlab s) => new()
    {
        SlabType = s.SlabType.ToString(), RateBasisPoints = s.RateBasisPoints,
        ValuePaisa = MoneyCodec.ToPaisa(s.Value),
        FromAmountPaisa = MoneyCodec.ToPaisa(s.FromAmount), ToAmountPaisa = MoneyCodec.ToPaisa(s.ToAmount),
    };

    private static SalaryStructureDto MapSalaryStructure(SalaryStructure s) => new()
    {
        Id = s.Id, Scope = s.Scope.ToString(), ScopeId = s.ScopeId, EffectiveFrom = Iso(s.EffectiveFrom),
        StartType = s.StartType.ToString(),
        Lines = s.Lines.Select(l => new SalaryStructureLineDto
        {
            PayHeadId = l.PayHeadId, Order = l.Order, AmountPaisa = MoneyCodec.ToPaisa(l.Amount),
        }).ToList(),
    };

    private static AttendanceEntryDto MapAttendanceEntry(AttendanceEntry a) => new()
    {
        Id = a.Id, EmployeeId = a.EmployeeId, AttendanceTypeId = a.AttendanceTypeId,
        FromDate = Iso(a.FromDate), ToDate = Iso(a.ToDate), ValueMicro = ToMicro(a.Value),
    };

    private static TaxDeclarationDto MapTaxDeclaration(TaxDeclaration d) => new()
    {
        EmployeeId = d.EmployeeId,
        Section80CPaisa = MoneyCodec.ToPaisa(d.Section80C),
        Section80DPaisa = MoneyCodec.ToPaisa(d.Section80D),
        Section80CCD1BPaisa = MoneyCodec.ToPaisa(d.Section80CCD1B),
        Section80CCD2EmployerPaisa = MoneyCodec.ToPaisa(d.Section80CCD2Employer),
        HraExemptPaisa = MoneyCodec.ToPaisa(d.HouseRentAllowanceExempt),
        HomeLoanInterestPaisa = MoneyCodec.ToPaisa(d.HomeLoanInterest24b),
        OtherIncomePaisa = MoneyCodec.ToPaisa(d.OtherIncome),
        PrevEmployerSalaryPaisa = MoneyCodec.ToPaisa(d.PreviousEmployerSalary),
        PrevEmployerTdsPaisa = MoneyCodec.ToPaisa(d.PreviousEmployerTds),
    };

    private static CurrencyDto MapCurrency(Currency x) => new()
    {
        Id = x.Id, Symbol = x.Symbol, FormalName = x.FormalName,
        DecimalPlaces = x.DecimalPlaces, IsBaseCurrency = x.IsBaseCurrency,
    };

    private static ExchangeRateDto MapExchangeRate(ExchangeRate x) => new()
    {
        Id = x.Id, CurrencyId = x.CurrencyId, Date = Iso(x.Date),
        StandardRateMicro = ToMicro(x.StandardRate),
        SellingRateMicro = ToMicro(x.SellingRate), BuyingRateMicro = ToMicro(x.BuyingRate),
    };

    private static BudgetDto MapBudget(Budget x) => new()
    {
        Id = x.Id, Name = x.Name, UnderId = x.UnderId,
        PeriodFrom = Iso(x.PeriodFrom), PeriodTo = Iso(x.PeriodTo),
        Lines = x.Lines.Select(MapBudgetLine).ToList(),
    };

    private static BudgetLineDto MapBudgetLine(BudgetLine l) => new()
    {
        GroupId = l.GroupId, LedgerId = l.LedgerId, BudgetType = l.Type.ToString(),
        AmountPaisa = MoneyCodec.ToPaisa(l.Amount),
    };

    private static ScenarioDto MapScenario(Scenario x) => new()
    {
        Id = x.Id, Name = x.Name, IncludeActuals = x.IncludeActuals,
        IncludedTypeIds = x.IncludedTypeIds.OrderBy(g => g).ToList(),
        ExcludedTypeIds = x.ExcludedTypeIds.OrderBy(g => g).ToList(),
    };

    private static VoucherTypeDto MapVoucherType(VoucherType t) => new()
    {
        Id = t.Id, Name = t.Name, BaseType = t.BaseType.ToString(), Numbering = t.Numbering.ToString(),
        DefaultShortcut = t.DefaultShortcut, Abbreviation = t.Abbreviation,
        IsActive = t.IsActive, IsPredefined = t.IsPredefined,
        AffectsAccounts = t.AffectsAccounts, AffectsStock = t.AffectsStock,
        UseAsManufacturingJournal = t.UseAsManufacturingJournal,
        TrackAdditionalCosts = t.TrackAdditionalCosts,
        AllowZeroValuedTransactions = t.AllowZeroValuedTransactions,
        UseForPos = t.UseForPos,
        UseForJobWork = t.UseForJobWork,
        AllowConsumption = t.AllowConsumption,
        IsStatPayment = t.IsStatPayment,
        IsRcmPaymentVoucher = t.IsRcmPaymentVoucher,
        IsGstStatAdjustment = t.IsGstStatAdjustment, // Phase 9 slice 7
        // v47 (numbering S3): the three scalars + the two date-keyed affix lists. Lists are null when empty so a
        // never-configured type serialises byte-identically (ER-13); each is ordered by (ApplicableFrom, Id) for
        // deterministic bytes, matching the render selection order.
        PreventDuplicate = t.PreventDuplicate,
        NumberWidth = t.NumberWidth,
        PrefillWithZero = t.PrefillWithZero,
        Prefixes = MapAffixes(t.Prefixes),
        Suffixes = MapAffixes(t.Suffixes),
        PosConfig = t.PosConfig is { } pc ? MapPosConfig(pc) : null,
    };

    private static IReadOnlyList<VoucherNumberAffixDto>? MapAffixes(IReadOnlyList<VoucherNumberAffix> affixes) =>
        affixes.Count == 0
            ? null
            : affixes
                .OrderBy(a => a.ApplicableFrom).ThenBy(a => a.Id)
                .Select(a => new VoucherNumberAffixDto { ApplicableFrom = Iso(a.ApplicableFrom), Particulars = a.Particulars })
                .ToList();

    private static PosConfigDto MapPosConfig(PosConfig c) => new()
    {
        DefaultGodownId = c.DefaultGodownId,
        DefaultPartyId = c.DefaultPartyId,
        PrintAfterSave = c.PrintAfterSave,
        DefaultTitle = c.DefaultTitle,
        Message1 = c.Message1,
        Message2 = c.Message2,
        Declaration = c.Declaration,
        // Ordered by tender-type ordinal so the byte stream is stable regardless of dictionary insertion order.
        TenderLedgerDefaults = c.TenderLedgerDefaults
            .OrderBy(kv => (int)kv.Key)
            .Select(kv => new PosTenderLedgerDefaultDto { TenderType = kv.Key.ToString(), LedgerId = kv.Value })
            .ToList(),
    };

    private static PriceLevelDto MapPriceLevel(PriceLevel x) => new() { Id = x.Id, Name = x.Name };

    private static PriceListDto MapPriceList(PriceList x) => new()
    {
        Id = x.Id, PriceLevelId = x.PriceLevelId, StockItemId = x.StockItemId,
        ApplicableFrom = Iso(x.ApplicableFrom),
        // Slab order is the list's own ascending slab order — preserved verbatim (NOT reordered).
        Slabs = x.Slabs.Select(s => new PriceListSlabDto
        {
            FromQty = s.FromQty, ToQty = s.ToQty, RatePaisa = MoneyCodec.ToPaisa(s.Rate),
            DiscountPercent = s.DiscountPercent,
        }).ToList(),
    };

    private static ReorderDefinitionDto MapReorderDefinition(ReorderDefinition d) => new()
    {
        Id = d.Id, Scope = d.Scope.ToString(), TargetId = d.TargetId,
        ReorderAdvanced = d.ReorderAdvanced, ReorderQuantity = d.ReorderQuantity,
        MinQtyAdvanced = d.MinQtyAdvanced, MinOrderQuantity = d.MinOrderQuantity,
        PeriodCount = d.PeriodCount,
        PeriodUnit = d.PeriodUnit is { } u ? u.ToString() : null,
        Criteria = d.Criteria is { } cr ? cr.ToString() : null,
    };

    private static UnitDto MapUnit(Unit u) => new()
    {
        Id = u.Id, Symbol = u.Symbol, FormalName = u.FormalName, IsCompound = u.IsCompound,
        UnitQuantityCode = u.UnitQuantityCode, DecimalPlaces = u.DecimalPlaces,
        FirstUnitId = u.FirstUnitId, TailUnitId = u.TailUnitId,
        ConversionNumerator = u.ConversionNumerator, ConversionDenominator = u.ConversionDenominator,
    };

    private static StockGroupDto MapStockGroup(StockGroup g) => new()
    {
        Id = g.Id, Name = g.Name, ParentId = g.ParentId, Alias = g.Alias, AddQuantities = g.AddQuantities,
    };

    private static StockCategoryDto MapStockCategory(StockCategory g) => new()
    {
        Id = g.Id, Name = g.Name, ParentId = g.ParentId, Alias = g.Alias,
    };

    private static GodownDto MapGodown(Godown g) => new()
    {
        Id = g.Id, Name = g.Name, ParentId = g.ParentId, Alias = g.Alias,
        ThirdParty = g.ThirdParty, IsMainLocation = g.IsMainLocation,
    };

    private static StockItemDto MapStockItem(StockItem i) => new()
    {
        Id = i.Id, Name = i.Name, StockGroupId = i.StockGroupId, BaseUnitId = i.BaseUnitId,
        CategoryId = i.CategoryId, Alias = i.Alias, ValuationMethod = i.ValuationMethod.ToString(),
        HsnSacCode = i.HsnSacCode, IsTaxable = i.IsTaxable,
        StandardCostPaisa = MoneyCodec.ToPaisa(i.StandardCost),
        ReorderLevel = i.ReorderLevel, MinimumOrderQuantity = i.MinimumOrderQuantity,
        Gst = i.Gst is { } g ? MapStockItemGst(g) : null,
        MaintainInBatches = i.MaintainInBatches,
        TrackManufacturingDate = i.TrackManufacturingDate,
        UseExpiryDates = i.UseExpiryDates,
        SetComponents = i.SetComponents,
        TcsNatureOfGoodsId = i.TcsNatureOfGoodsId,
    };

    private static BatchMasterDto MapBatchMaster(BatchMaster b) => new()
    {
        Id = b.Id, StockItemId = b.StockItemId, BatchNumber = b.BatchNumber,
        ManufacturingDate = Iso(b.ManufacturingDate), ExpiryDate = Iso(b.ExpiryDate),
        ExpiryPeriod = b.ExpiryPeriod?.RawText, GodownId = b.GodownId,
        InwardQuantity = b.InwardQuantity, InwardRatePaisa = MoneyCodec.ToPaisa(b.InwardRate),
    };

    private static BillOfMaterialsDto MapBom(BillOfMaterials b) => new()
    {
        Id = b.Id, StockItemId = b.StockItemId, Name = b.Name, UnitOfManufacture = b.UnitOfManufacture,
        // Line order is the recipe's own order — preserved verbatim (NOT reordered).
        Lines = b.Lines.Select(MapBomLine).ToList(),
    };

    private static BomLineDto MapBomLine(BomLine l) => new()
    {
        LineType = l.LineType.ToString(), ComponentStockItemId = l.ComponentStockItemId, GodownId = l.GodownId,
        QuantityPerBlock = l.QuantityPerBlock,
        RatePaisa = l.Rate is { } r ? MoneyCodec.ToPaisa(r) : null,
        PercentOfFinishedGoodCost = l.PercentOfFinishedGoodCost,
    };

    private static StockOpeningBalanceDto MapStockOpeningBalance(StockOpeningBalance b) => new()
    {
        Id = b.Id, StockItemId = b.StockItemId, GodownId = b.GodownId,
        Quantity = b.Quantity, RatePaisa = MoneyCodec.ToPaisa(b.Rate), BatchLabel = b.BatchLabel,
        ManufacturingDate = Iso(b.ManufacturingDate), ExpiryDate = Iso(b.ExpiryDate),
    };

    // ------------------------------------------------------------- gst value objects

    private static GstConfigDto MapGstConfig(GstConfig g) => new()
    {
        Enabled = g.Enabled, Gstin = g.Gstin, HomeStateCode = g.HomeStateCode,
        RegistrationType = g.RegistrationType.ToString(), ApplicableFrom = Iso(g.ApplicableFrom),
        Periodicity = g.Periodicity.ToString(),
        RateSlabs = g.RateSlabs.OrderBy(s => s.RateBasisPoints).ThenBy(s => s.Id)
            .Select(s => new GstRateSlabDto
            {
                Id = s.Id, RateBasisPoints = s.RateBasisPoints, Label = s.Label, IsPredefined = s.IsPredefined,
            }).ToList(),
        // Phase 9 slice 1: order deterministically so the byte stream is stable regardless of insertion order.
        RateHistory = g.RateHistory
            .OrderBy(h => h.EffectiveFrom).ThenBy(h => h.HsnSac ?? "").ThenBy(h => h.RateBasisPoints).ThenBy(h => h.Id)
            .Select(h => new GstRateHistoryDto
            {
                Id = h.Id, HsnSac = h.HsnSac, RateBasisPoints = h.RateBasisPoints, RateClass = h.RateClass.ToString(),
                EffectiveFrom = Iso(h.EffectiveFrom), EffectiveTo = Iso(h.EffectiveTo),
                ValuationBasis = h.ValuationBasis.ToString(), Label = h.Label, IsPredefined = h.IsPredefined,
            }).ToList(),
        CessRates = g.CessRates
            .OrderBy(c => c.EffectiveFrom).ThenBy(c => c.HsnSac ?? "").ThenBy(c => (int)c.ValuationMode).ThenBy(c => c.Id)
            .Select(c => new GstCessRateDto
            {
                Id = c.Id, HsnSac = c.HsnSac, ValuationMode = c.ValuationMode.ToString(),
                CessRateBasisPoints = c.CessRateBasisPoints, CessPerUnitPaisa = MoneyCodec.ToPaisa(c.CessPerUnit),
                CessRspFactorMillis = c.CessRspFactorMillis, EffectiveFrom = Iso(c.EffectiveFrom),
                EffectiveTo = Iso(c.EffectiveTo), Label = c.Label, IsPredefined = c.IsPredefined,
            }).ToList(),
        // Phase 9 slice 2: reverse-charge categories, ordered deterministically so the byte stream is stable.
        RcmCategories = g.RcmCategories
            .OrderBy(c => c.EffectiveFrom).ThenBy(c => c.Notification, StringComparer.Ordinal)
            .ThenBy(c => c.SupplyNature, StringComparer.Ordinal).ThenBy(c => c.Id)
            .Select(c => new RcmCategoryDto
            {
                Id = c.Id, Notification = c.Notification, Stream = c.Stream.ToString(),
                SupplyNature = c.SupplyNature, SupplyType = c.SupplyType.ToString(), HsnSac = c.HsnSac,
                RateBasisPoints = c.RateBasisPoints, SupplierQualifier = c.SupplierQualifier.ToString(),
                RecipientQualifier = c.RecipientQualifier.ToString(), EffectiveFrom = Iso(c.EffectiveFrom),
                EffectiveTo = Iso(c.EffectiveTo), Label = c.Label, IsPredefined = c.IsPredefined,
            }).ToList(),
        // Phase 9 slice 3: composition sub-type + opt-in date (null for a Regular company ⇒ byte-identical, ER-13).
        CompositionSubType = g.CompositionSubType?.ToString(),
        CompositionOptInDate = Iso(g.CompositionOptInDate),
        // Phase 9 slice 4a: NON-SECRET e-invoice / B2C-QR / connector-mode config (defaults ⇒ byte-identical when off,
        // ER-13). No NIC credential is mapped — it flows only through INicCredentialStore (ER-16), so this mapper cannot
        // serialise a secret even by mistake.
        EInvoicingEnabled = g.EInvoicingEnabled,
        EInvoiceApplicableFrom = Iso(g.EInvoiceApplicableFrom),
        EInvoiceAatoThresholdPaisa = MoneyCodec.ToPaisa(g.EInvoiceAatoThreshold),
        EInvoiceApplicabilityOverride = g.EInvoiceApplicabilityOverride,
        EInvoiceExemptionClasses = g.ExemptionClasses.ToString(),
        EInvoiceReportingAgeApplies = g.ReportingAgeLimitApplies,
        ConnectorMode = g.ConnectorMode.ToString(),
        B2cDynamicQrEnabled = g.B2cDynamicQrEnabled,
        B2cQrAatoThresholdPaisa = MoneyCodec.ToPaisa(g.B2cQrAatoThreshold),
        B2cQrUpiId = g.B2cQrUpiId,
        B2cQrPayeeName = g.B2cQrPayeeName,
        // Phase 9 slice 5: NON-SECRET e-Way Bill config + per-state overrides (defaults ⇒ byte-identical when off,
        // ER-13). No NIC credential is mapped — the live path reuses ConnectorMode + INicCredentialStore (ER-16).
        EWayBillEnabled = g.EWayBillEnabled,
        EWayApplicableFrom = Iso(g.EWayApplicableFrom),
        EWayThresholdPaisa = MoneyCodec.ToPaisa(g.EWayThreshold),
        ConsignmentBasis = g.ConsignmentBasis.ToString(),
        EWayIntraStateApplicable = g.EWayIntraStateApplicable,
        EWayStateThresholds = g.EWayStateThresholds
            .OrderBy(t => t.StateCode, StringComparer.Ordinal).ThenBy(t => (int)t.TxnType).ThenBy(t => t.Id)
            .Select(t => new EWayStateThresholdDto
            {
                Id = t.Id, StateCode = t.StateCode, TxnType = t.TxnType.ToString(),
                ThresholdPaisa = MoneyCodec.ToPaisa(t.Threshold),
            }).ToList(),
        // Phase 9 slice 6: the GSTR-2B reconciliation tolerance (defaults ⇒ byte-identical when off, ER-13; finding #5).
        ReconValueTolerancePaisa = MoneyCodec.ToPaisa(g.ReconValueTolerance),
        ReconDateWindowDays = g.ReconDateWindowDays,
    };

    private static PartyGstDto MapPartyGst(PartyGstDetails p) => new()
    {
        RegistrationType = p.RegistrationType.ToString(), Gstin = p.Gstin, StateCode = p.StateCode,
        // Phase 9 slice 2: reverse-charge qualifiers.
        IsPromoter = p.IsPromoter, IsBodyCorporate = p.IsBodyCorporate,
    };

    private static StockItemGstDto MapStockItemGst(StockItemGstDetails s) => new()
    {
        HsnSac = s.HsnSac, Taxability = s.Taxability.ToString(),
        RateBasisPoints = s.RateBasisPoints, SupplyType = s.SupplyType.ToString(),
        // Phase 9 slice 1: GST 2.0 RSP valuation + cess (Money? → long? paisa).
        ValuationBasis = s.ValuationBasis.ToString(), CessApplicable = s.CessApplicable,
        CessValuationMode = s.CessValuationMode?.ToString(), CessRateBasisPoints = s.CessRateBasisPoints,
        CessPerUnitPaisa = MoneyCodec.ToPaisa(s.CessPerUnit), CessRspFactorMillis = s.CessRspFactorMillis,
        RspPaisa = MoneyCodec.ToPaisa(s.RetailSalePrice),
        // Phase 9 slice 2: reverse-charge flags.
        ReverseChargeApplicable = s.ReverseChargeApplicable, GtaForwardCharge = s.GtaForwardCharge,
        RcmCategoryId = s.RcmCategoryId,
        // Phase 9 slice 6b: §17(5) ITC-eligibility (enum names; default Eligible/None ⇒ byte-identical, ER-13).
        ItcEligibility = s.ItcEligibility.ToString(), BlockedCreditCategory = s.BlockedCreditCategory.ToString(),
    };

    private static LedgerGstClassificationDto MapGstClassification(LedgerGstClassification c) => new()
    {
        TaxHead = c.TaxHead.ToString(), Direction = c.Direction.ToString(),
        // Phase 9 slice 2: the RCM Output-ledger discriminator.
        IsReverseCharge = c.IsReverseCharge,
    };

    // ------------------------------------------------------------- tds / tcs value objects (Phase 7 slice 1)

    private static TdsConfigDto MapTdsConfig(TdsConfig t) => new()
    {
        Enabled = t.Enabled, Tan = t.Tan, DeductorType = t.DeductorType.ToString(),
        ResponsiblePersonName = t.ResponsiblePersonName, ResponsiblePersonPan = t.ResponsiblePersonPan,
        ResponsiblePersonDesignation = t.ResponsiblePersonDesignation, ResponsiblePersonAddress = t.ResponsiblePersonAddress,
        SurchargeApplicable = t.SurchargeApplicable, CessApplicable = t.CessApplicable,
        Periodicity = t.Periodicity.ToString(), ApplicableFrom = Iso(t.ApplicableFrom),
        // Ordered by section code then id so the byte stream is stable regardless of insertion order.
        NaturesOfPayment = t.NaturesOfPayment
            .OrderBy(n => n.SectionCode, StringComparer.Ordinal).ThenBy(n => n.Id)
            .Select(MapNatureOfPayment).ToList(),
    };

    private static TcsConfigDto MapTcsConfig(TcsConfig t) => new()
    {
        Enabled = t.Enabled, Tan = t.Tan, CollectorType = t.CollectorType.ToString(),
        ResponsiblePersonName = t.ResponsiblePersonName, ResponsiblePersonPan = t.ResponsiblePersonPan,
        ResponsiblePersonDesignation = t.ResponsiblePersonDesignation, ResponsiblePersonAddress = t.ResponsiblePersonAddress,
        SurchargeApplicable = t.SurchargeApplicable, CessApplicable = t.CessApplicable,
        Periodicity = t.Periodicity.ToString(), ApplicableFrom = Iso(t.ApplicableFrom),
        NaturesOfGoods = t.NaturesOfGoods
            .OrderBy(n => n.CollectionCode, StringComparer.Ordinal).ThenBy(n => n.Id)
            .Select(MapNatureOfGoods).ToList(),
    };

    private static NatureOfPaymentDto MapNatureOfPayment(NatureOfPayment n) => new()
    {
        Id = n.Id, SectionCode = n.SectionCode, Name = n.Name,
        RateWithPanBp = n.RateWithPanBp, RateWithoutPanBp = n.RateWithoutPanBp,
        SingleThresholdPaisa = n.SingleTransactionThreshold is { } s ? MoneyCodec.ToPaisa(s) : null,
        CumulativeThresholdPaisa = n.CumulativeThreshold is { } c ? MoneyCodec.ToPaisa(c) : null,
        FvuSectionCode = n.FvuSectionCode, EffectiveFrom = Iso(n.EffectiveFrom), IsPredefined = n.IsPredefined,
    };

    private static NatureOfGoodsDto MapNatureOfGoods(NatureOfGoods n) => new()
    {
        Id = n.Id, CollectionCode = n.CollectionCode, Name = n.Name,
        RateWithPanBp = n.RateWithPanBp, RateWithoutPanBp = n.RateWithoutPanBp,
        ThresholdPaisa = n.Threshold is { } th ? MoneyCodec.ToPaisa(th) : null,
        BaseIncludesGst = n.BaseIncludesGst, FvuCode = n.FvuCode, EffectiveFrom = Iso(n.EffectiveFrom),
        IsPredefined = n.IsPredefined, IsLegacy = n.IsLegacy, LegacyCutoff = Iso(n.LegacyCutoff),
    };

    // ------------------------------------------------------------- vouchers

    private static VoucherDto MapVoucher(Voucher v) => new()
    {
        Id = v.Id, TypeId = v.TypeId, Number = v.Number, Date = Iso(v.Date),
        Narration = v.Narration, PartyId = v.PartyId,
        Cancelled = v.Cancelled, Optional = v.Optional, PostDated = v.PostDated,
        ApplicableUpto = Iso(v.ApplicableUpto),
        Lines = v.Lines.Select(MapEntryLine).ToList(),
        InventoryLines = v.InventoryLines.Select(MapVoucherInventoryLine).ToList(),
        // POS tenders preserved in their declared (stable) order — Gift, Card, Cheque, Cash.
        PosTenders = v.PosTenders.Select(MapPosTender).ToList(),
    };

    private static PosTenderDto MapPosTender(PosTender t) => new()
    {
        TenderType = t.Type.ToString(), LedgerId = t.LedgerId, AmountPaisa = MoneyCodec.ToPaisa(t.Amount),
        TenderedPaisa = t.Tendered is { } td ? MoneyCodec.ToPaisa(td) : null,
        ChangePaisa = t.Change is { } ch ? MoneyCodec.ToPaisa(ch) : null,
        CardNo = t.CardNo, BankName = t.BankName, ChequeNo = t.ChequeNo,
    };

    private static EntryLineDto MapEntryLine(EntryLine l) => new()
    {
        LedgerId = l.LedgerId, AmountPaisa = MoneyCodec.ToPaisa(l.Amount), Side = l.Side.ToString(),
        BillAllocations = l.BillAllocations.Select(MapBillAllocation).ToList(),
        CostAllocations = l.CostAllocations.Select(MapCostAllocation).ToList(),
        BankAllocation = l.BankAllocation is { } b ? MapBankAllocation(b) : null,
        Forex = l.Forex is { } f ? MapForex(f) : null,
        Gst = l.Gst is { } g ? MapGstLineTax(g) : null,
        Tds = l.Tds is { } t ? MapTdsLineTax(t) : null,
        Tcs = l.Tcs is { } tc ? MapTcsLineTax(tc) : null,
        Payroll = l.Payroll is { } pr ? MapPayrollLine(pr) : null,
    };

    private static PayrollLineDto MapPayrollLine(PayrollLineDetail p) => new()
    {
        EmployeeId = p.EmployeeId, PayHeadId = p.PayHeadId, Category = p.Category.ToString(),
        AmountPaisa = MoneyCodec.ToPaisa(p.Amount),
    };

    private static TdsLineTaxDto MapTdsLineTax(TdsLineTax t) => new()
    {
        NatureId = t.NatureId, SectionCode = t.SectionCode,
        AssessableValuePaisa = MoneyCodec.ToPaisa(t.AssessableValue), RateBasisPoints = t.RateBasisPoints,
        TdsAmountPaisa = MoneyCodec.ToPaisa(t.TdsAmount), DeducteeLedgerId = t.DeducteeLedgerId,
        PanApplied = t.PanApplied,
    };

    private static TcsLineTaxDto MapTcsLineTax(TcsLineTax t) => new()
    {
        NatureId = t.NatureId, CollectionCode = t.CollectionCode,
        AssessableValuePaisa = MoneyCodec.ToPaisa(t.AssessableValue), RateBasisPoints = t.RateBasisPoints,
        TcsAmountPaisa = MoneyCodec.ToPaisa(t.TcsAmount), CollecteeLedgerId = t.CollecteeLedgerId,
        PanApplied = t.PanApplied,
    };

    private static BillAllocationDto MapBillAllocation(BillAllocation a) => new()
    {
        RefType = a.RefType.ToString(), Name = a.Name, AmountPaisa = MoneyCodec.ToPaisa(a.Amount),
        DueDate = Iso(a.DueDate), CreditPeriodDays = a.CreditPeriodDays,
    };

    private static CostAllocationDto MapCostAllocation(CostAllocation a) => new()
    {
        CategoryId = a.CategoryId, CentreId = a.CentreId, AmountPaisa = MoneyCodec.ToPaisa(a.Amount),
    };

    private static BankAllocationDto MapBankAllocation(BankAllocation b) => new()
    {
        TransactionType = b.TransactionType.ToString(), InstrumentNumber = b.InstrumentNumber,
        InstrumentDate = Iso(b.InstrumentDate), BankDate = Iso(b.BankDate),
    };

    private static ForexDto MapForex(ForexInfo f) => new()
    {
        CurrencyId = f.CurrencyId, ForexAmountPaisa = MoneyCodec.ToPaisa(f.ForexAmount), RateMicro = ToMicro(f.Rate),
    };

    private static GstLineTaxDto MapGstLineTax(GstLineTax g) => new()
    {
        TaxHead = g.TaxHead.ToString(), RateBasisPoints = g.RateBasisPoints,
        TaxableValuePaisa = MoneyCodec.ToPaisa(g.TaxableValue),
        // Phase 9 slice 2: reverse-charge tag. Phase 9 slice 7: the set-off / reversal adjustment tag.
        IsReverseCharge = g.IsReverseCharge, RcmScheme = g.RcmScheme?.ToString(),
        Adjustment = g.Adjustment?.ToString(),
    };

    private static VoucherInventoryLineDto MapVoucherInventoryLine(VoucherInventoryLine l) => new()
    {
        StockItemId = l.StockItemId, GodownId = l.GodownId, Quantity = l.Quantity,
        RatePaisa = MoneyCodec.ToPaisa(l.Rate), Direction = l.Direction.ToString(), BatchLabel = l.BatchLabel,
        // Emit Billed only when it differs from Actual (feature off ⇒ null ⇒ byte-identical, ER-13).
        BilledQuantity = l.BilledQuantity == l.Quantity ? null : l.BilledQuantity,
        // WI-10 Gap 2: the line unit, verbatim. null ⇒ the item's base unit ⇒ the key/attribute is omitted
        // entirely, so a company with no unit-carrying item line exports byte-identically (ER-13).
        UnitId = l.UnitId,
    };

    // ------------------------------------------------------------- inventory / order vouchers

    private static InventoryVoucherDto MapInventoryVoucher(InventoryVoucher v) => new()
    {
        Id = v.Id, TypeId = v.TypeId, Number = v.Number, Date = Iso(v.Date),
        Narration = v.Narration, PartyId = v.PartyId, Cancelled = v.Cancelled, PostDated = v.PostDated,
        Allocations = v.Allocations.Select(MapInventoryAllocation).ToList(),
        DestinationAllocations = v.DestinationAllocations.Select(MapInventoryAllocation).ToList(),
        OrderLines = v.OrderLines.Select(MapOrderLine).ToList(),
        PhysicalLines = v.PhysicalLines.Select(MapPhysicalStockLine).ToList(),
        // Additional-cost lines preserve their own order (load-bearing for apportionment reporting).
        AdditionalCostLines = v.AdditionalCostLines
            .Select(a => new AdditionalCostLineDto { LedgerId = a.LedgerId, AmountPaisa = MoneyCodec.ToPaisa(a.Amount) })
            .ToList(),
        JobWorkOrder = v.JobWorkOrder is { } jwo ? MapJobWorkOrder(jwo) : null,
        // Order links preserved verbatim (each is a source Job Work Order voucher id).
        OrderLinks = v.OrderLinks.ToList(),
    };

    private static JobWorkOrderDto MapJobWorkOrder(JobWorkOrder j) => new()
    {
        Direction = j.Direction.ToString(), OrderNo = j.OrderNo,
        DurationOfProcess = j.DurationOfProcess, NatureOfProcessing = j.NatureOfProcessing,
        FinishedGoodStockItemId = j.FinishedGoodStockItemId, FinishedGoodQuantity = j.FinishedGoodQuantity,
        FinishedGoodDueDate = Iso(j.FinishedGoodDueDate), FinishedGoodGodownId = j.FinishedGoodGodownId,
        FinishedGoodRatePaisa = j.FinishedGoodRate is { } r ? MoneyCodec.ToPaisa(r) : null,
        TrackingComponents = j.TrackingComponents, FillComponentsBomId = j.FillComponentsBomId,
        // Component line order is the order's own order — preserved verbatim.
        Lines = j.Lines.Select(l => new JobWorkOrderLineDto
        {
            ComponentStockItemId = l.ComponentStockItemId, Track = l.Track.ToString(),
            DueDate = Iso(l.DueDate), GodownId = l.GodownId, Quantity = l.Quantity,
            RatePaisa = l.Rate is { } r ? MoneyCodec.ToPaisa(r) : null,
        }).ToList(),
    };

    private static InventoryAllocationDto MapInventoryAllocation(InventoryAllocation a) => new()
    {
        StockItemId = a.StockItemId, GodownId = a.GodownId, Quantity = a.Quantity,
        Direction = a.Direction.ToString(), RatePaisa = MoneyCodec.ToPaisa(a.Rate),
        BatchLabel = a.BatchLabel, UnitId = a.UnitId,
    };

    private static OrderLineDto MapOrderLine(OrderLine l) => new()
    {
        StockItemId = l.StockItemId, GodownId = l.GodownId, Quantity = l.Quantity,
        RatePaisa = MoneyCodec.ToPaisa(l.Rate),
    };

    private static PhysicalStockLineDto MapPhysicalStockLine(PhysicalStockLine l) => new()
    {
        StockItemId = l.StockItemId, GodownId = l.GodownId, CountedQuantity = l.CountedQuantity, BatchLabel = l.BatchLabel,
    };
}
