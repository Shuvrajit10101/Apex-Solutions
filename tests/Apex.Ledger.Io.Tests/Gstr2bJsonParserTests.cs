using System.Text;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-6 <b>GSTR-2B/2A parser</b> gate (RQ-12; DP-13). The parser is pure + deterministic: a fixed portal JSON
/// fixture yields byte-identical DTOs across runs (invariant-culture, ordinal, money as integer paisa), across the
/// B2B / CDNR / IMPG / ISD sections + the FY2025-26 tobacco cess column + the 2B ITC-Available (Y/N) bifurcation + the
/// supplier reverse-charge flag. The offline connector parses with zero credentials; the GSP connector throws
/// NotSupported (2A/2B stay offline-only). A malformed / wrong-recipient-GSTIN file fails fast, importing nothing.
/// </summary>
public sealed class Gstr2bJsonParserTests
{
    private const string RecipientGstin = "27AAPFU0939F1ZV";

    // A faithful-structured GSTR-2B fixture: 2 B2B invoices (one plain 18%, one tobacco 28%+cess flagged ITC-unavailable
    // + reverse-charge), 1 CDNR credit note, 1 IMPG bill-of-entry, 1 ISD document. Money is decimal rupees.
    private const string Fixture = """
        {
          "gstin": "27AAPFU0939F1ZV",
          "rtnprd": "102025",
          "gendt": "14-11-2025",
          "itcsumm": { "igst": 1000.00, "cgst": 500.00, "sgst": 500.00, "cess": 40.00 },
          "docdata": {
            "b2b": [
              { "ctin": "24AAACC1206D1ZM", "trdnm": "Gujarat Supplier", "inv": [
                { "inum": "INV-001", "idt": "10-10-2025", "pos": "27", "rev": "N", "itcavl": "Y",
                  "itms": [ { "itm_det": { "rt": 18, "txval": 10000.00, "iamt": 1800.00 } } ] }
              ] },
              { "ctin": "27AAFCT1234K1Z5", "trdnm": "Tobacco Co", "inv": [
                { "inum": "TOB-9", "idt": "12-10-2025", "pos": "27", "rev": "Y", "itcavl": "N", "rsn": "P",
                  "itms": [ { "itm_det": { "rt": 28, "txval": 5000.00, "camt": 700.00, "samt": 700.00, "csamt": 2000.00 } } ] }
              ] }
            ],
            "cdnr": [
              { "ctin": "24AAACC1206D1ZM", "trdnm": "Gujarat Supplier", "nt": [
                { "nt_num": "CN-5", "nt_dt": "20-10-2025", "ntty": "C", "pos": "27", "rev": "N", "itcavl": "Y",
                  "itms": [ { "itm_det": { "rt": 18, "txval": 1000.00, "iamt": 180.00 } } ] }
              ] }
            ],
            "impg": [
              { "boenum": "BOE-77", "boedt": "05-10-2025", "portcode": "INMAA1", "txval": 20000.00, "iamt": 3600.00, "csamt": 0.00 }
            ],
            "isd": [
              { "ctin": "29AAAAA0000A1Z5", "trdnm": "Head Office ISD", "doclist": [
                { "docnum": "ISD-1", "docdt": "08-10-2025", "itms": [ { "itm_det": { "iamt": 500.00 } } ] }
              ] }
            ]
          }
        }
        """;

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Parse_is_deterministic_across_all_sections()
    {
        var a = Gstr2bJsonParser.Parse(Bytes(Fixture), GstStatementType.Gstr2b, RecipientGstin);
        var b = Gstr2bJsonParser.Parse(Bytes(Fixture), GstStatementType.Gstr2b, RecipientGstin);

        Assert.True(a.Parsed);
        Assert.True(b.Parsed);
        // Byte-identical parse: every scalar field + every line (record structural equality per element) matches. (The
        // whole-DTO record equality can't be used directly because the List<line> member is reference-compared.)
        Assert.Equal(a.Statement! with { Lines = System.Array.Empty<Gstr2bStatementLineDto>() },
                     b.Statement! with { Lines = System.Array.Empty<Gstr2bStatementLineDto>() });
        Assert.True(a.Statement!.Lines.SequenceEqual(b.Statement!.Lines));

        var s = a.Statement!;
        Assert.Equal(GstStatementType.Gstr2b, s.StatementType);
        Assert.Equal("2025-10", s.ReturnPeriod);
        Assert.Equal(RecipientGstin, s.RecipientGstin);
        Assert.Equal(new DateOnly(2025, 11, 14), s.GeneratedOn);
        Assert.Equal(5, s.Lines.Count);

        // Every section is represented.
        Assert.Contains(s.Lines, l => l.DocType == Gstr2bDocType.B2b);
        Assert.Contains(s.Lines, l => l.DocType == Gstr2bDocType.CreditNote);
        Assert.Contains(s.Lines, l => l.DocType == Gstr2bDocType.ImportOfGoods);
        Assert.Contains(s.Lines, l => l.DocType == Gstr2bDocType.Isd);

        // The plain 18% B2B invoice: paisa-exact taxable + IGST, normalised doc-no.
        var inv = s.Lines.Single(l => l.DocNumber == "INV-001");
        Assert.Equal(1_000_000, inv.TaxableValuePaisa);
        Assert.Equal(180_000, inv.IgstPaisa);
        Assert.Equal("INV001", inv.DocNumberNorm); // upper + strip non-alnum; leading-zero trim only bites the string start
        Assert.Equal(new DateOnly(2025, 10, 10), inv.DocDate);

        // The import BoE line uses a non-blank sentinel GSTIN (import has no counterparty GSTIN).
        var impg = s.Lines.Single(l => l.DocType == Gstr2bDocType.ImportOfGoods);
        Assert.Equal("IMPG:INMAA1", impg.SupplierGstin);
        Assert.Equal(2_000_000, impg.TaxableValuePaisa);
        Assert.Equal(360_000, impg.IgstPaisa);
    }

