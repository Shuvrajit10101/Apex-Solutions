using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Canonical XML export/parse (DP-4 "literal plan text" variant): the <b>same</b> versioned
/// <see cref="CanonicalModel"/> the JSON path uses, serialised as XML. Money is integer paisa; ordering is the
/// mapper's deterministic ordering, so the bytes are stable and the round-trip is lossless. Like the JSON
/// parser, <see cref="Parse"/> collects per-problem messages and never throws on bad data (RQ-21).
/// <para>
/// Uses <see cref="System.Xml"/> / <see cref="System.Xml.Linq"/> only — no NuGet, no clock/RNG/Avalonia. The
/// element/attribute layout deliberately mirrors the DTO tree 1:1, so a reader can round-trip it without a
/// schema (DP-4).
/// </para>
/// </summary>
public static class CanonicalXml
{
    private const string Root = "apexExport";

    public static byte[] Export(Company company) => Export(CanonicalMapper.ToModel(company));

    public static byte[] Export(CanonicalModel model)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), BuildRoot(model));
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            NewLineChars = "\n",
            OmitXmlDeclaration = false,
        };
        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, settings))
            doc.Save(w);
        return ms.ToArray();
    }

    // ------------------------------------------------------------- build (model -> XML)

    private static XElement BuildRoot(CanonicalModel m)
    {
        var c = m.Company;
        var root = new XElement(Root,
            new XAttribute("formatVersion", m.FormatVersion),
            new XAttribute("schemaVersion", m.SchemaVersion));

        var company = new XElement("company",
            Attr("id", c.Id), Attr("name", c.Name),
            Opt("mailingName", c.MailingName), Opt("address", c.Address),
            Opt("country", c.Country), Opt("state", c.State), Opt("pin", c.Pin),
            Attr("financialYearStart", c.FinancialYearStart), Attr("booksBeginFrom", c.BooksBeginFrom),
            Opt("baseCurrencySymbol", c.BaseCurrencySymbol), Opt("baseCurrencyName", c.BaseCurrencyName),
            Attr("decimalPlaces", c.DecimalPlaces), Opt("decimalUnitName", c.DecimalUnitName),
            Attr("useSeparateActualBilledQuantity", c.UseSeparateActualBilledQuantity),
            Attr("enableMultiplePriceLevels", c.EnableMultiplePriceLevels),
            Attr("enableJobOrderProcessing", c.EnableJobOrderProcessing),
            Attr("payrollEnabled", c.PayrollEnabled),
            Attr("payrollStatutoryEnabled", c.PayrollStatutoryEnabled),
            Attr("salaryTdsEnabled", c.SalaryTdsEnabled));
        if (c.Gst is { } gst) company.Add(BuildGstConfig(gst));
        if (c.Tds is { } tds) company.Add(BuildTdsConfig(tds));
        if (c.Tcs is { } tcs) company.Add(BuildTcsConfig(tcs));
        if (c.Pf is { } pf) company.Add(BuildPfConfig(pf));
        if (c.Esi is { } esi) company.Add(BuildEsiConfig(esi));
        if (c.Pt is { } pt) company.Add(BuildPtConfig(pt));
        if (c.Gratuity is { } gr) company.Add(BuildGratuityConfig(gr));
        if (c.Bonus is { } bo) company.Add(BuildBonusConfig(bo));
        root.Add(company);

        var p = m.Payload;
        root.Add(
            List("groups", "group", p.Groups, BuildGroup),
            List("ledgers", "ledger", p.Ledgers, BuildLedger),
            List("voucherTypes", "voucherType", p.VoucherTypes, BuildVoucherType),
            List("costCategories", "costCategory", p.CostCategories, BuildCostCategory),
            List("costCentres", "costCentre", p.CostCentres, BuildCostCentre),
            List("currencies", "currency", p.Currencies, BuildCurrency),
            List("exchangeRates", "exchangeRate", p.ExchangeRates, BuildExchangeRate),
            List("budgets", "budget", p.Budgets, BuildBudget),
            List("scenarios", "scenario", p.Scenarios, BuildScenario),
            List("units", "unit", p.Units, BuildUnit),
            List("stockGroups", "stockGroup", p.StockGroups, BuildStockGroup),
            List("stockCategories", "stockCategory", p.StockCategories, BuildStockCategory),
            List("godowns", "godown", p.Godowns, BuildGodown),
            List("stockItems", "stockItem", p.StockItems, BuildStockItem),
            List("batchMasters", "batchMaster", p.BatchMasters, BuildBatchMaster),
            List("billsOfMaterials", "billOfMaterials", p.BillsOfMaterials, BuildBom),
            List("stockOpeningBalances", "stockOpeningBalance", p.StockOpeningBalances, BuildStockOpeningBalance),
            List("priceLevels", "priceLevel", p.PriceLevels, BuildPriceLevel),
            List("priceLists", "priceList", p.PriceLists, BuildPriceList),
            List("reorderDefinitions", "reorderDefinition", p.ReorderDefinitions, BuildReorderDefinition),
            List("employeeCategories", "employeeCategory", p.EmployeeCategories, BuildEmployeeCategory),
            List("employeeGroups", "employeeGroup", p.EmployeeGroups, BuildEmployeeGroup),
            List("payrollUnits", "payrollUnit", p.PayrollUnits, BuildPayrollUnit),
            List("attendanceTypes", "attendanceType", p.AttendanceTypes, BuildAttendanceType),
            List("employees", "employee", p.Employees, BuildEmployee),
            List("payHeads", "payHead", p.PayHeads, BuildPayHead),
            List("salaryStructures", "salaryStructure", p.SalaryStructures, BuildSalaryStructure),
            List("attendanceEntries", "attendanceEntry", p.AttendanceEntries, BuildAttendanceEntry),
            List("taxDeclarations", "taxDeclaration", p.TaxDeclarations, BuildTaxDeclaration),
            List("vouchers", "voucher", p.Vouchers, BuildVoucher),
            List("inventoryVouchers", "inventoryVoucher", p.InventoryVouchers, BuildInventoryVoucher),
            List("tdsChallans", "tdsChallan", p.TdsChallans, BuildTdsChallan),
            List("challanVoucherLinks", "challanVoucherLink", p.ChallanVoucherLinks, BuildChallanVoucherLink),
            List("tcsChallans", "tcsChallan", p.TcsChallans, BuildTcsChallan),
            List("tcsChallanVoucherLinks", "tcsChallanVoucherLink", p.TcsChallanVoucherLinks, BuildChallanVoucherLink),
            // Phase 9 slice 2: RCM generated documents + §34-CDN links + GST-on-advance receipts (empty until used).
            List("rcmDocuments", "rcmDocument", p.RcmDocuments, BuildRcmDocument),
            // Phase 9 slice 4a: e-invoice IRP artefacts (empty when e-invoicing is off, ER-13).
            List("eInvoiceRecords", "eInvoiceRecord", p.EInvoiceRecords, BuildEInvoiceRecord),
            // Phase 9 slice 5: e-Way Bill artefacts (empty when e-Way is off, ER-13).
            List("eWayBillRecords", "eWayBillRecord", p.EWayBillRecords, BuildEWayBillRecord),
            List("creditDebitNoteLinks", "creditDebitNoteLink", p.CreditDebitNoteLinks, BuildCdnLink),
            List("advanceReceipts", "advanceReceipt", p.AdvanceReceipts, BuildAdvanceReceipt),
            // Phase 9 slice 6: imported GSTR-2B/2A snapshots (owning their lines) + reconciliation results (empty when
            // 2B is never imported, ER-13).
            List("gstr2bSnapshots", "gstr2bSnapshot", p.Gstr2bSnapshots, BuildGstr2bSnapshot),
            List("gstr2bReconResults", "gstr2bReconResult", p.Gstr2bReconResults, BuildGstr2bReconResult),
            // Phase 9 slice 6b: offline IMS decisions (empty until the user acts, ER-13).
            List("imsActions", "imsAction", p.ImsActions, BuildImsAction),
            // Phase 9 slice 7: electronic-ledger set-off / reversal / challan / DRC-03 records (empty until the
            // company sets off / pays / reverses, ER-13).
            List("gstSetoffLines", "gstSetoffLine", p.GstSetoffLines, BuildGstSetoffLine),
            List("itcReversals", "itcReversal", p.ItcReversals, BuildItcReversal),
            List("gstChallans", "gstChallan", p.GstChallans, BuildGstChallan),
            List("gstDrc03s", "gstDrc03", p.GstDrc03s, BuildGstDrc03));
        return root;
    }

    private static XElement BuildGstSetoffLine(GstSetoffLineDto l) => new("gstSetoffLine",
        Attr("id", l.Id), Attr("voucherId", l.VoucherId), Attr("period", l.Period),
        Attr("creditHead", l.CreditHead), Attr("liabilityHead", l.LiabilityHead),
        OptTrue("isCash", l.IsCash), Attr("amountPaisa", l.AmountPaisa));

    private static XElement BuildItcReversal(ItcReversalDto r) => new("itcReversal",
        Attr("id", r.Id), Attr("rule", r.Rule), Attr("period", r.Period),
        Attr("cgstPaisa", r.CgstPaisa), Attr("sgstPaisa", r.SgstPaisa), Attr("igstPaisa", r.IgstPaisa),
        Attr("cessPaisa", r.CessPaisa), OptLong("d1BasisPaisa", r.D1BasisPaisa), OptLong("d2BasisPaisa", r.D2BasisPaisa),
        OptId("sourceVoucherId", r.SourceVoucherId), OptId("sourceLineId", r.SourceLineId),
        Attr("reversalVoucherId", r.ReversalVoucherId), OptId("reclaimOfId", r.ReclaimOfId),
        OptId("drc03Id", r.Drc03Id), Attr("table4bBucket", r.Table4bBucket), Attr("createdAt", r.CreatedAt));

    private static XElement BuildGstChallan(GstChallanDto ch) => new("gstChallan",
        Attr("id", ch.Id), Attr("cpin", ch.Cpin), Opt("cin", ch.Cin), Opt("brn", ch.Brn),
        Attr("depositDate", ch.DepositDate), Attr("majorHead", ch.MajorHead), Attr("minorHead", ch.MinorHead),
        Attr("amountPaisa", ch.AmountPaisa), Attr("voucherId", ch.VoucherId), OptTrue("interestFlag", ch.InterestFlag));

    private static XElement BuildGstDrc03(GstDrc03Dto d) => new("gstDrc03",
        Attr("id", d.Id), Opt("drc03Ref", d.Drc03Ref), Attr("cause", d.Cause), Attr("period", d.Period),
        Attr("cgstPaisa", d.CgstPaisa), Attr("sgstPaisa", d.SgstPaisa), Attr("igstPaisa", d.IgstPaisa),
        Attr("cessPaisa", d.CessPaisa), Attr("interestPaisa", d.InterestPaisa),
        Opt("drc03aDemandRef", d.Drc03aDemandRef), OptId("voucherId", d.VoucherId), Attr("createdAt", d.CreatedAt));

    private static XElement BuildGstr2bSnapshot(Gstr2bSnapshotDto s)
    {
        var el = new XElement("gstr2bSnapshot",
            Attr("id", s.Id), Attr("statementType", s.StatementType), Attr("returnPeriod", s.ReturnPeriod),
            Attr("recipientGstin", s.RecipientGstin), Opt("generatedOn", s.GeneratedOn),
            Attr("sourceFileHash", s.SourceFileHash), Attr("importedAt", s.ImportedAt),
            Attr("summaryIgstPaisa", s.SummaryIgstPaisa), Attr("summaryCgstPaisa", s.SummaryCgstPaisa),
            Attr("summarySgstPaisa", s.SummarySgstPaisa), Attr("summaryCessPaisa", s.SummaryCessPaisa));
        var lines = new XElement("lines");
        foreach (var l in s.Lines) lines.Add(BuildGstr2bLine(l));
        el.Add(lines);
        return el;
    }

    private static XElement BuildGstr2bLine(Gstr2bLineDto l) => new("gstr2bLine",
        Attr("id", l.Id), Attr("supplierGstin", l.SupplierGstin), Opt("supplierTradeName", l.SupplierTradeName),
        Attr("docType", l.DocType), Attr("docNumber", l.DocNumber), Opt("docNumberNorm", l.DocNumberNorm),
        Attr("docDate", l.DocDate), Opt("posStateCode", l.PosStateCode),
        Attr("taxableValuePaisa", l.TaxableValuePaisa), Attr("igstPaisa", l.IgstPaisa), Attr("cgstPaisa", l.CgstPaisa),
        Attr("sgstPaisa", l.SgstPaisa), Attr("cessPaisa", l.CessPaisa), Attr("itcAvailable", l.ItcAvailable),
        Opt("itcUnavailableReason", l.ItcUnavailableReason), Attr("reverseCharge", l.ReverseCharge));

    private static XElement BuildGstr2bReconResult(Gstr2bReconResultDto r) => new("gstr2bReconResult",
        Attr("id", r.Id), Attr("lineId", r.LineId), Attr("bucket", r.Bucket),
        OptId("matchedVoucherId", r.MatchedVoucherId), Attr("taxableVariancePaisa", r.TaxableVariancePaisa),
        Attr("taxVariancePaisa", r.TaxVariancePaisa), Attr("matchPinned", r.MatchPinned),
        Opt("reconciledAt", r.ReconciledAt));

    private static XElement BuildImsAction(ImsActionDto a) => new("imsAction",
        Attr("id", a.Id), Attr("lineId", a.LineId), Attr("status", a.Status), Opt("remarks", a.Remarks),
        OptLong("declaredReversalPaisa", a.DeclaredReversalPaisa), OptTrue("noReversalDeclared", a.NoReversalDeclared),
        Opt("actedOn", a.ActedOn));

    private static XElement BuildTdsChallan(TdsChallanDto ch) => new("tdsChallan",
        Attr("id", ch.Id), Attr("challanNo", ch.ChallanNo), Attr("bsrCode", ch.BsrCode),
        Attr("depositDate", ch.DepositDate), Attr("amountPaisa", ch.AmountPaisa),
        Attr("section", ch.Section), Attr("minorHead", ch.MinorHead));

    private static XElement BuildChallanVoucherLink(ChallanVoucherLinkDto l) => new("challanVoucherLink",
        Attr("challanId", l.ChallanId), Attr("voucherId", l.VoucherId));

    private static XElement BuildRcmDocument(RcmDocumentDto d) => new("rcmDocument",
        Attr("id", d.Id), Attr("kind", d.Kind), Attr("sourceVoucherId", d.SourceVoucherId),
        Attr("seriesNumber", d.SeriesNumber), Attr("docDate", d.DocDate), OptId("supplierLedgerId", d.SupplierLedgerId));

    private static XElement BuildEInvoiceRecord(EInvoiceRecordDto r) => new("eInvoiceRecord",
        Attr("id", r.Id), Attr("sourceVoucherId", r.SourceVoucherId), Attr("documentNumberUpper", r.DocumentNumberUpper),
        Attr("status", r.Status), Opt("irn", r.Irn), Opt("ackNo", r.AckNo), Opt("ackDate", r.AckDate),
        Opt("signedQr", r.SignedQr), Opt("signedJsonBase64", r.SignedJsonBase64), Opt("cancelledOn", r.CancelledOn),
        Opt("cancelReasonCode", r.CancelReasonCode), Opt("errorCode", r.ErrorCode), Opt("errorMessage", r.ErrorMessage));

    private static XElement BuildEWayBillRecord(EWayBillRecordDto r) => new("eWayBillRecord",
        Attr("id", r.Id), Attr("sourceVoucherId", r.SourceVoucherId), Attr("documentNumberUpper", r.DocumentNumberUpper),
        Attr("status", r.Status), Opt("supplyType", r.SupplyType), Opt("subSupplyType", r.SubSupplyType),
        Opt("docType", r.DocType), Attr("consignmentValuePaisa", r.ConsignmentValuePaisa),
        Opt("transporterId", r.TransporterId), Opt("transMode", r.TransMode), Opt("vehicleNumber", r.VehicleNumber),
        Attr("distanceKm", r.DistanceKm), Opt("transportDocNo", r.TransportDocNo),
        Opt("shipFromStateCode", r.ShipFromStateCode), Opt("shipToStateCode", r.ShipToStateCode),
        Attr("isOverDimensionalCargo", r.IsOverDimensionalCargo), Opt("shipToGstin", r.ShipToGstin),
        Attr("closureRequested", r.ClosureRequested), Opt("closedOn", r.ClosedOn), Opt("ewbNumber", r.EwbNumber),
        Opt("generatedAt", r.GeneratedAt), Opt("validUpto", r.ValidUpto), Opt("cancelledOn", r.CancelledOn),
        Opt("cancelReasonCode", r.CancelReasonCode), Opt("errorCode", r.ErrorCode), Opt("errorMessage", r.ErrorMessage));

    private static XElement BuildCdnLink(GstCdnLinkDto l) => new("creditDebitNoteLink",
        Attr("id", l.Id), Attr("cdnVoucherId", l.CdnVoucherId), Attr("cdnType", l.CdnType),
        OptId("originalInvoiceVoucherId", l.OriginalInvoiceVoucherId), Opt("originalInvoiceNumber", l.OriginalInvoiceNumber),
        Opt("originalInvoiceDate", l.OriginalInvoiceDate), Attr("reasonCode", l.ReasonCode),
        Attr("is9BTarget", l.Is9BTarget));

    private static XElement BuildAdvanceReceipt(GstAdvanceReceiptDto a) => new("advanceReceipt",
        Attr("id", a.Id), Attr("receiptVoucherId", a.ReceiptVoucherId), Attr("isService", a.IsService),
        Attr("advanceAmountPaisa", a.AdvanceAmountPaisa), Attr("rateBasisPoints", a.RateBasisPoints),
        Attr("interState", a.InterState), Opt("placeOfSupplyStateCode", a.PlaceOfSupplyStateCode),
        Attr("advanceTaxPaisa", a.AdvanceTaxPaisa),
        OptId("adjustedAgainstInvoiceVoucherId", a.AdjustedAgainstInvoiceVoucherId),
        OptId("refundVoucherId", a.RefundVoucherId));

    private static XElement BuildTcsChallan(TcsChallanDto ch) => new("tcsChallan",
        Attr("id", ch.Id), Attr("challanNo", ch.ChallanNo), Attr("bsrCode", ch.BsrCode),
        Attr("depositDate", ch.DepositDate), Attr("amountPaisa", ch.AmountPaisa),
        Attr("collectionCode", ch.CollectionCode), Attr("minorHead", ch.MinorHead));

    private static XElement List<T>(string wrapper, string item, IReadOnlyList<T> items, Func<T, XElement> build)
    {
        var el = new XElement(wrapper);
        foreach (var it in items) el.Add(build(it));
        return el;
    }

    private static XElement BuildGroup(GroupDto g) => new("group",
        Attr("id", g.Id), Attr("name", g.Name), Attr("nature", g.Nature),
        OptId("parentId", g.ParentId), Opt("alias", g.Alias), Attr("isPredefined", g.IsPredefined));

    private static XElement BuildLedger(LedgerDto l)
    {
        var el = new XElement("ledger",
            Attr("id", l.Id), Attr("name", l.Name), Attr("groupId", l.GroupId),
            Attr("openingBalancePaisa", l.OpeningBalancePaisa), Attr("openingIsDebit", l.OpeningIsDebit),
            Opt("alias", l.Alias), Attr("isPredefined", l.IsPredefined),
            Attr("maintainBillByBill", l.MaintainBillByBill),
            OptInt("defaultCreditPeriodDays", l.DefaultCreditPeriodDays),
            OptBool("costCentresApplicable", l.CostCentresApplicable),
            Attr("enableChequePrinting", l.EnableChequePrinting),
            Opt("chequePrintingBankName", l.ChequePrintingBankName),
            OptId("currencyId", l.CurrencyId),
            Opt("methodOfAppropriation", l.MethodOfAppropriation),
            OptId("defaultPriceLevelId", l.DefaultPriceLevelId),
            // Phase 7 slice 1: TDS/TCS applicability flags. Emit the booleans only when true so an ordinary ledger
            // serialises byte-identically to a v24 export (ER-13); the optional refs/enums emit only when present.
            OptTrue("tdsApplicable", l.TdsApplicable),
            OptId("tdsNatureOfPaymentId", l.TdsNatureOfPaymentId),
            Opt("deducteeType", l.DeducteeType),
            Opt("partyPan", l.PartyPan),
            OptTrue("deductTdsInSameVoucher", l.DeductTdsInSameVoucher),
            OptTrue("tcsApplicable", l.TcsApplicable),
            OptId("tcsNatureOfGoodsId", l.TcsNatureOfGoodsId),
            Opt("collecteeType", l.CollecteeType),
            Opt("tdsTcsClassification", l.TdsTcsClassification));
        if (l.Interest is { } i) el.Add(BuildInterest(i));
        // WI-4 (v45): the party Mailing Details block — emitted ONLY when present, so a ledger without one produces
        // bytes identical to a v44 export (ER-13).
        if (l.Mailing is { } ml) el.Add(BuildPartyMailing(ml));
        if (l.PartyGst is { } p) el.Add(BuildPartyGst(p));
        if (l.SalesPurchaseGst is { } s) el.Add(BuildStockItemGst("salesPurchaseGst", s));
        if (l.GstClassification is { } gc) el.Add(BuildGstClassification(gc));
        return el;
    }

    private static XElement BuildPriceLevel(PriceLevelDto x) => new("priceLevel",
        Attr("id", x.Id), Attr("name", x.Name));

    private static XElement BuildPriceList(PriceListDto x)
    {
        var el = new XElement("priceList",
            Attr("id", x.Id), Attr("priceLevelId", x.PriceLevelId), Attr("stockItemId", x.StockItemId),
            Attr("applicableFrom", x.ApplicableFrom));
        var slabs = new XElement("slabs");
        foreach (var s in x.Slabs)
            slabs.Add(new XElement("slab", Attr("fromQty", Dec(s.FromQty)), OptDec("toQty", s.ToQty),
                Attr("ratePaisa", s.RatePaisa), Attr("discountPercent", Dec(s.DiscountPercent))));
        el.Add(slabs);
        return el;
    }

    private static XElement BuildReorderDefinition(ReorderDefinitionDto d) => new("reorderDefinition",
        Attr("id", d.Id), Attr("scope", d.Scope), Attr("targetId", d.TargetId),
        Attr("reorderAdvanced", d.ReorderAdvanced), OptDec("reorderQuantity", d.ReorderQuantity),
        Attr("minQtyAdvanced", d.MinQtyAdvanced), OptDec("minOrderQuantity", d.MinOrderQuantity),
        OptInt("periodCount", d.PeriodCount), Opt("periodUnit", d.PeriodUnit), Opt("criteria", d.Criteria));

    private static XElement BuildInterest(InterestParametersDto i) => new("interest",
        Attr("enabled", i.Enabled), Attr("ratePercent", Dec(i.RatePercent)), Attr("per", i.Per),
        Attr("onBalance", i.OnBalance), Attr("applicability", i.Applicability), Opt("calculateFrom", i.CalculateFrom),
        Attr("style", i.Style), Attr("roundingMethod", i.RoundingMethod), Attr("roundingDecimals", i.RoundingDecimals));

    private static XElement BuildCostCategory(CostCategoryDto x) => new("costCategory",
        Attr("id", x.Id), Attr("name", x.Name), Attr("allocateRevenueItems", x.AllocateRevenueItems),
        Attr("allocateNonRevenueItems", x.AllocateNonRevenueItems), Attr("isPredefined", x.IsPredefined));

    private static XElement BuildCostCentre(CostCentreDto x) => new("costCentre",
        Attr("id", x.Id), Attr("name", x.Name), Attr("categoryId", x.CategoryId),
        OptId("parentId", x.ParentId), Opt("alias", x.Alias));

    // ---- payroll masters (Phase 8 slice 1) ----

    private static XElement BuildEmployeeCategory(EmployeeCategoryDto x) => new("employeeCategory",
        Attr("id", x.Id), Attr("name", x.Name), Attr("allocateRevenueItems", x.AllocateRevenueItems),
        Attr("allocateNonRevenueItems", x.AllocateNonRevenueItems), Attr("isPredefined", x.IsPredefined));

    private static XElement BuildEmployeeGroup(EmployeeGroupDto x) => new("employeeGroup",
        Attr("id", x.Id), Attr("name", x.Name), OptId("parentId", x.ParentId), Opt("alias", x.Alias),
        Attr("defineSalaryDetails", x.DefineSalaryDetails));

    private static XElement BuildPayrollUnit(PayrollUnitDto u) => new("payrollUnit",
        Attr("id", u.Id), Attr("symbol", u.Symbol), Attr("formalName", u.FormalName),
        Attr("isCompound", u.IsCompound), Attr("decimalPlaces", u.DecimalPlaces),
        OptId("firstUnitId", u.FirstUnitId), OptId("tailUnitId", u.TailUnitId),
        OptInt("conversionNumerator", u.ConversionNumerator), OptInt("conversionDenominator", u.ConversionDenominator));

    private static XElement BuildAttendanceType(AttendanceTypeDto a) => new("attendanceType",
        Attr("id", a.Id), Attr("name", a.Name), OptId("parentId", a.ParentId), Attr("kind", a.Kind),
        OptId("payrollUnitId", a.PayrollUnitId));

    private static XElement BuildEmployee(EmployeeDto e) => new("employee",
        Attr("id", e.Id), Attr("name", e.Name), Attr("employeeGroupId", e.EmployeeGroupId),
        OptId("employeeCategoryId", e.EmployeeCategoryId), Opt("employeeNumber", e.EmployeeNumber),
        Opt("dateOfJoining", e.DateOfJoining), Opt("dateOfLeaving", e.DateOfLeaving),
        Opt("designation", e.Designation), Opt("function", e.Function), Opt("location", e.Location),
        Opt("gender", e.Gender), Opt("dateOfBirth", e.DateOfBirth), Opt("pan", e.Pan), Opt("aadhaar", e.Aadhaar),
        Opt("uan", e.Uan), Opt("pfAccountNumber", e.PfAccountNumber), Opt("esiNumber", e.EsiNumber),
        Opt("bankAccountNumber", e.BankAccountNumber), Opt("bankName", e.BankName), Opt("bankIfsc", e.BankIfsc),
        Attr("applicableTaxRegime", e.ApplicableTaxRegime),
        Attr("pfApplicable", e.PfApplicable), Attr("pfContributeOnHigherWages", e.PfContributeOnHigherWages),
        Opt("pfJoinDate", e.PfJoinDate), Attr("esiApplicable", e.EsiApplicable),
        Attr("isPersonWithDisability", e.IsPersonWithDisability));

    private static XElement BuildPayHead(PayHeadDto p)
    {
        var el = new XElement("payHead",
            Attr("id", p.Id), Attr("name", p.Name), Opt("displayName", p.DisplayName),
            Attr("payHeadType", p.PayHeadType), Attr("calculationType", p.CalculationType),
            Attr("affectsNetSalary", p.AffectsNetSalary), OptId("underGroupId", p.UnderGroupId),
            OptId("ledgerId", p.LedgerId), OptId("employerExpenseLedgerId", p.EmployerExpenseLedgerId),
            Attr("incomeTaxComponent", p.IncomeTaxComponent),
            Attr("useForGratuity", p.UseForGratuity), Attr("roundingMethod", p.RoundingMethod),
            Attr("roundingLimitPaisa", p.RoundingLimitPaisa), Attr("calculationPeriod", p.CalculationPeriod),
            OptId("attendanceTypeId", p.AttendanceTypeId),
            OptInt("perDayCalculationBasisDays", p.PerDayCalculationBasisDays),
            Attr("pfComponent", p.PfComponent), Attr("partOfPfWages", p.PartOfPfWages),
            Attr("esiComponent", p.EsiComponent), Attr("partOfEsiWages", p.PartOfEsiWages),
            Attr("isOvertime", p.IsOvertime), Attr("ptComponent", p.PtComponent));
        var comps = new XElement("computationComponents");
        foreach (var c in p.ComputationComponents)
            comps.Add(new XElement("component", Attr("payHeadId", c.PayHeadId), Attr("isSubtraction", c.IsSubtraction)));
        var slabs = new XElement("computationSlabs");
        foreach (var s in p.ComputationSlabs)
            slabs.Add(new XElement("slab", Attr("slabType", s.SlabType), Attr("rateBasisPoints", s.RateBasisPoints),
                Attr("valuePaisa", s.ValuePaisa), OptLong("fromAmountPaisa", s.FromAmountPaisa),
                OptLong("toAmountPaisa", s.ToAmountPaisa)));
        el.Add(comps, slabs);
        return el;
    }

    private static XElement BuildSalaryStructure(SalaryStructureDto s)
    {
        var el = new XElement("salaryStructure",
            Attr("id", s.Id), Attr("scope", s.Scope), Attr("scopeId", s.ScopeId),
            Attr("effectiveFrom", s.EffectiveFrom), Attr("startType", s.StartType));
        var lines = new XElement("lines");
        foreach (var l in s.Lines)
            lines.Add(new XElement("line", Attr("payHeadId", l.PayHeadId), Attr("order", l.Order),
                OptLong("amountPaisa", l.AmountPaisa)));
        el.Add(lines);
        return el;
    }

    private static XElement BuildAttendanceEntry(AttendanceEntryDto a) => new("attendanceEntry",
        Attr("id", a.Id), Attr("employeeId", a.EmployeeId), Attr("attendanceTypeId", a.AttendanceTypeId),
        Attr("fromDate", a.FromDate), Attr("toDate", a.ToDate), Attr("valueMicro", a.ValueMicro));

    private static XElement BuildTaxDeclaration(TaxDeclarationDto d) => new("taxDeclaration",
        Attr("employeeId", d.EmployeeId),
        Attr("section80CPaisa", d.Section80CPaisa), Attr("section80DPaisa", d.Section80DPaisa),
        Attr("section80CCD1BPaisa", d.Section80CCD1BPaisa), Attr("section80CCD2EmployerPaisa", d.Section80CCD2EmployerPaisa),
        Attr("hraExemptPaisa", d.HraExemptPaisa), Attr("homeLoanInterestPaisa", d.HomeLoanInterestPaisa),
        Attr("otherIncomePaisa", d.OtherIncomePaisa), Attr("prevEmployerSalaryPaisa", d.PrevEmployerSalaryPaisa),
        Attr("prevEmployerTdsPaisa", d.PrevEmployerTdsPaisa));

    private static XElement BuildCurrency(CurrencyDto x) => new("currency",
        Attr("id", x.Id), Attr("symbol", x.Symbol), Attr("formalName", x.FormalName),
        Attr("decimalPlaces", x.DecimalPlaces), Attr("isBaseCurrency", x.IsBaseCurrency));

    private static XElement BuildExchangeRate(ExchangeRateDto x) => new("exchangeRate",
        Attr("id", x.Id), Attr("currencyId", x.CurrencyId), Attr("date", x.Date),
        Attr("standardRateMicro", x.StandardRateMicro),
        OptLong("sellingRateMicro", x.SellingRateMicro), OptLong("buyingRateMicro", x.BuyingRateMicro));

    private static XElement BuildBudget(BudgetDto x)
    {
        var el = new XElement("budget",
            Attr("id", x.Id), Attr("name", x.Name), OptId("underId", x.UnderId),
            Attr("periodFrom", x.PeriodFrom), Attr("periodTo", x.PeriodTo));
        var lines = new XElement("lines");
        foreach (var l in x.Lines)
            lines.Add(new XElement("line", OptId("groupId", l.GroupId), OptId("ledgerId", l.LedgerId),
                Attr("budgetType", l.BudgetType), Attr("amountPaisa", l.AmountPaisa)));
        el.Add(lines);
        return el;
    }

    private static XElement BuildScenario(ScenarioDto x)
    {
        var el = new XElement("scenario",
            Attr("id", x.Id), Attr("name", x.Name), Attr("includeActuals", x.IncludeActuals));
        var inc = new XElement("includedTypeIds");
        foreach (var g in x.IncludedTypeIds) inc.Add(new XElement("typeId", Attr("id", g)));
        var exc = new XElement("excludedTypeIds");
        foreach (var g in x.ExcludedTypeIds) exc.Add(new XElement("typeId", Attr("id", g)));
        el.Add(inc, exc);
        return el;
    }

    private static XElement BuildVoucherType(VoucherTypeDto t)
    {
        var el = new XElement("voucherType",
            Attr("id", t.Id), Attr("name", t.Name), Attr("baseType", t.BaseType), Attr("numbering", t.Numbering),
            Opt("defaultShortcut", t.DefaultShortcut), Opt("abbreviation", t.Abbreviation),
            Attr("isActive", t.IsActive), Attr("isPredefined", t.IsPredefined),
            Attr("affectsAccounts", t.AffectsAccounts), Attr("affectsStock", t.AffectsStock),
            Attr("useAsManufacturingJournal", t.UseAsManufacturingJournal),
            Attr("trackAdditionalCosts", t.TrackAdditionalCosts),
            Attr("allowZeroValuedTransactions", t.AllowZeroValuedTransactions),
            Attr("useForPos", t.UseForPos),
            Attr("useForJobWork", t.UseForJobWork),
            Attr("allowConsumption", t.AllowConsumption),
            Attr("isStatPayment", t.IsStatPayment),
            Attr("isRcmPaymentVoucher", t.IsRcmPaymentVoucher),
            Attr("isGstStatAdjustment", t.IsGstStatAdjustment));
        if (t.PosConfig is { } pc) el.Add(BuildPosConfig(pc));
        return el;
    }

    private static XElement BuildPosConfig(PosConfigDto c)
    {
        var el = new XElement("posConfig",
            OptId("defaultGodownId", c.DefaultGodownId), OptId("defaultPartyId", c.DefaultPartyId),
            Attr("printAfterSave", c.PrintAfterSave), Opt("defaultTitle", c.DefaultTitle),
            Opt("message1", c.Message1), Opt("message2", c.Message2), Opt("declaration", c.Declaration));
        var defaults = new XElement("tenderLedgerDefaults");
        foreach (var d in c.TenderLedgerDefaults)
            defaults.Add(new XElement("tenderLedgerDefault", Attr("tenderType", d.TenderType), Attr("ledgerId", d.LedgerId)));
        el.Add(defaults);
        return el;
    }

    private static XElement BuildUnit(UnitDto u) => new("unit",
        Attr("id", u.Id), Attr("symbol", u.Symbol), Attr("formalName", u.FormalName),
        Attr("isCompound", u.IsCompound), Opt("unitQuantityCode", u.UnitQuantityCode),
        Attr("decimalPlaces", u.DecimalPlaces), OptId("firstUnitId", u.FirstUnitId), OptId("tailUnitId", u.TailUnitId),
        OptInt("conversionNumerator", u.ConversionNumerator), OptInt("conversionDenominator", u.ConversionDenominator));

    private static XElement BuildStockGroup(StockGroupDto g) => new("stockGroup",
        Attr("id", g.Id), Attr("name", g.Name), OptId("parentId", g.ParentId),
        Opt("alias", g.Alias), Attr("addQuantities", g.AddQuantities));

    private static XElement BuildStockCategory(StockCategoryDto g) => new("stockCategory",
        Attr("id", g.Id), Attr("name", g.Name), OptId("parentId", g.ParentId), Opt("alias", g.Alias));

    private static XElement BuildGodown(GodownDto g) => new("godown",
        Attr("id", g.Id), Attr("name", g.Name), OptId("parentId", g.ParentId), Opt("alias", g.Alias),
        Attr("thirdParty", g.ThirdParty), Attr("isMainLocation", g.IsMainLocation));

    private static XElement BuildStockItem(StockItemDto i)
    {
        var el = new XElement("stockItem",
            Attr("id", i.Id), Attr("name", i.Name), Attr("stockGroupId", i.StockGroupId),
            Attr("baseUnitId", i.BaseUnitId), OptId("categoryId", i.CategoryId), Opt("alias", i.Alias),
            Attr("valuationMethod", i.ValuationMethod), Opt("hsnSacCode", i.HsnSacCode),
            Attr("isTaxable", i.IsTaxable), OptLong("standardCostPaisa", i.StandardCostPaisa),
            OptDec("reorderLevel", i.ReorderLevel), OptDec("minimumOrderQuantity", i.MinimumOrderQuantity),
            Attr("maintainInBatches", i.MaintainInBatches),
            Attr("trackManufacturingDate", i.TrackManufacturingDate),
            Attr("useExpiryDates", i.UseExpiryDates),
            Attr("setComponents", i.SetComponents),
            OptId("tcsNatureOfGoodsId", i.TcsNatureOfGoodsId)); // Phase 7 slice 1
        if (i.Gst is { } g) el.Add(BuildStockItemGst("gst", g));
        return el;
    }

    private static XElement BuildBatchMaster(BatchMasterDto b) => new("batchMaster",
        Attr("id", b.Id), Attr("stockItemId", b.StockItemId), Attr("batchNumber", b.BatchNumber),
        Opt("manufacturingDate", b.ManufacturingDate), Opt("expiryDate", b.ExpiryDate),
        Opt("expiryPeriod", b.ExpiryPeriod), OptId("godownId", b.GodownId),
        OptDec("inwardQuantity", b.InwardQuantity), OptLong("inwardRatePaisa", b.InwardRatePaisa));

    private static XElement BuildBom(BillOfMaterialsDto b)
    {
        var el = new XElement("billOfMaterials",
            Attr("id", b.Id), Attr("stockItemId", b.StockItemId), Attr("name", b.Name),
            Attr("unitOfManufacture", Dec(b.UnitOfManufacture)));
        var lines = new XElement("lines");
        foreach (var l in b.Lines)
            lines.Add(new XElement("line",
                Attr("lineType", l.LineType), Attr("componentStockItemId", l.ComponentStockItemId),
                OptId("godownId", l.GodownId), Attr("quantityPerBlock", Dec(l.QuantityPerBlock)),
                OptLong("ratePaisa", l.RatePaisa), OptDec("percentOfFinishedGoodCost", l.PercentOfFinishedGoodCost)));
        el.Add(lines);
        return el;
    }

    private static XElement BuildStockOpeningBalance(StockOpeningBalanceDto b) => new("stockOpeningBalance",
        Attr("id", b.Id), Attr("stockItemId", b.StockItemId), Attr("godownId", b.GodownId),
        Attr("quantity", Dec(b.Quantity)), Attr("ratePaisa", b.RatePaisa), Opt("batchLabel", b.BatchLabel),
        Opt("manufacturingDate", b.ManufacturingDate), Opt("expiryDate", b.ExpiryDate));

    private static XElement BuildGstConfig(GstConfigDto g)
    {
        var el = new XElement("gst",
            Attr("enabled", g.Enabled), Opt("gstin", g.Gstin), Opt("homeStateCode", g.HomeStateCode),
            Attr("registrationType", g.RegistrationType), Opt("applicableFrom", g.ApplicableFrom),
            Attr("periodicity", g.Periodicity),
            // Phase 9 slice 3: composition sub-type + opt-in date (null-omitting ⇒ byte-identical when off, ER-13).
            Opt("compositionSubType", g.CompositionSubType), Opt("compositionOptInDate", g.CompositionOptInDate),
            // Phase 9 slice 4a: NON-SECRET e-invoice / B2C-QR / connector-mode config. No NIC credential attr exists here
            // (ER-16). Booleans/mode/threshold always emitted (defaults ⇒ byte-identical when off, ER-13).
            Attr("eInvoicingEnabled", g.EInvoicingEnabled), Opt("eInvoiceApplicableFrom", g.EInvoiceApplicableFrom),
            Attr("eInvoiceAatoThresholdPaisa", g.EInvoiceAatoThresholdPaisa),
            Attr("eInvoiceApplicabilityOverride", g.EInvoiceApplicabilityOverride),
            Attr("eInvoiceExemptionClasses", g.EInvoiceExemptionClasses),
            Attr("eInvoiceReportingAgeApplies", g.EInvoiceReportingAgeApplies),
            Attr("connectorMode", g.ConnectorMode),
            Attr("b2cDynamicQrEnabled", g.B2cDynamicQrEnabled),
            Attr("b2cQrAatoThresholdPaisa", g.B2cQrAatoThresholdPaisa),
            Opt("b2cQrUpiId", g.B2cQrUpiId), Opt("b2cQrPayeeName", g.B2cQrPayeeName),
            // Phase 9 slice 5: NON-SECRET e-Way Bill config (defaults ⇒ byte-identical when off, ER-13). No NIC
            // credential attr exists here (ER-16). Booleans/threshold/basis always emitted.
            Attr("eWayBillEnabled", g.EWayBillEnabled), Opt("eWayApplicableFrom", g.EWayApplicableFrom),
            Attr("eWayThresholdPaisa", g.EWayThresholdPaisa), Attr("consignmentBasis", g.ConsignmentBasis),
            Attr("eWayIntraStateApplicable", g.EWayIntraStateApplicable),
            // Phase 9 slice 6: the GSTR-2B reconciliation tolerance (always emitted; defaults 0/0 ⇒ byte-stable, finding #5).
            Attr("reconValueTolerancePaisa", g.ReconValueTolerancePaisa),
            Attr("reconDateWindowDays", g.ReconDateWindowDays));
        var slabs = new XElement("rateSlabs");
        foreach (var s in g.RateSlabs)
            slabs.Add(new XElement("rateSlab", Attr("id", s.Id), Attr("rateBasisPoints", s.RateBasisPoints),
                Attr("label", s.Label), Attr("isPredefined", s.IsPredefined)));
        el.Add(slabs);

        // Phase 9 slice 1: dated rate-history + Compensation-Cess windows (empty elements when advanced GST is off).
        var history = new XElement("rateHistory");
        foreach (var h in g.RateHistory)
            history.Add(new XElement("entry",
                Attr("id", h.Id), Opt("hsnSac", h.HsnSac), Attr("rateBasisPoints", h.RateBasisPoints),
                Attr("rateClass", h.RateClass), Attr("effectiveFrom", h.EffectiveFrom), Opt("effectiveTo", h.EffectiveTo),
                Attr("valuationBasis", h.ValuationBasis), Attr("label", h.Label), Attr("isPredefined", h.IsPredefined)));
        el.Add(history);

        var cess = new XElement("cessRates");
        foreach (var c in g.CessRates)
            cess.Add(new XElement("cess",
                Attr("id", c.Id), Opt("hsnSac", c.HsnSac), Attr("valuationMode", c.ValuationMode),
                Attr("cessRateBasisPoints", c.CessRateBasisPoints), Attr("cessPerUnitPaisa", c.CessPerUnitPaisa),
                Attr("cessRspFactorMillis", c.CessRspFactorMillis), Attr("effectiveFrom", c.EffectiveFrom),
                Opt("effectiveTo", c.EffectiveTo), Attr("label", c.Label), Attr("isPredefined", c.IsPredefined)));
        el.Add(cess);

        // Phase 9 slice 2: dated reverse-charge categories (empty element when RCM is off).
        var rcm = new XElement("rcmCategories");
        foreach (var c in g.RcmCategories)
            rcm.Add(new XElement("rcmCategory",
                Attr("id", c.Id), Attr("notification", c.Notification), Attr("stream", c.Stream),
                Attr("supplyNature", c.SupplyNature), Attr("supplyType", c.SupplyType), Opt("hsnSac", c.HsnSac),
                Attr("rateBasisPoints", c.RateBasisPoints), Attr("supplierQualifier", c.SupplierQualifier),
                Attr("recipientQualifier", c.RecipientQualifier), Attr("effectiveFrom", c.EffectiveFrom),
                Opt("effectiveTo", c.EffectiveTo), Attr("label", c.Label), Attr("isPredefined", c.IsPredefined)));
        el.Add(rcm);

        // Phase 9 slice 5: per-state e-Way threshold overrides (empty element when on the flat ₹50,000 default).
        var ewayThresholds = new XElement("ewayStateThresholds");
        foreach (var t in g.EWayStateThresholds)
            ewayThresholds.Add(new XElement("ewayStateThreshold",
                Attr("id", t.Id), Attr("stateCode", t.StateCode), Attr("txnType", t.TxnType),
                Attr("thresholdPaisa", t.ThresholdPaisa)));
        el.Add(ewayThresholds);
        return el;
    }

    private static XElement BuildPfConfig(PfConfigDto pf) => new("pf",
        Attr("epfRateBasisPoints", pf.EpfRateBasisPoints), Opt("establishmentCode", pf.EstablishmentCode),
        Attr("capWagesAtCeiling", pf.CapWagesAtCeiling));

    private static XElement BuildEsiConfig(EsiConfigDto esi) => new("esi",
        Attr("employeeRateBasisPoints", esi.EmployeeRateBasisPoints),
        Attr("employerRateBasisPoints", esi.EmployerRateBasisPoints), Opt("employerCode", esi.EmployerCode));

    private static XElement BuildPtConfig(PtConfigDto pt)
    {
        var el = new XElement("pt",
            Opt("stateCode", pt.StateCode), Opt("registrationNumber", pt.RegistrationNumber),
            Attr("wageBasis", pt.WageBasis));
        var tables = new XElement("slabTables");
        foreach (var s in pt.SlabTables)
        {
            var table = new XElement("slab",
                Attr("id", s.Id), Attr("stateCode", s.StateCode), Attr("genderScope", s.GenderScope));
            var bands = new XElement("bands");
            foreach (var b in s.Bands)
            {
                var band = new XElement("band",
                    Attr("fromWagePaisa", b.FromWagePaisa), OptLong("toWagePaisa", b.ToWagePaisa),
                    Attr("monthlyAmountPaisa", b.MonthlyAmountPaisa));
                var overrides = new XElement("monthOverrides");
                foreach (var o in b.MonthOverrides)
                    overrides.Add(new XElement("override", Attr("month", o.Month), Attr("amountPaisa", o.AmountPaisa)));
                band.Add(overrides);
                bands.Add(band);
            }
            table.Add(bands);
            tables.Add(table);
        }
        el.Add(tables);
        return el;
    }

    private static XElement BuildGratuityConfig(GratuityConfigDto gr) => new("gratuity",
        Attr("capPaisa", gr.CapPaisa), Attr("wageBasis", gr.WageBasis), Attr("population", gr.Population));

    private static XElement BuildBonusConfig(BonusConfigDto bo) => new("bonus",
        Attr("rateBasisPoints", bo.RateBasisPoints), Attr("calculationCeilingPaisa", bo.CalculationCeilingPaisa),
        Attr("minimumWagePaisa", bo.MinimumWagePaisa), Attr("prorate", bo.Prorate));

    /// <summary>The v45 party Mailing Details element. Every attribute is optional (<c>Opt</c> omits a null), and
    /// the element itself is only added when the block exists — so nothing is emitted for a ledger without mailing
    /// details. No <c>state</c> attribute: the party State rides on <c>partyGst/@stateCode</c>, the single stored
    /// State that drives GST place of supply.</summary>
    private static XElement BuildPartyMailing(PartyMailingDto m) => new("mailing",
        Opt("mailingName", m.MailingName), Opt("address", m.Address),
        Opt("country", m.Country), Opt("pincode", m.Pincode));

    private static XElement BuildPartyGst(PartyGstDto p) => new("partyGst",
        Attr("registrationType", p.RegistrationType), Opt("gstin", p.Gstin), Opt("stateCode", p.StateCode),
        // Phase 9 slice 2: reverse-charge qualifiers (emitted only when true so an off party is byte-identical, ER-13).
        OptTrue("isPromoter", p.IsPromoter), OptTrue("isBodyCorporate", p.IsBodyCorporate));

    private static XElement BuildStockItemGst(string name, StockItemGstDto s) => new(name,
        Opt("hsnSac", s.HsnSac), Attr("taxability", s.Taxability),
        OptInt("rateBasisPoints", s.RateBasisPoints), Attr("supplyType", s.SupplyType),
        // Phase 9 slice 1: RSP valuation + cess (required valuationBasis/cessApplicable always emitted; cess scalars
        // omitted via Opt* when null → an off item's bytes are identical to a Phase-8 item, ER-13).
        Attr("valuationBasis", s.ValuationBasis), Attr("cessApplicable", s.CessApplicable),
        Opt("cessValuationMode", s.CessValuationMode), OptInt("cessRateBasisPoints", s.CessRateBasisPoints),
        OptLong("cessPerUnitPaisa", s.CessPerUnitPaisa), OptInt("cessRspFactorMillis", s.CessRspFactorMillis),
        OptLong("rspPaisa", s.RspPaisa),
        // Phase 9 slice 2: reverse-charge flags (emitted only when set so an off item is byte-identical, ER-13).
        OptTrue("reverseChargeApplicable", s.ReverseChargeApplicable), OptTrue("gtaForwardCharge", s.GtaForwardCharge),
        OptId("rcmCategoryId", s.RcmCategoryId),
        // Phase 9 slice 6b: §17(5) ITC-eligibility (required attrs always emitted, mirroring valuationBasis/cessApplicable;
        // an off item is Eligible/None ⇒ round-trip-stable + byte-identical, ER-13).
        Attr("itcEligibility", s.ItcEligibility), Attr("blockedCreditCategory", s.BlockedCreditCategory));

    private static XElement BuildGstClassification(LedgerGstClassificationDto c) => new("gstClassification",
        Attr("taxHead", c.TaxHead), Attr("direction", c.Direction),
        // Phase 9 slice 2: the RCM Output discriminator (emitted only when true so an ordinary ledger is byte-identical).
        OptTrue("isReverseCharge", c.IsReverseCharge));

    private static XElement BuildTdsConfig(TdsConfigDto t)
    {
        var el = new XElement("tds",
            Attr("enabled", t.Enabled), Opt("tan", t.Tan), Attr("deductorType", t.DeductorType),
            Opt("responsiblePersonName", t.ResponsiblePersonName), Opt("responsiblePersonPan", t.ResponsiblePersonPan),
            Opt("responsiblePersonDesignation", t.ResponsiblePersonDesignation),
            Opt("responsiblePersonAddress", t.ResponsiblePersonAddress),
            Attr("surchargeApplicable", t.SurchargeApplicable), Attr("cessApplicable", t.CessApplicable),
            Attr("periodicity", t.Periodicity), Opt("applicableFrom", t.ApplicableFrom));
        var natures = new XElement("naturesOfPayment");
        foreach (var n in t.NaturesOfPayment) natures.Add(BuildNatureOfPayment(n));
        el.Add(natures);
        return el;
    }

    private static XElement BuildNatureOfPayment(NatureOfPaymentDto n) => new("natureOfPayment",
        Attr("id", n.Id), Attr("sectionCode", n.SectionCode), Attr("name", n.Name),
        Attr("rateWithPanBp", n.RateWithPanBp), Attr("rateWithoutPanBp", n.RateWithoutPanBp),
        OptLong("singleThresholdPaisa", n.SingleThresholdPaisa), OptLong("cumulativeThresholdPaisa", n.CumulativeThresholdPaisa),
        Attr("fvuSectionCode", n.FvuSectionCode), Opt("effectiveFrom", n.EffectiveFrom), Attr("isPredefined", n.IsPredefined));

    private static XElement BuildTcsConfig(TcsConfigDto t)
    {
        var el = new XElement("tcs",
            Attr("enabled", t.Enabled), Opt("tan", t.Tan), Attr("collectorType", t.CollectorType),
            Opt("responsiblePersonName", t.ResponsiblePersonName), Opt("responsiblePersonPan", t.ResponsiblePersonPan),
            Opt("responsiblePersonDesignation", t.ResponsiblePersonDesignation),
            Opt("responsiblePersonAddress", t.ResponsiblePersonAddress),
            Attr("surchargeApplicable", t.SurchargeApplicable), Attr("cessApplicable", t.CessApplicable),
            Attr("periodicity", t.Periodicity), Opt("applicableFrom", t.ApplicableFrom));
        var natures = new XElement("naturesOfGoods");
        foreach (var n in t.NaturesOfGoods) natures.Add(BuildNatureOfGoods(n));
        el.Add(natures);
        return el;
    }

    private static XElement BuildNatureOfGoods(NatureOfGoodsDto n) => new("natureOfGoods",
        Attr("id", n.Id), Attr("collectionCode", n.CollectionCode), Attr("name", n.Name),
        Attr("rateWithPanBp", n.RateWithPanBp), Attr("rateWithoutPanBp", n.RateWithoutPanBp),
        OptLong("thresholdPaisa", n.ThresholdPaisa), Attr("baseIncludesGst", n.BaseIncludesGst),
        Attr("fvuCode", n.FvuCode), Opt("effectiveFrom", n.EffectiveFrom), Attr("isPredefined", n.IsPredefined),
        Attr("isLegacy", n.IsLegacy), Opt("legacyCutoff", n.LegacyCutoff));

    private static XElement BuildVoucher(VoucherDto v)
    {
        var el = new XElement("voucher",
            Attr("id", v.Id), Attr("typeId", v.TypeId), Attr("number", v.Number), Attr("date", v.Date),
            Opt("narration", v.Narration), OptId("partyId", v.PartyId),
            Attr("cancelled", v.Cancelled), Attr("optional", v.Optional), Attr("postDated", v.PostDated),
            Opt("applicableUpto", v.ApplicableUpto));
        var lines = new XElement("lines");
        foreach (var l in v.Lines) lines.Add(BuildEntryLine(l));
        el.Add(lines);
        var inv = new XElement("inventoryLines");
        foreach (var il in v.InventoryLines) inv.Add(BuildVoucherInventoryLine(il));
        el.Add(inv);
        var tenders = new XElement("posTenders");
        foreach (var t in v.PosTenders) tenders.Add(BuildPosTender(t));
        el.Add(tenders);
        return el;
    }

    private static XElement BuildPosTender(PosTenderDto t) => new("posTender",
        Attr("tenderType", t.TenderType), Attr("ledgerId", t.LedgerId), Attr("amountPaisa", t.AmountPaisa),
        OptLong("tenderedPaisa", t.TenderedPaisa), OptLong("changePaisa", t.ChangePaisa),
        Opt("cardNo", t.CardNo), Opt("bankName", t.BankName), Opt("chequeNo", t.ChequeNo));

    private static XElement BuildEntryLine(EntryLineDto l)
    {
        var el = new XElement("line",
            Attr("ledgerId", l.LedgerId), Attr("amountPaisa", l.AmountPaisa), Attr("side", l.Side));
        var bills = new XElement("billAllocations");
        foreach (var b in l.BillAllocations)
            bills.Add(new XElement("billAllocation", Attr("refType", b.RefType), Attr("name", b.Name),
                Attr("amountPaisa", b.AmountPaisa), Opt("dueDate", b.DueDate), OptInt("creditPeriodDays", b.CreditPeriodDays)));
        el.Add(bills);
        var costs = new XElement("costAllocations");
        foreach (var a in l.CostAllocations)
            costs.Add(new XElement("costAllocation", Attr("categoryId", a.CategoryId), Attr("centreId", a.CentreId),
                Attr("amountPaisa", a.AmountPaisa)));
        el.Add(costs);
        if (l.BankAllocation is { } ba)
            el.Add(new XElement("bankAllocation", Attr("transactionType", ba.TransactionType),
                Attr("instrumentNumber", ba.InstrumentNumber), Opt("instrumentDate", ba.InstrumentDate),
                Opt("bankDate", ba.BankDate)));
        if (l.Forex is { } fx)
            el.Add(new XElement("forex", Attr("currencyId", fx.CurrencyId),
                Attr("forexAmountPaisa", fx.ForexAmountPaisa), Attr("rateMicro", fx.RateMicro)));
        if (l.Gst is { } g)
            el.Add(new XElement("gst", Attr("taxHead", g.TaxHead), Attr("rateBasisPoints", g.RateBasisPoints),
                Attr("taxableValuePaisa", g.TaxableValuePaisa),
                // Phase 9 slice 2: reverse-charge tag (emitted only when set so a forward-charge line is byte-identical).
                // Phase 9 slice 7: the set-off / reversal adjustment tag (emitted only when set).
                OptTrue("isReverseCharge", g.IsReverseCharge), Opt("rcmScheme", g.RcmScheme),
                Opt("adjustment", g.Adjustment)));
        if (l.Tds is { } t)
            el.Add(new XElement("tds", Attr("natureId", t.NatureId), Attr("sectionCode", t.SectionCode),
                Attr("assessableValuePaisa", t.AssessableValuePaisa), Attr("rateBasisPoints", t.RateBasisPoints),
                Attr("tdsAmountPaisa", t.TdsAmountPaisa), Attr("deducteeLedgerId", t.DeducteeLedgerId),
                Attr("panApplied", t.PanApplied)));
        if (l.Tcs is { } tc)
            el.Add(new XElement("tcs", Attr("natureId", tc.NatureId), Attr("collectionCode", tc.CollectionCode),
                Attr("assessableValuePaisa", tc.AssessableValuePaisa), Attr("rateBasisPoints", tc.RateBasisPoints),
                Attr("tcsAmountPaisa", tc.TcsAmountPaisa), Attr("collecteeLedgerId", tc.CollecteeLedgerId),
                Attr("panApplied", tc.PanApplied)));
        if (l.Payroll is { } pr)
            el.Add(new XElement("payroll", Attr("employeeId", pr.EmployeeId), OptId("payHeadId", pr.PayHeadId),
                Attr("category", pr.Category), Attr("amountPaisa", pr.AmountPaisa)));
        return el;
    }

    private static XElement BuildVoucherInventoryLine(VoucherInventoryLineDto l) => new("inventoryLine",
        Attr("stockItemId", l.StockItemId), Attr("godownId", l.GodownId), Attr("quantity", Dec(l.Quantity)),
        Attr("ratePaisa", l.RatePaisa), Attr("direction", l.Direction), Opt("batchLabel", l.BatchLabel),
        OptDec("billedQuantity", l.BilledQuantity),
        // WI-10 Gap 2: the line unit. OptId emits nothing when null, so a base-unit line's element is
        // byte-identical to a pre-v46 export (ER-13).
        OptId("unitId", l.UnitId));

    private static XElement BuildInventoryVoucher(InventoryVoucherDto v)
    {
        var el = new XElement("inventoryVoucher",
            Attr("id", v.Id), Attr("typeId", v.TypeId), Attr("number", v.Number), Attr("date", v.Date),
            Opt("narration", v.Narration), OptId("partyId", v.PartyId),
            Attr("cancelled", v.Cancelled), Attr("postDated", v.PostDated));
        el.Add(List("allocations", "allocation", v.Allocations, BuildInventoryAllocation));
        el.Add(List("destinationAllocations", "allocation", v.DestinationAllocations, BuildInventoryAllocation));
        el.Add(List("orderLines", "orderLine", v.OrderLines, BuildOrderLine));
        el.Add(List("physicalLines", "physicalLine", v.PhysicalLines, BuildPhysicalStockLine));
        el.Add(List("additionalCostLines", "additionalCostLine", v.AdditionalCostLines, BuildAdditionalCostLine));
        if (v.JobWorkOrder is { } jwo) el.Add(BuildJobWorkOrder(jwo));
        var links = new XElement("orderLinks");
        foreach (var id in v.OrderLinks) links.Add(new XElement("orderLink", Attr("id", id)));
        el.Add(links);
        return el;
    }

    private static XElement BuildAdditionalCostLine(AdditionalCostLineDto a) => new("additionalCostLine",
        Attr("ledgerId", a.LedgerId), Attr("amountPaisa", a.AmountPaisa));

    private static XElement BuildJobWorkOrder(JobWorkOrderDto j)
    {
        var el = new XElement("jobWorkOrder",
            Attr("direction", j.Direction), Attr("orderNo", j.OrderNo),
            Opt("durationOfProcess", j.DurationOfProcess), Opt("natureOfProcessing", j.NatureOfProcessing),
            Attr("finishedGoodStockItemId", j.FinishedGoodStockItemId),
            Attr("finishedGoodQuantity", Dec(j.FinishedGoodQuantity)),
            Opt("finishedGoodDueDate", j.FinishedGoodDueDate), OptId("finishedGoodGodownId", j.FinishedGoodGodownId),
            OptLong("finishedGoodRatePaisa", j.FinishedGoodRatePaisa),
            Attr("trackingComponents", j.TrackingComponents), OptId("fillComponentsBomId", j.FillComponentsBomId));
        var lines = new XElement("lines");
        foreach (var l in j.Lines)
            lines.Add(new XElement("line",
                Attr("componentStockItemId", l.ComponentStockItemId), Attr("track", l.Track),
                Opt("dueDate", l.DueDate), OptId("godownId", l.GodownId),
                Attr("quantity", Dec(l.Quantity)), OptLong("ratePaisa", l.RatePaisa)));
        el.Add(lines);
        return el;
    }

    private static XElement BuildInventoryAllocation(InventoryAllocationDto a) => new("allocation",
        Attr("stockItemId", a.StockItemId), Attr("godownId", a.GodownId), Attr("quantity", Dec(a.Quantity)),
        Attr("direction", a.Direction), OptLong("ratePaisa", a.RatePaisa), Opt("batchLabel", a.BatchLabel),
        OptId("unitId", a.UnitId));

    private static XElement BuildOrderLine(OrderLineDto l) => new("orderLine",
        Attr("stockItemId", l.StockItemId), Attr("godownId", l.GodownId), Attr("quantity", Dec(l.Quantity)),
        OptLong("ratePaisa", l.RatePaisa));

    private static XElement BuildPhysicalStockLine(PhysicalStockLineDto l) => new("physicalLine",
        Attr("stockItemId", l.StockItemId), Attr("godownId", l.GodownId),
        Attr("countedQuantity", Dec(l.CountedQuantity)), Opt("batchLabel", l.BatchLabel));

    // ------------------------------------------------------------- attribute helpers

    private static XAttribute Attr(string name, string value) => new(name, value);
    private static XAttribute Attr(string name, Guid value) => new(name, value.ToString("D"));
    private static XAttribute Attr(string name, long value) => new(name, value.ToString(CultureInfo.InvariantCulture));
    private static XAttribute Attr(string name, int value) => new(name, value.ToString(CultureInfo.InvariantCulture));
    private static XAttribute Attr(string name, bool value) => new(name, value ? "true" : "false");

    private static XAttribute? Opt(string name, string? value) => value is null ? null : new XAttribute(name, value);
    private static XAttribute? OptId(string name, Guid? value) => value is { } v ? new XAttribute(name, v.ToString("D")) : null;
    private static XAttribute? OptInt(string name, int? value) => value is { } v ? new XAttribute(name, v.ToString(CultureInfo.InvariantCulture)) : null;
    private static XAttribute? OptLong(string name, long? value) => value is { } v ? new XAttribute(name, v.ToString(CultureInfo.InvariantCulture)) : null;
    private static XAttribute? OptBool(string name, bool? value) => value is { } v ? new XAttribute(name, v ? "true" : "false") : null;
    // Emit a boolean attribute ONLY when true, so a default-false flag is absent (byte-identical to a prior export).
    private static XAttribute? OptTrue(string name, bool value) => value ? new XAttribute(name, "true") : null;
    private static XAttribute? OptDec(string name, decimal? value) => value is { } v ? new XAttribute(name, Dec(v)) : null;

    private static string Dec(decimal d) => d.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------- parse (XML -> model)

    /// <summary>
    /// Parses canonical XML bytes back to a <see cref="CanonicalModel"/>. On any structural problem it returns
    /// <c>(null, [messages])</c>; on success <c>(model, [])</c>. Never throws on bad input.
    /// </summary>
    public static (CanonicalModel? Model, IReadOnlyList<string> Errors) Parse(byte[] bytes)
    {
        var errors = new List<string>();
        if (bytes is null || bytes.Length == 0)
        {
            errors.Add("Import document is empty.");
            return (null, errors);
        }

        XDocument doc;
        try
        {
            // XXE-safe load: no DTD (DtdProcessing.Prohibit throws on a DOCTYPE), no external entity resolution
            // (XmlResolver = null). A document carrying a DOCTYPE / internal or external entity is rejected as a
            // structured error below — it is never expanded, and no exception escapes to the caller (RQ-21).
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                CloseInput = true,
            };
            using var ms = new MemoryStream(bytes);
            using var reader = XmlReader.Create(ms, settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            errors.Add($"Malformed XML: {ex.Message}");
            return (null, errors);
        }
        catch (InvalidOperationException ex)
        {
            // Defensive: any reader-configuration/entity edge case surfaces as a structured error, never a throw.
            errors.Add($"Malformed XML: {ex.Message}");
            return (null, errors);
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != Root)
        {
            errors.Add($"XML root element must be <{Root}>.");
            return (null, errors);
        }

        CanonicalModel? model;
        try
        {
            model = ReadModel(root, errors);
        }
        catch (Exception ex)
        {
            errors.Add($"XML parse error: {ex.Message}");
            return (null, errors);
        }

        if (model is null) return (null, errors);

        CanonicalValidation.Validate(model, errors);
        return errors.Count == 0 ? (model, errors) : (null, errors);
    }

    private static CanonicalModel? ReadModel(XElement root, List<string> errors)
    {
        if (!TryInt(root.Attribute("formatVersion")?.Value, out var fv))
        { errors.Add("Root @formatVersion is missing or not an integer."); return null; }
        if (!TryInt(root.Attribute("schemaVersion")?.Value, out var sv))
        { errors.Add("Root @schemaVersion is missing or not an integer."); return null; }

        var companyEl = root.Element("company");
        if (companyEl is null) { errors.Add("Missing <company> element."); return null; }

        var company = ReadCompany(companyEl, errors);
        var payload = ReadPayload(root, errors);
        if (company is null || payload is null) return null;

        return new CanonicalModel { FormatVersion = fv, SchemaVersion = sv, Company = company, Payload = payload };
    }

    private static CompanyDto? ReadCompany(XElement e, List<string> errors)
    {
        if (!TryGuid(e.Attribute("id")?.Value, out var id)) { errors.Add("company @id is missing or not a GUID."); return null; }
        var gstEl = e.Element("gst");
        var tdsEl = e.Element("tds");
        var tcsEl = e.Element("tcs");
        var pfEl = e.Element("pf");
        var esiEl = e.Element("esi");
        var ptEl = e.Element("pt");
        var gratuityEl = e.Element("gratuity");
        var bonusEl = e.Element("bonus");
        return new CompanyDto
        {
            Id = id,
            Name = Str(e, "name") ?? string.Empty,
            MailingName = Str(e, "mailingName"),
            Address = Str(e, "address"),
            Country = Str(e, "country"),
            State = Str(e, "state"),
            Pin = Str(e, "pin"),
            FinancialYearStart = Str(e, "financialYearStart") ?? string.Empty,
            BooksBeginFrom = Str(e, "booksBeginFrom") ?? string.Empty,
            BaseCurrencySymbol = Str(e, "baseCurrencySymbol"),
            BaseCurrencyName = Str(e, "baseCurrencyName"),
            DecimalPlaces = Int(e, "decimalPlaces"),
            DecimalUnitName = Str(e, "decimalUnitName"),
            Gst = gstEl is null ? null : ReadGstConfig(gstEl),
            Tds = tdsEl is null ? null : ReadTdsConfig(tdsEl),
            Tcs = tcsEl is null ? null : ReadTcsConfig(tcsEl),
            UseSeparateActualBilledQuantity = Bool(e, "useSeparateActualBilledQuantity"),
            EnableMultiplePriceLevels = Bool(e, "enableMultiplePriceLevels"),
            EnableJobOrderProcessing = Bool(e, "enableJobOrderProcessing"),
            PayrollEnabled = Bool(e, "payrollEnabled"),
            PayrollStatutoryEnabled = Bool(e, "payrollStatutoryEnabled"),
            SalaryTdsEnabled = Bool(e, "salaryTdsEnabled"),
            Pf = pfEl is null ? null : ReadPfConfig(pfEl),
            Esi = esiEl is null ? null : ReadEsiConfig(esiEl),
            Pt = ptEl is null ? null : ReadPtConfig(ptEl),
            Gratuity = gratuityEl is null ? null : ReadGratuityConfig(gratuityEl),
            Bonus = bonusEl is null ? null : ReadBonusConfig(bonusEl),
        };
    }

    private static GratuityConfigDto ReadGratuityConfig(XElement e) => new()
    {
        CapPaisa = Long(e, "capPaisa"),
        WageBasis = Str(e, "wageBasis") ?? nameof(Apex.Ledger.Domain.GratuityWageBasis.BasicAndDearnessAllowance),
        Population = Str(e, "population") ?? nameof(Apex.Ledger.Domain.GratuityProvisionPopulation.AllActiveEmployees),
    };

    private static BonusConfigDto ReadBonusConfig(XElement e) => new()
    {
        RateBasisPoints = Int(e, "rateBasisPoints"),
        CalculationCeilingPaisa = Long(e, "calculationCeilingPaisa"),
        MinimumWagePaisa = Long(e, "minimumWagePaisa"),
        Prorate = Bool(e, "prorate"),
    };

    private static PfConfigDto ReadPfConfig(XElement e) => new()
    {
        EpfRateBasisPoints = Int(e, "epfRateBasisPoints"),
        EstablishmentCode = Str(e, "establishmentCode"),
        CapWagesAtCeiling = Bool(e, "capWagesAtCeiling"),
    };

    private static EsiConfigDto ReadEsiConfig(XElement e) => new()
    {
        EmployeeRateBasisPoints = Int(e, "employeeRateBasisPoints"),
        EmployerRateBasisPoints = Int(e, "employerRateBasisPoints"),
        EmployerCode = Str(e, "employerCode"),
    };

    private static PtConfigDto ReadPtConfig(XElement e) => new()
    {
        StateCode = Str(e, "stateCode"),
        RegistrationNumber = Str(e, "registrationNumber"),
        WageBasis = Str(e, "wageBasis") ?? nameof(Apex.Ledger.Domain.PtWageBasis.GrossEarnings),
        SlabTables = (e.Element("slabTables")?.Elements("slab") ?? Enumerable.Empty<XElement>())
            .Select(s => new PtSlabDto
            {
                Id = Guid(s, "id"),
                StateCode = Str(s, "stateCode")!,
                GenderScope = Str(s, "genderScope") ?? nameof(Apex.Ledger.Domain.PtGenderScope.Any),
                Bands = (s.Element("bands")?.Elements("band") ?? Enumerable.Empty<XElement>())
                    .Select(b => new PtSlabBandDto
                    {
                        FromWagePaisa = Long(b, "fromWagePaisa"),
                        ToWagePaisa = OptLong(b, "toWagePaisa"),
                        MonthlyAmountPaisa = Long(b, "monthlyAmountPaisa"),
                        MonthOverrides = (b.Element("monthOverrides")?.Elements("override") ?? Enumerable.Empty<XElement>())
                            .Select(o => new PtMonthOverrideDto { Month = Int(o, "month"), AmountPaisa = Long(o, "amountPaisa") })
                            .ToList(),
                    }).ToList(),
            }).ToList(),
    };

    private static PayloadDto? ReadPayload(XElement root, List<string> errors)
    {
        try
        {
            return new PayloadDto
            {
                Groups = ReadList(root, "groups", "group", ReadGroup),
                Ledgers = ReadList(root, "ledgers", "ledger", ReadLedger),
                VoucherTypes = ReadList(root, "voucherTypes", "voucherType", ReadVoucherType),
                CostCategories = ReadList(root, "costCategories", "costCategory", ReadCostCategory),
                CostCentres = ReadList(root, "costCentres", "costCentre", ReadCostCentre),
                Currencies = ReadList(root, "currencies", "currency", ReadCurrency),
                ExchangeRates = ReadList(root, "exchangeRates", "exchangeRate", ReadExchangeRate),
                Budgets = ReadList(root, "budgets", "budget", ReadBudget),
                Scenarios = ReadList(root, "scenarios", "scenario", ReadScenario),
                Units = ReadList(root, "units", "unit", ReadUnit),
                StockGroups = ReadList(root, "stockGroups", "stockGroup", ReadStockGroup),
                StockCategories = ReadList(root, "stockCategories", "stockCategory", ReadStockCategory),
                Godowns = ReadList(root, "godowns", "godown", ReadGodown),
                StockItems = ReadList(root, "stockItems", "stockItem", ReadStockItem),
                BatchMasters = ReadList(root, "batchMasters", "batchMaster", ReadBatchMaster),
                BillsOfMaterials = ReadList(root, "billsOfMaterials", "billOfMaterials", ReadBom),
                StockOpeningBalances = ReadList(root, "stockOpeningBalances", "stockOpeningBalance", ReadStockOpeningBalance),
                PriceLevels = ReadList(root, "priceLevels", "priceLevel", ReadPriceLevel),
                PriceLists = ReadList(root, "priceLists", "priceList", ReadPriceList),
                ReorderDefinitions = ReadList(root, "reorderDefinitions", "reorderDefinition", ReadReorderDefinition),
                EmployeeCategories = ReadList(root, "employeeCategories", "employeeCategory", ReadEmployeeCategory),
                EmployeeGroups = ReadList(root, "employeeGroups", "employeeGroup", ReadEmployeeGroup),
                PayrollUnits = ReadList(root, "payrollUnits", "payrollUnit", ReadPayrollUnit),
                AttendanceTypes = ReadList(root, "attendanceTypes", "attendanceType", ReadAttendanceType),
                Employees = ReadList(root, "employees", "employee", ReadEmployee),
                PayHeads = ReadList(root, "payHeads", "payHead", ReadPayHead),
                SalaryStructures = ReadList(root, "salaryStructures", "salaryStructure", ReadSalaryStructure),
                AttendanceEntries = ReadList(root, "attendanceEntries", "attendanceEntry", ReadAttendanceEntry),
                TaxDeclarations = ReadList(root, "taxDeclarations", "taxDeclaration", ReadTaxDeclaration),
                Vouchers = ReadList(root, "vouchers", "voucher", ReadVoucher),
                InventoryVouchers = ReadList(root, "inventoryVouchers", "inventoryVoucher", ReadInventoryVoucher),
                TdsChallans = ReadList(root, "tdsChallans", "tdsChallan", ReadTdsChallan),
                ChallanVoucherLinks = ReadList(root, "challanVoucherLinks", "challanVoucherLink", ReadChallanVoucherLink),
                TcsChallans = ReadList(root, "tcsChallans", "tcsChallan", ReadTcsChallan),
                TcsChallanVoucherLinks = ReadList(root, "tcsChallanVoucherLinks", "challanVoucherLink", ReadChallanVoucherLink),
                // Phase 9 slice 2: RCM generated documents + §34-CDN links + GST-on-advance receipts.
                RcmDocuments = ReadList(root, "rcmDocuments", "rcmDocument", ReadRcmDocument),
                // Phase 9 slice 4a: e-invoice IRP artefacts (absent element ⇒ empty list, ER-13).
                EInvoiceRecords = ReadList(root, "eInvoiceRecords", "eInvoiceRecord", ReadEInvoiceRecord),
                // Phase 9 slice 5: e-Way Bill artefacts (absent element ⇒ empty list, ER-13).
                EWayBillRecords = ReadList(root, "eWayBillRecords", "eWayBillRecord", ReadEWayBillRecord),
                CreditDebitNoteLinks = ReadList(root, "creditDebitNoteLinks", "creditDebitNoteLink", ReadCdnLink),
                AdvanceReceipts = ReadList(root, "advanceReceipts", "advanceReceipt", ReadAdvanceReceipt),
                // Phase 9 slice 6: imported GSTR-2B/2A snapshots (owning their lines) + reconciliation results (absent
                // element ⇒ empty list, ER-13).
                Gstr2bSnapshots = ReadList(root, "gstr2bSnapshots", "gstr2bSnapshot", ReadGstr2bSnapshot),
                Gstr2bReconResults = ReadList(root, "gstr2bReconResults", "gstr2bReconResult", ReadGstr2bReconResult),
                ImsActions = ReadList(root, "imsActions", "imsAction", ReadImsAction),
                // Phase 9 slice 7: electronic-ledger records (absent element ⇒ empty list, ER-13).
                GstSetoffLines = ReadList(root, "gstSetoffLines", "gstSetoffLine", ReadGstSetoffLine),
                ItcReversals = ReadList(root, "itcReversals", "itcReversal", ReadItcReversal),
                GstChallans = ReadList(root, "gstChallans", "gstChallan", ReadGstChallan),
                GstDrc03s = ReadList(root, "gstDrc03s", "gstDrc03", ReadGstDrc03),
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Payload parse error: {ex.Message}");
            return null;
        }
    }

    private static List<T> ReadList<T>(XElement root, string wrapper, string item, Func<XElement, T> read)
    {
        var wrap = root.Element(wrapper);
        if (wrap is null) return [];
        return wrap.Elements(item).Select(read).ToList();
    }

    private static GroupDto ReadGroup(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, Nature = Str(e, "nature")!,
        ParentId = OptGuid(e, "parentId"), Alias = Str(e, "alias"), IsPredefined = Bool(e, "isPredefined"),
    };

    private static LedgerDto ReadLedger(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, GroupId = Guid(e, "groupId"),
        OpeningBalancePaisa = Long(e, "openingBalancePaisa"), OpeningIsDebit = Bool(e, "openingIsDebit"),
        Alias = Str(e, "alias"), IsPredefined = Bool(e, "isPredefined"),
        MaintainBillByBill = Bool(e, "maintainBillByBill"),
        DefaultCreditPeriodDays = OptInt(e, "defaultCreditPeriodDays"),
        CostCentresApplicable = OptBool(e, "costCentresApplicable"),
        EnableChequePrinting = Bool(e, "enableChequePrinting"),
        ChequePrintingBankName = Str(e, "chequePrintingBankName"),
        CurrencyId = OptGuid(e, "currencyId"),
        Interest = e.Element("interest") is { } iv ? ReadInterest(iv) : null,
        PartyGst = e.Element("partyGst") is { } p ? ReadPartyGst(p) : null,
        SalesPurchaseGst = e.Element("salesPurchaseGst") is { } s ? ReadStockItemGst(s) : null,
        GstClassification = e.Element("gstClassification") is { } gc ? ReadGstClassification(gc) : null,
        MethodOfAppropriation = Str(e, "methodOfAppropriation"),
        DefaultPriceLevelId = OptGuid(e, "defaultPriceLevelId"),
        TdsApplicable = Bool(e, "tdsApplicable"),
        TdsNatureOfPaymentId = OptGuid(e, "tdsNatureOfPaymentId"),
        DeducteeType = Str(e, "deducteeType"),
        PartyPan = Str(e, "partyPan"),
        DeductTdsInSameVoucher = Bool(e, "deductTdsInSameVoucher"),
        TcsApplicable = Bool(e, "tcsApplicable"),
        TcsNatureOfGoodsId = OptGuid(e, "tcsNatureOfGoodsId"),
        CollecteeType = Str(e, "collecteeType"),
        TdsTcsClassification = Str(e, "tdsTcsClassification"),
        // WI-4 (v45): the party Mailing Details block (absent ⇒ null, ER-13).
        Mailing = e.Element("mailing") is { } ml ? ReadPartyMailing(ml) : null,
    };

    private static PartyMailingDto ReadPartyMailing(XElement e) => new()
    {
        MailingName = Str(e, "mailingName"), Address = Str(e, "address"),
        Country = Str(e, "country"), Pincode = Str(e, "pincode"),
    };

    private static PriceLevelDto ReadPriceLevel(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!,
    };

    private static PriceListDto ReadPriceList(XElement e) => new()
    {
        Id = Guid(e, "id"), PriceLevelId = Guid(e, "priceLevelId"), StockItemId = Guid(e, "stockItemId"),
        ApplicableFrom = Str(e, "applicableFrom") ?? string.Empty,
        Slabs = (e.Element("slabs")?.Elements("slab") ?? Enumerable.Empty<XElement>())
            .Select(s => new PriceListSlabDto
            {
                FromQty = DecReq(s, "fromQty"), ToQty = OptDec(s, "toQty"),
                RatePaisa = Long(s, "ratePaisa"), DiscountPercent = DecReq(s, "discountPercent"),
            }).ToList(),
    };

    private static ReorderDefinitionDto ReadReorderDefinition(XElement e) => new()
    {
        Id = Guid(e, "id"), Scope = Str(e, "scope")!, TargetId = Guid(e, "targetId"),
        ReorderAdvanced = Bool(e, "reorderAdvanced"), ReorderQuantity = OptDec(e, "reorderQuantity"),
        MinQtyAdvanced = Bool(e, "minQtyAdvanced"), MinOrderQuantity = OptDec(e, "minOrderQuantity"),
        PeriodCount = OptInt(e, "periodCount"), PeriodUnit = Str(e, "periodUnit"), Criteria = Str(e, "criteria"),
    };

    private static InterestParametersDto ReadInterest(XElement e) => new()
    {
        Enabled = Bool(e, "enabled"), RatePercent = DecReq(e, "ratePercent"), Per = Str(e, "per")!,
        OnBalance = Str(e, "onBalance")!, Applicability = Str(e, "applicability")!,
        CalculateFrom = Str(e, "calculateFrom"), Style = Str(e, "style")!,
        RoundingMethod = Str(e, "roundingMethod")!, RoundingDecimals = Int(e, "roundingDecimals"),
    };

    private static CostCategoryDto ReadCostCategory(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!,
        AllocateRevenueItems = Bool(e, "allocateRevenueItems"),
        AllocateNonRevenueItems = Bool(e, "allocateNonRevenueItems"), IsPredefined = Bool(e, "isPredefined"),
    };

    private static CostCentreDto ReadCostCentre(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, CategoryId = Guid(e, "categoryId"),
        ParentId = OptGuid(e, "parentId"), Alias = Str(e, "alias"),
    };

    // ---- payroll masters (Phase 8 slice 1) ----

    private static EmployeeCategoryDto ReadEmployeeCategory(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!,
        AllocateRevenueItems = Bool(e, "allocateRevenueItems"),
        AllocateNonRevenueItems = Bool(e, "allocateNonRevenueItems"), IsPredefined = Bool(e, "isPredefined"),
    };

    private static EmployeeGroupDto ReadEmployeeGroup(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, ParentId = OptGuid(e, "parentId"),
        Alias = Str(e, "alias"), DefineSalaryDetails = Bool(e, "defineSalaryDetails"),
    };

    private static PayrollUnitDto ReadPayrollUnit(XElement e) => new()
    {
        Id = Guid(e, "id"), Symbol = Str(e, "symbol")!, FormalName = Str(e, "formalName")!,
        IsCompound = Bool(e, "isCompound"), DecimalPlaces = Int(e, "decimalPlaces"),
        FirstUnitId = OptGuid(e, "firstUnitId"), TailUnitId = OptGuid(e, "tailUnitId"),
        ConversionNumerator = OptInt(e, "conversionNumerator"), ConversionDenominator = OptInt(e, "conversionDenominator"),
    };

    private static AttendanceTypeDto ReadAttendanceType(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, ParentId = OptGuid(e, "parentId"),
        Kind = Str(e, "kind")!, PayrollUnitId = OptGuid(e, "payrollUnitId"),
    };

    private static EmployeeDto ReadEmployee(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, EmployeeGroupId = Guid(e, "employeeGroupId"),
        EmployeeCategoryId = OptGuid(e, "employeeCategoryId"), EmployeeNumber = Str(e, "employeeNumber"),
        DateOfJoining = Str(e, "dateOfJoining"), DateOfLeaving = Str(e, "dateOfLeaving"),
        Designation = Str(e, "designation"), Function = Str(e, "function"), Location = Str(e, "location"),
        Gender = Str(e, "gender"), DateOfBirth = Str(e, "dateOfBirth"), Pan = Str(e, "pan"), Aadhaar = Str(e, "aadhaar"),
        Uan = Str(e, "uan"), PfAccountNumber = Str(e, "pfAccountNumber"), EsiNumber = Str(e, "esiNumber"),
        BankAccountNumber = Str(e, "bankAccountNumber"), BankName = Str(e, "bankName"), BankIfsc = Str(e, "bankIfsc"),
        ApplicableTaxRegime = Str(e, "applicableTaxRegime") ?? nameof(Apex.Ledger.Domain.TaxRegime.New),
        PfApplicable = Bool(e, "pfApplicable"), PfContributeOnHigherWages = Bool(e, "pfContributeOnHigherWages"),
        PfJoinDate = Str(e, "pfJoinDate"), EsiApplicable = Bool(e, "esiApplicable"),
        IsPersonWithDisability = Bool(e, "isPersonWithDisability"),
    };

    private static PayHeadDto ReadPayHead(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, DisplayName = Str(e, "displayName"),
        PayHeadType = Str(e, "payHeadType")!, CalculationType = Str(e, "calculationType")!,
        AffectsNetSalary = Bool(e, "affectsNetSalary"), UnderGroupId = OptGuid(e, "underGroupId"),
        LedgerId = OptGuid(e, "ledgerId"), EmployerExpenseLedgerId = OptGuid(e, "employerExpenseLedgerId"),
        IncomeTaxComponent = Str(e, "incomeTaxComponent")!,
        UseForGratuity = Bool(e, "useForGratuity"), RoundingMethod = Str(e, "roundingMethod")!,
        RoundingLimitPaisa = Long(e, "roundingLimitPaisa"), CalculationPeriod = Str(e, "calculationPeriod")!,
        AttendanceTypeId = OptGuid(e, "attendanceTypeId"),
        PerDayCalculationBasisDays = OptInt(e, "perDayCalculationBasisDays"),
        PfComponent = Str(e, "pfComponent") ?? nameof(Apex.Ledger.Domain.PfStatutoryComponent.None),
        PartOfPfWages = Bool(e, "partOfPfWages"),
        EsiComponent = Str(e, "esiComponent") ?? nameof(Apex.Ledger.Domain.EsiStatutoryComponent.None),
        PartOfEsiWages = Bool(e, "partOfEsiWages"), IsOvertime = Bool(e, "isOvertime"),
        PtComponent = Str(e, "ptComponent") ?? nameof(Apex.Ledger.Domain.PtStatutoryComponent.None),
        ComputationComponents = (e.Element("computationComponents")?.Elements("component") ?? Enumerable.Empty<XElement>())
            .Select(c => new PayHeadComputationComponentDto
            {
                PayHeadId = Guid(c, "payHeadId"), IsSubtraction = Bool(c, "isSubtraction"),
            }).ToList(),
        ComputationSlabs = (e.Element("computationSlabs")?.Elements("slab") ?? Enumerable.Empty<XElement>())
            .Select(s => new PayHeadComputationSlabDto
            {
                SlabType = Str(s, "slabType")!, RateBasisPoints = Int(s, "rateBasisPoints"),
                ValuePaisa = Long(s, "valuePaisa"),
                FromAmountPaisa = OptLong(s, "fromAmountPaisa"), ToAmountPaisa = OptLong(s, "toAmountPaisa"),
            }).ToList(),
    };

    private static SalaryStructureDto ReadSalaryStructure(XElement e) => new()
    {
        Id = Guid(e, "id"), Scope = Str(e, "scope")!, ScopeId = Guid(e, "scopeId"),
        EffectiveFrom = Str(e, "effectiveFrom") ?? string.Empty, StartType = Str(e, "startType")!,
        Lines = (e.Element("lines")?.Elements("line") ?? Enumerable.Empty<XElement>())
            .Select(l => new SalaryStructureLineDto
            {
                PayHeadId = Guid(l, "payHeadId"), Order = Int(l, "order"), AmountPaisa = OptLong(l, "amountPaisa"),
            }).ToList(),
    };

    private static AttendanceEntryDto ReadAttendanceEntry(XElement e) => new()
    {
        Id = Guid(e, "id"), EmployeeId = Guid(e, "employeeId"), AttendanceTypeId = Guid(e, "attendanceTypeId"),
        FromDate = Str(e, "fromDate") ?? string.Empty, ToDate = Str(e, "toDate") ?? string.Empty,
        ValueMicro = Long(e, "valueMicro"),
    };

    private static TaxDeclarationDto ReadTaxDeclaration(XElement e) => new()
    {
        EmployeeId = Guid(e, "employeeId"),
        Section80CPaisa = Long(e, "section80CPaisa"), Section80DPaisa = Long(e, "section80DPaisa"),
        Section80CCD1BPaisa = Long(e, "section80CCD1BPaisa"), Section80CCD2EmployerPaisa = Long(e, "section80CCD2EmployerPaisa"),
        HraExemptPaisa = Long(e, "hraExemptPaisa"), HomeLoanInterestPaisa = Long(e, "homeLoanInterestPaisa"),
        OtherIncomePaisa = Long(e, "otherIncomePaisa"), PrevEmployerSalaryPaisa = Long(e, "prevEmployerSalaryPaisa"),
        PrevEmployerTdsPaisa = Long(e, "prevEmployerTdsPaisa"),
    };

    private static CurrencyDto ReadCurrency(XElement e) => new()
    {
        Id = Guid(e, "id"), Symbol = Str(e, "symbol")!, FormalName = Str(e, "formalName")!,
        DecimalPlaces = Int(e, "decimalPlaces"), IsBaseCurrency = Bool(e, "isBaseCurrency"),
    };

    private static ExchangeRateDto ReadExchangeRate(XElement e) => new()
    {
        Id = Guid(e, "id"), CurrencyId = Guid(e, "currencyId"), Date = Str(e, "date") ?? string.Empty,
        StandardRateMicro = Long(e, "standardRateMicro"),
        SellingRateMicro = OptLong(e, "sellingRateMicro"), BuyingRateMicro = OptLong(e, "buyingRateMicro"),
    };

    private static BudgetDto ReadBudget(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, UnderId = OptGuid(e, "underId"),
        PeriodFrom = Str(e, "periodFrom") ?? string.Empty, PeriodTo = Str(e, "periodTo") ?? string.Empty,
        Lines = (e.Element("lines")?.Elements("line") ?? Enumerable.Empty<XElement>())
            .Select(l => new BudgetLineDto
            {
                GroupId = OptGuid(l, "groupId"), LedgerId = OptGuid(l, "ledgerId"),
                BudgetType = Str(l, "budgetType")!, AmountPaisa = Long(l, "amountPaisa"),
            }).ToList(),
    };

    private static ScenarioDto ReadScenario(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, IncludeActuals = Bool(e, "includeActuals"),
        IncludedTypeIds = (e.Element("includedTypeIds")?.Elements("typeId") ?? Enumerable.Empty<XElement>())
            .Select(t => Guid(t, "id")).ToList(),
        ExcludedTypeIds = (e.Element("excludedTypeIds")?.Elements("typeId") ?? Enumerable.Empty<XElement>())
            .Select(t => Guid(t, "id")).ToList(),
    };

    private static VoucherTypeDto ReadVoucherType(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, BaseType = Str(e, "baseType")!, Numbering = Str(e, "numbering")!,
        DefaultShortcut = Str(e, "defaultShortcut"), Abbreviation = Str(e, "abbreviation"),
        IsActive = Bool(e, "isActive"), IsPredefined = Bool(e, "isPredefined"),
        AffectsAccounts = Bool(e, "affectsAccounts"), AffectsStock = Bool(e, "affectsStock"),
        UseAsManufacturingJournal = Bool(e, "useAsManufacturingJournal"),
        TrackAdditionalCosts = Bool(e, "trackAdditionalCosts"),
        AllowZeroValuedTransactions = Bool(e, "allowZeroValuedTransactions"),
        UseForPos = Bool(e, "useForPos"),
        UseForJobWork = Bool(e, "useForJobWork"),
        AllowConsumption = Bool(e, "allowConsumption"),
        IsStatPayment = Bool(e, "isStatPayment"),
        IsRcmPaymentVoucher = Bool(e, "isRcmPaymentVoucher"),
        IsGstStatAdjustment = Bool(e, "isGstStatAdjustment"),
        PosConfig = e.Element("posConfig") is { } pc ? ReadPosConfig(pc) : null,
    };

    private static TdsChallanDto ReadTdsChallan(XElement e) => new()
    {
        Id = Guid(e, "id"), ChallanNo = Str(e, "challanNo")!, BsrCode = Str(e, "bsrCode")!,
        DepositDate = Str(e, "depositDate") ?? string.Empty, AmountPaisa = Long(e, "amountPaisa"),
        Section = Str(e, "section")!, MinorHead = Str(e, "minorHead")!,
    };

    private static ChallanVoucherLinkDto ReadChallanVoucherLink(XElement e) => new()
    {
        ChallanId = Guid(e, "challanId"), VoucherId = Guid(e, "voucherId"),
    };

    private static RcmDocumentDto ReadRcmDocument(XElement e) => new()
    {
        Id = Guid(e, "id"), Kind = Str(e, "kind")!, SourceVoucherId = Guid(e, "sourceVoucherId"),
        SeriesNumber = Int(e, "seriesNumber"), DocDate = Str(e, "docDate") ?? string.Empty,
        SupplierLedgerId = OptGuid(e, "supplierLedgerId"),
    };

    private static EInvoiceRecordDto ReadEInvoiceRecord(XElement e) => new()
    {
        Id = Guid(e, "id"), SourceVoucherId = Guid(e, "sourceVoucherId"),
        DocumentNumberUpper = Str(e, "documentNumberUpper") ?? string.Empty, Status = Str(e, "status")!,
        Irn = Str(e, "irn"), AckNo = Str(e, "ackNo"), AckDate = Str(e, "ackDate"), SignedQr = Str(e, "signedQr"),
        SignedJsonBase64 = Str(e, "signedJsonBase64"), CancelledOn = Str(e, "cancelledOn"),
        CancelReasonCode = Str(e, "cancelReasonCode"), ErrorCode = Str(e, "errorCode"), ErrorMessage = Str(e, "errorMessage"),
    };

    private static EWayBillRecordDto ReadEWayBillRecord(XElement e) => new()
    {
        Id = Guid(e, "id"), SourceVoucherId = Guid(e, "sourceVoucherId"),
        DocumentNumberUpper = Str(e, "documentNumberUpper") ?? string.Empty, Status = Str(e, "status")!,
        SupplyType = Str(e, "supplyType"), SubSupplyType = Str(e, "subSupplyType"), DocType = Str(e, "docType"),
        ConsignmentValuePaisa = Long(e, "consignmentValuePaisa"), TransporterId = Str(e, "transporterId"),
        TransMode = Str(e, "transMode"), VehicleNumber = Str(e, "vehicleNumber"), DistanceKm = Int(e, "distanceKm"),
        TransportDocNo = Str(e, "transportDocNo"), ShipFromStateCode = Str(e, "shipFromStateCode"),
        ShipToStateCode = Str(e, "shipToStateCode"), IsOverDimensionalCargo = Bool(e, "isOverDimensionalCargo"),
        ShipToGstin = Str(e, "shipToGstin"), ClosureRequested = Bool(e, "closureRequested"), ClosedOn = Str(e, "closedOn"),
        EwbNumber = Str(e, "ewbNumber"), GeneratedAt = Str(e, "generatedAt"), ValidUpto = Str(e, "validUpto"),
        CancelledOn = Str(e, "cancelledOn"), CancelReasonCode = Str(e, "cancelReasonCode"),
        ErrorCode = Str(e, "errorCode"), ErrorMessage = Str(e, "errorMessage"),
    };

    private static GstCdnLinkDto ReadCdnLink(XElement e) => new()
    {
        Id = Guid(e, "id"), CdnVoucherId = Guid(e, "cdnVoucherId"), CdnType = Str(e, "cdnType")!,
        OriginalInvoiceVoucherId = OptGuid(e, "originalInvoiceVoucherId"),
        OriginalInvoiceNumber = Str(e, "originalInvoiceNumber"), OriginalInvoiceDate = Str(e, "originalInvoiceDate"),
        ReasonCode = Str(e, "reasonCode")!, Is9BTarget = Bool(e, "is9BTarget"),
    };

    private static Gstr2bSnapshotDto ReadGstr2bSnapshot(XElement e) => new()
    {
        Id = Guid(e, "id"), StatementType = Str(e, "statementType")!, ReturnPeriod = Str(e, "returnPeriod") ?? string.Empty,
        RecipientGstin = Str(e, "recipientGstin") ?? string.Empty, GeneratedOn = Str(e, "generatedOn"),
        SourceFileHash = Str(e, "sourceFileHash") ?? string.Empty, ImportedAt = Str(e, "importedAt") ?? string.Empty,
        SummaryIgstPaisa = Long(e, "summaryIgstPaisa"), SummaryCgstPaisa = Long(e, "summaryCgstPaisa"),
        SummarySgstPaisa = Long(e, "summarySgstPaisa"), SummaryCessPaisa = Long(e, "summaryCessPaisa"),
        Lines = (e.Element("lines")?.Elements("gstr2bLine") ?? Enumerable.Empty<XElement>()).Select(ReadGstr2bLine).ToList(),
    };

    private static Gstr2bLineDto ReadGstr2bLine(XElement e) => new()
    {
        Id = Guid(e, "id"), SupplierGstin = Str(e, "supplierGstin") ?? string.Empty,
        SupplierTradeName = Str(e, "supplierTradeName"), DocType = Str(e, "docType")!,
        DocNumber = Str(e, "docNumber") ?? string.Empty, DocNumberNorm = Str(e, "docNumberNorm"),
        DocDate = Str(e, "docDate") ?? string.Empty, PosStateCode = Str(e, "posStateCode"),
        TaxableValuePaisa = Long(e, "taxableValuePaisa"), IgstPaisa = Long(e, "igstPaisa"), CgstPaisa = Long(e, "cgstPaisa"),
        SgstPaisa = Long(e, "sgstPaisa"), CessPaisa = Long(e, "cessPaisa"), ItcAvailable = Bool(e, "itcAvailable"),
        ItcUnavailableReason = Str(e, "itcUnavailableReason"), ReverseCharge = Bool(e, "reverseCharge"),
    };

    private static Gstr2bReconResultDto ReadGstr2bReconResult(XElement e) => new()
    {
        Id = Guid(e, "id"), LineId = Guid(e, "lineId"), Bucket = Str(e, "bucket")!,
        MatchedVoucherId = OptGuid(e, "matchedVoucherId"), TaxableVariancePaisa = Long(e, "taxableVariancePaisa"),
        TaxVariancePaisa = Long(e, "taxVariancePaisa"), MatchPinned = Bool(e, "matchPinned"),
        ReconciledAt = Str(e, "reconciledAt"),
    };

    private static ImsActionDto ReadImsAction(XElement e) => new()
    {
        Id = Guid(e, "id"), LineId = Guid(e, "lineId"), Status = Str(e, "status")!, Remarks = Str(e, "remarks"),
        DeclaredReversalPaisa = OptLong(e, "declaredReversalPaisa"), NoReversalDeclared = Bool(e, "noReversalDeclared"),
        ActedOn = Str(e, "actedOn"),
    };

    private static GstSetoffLineDto ReadGstSetoffLine(XElement e) => new()
    {
        Id = Guid(e, "id"), VoucherId = Guid(e, "voucherId"), Period = Str(e, "period")!,
        CreditHead = Str(e, "creditHead")!, LiabilityHead = Str(e, "liabilityHead")!,
        IsCash = Bool(e, "isCash"), AmountPaisa = Long(e, "amountPaisa"),
    };

    private static ItcReversalDto ReadItcReversal(XElement e) => new()
    {
        Id = Guid(e, "id"), Rule = Str(e, "rule")!, Period = Str(e, "period")!,
        CgstPaisa = Long(e, "cgstPaisa"), SgstPaisa = Long(e, "sgstPaisa"), IgstPaisa = Long(e, "igstPaisa"),
        CessPaisa = Long(e, "cessPaisa"), D1BasisPaisa = OptLong(e, "d1BasisPaisa"), D2BasisPaisa = OptLong(e, "d2BasisPaisa"),
        SourceVoucherId = OptGuid(e, "sourceVoucherId"), SourceLineId = OptGuid(e, "sourceLineId"),
        ReversalVoucherId = Guid(e, "reversalVoucherId"), ReclaimOfId = OptGuid(e, "reclaimOfId"),
        Drc03Id = OptGuid(e, "drc03Id"), Table4bBucket = Str(e, "table4bBucket")!, CreatedAt = Str(e, "createdAt")!,
    };

    private static GstChallanDto ReadGstChallan(XElement e) => new()
    {
        Id = Guid(e, "id"), Cpin = Str(e, "cpin")!, Cin = Str(e, "cin"), Brn = Str(e, "brn"),
        DepositDate = Str(e, "depositDate")!, MajorHead = Str(e, "majorHead")!, MinorHead = Str(e, "minorHead")!,
        AmountPaisa = Long(e, "amountPaisa"), VoucherId = Guid(e, "voucherId"), InterestFlag = Bool(e, "interestFlag"),
    };

    private static GstDrc03Dto ReadGstDrc03(XElement e) => new()
    {
        Id = Guid(e, "id"), Drc03Ref = Str(e, "drc03Ref"), Cause = Str(e, "cause")!, Period = Str(e, "period")!,
        CgstPaisa = Long(e, "cgstPaisa"), SgstPaisa = Long(e, "sgstPaisa"), IgstPaisa = Long(e, "igstPaisa"),
        CessPaisa = Long(e, "cessPaisa"), InterestPaisa = Long(e, "interestPaisa"),
        Drc03aDemandRef = Str(e, "drc03aDemandRef"), VoucherId = OptGuid(e, "voucherId"), CreatedAt = Str(e, "createdAt")!,
    };

    private static GstAdvanceReceiptDto ReadAdvanceReceipt(XElement e) => new()
    {
        Id = Guid(e, "id"), ReceiptVoucherId = Guid(e, "receiptVoucherId"), IsService = Bool(e, "isService"),
        AdvanceAmountPaisa = Long(e, "advanceAmountPaisa"), RateBasisPoints = Int(e, "rateBasisPoints"),
        InterState = Bool(e, "interState"), PlaceOfSupplyStateCode = Str(e, "placeOfSupplyStateCode"),
        AdvanceTaxPaisa = Long(e, "advanceTaxPaisa"),
        AdjustedAgainstInvoiceVoucherId = OptGuid(e, "adjustedAgainstInvoiceVoucherId"),
        RefundVoucherId = OptGuid(e, "refundVoucherId"),
    };

    private static TcsChallanDto ReadTcsChallan(XElement e) => new()
    {
        Id = Guid(e, "id"), ChallanNo = Str(e, "challanNo")!, BsrCode = Str(e, "bsrCode")!,
        DepositDate = Str(e, "depositDate") ?? string.Empty, AmountPaisa = Long(e, "amountPaisa"),
        CollectionCode = Str(e, "collectionCode")!, MinorHead = Str(e, "minorHead")!,
    };

    private static PosConfigDto ReadPosConfig(XElement e) => new()
    {
        DefaultGodownId = OptGuid(e, "defaultGodownId"), DefaultPartyId = OptGuid(e, "defaultPartyId"),
        PrintAfterSave = Bool(e, "printAfterSave"), DefaultTitle = Str(e, "defaultTitle"),
        Message1 = Str(e, "message1"), Message2 = Str(e, "message2"), Declaration = Str(e, "declaration"),
        TenderLedgerDefaults = (e.Element("tenderLedgerDefaults")?.Elements("tenderLedgerDefault") ?? Enumerable.Empty<XElement>())
            .Select(d => new PosTenderLedgerDefaultDto { TenderType = Str(d, "tenderType")!, LedgerId = Guid(d, "ledgerId") })
            .ToList(),
    };

    private static UnitDto ReadUnit(XElement e) => new()
    {
        Id = Guid(e, "id"), Symbol = Str(e, "symbol")!, FormalName = Str(e, "formalName")!,
        IsCompound = Bool(e, "isCompound"), UnitQuantityCode = Str(e, "unitQuantityCode"),
        DecimalPlaces = Int(e, "decimalPlaces"), FirstUnitId = OptGuid(e, "firstUnitId"), TailUnitId = OptGuid(e, "tailUnitId"),
        ConversionNumerator = OptInt(e, "conversionNumerator"), ConversionDenominator = OptInt(e, "conversionDenominator"),
    };

    private static StockGroupDto ReadStockGroup(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, ParentId = OptGuid(e, "parentId"),
        Alias = Str(e, "alias"), AddQuantities = Bool(e, "addQuantities"),
    };

    private static StockCategoryDto ReadStockCategory(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, ParentId = OptGuid(e, "parentId"), Alias = Str(e, "alias"),
    };

    private static GodownDto ReadGodown(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, ParentId = OptGuid(e, "parentId"), Alias = Str(e, "alias"),
        ThirdParty = Bool(e, "thirdParty"), IsMainLocation = Bool(e, "isMainLocation"),
    };

    private static StockItemDto ReadStockItem(XElement e) => new()
    {
        Id = Guid(e, "id"), Name = Str(e, "name")!, StockGroupId = Guid(e, "stockGroupId"), BaseUnitId = Guid(e, "baseUnitId"),
        CategoryId = OptGuid(e, "categoryId"), Alias = Str(e, "alias"), ValuationMethod = Str(e, "valuationMethod")!,
        HsnSacCode = Str(e, "hsnSacCode"), IsTaxable = Bool(e, "isTaxable"), StandardCostPaisa = OptLong(e, "standardCostPaisa"),
        ReorderLevel = OptDec(e, "reorderLevel"), MinimumOrderQuantity = OptDec(e, "minimumOrderQuantity"),
        Gst = e.Element("gst") is { } g ? ReadStockItemGst(g) : null,
        MaintainInBatches = Bool(e, "maintainInBatches"),
        TrackManufacturingDate = Bool(e, "trackManufacturingDate"),
        UseExpiryDates = Bool(e, "useExpiryDates"),
        SetComponents = Bool(e, "setComponents"),
        TcsNatureOfGoodsId = OptGuid(e, "tcsNatureOfGoodsId"),
    };

    private static BatchMasterDto ReadBatchMaster(XElement e) => new()
    {
        Id = Guid(e, "id"), StockItemId = Guid(e, "stockItemId"), BatchNumber = Str(e, "batchNumber")!,
        ManufacturingDate = Str(e, "manufacturingDate"), ExpiryDate = Str(e, "expiryDate"),
        ExpiryPeriod = Str(e, "expiryPeriod"), GodownId = OptGuid(e, "godownId"),
        InwardQuantity = OptDec(e, "inwardQuantity"), InwardRatePaisa = OptLong(e, "inwardRatePaisa"),
    };

    private static BillOfMaterialsDto ReadBom(XElement e) => new()
    {
        Id = Guid(e, "id"), StockItemId = Guid(e, "stockItemId"), Name = Str(e, "name")!,
        UnitOfManufacture = DecReq(e, "unitOfManufacture"),
        Lines = (e.Element("lines")?.Elements("line") ?? Enumerable.Empty<XElement>())
            .Select(l => new BomLineDto
            {
                LineType = Str(l, "lineType")!, ComponentStockItemId = Guid(l, "componentStockItemId"),
                GodownId = OptGuid(l, "godownId"), QuantityPerBlock = DecReq(l, "quantityPerBlock"),
                RatePaisa = OptLong(l, "ratePaisa"), PercentOfFinishedGoodCost = OptDec(l, "percentOfFinishedGoodCost"),
            }).ToList(),
    };

    private static StockOpeningBalanceDto ReadStockOpeningBalance(XElement e) => new()
    {
        Id = Guid(e, "id"), StockItemId = Guid(e, "stockItemId"), GodownId = Guid(e, "godownId"),
        Quantity = DecReq(e, "quantity"), RatePaisa = Long(e, "ratePaisa"), BatchLabel = Str(e, "batchLabel"),
        ManufacturingDate = Str(e, "manufacturingDate"), ExpiryDate = Str(e, "expiryDate"),
    };

    private static GstConfigDto ReadGstConfig(XElement e) => new()
    {
        Enabled = Bool(e, "enabled"), Gstin = Str(e, "gstin"), HomeStateCode = Str(e, "homeStateCode"),
        RegistrationType = Str(e, "registrationType")!, ApplicableFrom = Str(e, "applicableFrom"),
        Periodicity = Str(e, "periodicity")!,
        RateSlabs = (e.Element("rateSlabs")?.Elements("rateSlab") ?? Enumerable.Empty<XElement>())
            .Select(s => new GstRateSlabDto
            {
                Id = Guid(s, "id"), RateBasisPoints = Int(s, "rateBasisPoints"),
                Label = Str(s, "label")!, IsPredefined = Bool(s, "isPredefined"),
            }).ToList(),
        // Phase 9 slice 1: dated rate-history + Compensation-Cess windows (absent elements ⇒ empty lists, ER-13).
        RateHistory = (e.Element("rateHistory")?.Elements("entry") ?? Enumerable.Empty<XElement>())
            .Select(h => new GstRateHistoryDto
            {
                Id = Guid(h, "id"), HsnSac = Str(h, "hsnSac"), RateBasisPoints = Int(h, "rateBasisPoints"),
                RateClass = Str(h, "rateClass")!, EffectiveFrom = Str(h, "effectiveFrom")!,
                EffectiveTo = Str(h, "effectiveTo"), ValuationBasis = Str(h, "valuationBasis")!,
                Label = Str(h, "label")!, IsPredefined = Bool(h, "isPredefined"),
            }).ToList(),
        CessRates = (e.Element("cessRates")?.Elements("cess") ?? Enumerable.Empty<XElement>())
            .Select(c => new GstCessRateDto
            {
                Id = Guid(c, "id"), HsnSac = Str(c, "hsnSac"), ValuationMode = Str(c, "valuationMode")!,
                CessRateBasisPoints = Int(c, "cessRateBasisPoints"), CessPerUnitPaisa = Long(c, "cessPerUnitPaisa"),
                CessRspFactorMillis = Int(c, "cessRspFactorMillis"), EffectiveFrom = Str(c, "effectiveFrom")!,
                EffectiveTo = Str(c, "effectiveTo"), Label = Str(c, "label")!, IsPredefined = Bool(c, "isPredefined"),
            }).ToList(),
        // Phase 9 slice 2: dated reverse-charge categories (absent element ⇒ empty list, ER-13).
        RcmCategories = (e.Element("rcmCategories")?.Elements("rcmCategory") ?? Enumerable.Empty<XElement>())
            .Select(c => new RcmCategoryDto
            {
                Id = Guid(c, "id"), Notification = Str(c, "notification")!, Stream = Str(c, "stream")!,
                SupplyNature = Str(c, "supplyNature")!, SupplyType = Str(c, "supplyType")!, HsnSac = Str(c, "hsnSac"),
                RateBasisPoints = Int(c, "rateBasisPoints"), SupplierQualifier = Str(c, "supplierQualifier")!,
                RecipientQualifier = Str(c, "recipientQualifier")!, EffectiveFrom = Str(c, "effectiveFrom")!,
                EffectiveTo = Str(c, "effectiveTo"), Label = Str(c, "label")!, IsPredefined = Bool(c, "isPredefined"),
            }).ToList(),
        // Phase 9 slice 3: composition sub-type + opt-in date (absent attributes ⇒ null, ER-13).
        CompositionSubType = Str(e, "compositionSubType"), CompositionOptInDate = Str(e, "compositionOptInDate"),
        // Phase 9 slice 4a: NON-SECRET e-invoice / B2C-QR / connector-mode config (absent ⇒ off/default, ER-13). No NIC
        // credential is read here (ER-16).
        EInvoicingEnabled = Bool(e, "eInvoicingEnabled"), EInvoiceApplicableFrom = Str(e, "eInvoiceApplicableFrom"),
        EInvoiceAatoThresholdPaisa = OptLong(e, "eInvoiceAatoThresholdPaisa") ?? 5_000_000_000L,
        EInvoiceApplicabilityOverride = Bool(e, "eInvoiceApplicabilityOverride"),
        EInvoiceExemptionClasses = Str(e, "eInvoiceExemptionClasses") ?? "None",
        EInvoiceReportingAgeApplies = Bool(e, "eInvoiceReportingAgeApplies"),
        ConnectorMode = Str(e, "connectorMode") ?? "OfflineJson",
        B2cDynamicQrEnabled = Bool(e, "b2cDynamicQrEnabled"),
        B2cQrAatoThresholdPaisa = OptLong(e, "b2cQrAatoThresholdPaisa") ?? 500_000_000_000L,
        B2cQrUpiId = Str(e, "b2cQrUpiId"), B2cQrPayeeName = Str(e, "b2cQrPayeeName"),
        // Phase 9 slice 5: NON-SECRET e-Way Bill config + per-state overrides (absent ⇒ off/default, ER-13). No NIC
        // credential is read here (ER-16).
        EWayBillEnabled = Bool(e, "eWayBillEnabled"), EWayApplicableFrom = Str(e, "eWayApplicableFrom"),
        EWayThresholdPaisa = OptLong(e, "eWayThresholdPaisa") ?? 5_000_000L,
        ConsignmentBasis = Str(e, "consignmentBasis") ?? "Rule138Default",
        EWayIntraStateApplicable = e.Attribute("eWayIntraStateApplicable") is null ? true : Bool(e, "eWayIntraStateApplicable"),
        EWayStateThresholds = (e.Element("ewayStateThresholds")?.Elements("ewayStateThreshold") ?? Enumerable.Empty<XElement>())
            .Select(t => new EWayStateThresholdDto
            {
                Id = Guid(t, "id"), StateCode = Str(t, "stateCode")!, TxnType = Str(t, "txnType")!,
                ThresholdPaisa = Long(t, "thresholdPaisa"),
            }).ToList(),
        // Phase 9 slice 6: the GSTR-2B reconciliation tolerance (absent ⇒ 0/0 exact-match, ER-13; finding #5).
        ReconValueTolerancePaisa = OptLong(e, "reconValueTolerancePaisa") ?? 0L,
        ReconDateWindowDays = OptInt(e, "reconDateWindowDays") ?? 0,
    };

    private static PartyGstDto ReadPartyGst(XElement e) => new()
    {
        RegistrationType = Str(e, "registrationType")!, Gstin = Str(e, "gstin"), StateCode = Str(e, "stateCode"),
        // Phase 9 slice 2: reverse-charge qualifiers (absent ⇒ false, ER-13).
        IsPromoter = Bool(e, "isPromoter"), IsBodyCorporate = Bool(e, "isBodyCorporate"),
    };

    private static StockItemGstDto ReadStockItemGst(XElement e) => new()
    {
        HsnSac = Str(e, "hsnSac"), Taxability = Str(e, "taxability")!,
        RateBasisPoints = OptInt(e, "rateBasisPoints"), SupplyType = Str(e, "supplyType")!,
        // Phase 9 slice 1: RSP valuation + cess (absent valuationBasis ⇒ default TransactionValue, ER-13).
        ValuationBasis = Str(e, "valuationBasis") ?? "TransactionValue", CessApplicable = Bool(e, "cessApplicable"),
        CessValuationMode = Str(e, "cessValuationMode"), CessRateBasisPoints = OptInt(e, "cessRateBasisPoints"),
        CessPerUnitPaisa = OptLong(e, "cessPerUnitPaisa"), CessRspFactorMillis = OptInt(e, "cessRspFactorMillis"),
        RspPaisa = OptLong(e, "rspPaisa"),
        // Phase 9 slice 2: reverse-charge flags (absent ⇒ false/null, ER-13).
        ReverseChargeApplicable = Bool(e, "reverseChargeApplicable"), GtaForwardCharge = Bool(e, "gtaForwardCharge"),
        RcmCategoryId = OptGuid(e, "rcmCategoryId"),
        // Phase 9 slice 6b: §17(5) ITC-eligibility (absent ⇒ Eligible/None default, ER-13).
        ItcEligibility = Str(e, "itcEligibility") ?? "Eligible",
        BlockedCreditCategory = Str(e, "blockedCreditCategory") ?? "None",
    };

    private static LedgerGstClassificationDto ReadGstClassification(XElement e) => new()
    {
        TaxHead = Str(e, "taxHead")!, Direction = Str(e, "direction")!,
        // Phase 9 slice 2: the RCM Output discriminator (absent ⇒ false, ER-13).
        IsReverseCharge = Bool(e, "isReverseCharge"),
    };

    private static TdsConfigDto ReadTdsConfig(XElement e) => new()
    {
        Enabled = Bool(e, "enabled"), Tan = Str(e, "tan"), DeductorType = Str(e, "deductorType")!,
        ResponsiblePersonName = Str(e, "responsiblePersonName"), ResponsiblePersonPan = Str(e, "responsiblePersonPan"),
        ResponsiblePersonDesignation = Str(e, "responsiblePersonDesignation"),
        ResponsiblePersonAddress = Str(e, "responsiblePersonAddress"),
        SurchargeApplicable = Bool(e, "surchargeApplicable"), CessApplicable = Bool(e, "cessApplicable"),
        Periodicity = Str(e, "periodicity")!, ApplicableFrom = Str(e, "applicableFrom"),
        NaturesOfPayment = (e.Element("naturesOfPayment")?.Elements("natureOfPayment") ?? Enumerable.Empty<XElement>())
            .Select(ReadNatureOfPayment).ToList(),
    };

    private static NatureOfPaymentDto ReadNatureOfPayment(XElement e) => new()
    {
        Id = Guid(e, "id"), SectionCode = Str(e, "sectionCode")!, Name = Str(e, "name")!,
        RateWithPanBp = Int(e, "rateWithPanBp"), RateWithoutPanBp = Int(e, "rateWithoutPanBp"),
        SingleThresholdPaisa = OptLong(e, "singleThresholdPaisa"), CumulativeThresholdPaisa = OptLong(e, "cumulativeThresholdPaisa"),
        FvuSectionCode = Str(e, "fvuSectionCode")!, EffectiveFrom = Str(e, "effectiveFrom"), IsPredefined = Bool(e, "isPredefined"),
    };

    private static TcsConfigDto ReadTcsConfig(XElement e) => new()
    {
        Enabled = Bool(e, "enabled"), Tan = Str(e, "tan"), CollectorType = Str(e, "collectorType")!,
        ResponsiblePersonName = Str(e, "responsiblePersonName"), ResponsiblePersonPan = Str(e, "responsiblePersonPan"),
        ResponsiblePersonDesignation = Str(e, "responsiblePersonDesignation"),
        ResponsiblePersonAddress = Str(e, "responsiblePersonAddress"),
        SurchargeApplicable = Bool(e, "surchargeApplicable"), CessApplicable = Bool(e, "cessApplicable"),
        Periodicity = Str(e, "periodicity")!, ApplicableFrom = Str(e, "applicableFrom"),
        NaturesOfGoods = (e.Element("naturesOfGoods")?.Elements("natureOfGoods") ?? Enumerable.Empty<XElement>())
            .Select(ReadNatureOfGoods).ToList(),
    };

    private static NatureOfGoodsDto ReadNatureOfGoods(XElement e) => new()
    {
        Id = Guid(e, "id"), CollectionCode = Str(e, "collectionCode")!, Name = Str(e, "name")!,
        RateWithPanBp = Int(e, "rateWithPanBp"), RateWithoutPanBp = Int(e, "rateWithoutPanBp"),
        ThresholdPaisa = OptLong(e, "thresholdPaisa"), BaseIncludesGst = Bool(e, "baseIncludesGst"),
        FvuCode = Str(e, "fvuCode")!, EffectiveFrom = Str(e, "effectiveFrom"), IsPredefined = Bool(e, "isPredefined"),
        IsLegacy = Bool(e, "isLegacy"), LegacyCutoff = Str(e, "legacyCutoff"),
    };

    private static VoucherDto ReadVoucher(XElement e) => new()
    {
        Id = Guid(e, "id"), TypeId = Guid(e, "typeId"), Number = Int(e, "number"), Date = Str(e, "date") ?? string.Empty,
        Narration = Str(e, "narration"), PartyId = OptGuid(e, "partyId"),
        Cancelled = Bool(e, "cancelled"), Optional = Bool(e, "optional"), PostDated = Bool(e, "postDated"),
        ApplicableUpto = Str(e, "applicableUpto"),
        Lines = (e.Element("lines")?.Elements("line") ?? Enumerable.Empty<XElement>()).Select(ReadEntryLine).ToList(),
        InventoryLines = (e.Element("inventoryLines")?.Elements("inventoryLine") ?? Enumerable.Empty<XElement>())
            .Select(ReadVoucherInventoryLine).ToList(),
        PosTenders = (e.Element("posTenders")?.Elements("posTender") ?? Enumerable.Empty<XElement>())
            .Select(ReadPosTender).ToList(),
    };

    private static PosTenderDto ReadPosTender(XElement e) => new()
    {
        TenderType = Str(e, "tenderType")!, LedgerId = Guid(e, "ledgerId"), AmountPaisa = Long(e, "amountPaisa"),
        TenderedPaisa = OptLong(e, "tenderedPaisa"), ChangePaisa = OptLong(e, "changePaisa"),
        CardNo = Str(e, "cardNo"), BankName = Str(e, "bankName"), ChequeNo = Str(e, "chequeNo"),
    };

    private static EntryLineDto ReadEntryLine(XElement e) => new()
    {
        LedgerId = Guid(e, "ledgerId"), AmountPaisa = Long(e, "amountPaisa"), Side = Str(e, "side")!,
        BillAllocations = (e.Element("billAllocations")?.Elements("billAllocation") ?? Enumerable.Empty<XElement>())
            .Select(b => new BillAllocationDto
            {
                RefType = Str(b, "refType")!, Name = Str(b, "name") ?? string.Empty, AmountPaisa = Long(b, "amountPaisa"),
                DueDate = Str(b, "dueDate"), CreditPeriodDays = OptInt(b, "creditPeriodDays"),
            }).ToList(),
        CostAllocations = (e.Element("costAllocations")?.Elements("costAllocation") ?? Enumerable.Empty<XElement>())
            .Select(a => new CostAllocationDto
            {
                CategoryId = Guid(a, "categoryId"), CentreId = Guid(a, "centreId"), AmountPaisa = Long(a, "amountPaisa"),
            }).ToList(),
        BankAllocation = e.Element("bankAllocation") is { } ba ? new BankAllocationDto
        {
            TransactionType = Str(ba, "transactionType")!, InstrumentNumber = Str(ba, "instrumentNumber") ?? string.Empty,
            InstrumentDate = Str(ba, "instrumentDate"), BankDate = Str(ba, "bankDate"),
        } : null,
        Forex = e.Element("forex") is { } fx ? new ForexDto
        {
            CurrencyId = Guid(fx, "currencyId"), ForexAmountPaisa = Long(fx, "forexAmountPaisa"),
            RateMicro = Long(fx, "rateMicro"),
        } : null,
        Gst = e.Element("gst") is { } g ? new GstLineTaxDto
        {
            TaxHead = Str(g, "taxHead")!, RateBasisPoints = Int(g, "rateBasisPoints"),
            TaxableValuePaisa = Long(g, "taxableValuePaisa"),
            // Phase 9 slice 2: reverse-charge tag (absent ⇒ false/null, ER-13). Phase 9 slice 7: adjustment tag.
            IsReverseCharge = Bool(g, "isReverseCharge"), RcmScheme = Str(g, "rcmScheme"),
            Adjustment = Str(g, "adjustment"),
        } : null,
        Tds = e.Element("tds") is { } t ? new TdsLineTaxDto
        {
            NatureId = Guid(t, "natureId"), SectionCode = Str(t, "sectionCode")!,
            AssessableValuePaisa = Long(t, "assessableValuePaisa"), RateBasisPoints = Int(t, "rateBasisPoints"),
            TdsAmountPaisa = Long(t, "tdsAmountPaisa"), DeducteeLedgerId = Guid(t, "deducteeLedgerId"),
            PanApplied = Bool(t, "panApplied"),
        } : null,
        Tcs = e.Element("tcs") is { } tc ? new TcsLineTaxDto
        {
            NatureId = Guid(tc, "natureId"), CollectionCode = Str(tc, "collectionCode")!,
            AssessableValuePaisa = Long(tc, "assessableValuePaisa"), RateBasisPoints = Int(tc, "rateBasisPoints"),
            TcsAmountPaisa = Long(tc, "tcsAmountPaisa"), CollecteeLedgerId = Guid(tc, "collecteeLedgerId"),
            PanApplied = Bool(tc, "panApplied"),
        } : null,
        Payroll = e.Element("payroll") is { } pr ? new PayrollLineDto
        {
            EmployeeId = Guid(pr, "employeeId"), PayHeadId = OptGuid(pr, "payHeadId"),
            Category = Str(pr, "category")!, AmountPaisa = Long(pr, "amountPaisa"),
        } : null,
    };

    private static VoucherInventoryLineDto ReadVoucherInventoryLine(XElement e) => new()
    {
        StockItemId = Guid(e, "stockItemId"), GodownId = Guid(e, "godownId"), Quantity = DecReq(e, "quantity"),
        RatePaisa = Long(e, "ratePaisa"), Direction = Str(e, "direction")!, BatchLabel = Str(e, "batchLabel"),
        BilledQuantity = OptDec(e, "billedQuantity"),
        UnitId = OptGuid(e, "unitId"),   // WI-10 Gap 2; absent ⇒ null ⇒ the item's base unit
    };

    private static InventoryVoucherDto ReadInventoryVoucher(XElement e) => new()
    {
        Id = Guid(e, "id"), TypeId = Guid(e, "typeId"), Number = Int(e, "number"),
        Date = Str(e, "date") ?? string.Empty, Narration = Str(e, "narration"), PartyId = OptGuid(e, "partyId"),
        Cancelled = Bool(e, "cancelled"), PostDated = Bool(e, "postDated"),
        Allocations = (e.Element("allocations")?.Elements("allocation") ?? Enumerable.Empty<XElement>())
            .Select(ReadInventoryAllocation).ToList(),
        DestinationAllocations = (e.Element("destinationAllocations")?.Elements("allocation") ?? Enumerable.Empty<XElement>())
            .Select(ReadInventoryAllocation).ToList(),
        OrderLines = (e.Element("orderLines")?.Elements("orderLine") ?? Enumerable.Empty<XElement>())
            .Select(ReadOrderLine).ToList(),
        PhysicalLines = (e.Element("physicalLines")?.Elements("physicalLine") ?? Enumerable.Empty<XElement>())
            .Select(ReadPhysicalStockLine).ToList(),
        AdditionalCostLines = (e.Element("additionalCostLines")?.Elements("additionalCostLine") ?? Enumerable.Empty<XElement>())
            .Select(a => new AdditionalCostLineDto { LedgerId = Guid(a, "ledgerId"), AmountPaisa = Long(a, "amountPaisa") })
            .ToList(),
        JobWorkOrder = e.Element("jobWorkOrder") is { } jwo ? ReadJobWorkOrder(jwo) : null,
        OrderLinks = (e.Element("orderLinks")?.Elements("orderLink") ?? Enumerable.Empty<XElement>())
            .Select(l => Guid(l, "id")).ToList(),
    };

    private static JobWorkOrderDto ReadJobWorkOrder(XElement e) => new()
    {
        Direction = Str(e, "direction")!, OrderNo = Str(e, "orderNo")!,
        DurationOfProcess = Str(e, "durationOfProcess"), NatureOfProcessing = Str(e, "natureOfProcessing"),
        FinishedGoodStockItemId = Guid(e, "finishedGoodStockItemId"),
        FinishedGoodQuantity = DecReq(e, "finishedGoodQuantity"),
        FinishedGoodDueDate = Str(e, "finishedGoodDueDate"), FinishedGoodGodownId = OptGuid(e, "finishedGoodGodownId"),
        FinishedGoodRatePaisa = OptLong(e, "finishedGoodRatePaisa"),
        TrackingComponents = Bool(e, "trackingComponents"), FillComponentsBomId = OptGuid(e, "fillComponentsBomId"),
        Lines = (e.Element("lines")?.Elements("line") ?? Enumerable.Empty<XElement>())
            .Select(l => new JobWorkOrderLineDto
            {
                ComponentStockItemId = Guid(l, "componentStockItemId"), Track = Str(l, "track")!,
                DueDate = Str(l, "dueDate"), GodownId = OptGuid(l, "godownId"),
                Quantity = DecReq(l, "quantity"), RatePaisa = OptLong(l, "ratePaisa"),
            }).ToList(),
    };

    private static InventoryAllocationDto ReadInventoryAllocation(XElement e) => new()
    {
        StockItemId = Guid(e, "stockItemId"), GodownId = Guid(e, "godownId"), Quantity = DecReq(e, "quantity"),
        Direction = Str(e, "direction")!, RatePaisa = OptLong(e, "ratePaisa"),
        BatchLabel = Str(e, "batchLabel"), UnitId = OptGuid(e, "unitId"),
    };

    private static OrderLineDto ReadOrderLine(XElement e) => new()
    {
        StockItemId = Guid(e, "stockItemId"), GodownId = Guid(e, "godownId"), Quantity = DecReq(e, "quantity"),
        RatePaisa = OptLong(e, "ratePaisa"),
    };

    private static PhysicalStockLineDto ReadPhysicalStockLine(XElement e) => new()
    {
        StockItemId = Guid(e, "stockItemId"), GodownId = Guid(e, "godownId"),
        CountedQuantity = DecReq(e, "countedQuantity"), BatchLabel = Str(e, "batchLabel"),
    };

    // ------------------------------------------------------------- read helpers

    private static string? Str(XElement e, string name) => e.Attribute(name)?.Value;

    private static Guid Guid(XElement e, string name) =>
        System.Guid.TryParse(e.Attribute(name)?.Value, out var g)
            ? g : throw new FormatException($"<{e.Name.LocalName}> @{name} is missing or not a GUID.");

    private static Guid? OptGuid(XElement e, string name) =>
        System.Guid.TryParse(e.Attribute(name)?.Value, out var g) ? g : null;

    private static long Long(XElement e, string name) =>
        long.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : throw new FormatException($"<{e.Name.LocalName}> @{name} is missing or not an integer (paisa).");

    private static long? OptLong(XElement e, string name) =>
        long.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static int Int(XElement e, string name) =>
        int.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static int? OptInt(XElement e, string name) =>
        int.TryParse(e.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool Bool(XElement e, string name) => e.Attribute(name)?.Value == "true";

    private static bool? OptBool(XElement e, string name) =>
        e.Attribute(name)?.Value is { } s ? s == "true" : null;

    private static decimal DecReq(XElement e, string name) =>
        decimal.TryParse(e.Attribute(name)?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v : throw new FormatException($"<{e.Name.LocalName}> @{name} is missing or not a number.");

    private static decimal? OptDec(XElement e, string name) =>
        decimal.TryParse(e.Attribute(name)?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool TryInt(string? s, out int v) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static bool TryGuid(string? s, out Guid v) => System.Guid.TryParse(s, out v);
}
