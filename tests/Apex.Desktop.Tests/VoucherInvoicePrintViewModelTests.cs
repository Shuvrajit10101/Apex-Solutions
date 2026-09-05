using System;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-10 (RQ-10 voucher print, RQ-11 tax-invoice print, RQ-12 F12 print config):
/// Print (P/Ctrl+P) on a drilled voucher renders THAT voucher — a de-branded GST <b>tax invoice</b> for a Sales
/// item-invoice (both GSTINs, per-rate CGST/SGST or IGST, amount-in-words) or the plain Dr/Cr voucher otherwise —
/// via <c>Apex.Ledger.Io</c>; the F12 knobs (title override, narration on/off, copy marking) re-render the bytes.
///
/// <para>The renderers themselves are trusted (covered by <c>Apex.Ledger.Io.Tests</c>); these tests pin the thin
/// Avalonia layer: the drill → Print routing, the voucher-vs-invoice choice, the projected figures reconciling to
/// the posted tax ledgers, and the F12 config re-render. Every produced byte stream carries no "tally"
/// (case-insensitive) anywhere (RQ-13 de-brand). A real GST company + posted Sales item-invoice is built over a
/// throwaway <c>.db</c>, exactly like the GST item-invoice tests — no UI toolkit.</para>
/// </summary>
public sealed class VoucherInvoicePrintViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public VoucherInvoicePrintViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexVoucherPrintTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>
    /// The PDF bytes as text with content-stream string escapes resolved — <c>\(</c>, <c>\)</c> and <c>\\</c>.
    /// <para><b>Why this exists.</b> Round brackets are PDF string delimiters, so <c>PdfWriter</c> escapes them:
    /// a line printed as <c>State: Maharashtra (27)</c> appears in the raw bytes as
    /// <c>State: Maharashtra \(27\)</c>. Searching <see cref="AsLatin1"/> for the literal printed text therefore
    /// returns -1 and looks exactly like the line being ABSENT from the document. Every future bracketed
    /// assertion — state codes, HSN qualifiers, tax-rate labels — hits this. Use this helper whenever the
    /// expected substring contains a bracket; <see cref="AsLatin1"/> remains correct for everything else.</para>
    /// </summary>
    private static string AsPdfText(byte[] bytes) =>
        AsLatin1(bytes).Replace("\\(", "(").Replace("\\)", ")").Replace("\\\\", "\\");

    // ---------------------------------------------------------------- scaffolding (mirrors the GST invoice tests)

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid WidgetId { get; init; }        // 18%
        public required Guid GadgetId { get; init; }        // 5%
        public required Guid ExemptItemId { get; init; }    // exempt (no GST)
        public required Guid MainGodownId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid LocalCustomerId { get; init; } // in-state (27), B2B
        public required Guid InterCustomerId { get; init; } // Gujarat (24), inter-state
    }

    /// <param name="captureProfile">
    /// When supplied, the company's postal block is TYPED INTO THE REAL CREATION SCREEN instead of being
    /// assigned onto the aggregate afterwards — the difference between "the printer can render an address" and
    /// "a user can produce one", which is the whole point of the capture half.
    /// </param>
    private Kit NewGstKit(string companyName, Action<CompanyProfileViewModel>? captureProfile = null)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        captureProfile?.Invoke(vm.CreateCompanyProfile);
        vm.CreateCompany();

        var c = vm.Company!;
        if (captureProfile is null)
        {
            c.MailingName = "Acme Traders Pvt Ltd";
            c.Address = "12 Industrial Estate\nPune, Maharashtra 411001";
        }
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails { HsnSac = "852990", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };
        var exempt = inv.CreateStockItem("Exempt Item", grp.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "100610", Taxability = GstTaxability.Exempt };

        inv.AddOpeningBalance(widget.Id, main, 500m, Money.FromRupees(100m));
        inv.AddOpeningBalance(gadget.Id, main, 500m, Money.FromRupees(20m));
        inv.AddOpeningBalance(exempt.Id, main, 500m, Money.FromRupees(20m));

        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var localCustomer = AddLedger(c, "Local Customer", "Sundry Debtors");
        localCustomer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var interCustomer = AddLedger(c, "Gujarat Customer", "Sundry Debtors");
        interCustomer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            WidgetId = widget.Id,
            GadgetId = gadget.Id,
            ExemptItemId = exempt.Id,
            MainGodownId = main,
            SalesLedgerId = sales.Id,
            LocalCustomerId = localCustomer.Id,
            InterCustomerId = interCustomer.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    private static void FillItemLine(VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal qty, string rate, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    /// <summary>Posts a Sales item-invoice through the real entry VM and returns the posted voucher.</summary>
    private static Voucher PostSaleInvoice(Kit k, Guid partyId, Action<VoucherEntryViewModel> fill, string? narration = null)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, partyId);
        fill(entry);
        if (narration is not null) entry.Narration = narration;
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        return c.Vouchers.Single(v => v.TypeId == type.Id);
    }

    /// <summary>Opens the drilled voucher-detail column, then Print (P/Ctrl+P), and returns the preview VM.</summary>
    private PrintPreviewViewModel PrintDrilledVoucher(MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenVoucherDetail(voucherId);
        Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
        Assert.True(vm.IsPrintablePage);
        vm.OpenPrintPreview();
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.NotNull(vm.PrintPreview);
        return vm.PrintPreview!;
    }

    // ================================================================ RQ-11: tax-invoice print (intra: both GSTINs)

    [Fact]
    public void Printing_a_sales_item_invoice_yields_a_tax_invoice_pdf_with_both_gstins_and_amount_in_words()
    {
        var k = NewGstKit("Print Invoice Co");
        // 10 Widget @ ₹875 = ₹8,750 @ 18% intra ⇒ CGST 787.50 + SGST 787.50; grand total ₹10,325.00.
        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "875.00"));

        var preview = PrintDrilledVoucher(k.Vm, v.Id);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice, preview.Kind);
        Assert.True(preview.SupportsPrintConfig);

        var text = AsLatin1(preview.PdfBytes);
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("TAX INVOICE", text);
        Assert.Contains(GstinMaharashtra, text);      // seller GSTIN
        // Buyer GSTIN is the same registration in this fixture; assert it is present as the recipient too.
        Assert.Contains("847130", text);              // HSN
        Assert.Contains("8,750.00", text);            // taxable value
        Assert.Contains("787.50", text);              // CGST == SGST (engine)
        Assert.Contains("10,325.00", text);           // grand total
        // Amount in words (Indian numbering, from the pure Io layer).
        Assert.Contains("Rupees Ten Thousand Three Hundred Twenty Five", text);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Apex Solutions", text);      // de-branded metadata / footer
    }

    // ================================================================ WI-4: the buyer address actually prints

    [Fact]
    public void The_party_mailing_address_and_PIN_print_in_the_invoice_recipient_block()
    {
        // Before WI-4 this was hardcoded: VoucherPrintProjector emitted `AddressLines = Array.Empty<string>()`
        // with a comment saying a party ledger had no address field — so EVERY invoice this app printed carried a
        // blank recipient address. That is the regression this test exists to prevent recurring.
        var k = NewGstKit("Print Buyer Address Co");
        var party = k.Vm.Company!.FindLedger(k.LocalCustomerId)!;
        party.Mailing = new PartyMailingDetails
        {
            MailingName = "Naresh Traders Private Limited",
            Address = "12 Park Street\nBallygunge",
            Country = "India",
            Pincode = "700019",
        };

        var invoice = VoucherPrintProjector.ProjectInvoice(
            k.Vm.Company!,
            PostSaleInvoice(k, k.LocalCustomerId, e => FillItemLine(e, k.WidgetId, k.MainGodownId, 1m, "100.00")));

        // The projected DTO carries each address line, the country, and the PIN as its own final line.
        Assert.Equal("Naresh Traders Private Limited", invoice.Buyer.Name);
        Assert.Equal(
            new[] { "12 Park Street", "Ballygunge", "India", "PIN: 700019" },
            invoice.Buyer.AddressLines);

        // …and they reach the rendered PDF, which is what the CA actually looks at.
        var preview = PrintDrilledVoucher(k.Vm, k.Vm.Company!.Vouchers.Last().Id);
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("Naresh Traders Private Limited", text);
        Assert.Contains("12 Park Street", text);
        Assert.Contains("Ballygunge", text);
        Assert.Contains("700019", text);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_party_with_no_mailing_block_still_prints_exactly_as_before()
    {
        // ER-13 at the print boundary: an unaffected company's invoice is unchanged — a blank recipient address
        // and the ledger's own name.
        var k = NewGstKit("Print No Address Co");
        var party = k.Vm.Company!.FindLedger(k.LocalCustomerId)!;
        Assert.Null(party.Mailing);

        var invoice = VoucherPrintProjector.ProjectInvoice(
            k.Vm.Company!,
            PostSaleInvoice(k, k.LocalCustomerId, e => FillItemLine(e, k.WidgetId, k.MainGodownId, 1m, "100.00")));

        Assert.Equal(party.Name, invoice.Buyer.Name);
        Assert.Empty(invoice.Buyer.AddressLines);
    }

    // ========================================================== W0-2a / T0-8 (PRINT half only): the SUPPLIER block

    // These tests cover W0-2a — the print half of T0-8. They do NOT deliver W0-2b (the Company Create/Alter
    // screen), and nothing here reads Company.State under any shape, which is what makes this half independent
    // of the open R12 user gate. See plan.md W0-2a / W0-2b.

    /// <summary>
    /// <b>T0-8, print half.</b> Given a company whose postal address HAS been captured, the supplier block prints
    /// every component the WI-4 recipient block prints: the address lines, then Country, then the PIN as its own
    /// final line. Before this slice the supplier dropped Country and PIN, and <c>Company.Pin</c> had <b>zero
    /// readers in the whole print path</b> — its only references outside persistence were the canonical
    /// import/export copy sites.
    /// <para><b>This is a fidelity/parity test, NOT a statutory-delivery test.</b> Rule 46(a) requires "name,
    /// address and GSTIN"; Country and PIN are neither
    /// (<c>docs/w0-2-company-screen-grounding.md</c> §5.5: "Pin Code, Telephone, Mobile, Fax, E-Mail and Website
    /// are Tally-fidelity fields, not compliance fields"). What Rule 46(a) genuinely delivers today is pinned
    /// separately by <see cref="The_Rule_46a_name_and_GSTIN_pair_is_delivered_but_the_address_half_is_not"/>.
    /// The fixture state here — a captured <c>Address</c> — is one <b>no book on disk can currently reach</b>,
    /// because no UI writes that field.</para>
    /// </summary>
    [Fact]
    public void A_company_with_a_captured_postal_address_prints_every_component_the_recipient_block_prints()
    {
        var k = NewGstKit("Print Seller Address Co");
        var c = k.Vm.Company!;
        c.MailingName = "Acme Traders Private Limited";
        c.Address = "37B Kalyani Nagar\nYerawada";
        c.Country = "India";
        c.Pin = "411037";

        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 7m, "1313.57"));
        var invoice = VoucherPrintProjector.ProjectInvoice(c, v);

        Assert.Equal("Acme Traders Private Limited", invoice.Seller.Name);
        // Every captured component, PIN as its own final line, exactly as the recipient block does.
        Assert.Equal(
            new[] { "37B Kalyani Nagar", "Yerawada", "India", "PIN: 411037" },
            invoice.Seller.AddressLines);
        Assert.Equal(GstinMaharashtra, invoice.Seller.Gstin);

        // …and it reaches the rendered PDF, which is the document the buyer actually receives.
        var text = AsLatin1(PrintDrilledVoucher(k.Vm, v.Id).PdfBytes);
        Assert.Contains("Acme Traders Private Limited", text);
        Assert.Contains("37B Kalyani Nagar", text);
        Assert.Contains("Yerawada", text);
        Assert.Contains("PIN: 411037", text);       // 411037 appears nowhere else in this fixture
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What CGST Rule 46(a) — "name, address and GSTIN of the supplier" — delivers on a company whose address
    /// was never captured: the name and the GSTIN, and no address.
    ///
    /// <para><b>REWORDED when the capture screen shipped, deliberately rather than deleted.</b> Its old name
    /// said "the address half is NOT delivered", which was a statement about the PRODUCT: <c>Company.Address</c>
    /// had no assignment site anywhere in the desktop layer, so no book could carry one. That is now false —
    /// <c>A_company_created_through_the_screen_prints_a_Rule_46a_compliant_supplier_block</c> types the address
    /// in and prints it. What this fixture still pins is narrower and still worth pinning: a company that
    /// carries NO address prints none, rather than a bare "India" line manufactured from the country default.
    /// The behaviour is unchanged; only the claim its name was making has moved.</para>
    /// </summary>
    [Fact]
    public void The_Rule_46a_name_and_GSTIN_pair_survive_on_a_company_whose_address_was_never_captured()
    {
        var k = NewGstKit("Print Rule46a Co");
        var c = k.Vm.Company!;
        c.MailingName = "Acme Traders Private Limited";
        c.Address = null;   // an address the operator never typed

        var invoice = VoucherPrintProjector.ProjectInvoice(c,
            PostSaleInvoice(k, k.LocalCustomerId,
                e => FillItemLine(e, k.WidgetId, k.MainGodownId, 3m, "917.13")));

        Assert.Equal("Acme Traders Private Limited", invoice.Seller.Name);   // (a) name  ✔
        Assert.Equal(GstinMaharashtra, invoice.Seller.Gstin);                // (a) GSTIN ✔
        Assert.Empty(invoice.Seller.AddressLines);                           // no address captured, none printed
    }

    /// <summary>
    /// 🔴 THE END-TO-END CLAIM THIS SLICE EXISTS TO MAKE: a company created THROUGH THE CREATION SCREEN prints
    /// a CGST Rule 46(a)-compliant supplier block — <i>name, address and GSTIN of the supplier</i> — all three,
    /// in the projection AND in the bytes the buyer receives. Before the screen existed the address particular
    /// was unreachable from the product, so every invoice this application could produce breached it.
    ///
    /// <para><b>The fixture is deliberately awkward, and each oddity is load-bearing:</b></para>
    /// <list type="bullet">
    /// <item><b>Mailing Name ≠ Name.</b> Rule 46(a)'s supplier NAME maps to the Mailing Name, and a fixture
    /// where the two are equal cannot tell them apart.</item>
    /// <item><b>A three-line address with a BLANK middle line</b>, taken from the corpus's own worked example —
    /// so the split must drop the empty entry and print three lines, not four.</item>
    /// <item><b>An address line containing a comma</b> — pinning the newline-only split, so
    /// "13A, Picnic Garden Road" stays ONE line.</item>
    /// <item><b>A postal State that disagrees with the GST State.</b> Kerala typed on the screen against a
    /// Maharashtra registration: the printed State must still be the GST one, now proved from the capture
    /// side rather than only from an assigned aggregate.</item>
    /// </list>
    /// <para><i>Mutation that reddens it:</i> stop applying the typed postal fields in the creation path.</para>
    /// </summary>
    [Fact]
    public void A_company_created_through_the_screen_prints_a_Rule_46a_compliant_supplier_block()
    {
        var k = NewGstKit("Print Captured Co", profile =>
        {
            profile.MailingName = "Bright Traders";
            profile.Address = "13A, Picnic Garden Road\n\n3rd Lane\nKolkata";
            profile.SelectedState = profile.StateOptions.Single(o => o.State?.Name == "Kerala");
            profile.Pin = "700039";
        });

        var c = k.Vm.Company!;
        Assert.Equal("Print Captured Co", c.Name);          // the Name and the Mailing Name really do differ
        Assert.Equal("Bright Traders", c.MailingName);

        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 3m, "917.13"));
        var invoice = VoucherPrintProjector.ProjectInvoice(c, v);

        // (a) name — the Mailing Name, not the company Name.
        Assert.Equal("Bright Traders", invoice.Seller.Name);
        // (a) GSTIN.
        Assert.Equal(GstinMaharashtra, invoice.Seller.Gstin);
        // (a) address — three lines: the blank middle entry is dropped, and the comma does NOT split a line.
        Assert.Equal(
            new[] { "13A, Picnic Garden Road", "3rd Lane", "Kolkata", "India", "PIN: 700039" },
            invoice.Seller.AddressLines.ToArray());
        // The printed State is the GST registration's, even though the postal one says Kerala.
        Assert.Equal("Kerala", c.State);
        Assert.Equal("Maharashtra (27)", invoice.Seller.StateText);

        // …and all of it reaches the rendered PDF, which is the document the buyer actually receives.
        var text = AsPdfText(PrintDrilledVoucher(k.Vm, v.Id).PdfBytes);
        Assert.Contains("Bright Traders", text);
        Assert.Contains("13A, Picnic Garden Road", text);
        Assert.Contains("3rd Lane", text);
        Assert.Contains("PIN: 700039", text);
        Assert.Contains("State: Maharashtra (27)", text);
        Assert.DoesNotContain("Kerala", text);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>The State ruling, pinned.</b> <c>SellerBlock</c> takes the printed State from the GST home State and
    /// never from the postal <c>Company.State</c>. This fixture makes the two <b>DISAGREE</b> — postal "Kerala"
    /// against GST home code 27 — which is a state a canonical import can produce today
    /// (<c>CanonicalXml</c> writes and reads a company <c>state</c> attribute, and nothing ties it to
    /// <c>Gst.HomeStateCode</c>).
    /// <para><b>Why an agreeing fixture was not enough.</b> The original test set <c>State = "Maharashtra"</c>,
    /// agreeing with code 27, so both designs produced the same string and the assertion could not discriminate.
    /// Mutating <c>SellerBlock</c> to prefer the postal State
    /// (<c>StateText(IndianState.FromName(company.State)?.Code ?? company.Gst?.HomeStateCode)</c>) left the whole
    /// Desktop suite green. Under that mutation this test prints "Kerala (32)" and fails on all three
    /// assertions — the invoice would claim Kerala while GSTR-1 filed 27, the self-refuting invoice/return pair
    /// that HEAD <c>85f82dd</c> closed for the buyer side.</para>
    /// </summary>
    [Fact]
    public void A_company_whose_postal_State_disagrees_with_its_GST_State_prints_the_GST_one()
    {
        var k = NewGstKit("Print Divergent State Co");
        var c = k.Vm.Company!;
        c.Address = "37B Kalyani Nagar\nYerawada";
        c.State = "Kerala";        // postal State (code 32) — deliberately NOT the GST home State (27)
        Assert.Equal("27", c.Gst!.HomeStateCode);

        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 3m, "917.13"));
        var invoice = VoucherPrintProjector.ProjectInvoice(c, v);

        // Pre-PDF and unescaped, so the bracketed code can be asserted exactly.
        Assert.Equal("Maharashtra (27)", invoice.Seller.StateText);

        // …and in the rendered document. AsPdfText resolves the escaped brackets; AsLatin1 would not match.
        var text = AsPdfText(PrintDrilledVoucher(k.Vm, v.Id).PdfBytes);
        Assert.Contains("State: Maharashtra (27)", text);
        Assert.DoesNotContain("Kerala", text);
    }

    /// <summary>
    /// The asymmetry itself, asserted directly rather than inferred: given the SAME postal components on the
    /// company and on the party, the two printed address blocks must be identical. Before this slice the supplier
    /// got 2 lines where the recipient got 4.
    /// </summary>
    [Fact]
    public void The_supplier_address_block_is_built_exactly_like_the_recipient_one()
    {
        var k = NewGstKit("Print Symmetric Address Co");
        var c = k.Vm.Company!;
        c.Address = "37B Kalyani Nagar\nYerawada";
        c.Country = "India";
        c.Pin = "411037";

        var party = c.FindLedger(k.LocalCustomerId)!;
        party.Mailing = new PartyMailingDetails
        {
            MailingName = "Naresh Traders Private Limited",
            Address = "37B Kalyani Nagar\nYerawada",
            Country = "India",
            Pincode = "411037",
        };

        var invoice = VoucherPrintProjector.ProjectInvoice(c,
            PostSaleInvoice(k, k.LocalCustomerId,
                e => FillItemLine(e, k.WidgetId, k.MainGodownId, 3m, "917.13")));

        Assert.Equal(invoice.Buyer.AddressLines, invoice.Seller.AddressLines);
        Assert.Equal(4, invoice.Seller.AddressLines.Count);
    }

    /// <summary>
    /// The PIN append is CONDITIONAL: a company with an address but no PIN prints its address lines and its
    /// country, and nothing else — no bare "PIN:" line. <b>This is the PIN guard's proof.</b>
    /// <para><b>Rewritten 2026-08-15, twice over.</b> (1) This comment used to claim it discriminated <i>both</i>
    /// blank-guards; measured, it bites only the PIN half — removing the COUNTRY guard leaves it green, because
    /// <c>SplitAddress</c> drops blank entries so a null-safe <c>country!.Trim()</c> on a blank value just adds an
    /// empty string that is silently swallowed. The country guard's real proof is
    /// <see cref="A_party_with_an_address_and_PIN_but_no_country_prints_both_and_does_not_crash"/>, which walks
    /// the NULL path on the party side. (2) The fixture used to set <c>c.Country = "  "</c>, and setting it to
    /// <c>null</c> instead does not work either: <c>companies.country</c> is <c>TEXT NOT NULL</c>
    /// (<c>Schema.cs</c>), written unconditionally and read with <c>GetString</c>, so a null-Country company
    /// <b>cannot be saved at all</b> — the posting step throws before any assertion runs. A company with no
    /// country is not a state this product can be in, which is exactly why the country guard is provable only on
    /// the party side. <c>Country</c> is therefore left at its real default here.</para>
    /// </summary>
    [Fact]
    public void A_company_with_an_address_but_no_PIN_prints_no_stray_PIN_line()
    {
        var k = NewGstKit("Print Bare Address Co");
        var c = k.Vm.Company!;
        c.Address = "37B Kalyani Nagar\nYerawada";
        c.Pin = null;
        Assert.Equal("India", c.Country);   // untouched: the real, unavoidable default

        var invoice = VoucherPrintProjector.ProjectInvoice(c,
            PostSaleInvoice(k, k.LocalCustomerId,
                e => FillItemLine(e, k.WidgetId, k.MainGodownId, 3m, "917.13")));

        Assert.Equal(new[] { "37B Kalyani Nagar", "Yerawada", "India" }, invoice.Seller.AddressLines);
        Assert.DoesNotContain(invoice.Seller.AddressLines, l => l.StartsWith("PIN", StringComparison.Ordinal));
    }

    /// <summary>
    /// A company with no postal address at all still prints exactly what it printed before (ER-13): the block
    /// collapses to name + GSTIN, with no placeholder and no stray country/PIN line.
    /// <para><b>🔴 This test used to be doctored, and the doctoring hid a shipped regression.</b> It set
    /// <c>c.Country = "  "</c> — a value the product cannot produce. <c>Company.Country</c> is non-null and
    /// defaults to <c>"India"</c>, storage writes it unconditionally, and nothing in <c>src/Apex.Desktop</c> ever
    /// assigns it, so EVERY company in EVERY book on disk has <c>Country = "India"</c> and a blank Address.
    /// Against the real default the unguarded projector printed a supplier block containing exactly one line,
    /// "India", where it had printed none — changing every invoice and every reprint of every historical invoice,
    /// and replacing a visibly blank block with one that looks populated while still carrying no Rule 46(a)
    /// address. <c>Country</c> is left at its real default below; the guard under test is
    /// <c>SupplierPostalAddressText</c>.</para>
    /// </summary>
    [Fact]
    public void A_company_with_no_address_still_prints_exactly_as_before()
    {
        var k = NewGstKit("Print No Seller Address Co");
        var c = k.Vm.Company!;
        c.Address = null;
        c.Pin = null;
        Assert.Equal("India", c.Country);   // NOT doctored: the real default, on the real creation path

        var invoice = VoucherPrintProjector.ProjectInvoice(c,
            PostSaleInvoice(k, k.LocalCustomerId,
                e => FillItemLine(e, k.WidgetId, k.MainGodownId, 3m, "917.13")));

        Assert.Empty(invoice.Seller.AddressLines);
        Assert.Equal(GstinMaharashtra, invoice.Seller.Gstin);
    }

    /// <summary>
    /// The companion to the test above, and the only fixture that represents <b>any book on disk today</b>: a
    /// company built by the real <c>CreateCompany()</c> path with nothing else set. Its postal state is read off
    /// that path rather than hand-written, so the fixture cannot drift away from what the product actually
    /// produces — if <c>CreateCompany()</c> ever starts capturing an address, the probe assertions fail here
    /// first.
    /// </summary>
    [Fact]
    public void A_freshly_created_company_prints_no_supplier_address_lines_at_all()
    {
        // What the ONLY company-creation path in the product actually produces.
        var probeVm = new MainWindowViewModel(_storage) { NewCompanyName = "Freshly Created Probe Co" };
        probeVm.CreateCompany();
        var fresh = probeVm.Company!;
        Assert.True(string.IsNullOrWhiteSpace(fresh.Address));   // no assignment site in src/Apex.Desktop
        Assert.Equal("India", fresh.Country);                    // non-null default, never typed by anyone
        Assert.Null(fresh.Pin);

        // Reproduce exactly that postal state on a printable fixture.
        var k = NewGstKit("Print Fresh Company Co");
        var c = k.Vm.Company!;
        c.Address = fresh.Address;
        c.Country = fresh.Country;
        c.Pin = fresh.Pin;

        var invoice = VoucherPrintProjector.ProjectInvoice(c,
            PostSaleInvoice(k, k.LocalCustomerId,
                e => FillItemLine(e, k.WidgetId, k.MainGodownId, 3m, "917.13")));

        Assert.Empty(invoice.Seller.AddressLines);

        // And nothing stray reaches the rendered document either.
        var text = AsLatin1(PrintDrilledVoucher(k.Vm, k.Vm.Company!.Vouchers.Last().Id).PdfBytes);
        Assert.DoesNotContain("PIN:", text);
        Assert.DoesNotContain("India", text);
    }

    /// <summary>
    /// The country blank-guard is <b>load-bearing, and this is the test that proves it</b>. A party may capture an
    /// address and a PIN and leave Country empty — <c>PartyMailingDetails.Country</c> is nullable and the mailing
    /// screen does not require it — and the shared address builder must skip it rather than dereference it.
    /// <para><b>Why this test exists at all:</b> mutating the guard away to <c>country!.Trim()</c> left the ENTIRE
    /// 2,129-test Desktop suite green, because every party fixture in the repository happened to set Country and
    /// every company defaults it to "India". The guard was therefore unprovable — dead by the standard this repo
    /// holds itself to — until a fixture existed that actually walks the null. Note the whitespace half of the same
    /// guard is genuinely redundant (<see cref="SplitAddress"/> drops blank entries); the NULL half is not.</para>
    /// </summary>
    [Fact]
    public void A_party_with_an_address_and_PIN_but_no_country_prints_both_and_does_not_crash()
    {
        var k = NewGstKit("Print No Country Co");
        var party = k.Vm.Company!.FindLedger(k.LocalCustomerId)!;
        party.Mailing = new PartyMailingDetails
        {
            MailingName = "Naresh Traders Private Limited",
            Address = "37B Kalyani Nagar\nYerawada",
            Country = null,
            Pincode = "411037",
        };

        var invoice = VoucherPrintProjector.ProjectInvoice(k.Vm.Company!,
            PostSaleInvoice(k, k.LocalCustomerId,
                e => FillItemLine(e, k.WidgetId, k.MainGodownId, 3m, "917.13")));

        Assert.Equal(
            new[] { "37B Kalyani Nagar", "Yerawada", "PIN: 411037" },
            invoice.Buyer.AddressLines);
    }

    // ================================================================ RQ-11: inter-state IGST + place of supply

    [Fact]
    public void Printing_an_inter_state_sale_yields_a_tax_invoice_with_igst()
    {
        var k = NewGstKit("Print Inter Invoice Co");
        // 20 Widget @ ₹100 = ₹2,000 @ 18% inter ⇒ IGST ₹360; grand total ₹2,360.
        var v = PostSaleInvoice(k, k.InterCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 20m, "100.00"));

        var preview = PrintDrilledVoucher(k.Vm, v.Id);
        var text = AsLatin1(preview.PdfBytes);

        Assert.Contains("TAX INVOICE", text);
        Assert.Contains("360.00", text);              // IGST (engine)
        Assert.Contains("2,360.00", text);            // grand total
        Assert.Contains("Gujarat", text);             // place of supply (inter-state)
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ RQ-10: plain voucher print (non-item Sales -> voucher)

    [Fact]
    public void Printing_a_plain_voucher_yields_a_voucher_pdf_not_a_tax_invoice()
    {
        var k = NewGstKit("Print Plain Voucher Co");
        var c = k.Vm.Company!;

        // Post a plain (accounting-only) Journal voucher — no inventory lines ⇒ plain voucher print.
        var cash = AddLedger(c, "Cash", "Cash-in-Hand");
        var sales = c.FindLedger(k.SalesLedgerId)!;
        var jType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal && t.IsActive);
        var voucher = new Voucher(
            Guid.NewGuid(), jType.Id, FyStart,
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(500m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(500m), DrCr.Credit),
            },
            number: 1, narration: "Cash sale");
        new LedgerService(c).Post(voucher);
        _storage.Save(c);

        var preview = PrintDrilledVoucher(k.Vm, voucher.Id);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, preview.Kind);

        var text = AsLatin1(preview.PdfBytes);
        Assert.StartsWith("%PDF-", text);
        Assert.DoesNotContain("TAX INVOICE", text);   // it is NOT the invoice template
        Assert.Contains("Cash", text);                // Dr line ledger name
        Assert.Contains("500.00", text);
        Assert.Contains("Cash sale", text);           // narration (default on)
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ RQ-12: F12 title override + copy marking change bytes

    [Fact]
    public void F12_title_override_and_copy_marking_change_the_produced_bytes()
    {
        var k = NewGstKit("Print F12 Co");
        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "100.00"));
        var preview = PrintDrilledVoucher(k.Vm, v.Id);

        var baseline = preview.PdfBytes;

        // Open the F12 print-config panel, change the title + copy marking, apply.
        k.Vm.OpenPrintConfig();
        Assert.Equal(Screen.PrintConfig, k.Vm.CurrentScreen);
        var panel = k.Vm.PrintConfigPanel!;
        panel.TitleOverride = "PROFORMA INVOICE";
        panel.CopyMarking = CopyMarking.Original;
        panel.Apply();

        var changed = preview.PdfBytes;
        Assert.NotEqual(baseline, changed);                       // bytes changed
        var text = AsLatin1(changed);
        Assert.Contains("PROFORMA INVOICE", text);                // title override applied
        Assert.Contains("ORIGINAL FOR RECIPIENT", text);          // copy marking label
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ RQ-12: narration toggle

    [Fact]
    public void F12_narration_toggle_adds_or_removes_the_narration_line()
    {
        var k = NewGstKit("Print Narration Co");
        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "100.00"),
            narration: "Sold on credit terms 30 days");
        var preview = PrintDrilledVoucher(k.Vm, v.Id);

        Assert.Contains("Sold on credit terms 30 days", AsLatin1(preview.PdfBytes)); // default on

        preview.ShowNarration = false;
        Assert.DoesNotContain("Sold on credit terms 30 days", AsLatin1(preview.PdfBytes));

        preview.ShowNarration = true;
        Assert.Contains("Sold on credit terms 30 days", AsLatin1(preview.PdfBytes));
    }

    // ================================================================ multi-rate invoice reconciles to the engine

    [Fact]
    public void Multi_rate_invoice_print_shows_both_rate_breakups_reconciling_to_the_engine()
    {
        var k = NewGstKit("Print Multi Rate Co");
        // 10 Widget @ ₹100 (18%) + 10 Gadget @ ₹100 (5%) ⇒ CGST 115, SGST 115, taxable 2000, grand 2230.
        var v = PostSaleInvoice(k, k.LocalCustomerId, e =>
        {
            FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "100.00", index: 0);
            FillItemLine(e, k.GadgetId, k.MainGodownId, 10m, "100.00", index: 1);
        });
        var preview = PrintDrilledVoucher(k.Vm, v.Id);
        var text = AsLatin1(preview.PdfBytes);

        Assert.Contains("18%", text);
        Assert.Contains("5%", text);
        Assert.Contains("2,000.00", text);   // taxable
        Assert.Contains("2,230.00", text);   // grand total
        Assert.Contains("115.00", text);     // per-head CGST/SGST total
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ Fix 1: exempt line is not dropped from totals

    [Fact]
    public void Invoice_with_an_exempt_line_foots_the_full_goods_value_not_only_the_rated_lines()
    {
        var k = NewGstKit("Print Exempt Mix Co");
        // 10 Widget @ ₹875 = ₹8,750 (18% ⇒ tax ₹1,575) + 10 Exempt @ ₹200 = ₹2,000 (no tax).
        // Taxable Value must be the FULL goods value 10,750; tax 1,575; grand total 12,325 — the 2,000 is NOT dropped.
        var v = PostSaleInvoice(k, k.LocalCustomerId, e =>
        {
            FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "875.00", index: 0);
            FillItemLine(e, k.ExemptItemId, k.MainGodownId, 10m, "200.00", index: 1);
        });

        // Assert the projected DTO foots correctly (the render is covered separately).
        var c = k.Vm.Company!;
        var invoice = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(10750m, invoice.TotalTaxable.Amount);   // 8,750 + 2,000 exempt (was 8,750 before the fix)
        Assert.Equal(1575m, invoice.TotalTax.Amount);        // tax only on the rated line
        Assert.Equal(12325m, invoice.GrandTotal.Amount);     // full goods value + tax

        var preview = PrintDrilledVoucher(k.Vm, v.Id);
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("10,750.00", text);                  // taxable value = full goods value
        Assert.Contains("12,325.00", text);                  // grand total
        Assert.Contains("Rupees Twelve Thousand Three Hundred Twenty Five", text);   // words match the grand total
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ F12 over a report preview

    /// <summary>
    /// 🔴 <b>W2-31 (census 12.4) CHANGED THIS BEHAVIOUR DELIBERATELY, and the assertion is rewritten rather than
    /// deleted.</b> This test used to read <c>Report_preview_does_not_support_print_config_and_f12_is_a_noop</c>
    /// and asserted <c>Assert.Null(vm.PrintConfigPanel)</c>: F12 over a report opened nothing at all.
    ///
    /// <para>That was right while the panel held only the RQ-12 <i>document</i> knobs — a report has no
    /// narration line and no CGST Rule 48(1) copy marking, so there was nothing to configure. W2-31 added the
    /// F8 print format, the F9 paper toggle, the F5 copy count and the F10 range/starting-number to the same
    /// panel, and those apply to <b>every</b> document kind. Leaving the old gate in place would have left the
    /// whole of row 12.4 unreachable from the screen most prints are taken from.</para>
    ///
    /// <para>What is UNCHANGED, and is asserted below so the change cannot creep: a report preview still does
    /// not support the document knobs, and the panel still hides them.</para>
    /// </summary>
    [Fact]
    public void Report_preview_opens_the_page_knobs_but_not_the_document_knobs()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.OpenReport(ReportKind.TrialBalance);
        vm.OpenPrintPreview();

        Assert.Equal(PrintPreviewViewModel.PrintKind.Report, vm.PrintPreview!.Kind);
        Assert.False(vm.PrintPreview!.SupportsPrintConfig);   // no title override / narration / copy marking

        vm.OpenPrintConfig();

        Assert.NotNull(vm.PrintConfigPanel);                  // W2-31: the F8/F9/F5/F10 knobs are reachable
        Assert.False(vm.PrintConfigPanel!.SupportsDocumentKnobs);
        Assert.True(vm.PrintConfigPanel!.SupportsPageKnobs);
    }

    // ================================================================ Esc pops the F12 panel and keeps the preview live

    [Fact]
    public void Closing_the_print_config_panel_keeps_the_preview_live()
    {
        var k = NewGstKit("Print Pop Co");
        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "100.00"));
        var preview = PrintDrilledVoucher(k.Vm, v.Id);

        k.Vm.OpenPrintConfig();
        Assert.NotNull(k.Vm.PrintConfigPanel);

        k.Vm.Back();                          // Esc / Back pops the config column
        Assert.Null(k.Vm.PrintConfigPanel);
        Assert.Same(preview, k.Vm.PrintPreview);      // the preview survives beneath
        Assert.Equal(Screen.PrintPreview, k.Vm.CurrentScreen);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