    [Fact]
    public void Parse_reads_tobacco_cess_column_paisa_exact()
    {
        var r = Gstr2bJsonParser.Parse(Bytes(Fixture), GstStatementType.Gstr2b, RecipientGstin);
        var tob = r.Statement!.Lines.Single(l => l.DocNumber == "TOB-9");
        Assert.Equal(500_000, tob.TaxableValuePaisa);
        Assert.Equal(70_000, tob.CgstPaisa);
        Assert.Equal(70_000, tob.SgstPaisa);
        Assert.Equal(200_000, tob.CessPaisa); // FY2025-26 tobacco 2Bs still carry cess
    }

    [Fact]
    public void Parse_captures_itc_available_bifurcation_and_reverse_charge_flag()
    {
        var r = Gstr2bJsonParser.Parse(Bytes(Fixture), GstStatementType.Gstr2b, RecipientGstin);
        var lines = r.Statement!.Lines;

        var available = lines.Single(l => l.DocNumber == "INV-001");
        Assert.True(available.ItcAvailable);
        Assert.Null(available.ItcUnavailableReason);
        Assert.False(available.ReverseCharge);

        var unavailable = lines.Single(l => l.DocNumber == "TOB-9");
        Assert.False(unavailable.ItcAvailable);
        Assert.Equal("P", unavailable.ItcUnavailableReason);
        Assert.True(unavailable.ReverseCharge); // supplier-flagged RCM (bypasses IMS)
    }

    [Fact]
    public void Offline_connector_parses_with_zero_credentials_gsp_throws()
    {
        IGstPortalConnector offline = new OfflineJsonConnector(); // no credentials at all
        var result = offline.FetchStatement(new Gstr2bFetchRequest(Bytes(Fixture), GstStatementType.Gstr2b, RecipientGstin));
        Assert.True(result.Parsed);
        Assert.Equal(5, result.Statement!.Lines.Count);

        IGstPortalConnector gsp = new GspConnector();
        Assert.Throws<NotSupportedException>(() =>
            gsp.FetchStatement(new Gstr2bFetchRequest(Bytes(Fixture), GstStatementType.Gstr2b, RecipientGstin)));
    }

    [Fact]
    public void Malformed_or_wrong_recipient_fails_fast_importing_nothing()
    {
        var bad = Gstr2bJsonParser.Parse(Bytes("{ not json ]"), GstStatementType.Gstr2b, RecipientGstin);
        Assert.False(bad.Parsed);
        Assert.Null(bad.Statement);
        Assert.Equal("MALFORMED_JSON", bad.ErrorCode);

        var wrongGstin = Gstr2bJsonParser.Parse(Bytes(Fixture), GstStatementType.Gstr2b, "24AAACC1206D1ZM");
        Assert.False(wrongGstin.Parsed);
        Assert.Null(wrongGstin.Statement);
        Assert.Equal("GSTIN_MISMATCH", wrongGstin.ErrorCode);
    }

    [Fact]
    public void Malformed_itcsumm_value_fails_fast_with_bad_summary()
    {
        // Finding #3: a malformed itcsumm money value must fail fast (Parsed = false, BAD_SUMMARY) like docdata/gendt/
        // rtnprd — NOT throw an opaque exception out of Parse.
        const string badSummary = """
            {
              "gstin": "27AAPFU0939F1ZV",
              "rtnprd": "102025",
              "itcsumm": { "igst": "not-a-number", "cgst": 0.00, "sgst": 0.00, "cess": 0.00 },
              "docdata": {}
            }
            """;
        var r = Gstr2bJsonParser.Parse(Bytes(badSummary), GstStatementType.Gstr2b, RecipientGstin);
        Assert.False(r.Parsed);
        Assert.Null(r.Statement);
        Assert.Equal("BAD_SUMMARY", r.ErrorCode);
    }

    [Fact]
    public void Source_file_hash_is_stable_and_content_addressed()
    {
        var a = Gstr2bJsonParser.Parse(Bytes(Fixture), GstStatementType.Gstr2b, RecipientGstin);
        var b = Gstr2bJsonParser.Parse(Bytes(Fixture + " "), GstStatementType.Gstr2b, RecipientGstin);
        Assert.Equal(a.Statement!.SourceFileHash, Gstr2bJsonParser.Parse(Bytes(Fixture), GstStatementType.Gstr2b, RecipientGstin).Statement!.SourceFileHash);
        Assert.NotEqual(a.Statement!.SourceFileHash, b.Statement!.SourceFileHash); // a changed byte ⇒ a changed hash
    }
}
