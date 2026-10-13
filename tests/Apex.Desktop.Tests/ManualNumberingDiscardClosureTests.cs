using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.10 / T2-131 — THE MANUAL-NUMBERING DISCARD, ON THE SCREENS WAVE 46 NAMED AND DID NOT TOUCH.</b>
///
/// <para><b>THE DEFECT.</b> On a <b>Manual</b>-numbering voucher type the operator's keyed Voucher No. was
/// <b>discarded</b> and the voucher posted as <c>0</c>. Both post paths number a voucher only when its type's
/// method <c>AssignsNumberAutomatically</c> (<c>LedgerService.Post</c>, <c>InventoryPostingService.Post</c>) — and
/// every build on these screens read <c>number: 0</c>, with the comment <i>"let the engine assign the automatic
/// number"</i> on the accounting one. So under Manual <b>nobody</b> numbered the voucher: not the engine, which
/// correctly declines, and not the screen, which threw the operator's figure away. The result renders as the empty
/// string everywhere, because <c>VoucherNumberFormatter.Render</c> returns <c>""</c> for <c>&lt;= 0</c> — so the
/// voucher is invisible in every report, and invisible to Prevent Duplicates too.
///
/// <para>Wave 46 (PR #155) closed <b>one</b> screen — <c>InventoryVoucherEntryViewModel</c>, pinned by
/// <c>W46OrderVoucherRemediationTests</c> — and deliberately NAMED the rest rather than reaching into a sibling
/// track's file. These are the rest:</para>
/// <list type="bullet">
///   <item><c>VoucherEntryViewModel</c> — <b>all THREE create paths</b>: the plain Dr/Cr grid, the accounting
///         (service) invoice, and the item invoice. Three separate <c>new Voucher(…)</c> calls.</item>
///   <item><c>MaterialMovementEntryViewModel</c> — and see the next paragraph, because this one is <b>three</b>
///         build branches, not one.</item>
///   <item><c>JobWorkOrderEntryViewModel</c> — <b>Area 9, OUTSIDE the filing core.</b> Fixed because it is the
///         identical one-line expression on one branch; no Area-9 row is claimed for it.</item>
/// </list></para>
///
/// <para>🔴 <b>THE CENSUS CELL NAMED ONE OF THE MATERIAL-MOVEMENT SCREEN'S THREE BRANCHES, AND THE OTHER TWO ARE
/// THE ORDINARY CASE.</b> <c>Accept</c> builds the voucher three different ways: the inline
/// <c>InventoryVoucher.MaterialMovement(…)</c> for a ONE-SIDED movement, and — for a two-sided one —
/// <c>JobWorkService.BuildConsumingMaterialIn</c> (Allow Consumption: a TRANSFORM) or
/// <c>JobWorkService.BuildMaterialOutTransfer</c> (a value-neutral TRANSFER to a third-party godown). Both
/// builders hard-coded <c>number: 0</c> of their own. Fixing only the site the cell names would have closed the
/// branch an operator reaches least and left the two they reach by sending goods out against an order — i.e. job
/// work as it is actually keyed — still silently discarding. Each branch therefore has its own test below; a
/// single "the material-movement screen is fixed" test would have been true and would have proved one third of
/// what it claimed.</para>
///
/// <para><b>FIDELITY (R7, ruling 14 — the vendor's own published documentation, opened by content).</b>
/// <i>help.tallysolutions.com/use-voucher-numbering-methods/</i>, verbatim: <b>Manual</b> is <i>"Select this to
/// manually enter the voucher number in each voucher"</i>; <b>Automatic (Manual Override)</b> is <i>"Select this
/// if you want to automate the number for vouchers, and manually override the automated number, if required"</i>,
/// and the same page works the override through — a voucher auto-numbered 10 typed over as 21 leaves <i>"the next
/// prefilled number will be 22"</i>. That last clause is asserted below, because it is the one place the vendor
/// states a CONSEQUENCE of the override rather than just permitting it, and our <c>NextNumber</c> is max+1, so it
/// follows only once the 21 actually reaches the book.</para>
///
/// <para>🔴 <b>WHAT IS NOT CLOSED, STATED HERE RATHER THAN LEFT TO BE REDISCOVERED.</b> The vendor's Manual method
/// <i>"permits you to specify anything that you wish in the voucher number field"</i> — arbitrary alphanumeric
/// text. <c>Voucher.Number</c> is an <c>int</c>, so <c>INV/2026/A-17</c> is still unkeepable. That is a domain +
/// schema change (v66 is owned elsewhere this wave) and row 5.10 cannot CLOSE on this work. A Manual type whose
/// box is <i>cleared</i> also still posts unnumbered rather than being refused — the vendor requires a number
/// under Manual, but refusing is a new message on a new path and needs its own grounding. Neither is invented
/// away here, and <c>Reaching_zero_on_a_Manual_type_still_posts_unnumbered_and_this_pins_it</c> pins the second so
/// the residual cannot drift without going red.</para>
///
/// <para>🔴 <b>AND ONE SITE FOUND HERE THAT THE CELL DOES NOT NAME, NOT FIXED:</b>
/// <c>PosBillingViewModel</c> also posts <c>number: 0</c> — but that screen has <b>no <c>VoucherNumber</c>
/// property and no Voucher No. box at all</b>, so it is a different gap (a missing field, not a discarded value)
/// and needs its own row and its own grounding. <c>PosBilling_has_no_voucher_number_field_at_all</c> pins the
/// measurement so the claim is a fact in the suite rather than a sentence in a report.</para>
///
/// <para>Every assertion reads the number off the <b>RELOADED</b> company — the posted voucher as it comes back
/// out of the <c>.db</c> — never off the screen that set it.</para>
/// </summary>
public sealed class ManualNumberingDiscardClosureTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ManualNumberingDiscardClosureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexManualNum_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ================================================================ scaffolding

    /// <summary>Which of the three CREATE paths on the accounting entry screen a case drives. The screen has three
    /// separate <c>new Voucher(…)</c> calls and each had its own <c>number: 0</c>, so "the accounting screen is
    /// fixed" is three claims.</summary>
    public enum AcctPath
    {
        /// <summary>The classic Dr/Cr grid (<c>Accept</c> → <c>PostAndSave</c>).</summary>
        PlainGrid,

        /// <summary>The service / accounting invoice (<c>AcceptAccountingInvoice</c>).</summary>
        AccountingInvoice,

        /// <summary>The stock item invoice (<c>AcceptItemInvoice</c>).</summary>
        ItemInvoice,
    }

    private sealed record Kit(
        MainWindowViewModel Vm, string CompanyName, Guid ItemId, Guid GodownId,
        Guid CustomerId, Guid ServiceIncomeId, Guid BankId, Guid ExpenseId);

    /// <summary>A company with a "Widget" (Nos) item at 500 opening, a bill-wise customer, a service-income
    /// ledger, a bank and an expense — enough to key all three accounting paths without touching GST.</summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 500m, Money.FromRupees(100m));

        AddLedger(c, "Sales", "Sales Accounts");
        var serviceIncome = AddLedger(c, "Consultancy Income", "Sales Accounts");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors", billWise: true);
        var bank = AddLedger(c, "HDFC Current", "Bank Accounts");
        var expense = AddLedger(c, "Office Rent", "Indirect Expenses");

        _storage.Save(c);
        return new Kit(vm, companyName, item.Id, c.MainLocation!.Id,
                       customer.Id, serviceIncome.Id, bank.Id, expense.Id);
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool billWise = false)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var l = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false)
        {
            MaintainBillByBill = billWise,
        };
        c.AddLedger(l);
        return l;
    }

    private Company Reload(string companyName) =>
        _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));

    /// <summary>Sets the numbering METHOD on the live (active) voucher type for <paramref name="baseType"/> and
    /// persists it, exactly as the Voucher Type master's picker does.</summary>
    private VoucherType SetNumbering(MainWindowViewModel vm, VoucherBaseType baseType, NumberingMethod method)
    {
        var c = vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == baseType && t.IsActive);
        type.Numbering = method;
        _storage.Save(c);
        return type;
    }

    /// <summary>The base voucher type each accounting path is keyed on. Sales carries the two invoice modes;
    /// the plain grid is keyed on a Journal, which opens in the Dr/Cr grid without a Single-Entry detour.</summary>
    private static VoucherBaseType BaseTypeFor(AcctPath path) => path switch
    {
        AcctPath.PlainGrid => VoucherBaseType.Journal,
        _ => VoucherBaseType.Sales,
    };

    /// <summary>
    /// Opens the accounting entry screen for <paramref name="path"/> and keys a complete, acceptable voucher —
    /// WITHOUT accepting it, so the caller can set the Voucher No. and inspect the screen first. Odd-paisa
    /// figures throughout: a rounding or cross-wiring slip is visible to the paisa.
    /// </summary>
    private static VoucherEntryViewModel OpenAndFill(Kit k, AcctPath path)
    {
        var vm = k.Vm;
        vm.OpenVoucher(BaseTypeFor(path));
        var e = vm.VoucherEntry!;

        switch (path)
        {
            case AcctPath.PlainGrid:
                // Dr Office Rent 8,765.43 / Cr HDFC Current 8,765.43.
                e.Lines[0].SelectedLedger = e.Ledgers.Single(l => l.Id == k.ExpenseId);
                e.Lines[0].Side = DrCr.Debit;
                e.Lines[0].AmountText = "8765.43";
                e.Lines[1].SelectedLedger = e.Ledgers.Single(l => l.Id == k.BankId);
                e.Lines[1].Side = DrCr.Credit;
                e.Lines[1].AmountText = "8765.43";
                e.Recalculate();
                break;

            case AcctPath.AccountingInvoice:
                e.Mode = VoucherEntryMode.AccountingInvoice;
                Assert.True(e.IsAccountingInvoice, "The screen did not enter accounting-invoice mode, so this "
                                                 + "case would silently measure the plain-grid path instead.");
                e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
                var svc = e.AccountingInvoiceLines[0];
                svc.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Id == k.ServiceIncomeId);
                svc.AmountText = "8765.43";
                e.RecalculateAccountingInvoice();
                break;

            case AcctPath.ItemInvoice:
                vm.ToggleItemInvoice();
                Assert.True(e.IsItemInvoice, "The screen did not enter item-invoice mode, so this case would "
                                           + "silently measure the plain-grid path instead.");
                e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
                var line = e.InventoryLines[0];
                line.SelectedItem = e.StockItems.Single(i => i.Id == k.ItemId);
                line.SelectedGodown = e.Godowns.Single(g => g.Id == k.GodownId);
                line.QuantityText = "3";
                line.RateText = "1234.57";
                e.RecalculateItemInvoice();
                break;

            default: throw new ArgumentOutOfRangeException(nameof(path));
        }

        return e;
    }

    /// <summary>The one voucher of <paramref name="path"/>'s base type on the RELOADED book.</summary>
    private Voucher PostedOnReloadedBook(Kit k, AcctPath path, out Company reloaded)
    {
        reloaded = Reload(k.CompanyName);
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == BaseTypeFor(path) && t.IsActive);
        return reloaded.Vouchers.Single(v => v.TypeId == type.Id);
    }

    // ================================================================ (1) the defect, on all three accounting paths

    /// <summary>
    /// 🔴 <b>THE DRIVING TEST FOR THE ACCOUNTING SCREEN — THREE PATHS, NOT ONE WITH TWO ASSUMED.</b> On a
    /// <b>Manual</b>-numbering type the operator keys a Voucher No.; the RELOADED book must hold that number, and
    /// the rendered string must be it rather than the empty string an unnumbered voucher renders as.
    ///
    /// <para>Each path keys a DIFFERENT number (731 / 842 / 953), so a case that silently drove the wrong path —
    /// which is the easiest way to write a three-arm theory that proves one thing three times — shows up as the
    /// wrong figure rather than as a pass. <c>OpenAndFill</c> also asserts the mode it believes it is in.</para>
    ///
    /// <para><b>Mutation (M1):</b> change <c>VoucherEntryViewModel.NumberToPost</c> back to <c>0</c>. All three
    /// arms redden with <i>Expected: 731/842/953 · Actual: 0</i>.</para>
    /// </summary>
    [Theory]
    [InlineData(AcctPath.PlainGrid, 731)]
    [InlineData(AcctPath.AccountingInvoice, 842)]
    [InlineData(AcctPath.ItemInvoice, 953)]
    public void Manual_numbering_posts_the_typed_voucher_number_on_every_accounting_path(AcctPath path, int keyed)
    {
        var k = NewKit($"Manual Acct {path} Co");
        SetNumbering(k.Vm, BaseTypeFor(path), NumberingMethod.Manual);

        var e = OpenAndFill(k, path);
        Assert.True(e.IsVoucherNumberEditable,
            "A Manual-numbering type must let the operator key the number, or this test is not measuring the "
            + "path the defect was on.");

        e.VoucherNumber = keyed;
        Assert.True(e.Accept(), e.Message);

        var posted = PostedOnReloadedBook(k, path, out var reloaded);
        Assert.Equal(keyed, posted.Number);

        // The observable consequence, not just the stored int: the number an operator and every report SEE.
        // Before the fix this was "" on all three paths.
        Assert.Equal(keyed.ToString(CultureInfo.InvariantCulture), reloaded.FormatVoucherNumber(posted));
    }

    /// <summary>
    /// 🔴 <b>THE VENDOR'S OWN WORKED EXAMPLE, END TO END.</b>
    /// <i>help.tallysolutions.com/use-voucher-numbering-methods/</i> on <b>Automatic (Manual Override)</b>: the
    /// screen suggests a number, the operator may type over it, and after an override to 21 <i>"the next prefilled
    /// number will be 22"</i>.
    ///
    /// <para>This is the one clause on that page that states a CONSEQUENCE rather than a permission, and it is the
    /// reason the fix could not simply be "pass the number under Manual": our <c>NextNumber</c> is <c>max + 1</c>,
    /// so the 22 follows for free — but <b>only once the 21 actually reaches the book</b>. While the override was
    /// discarded the book's max stayed 0 and the next screen suggested 1 again, forever.</para>
    ///
    /// <para><b>Mutation (M1):</b> with <c>NumberToPost</c> back at <c>0</c> this reddens twice — the posted
    /// number is 0 and the next preview is 1.</para>
    /// </summary>
    [Fact]
    public void An_Automatic_Manual_Override_jump_is_kept_and_the_NEXT_number_follows_it()
    {
        var k = NewKit("Override Jump Co");
        SetNumbering(k.Vm, VoucherBaseType.Journal, NumberingMethod.AutomaticManualOverride);

        var first = OpenAndFill(k, AcctPath.PlainGrid);
        Assert.True(first.IsVoucherNumberEditable, "Automatic (Manual Override) must offer a typeable box.");
        Assert.Equal(1, first.VoucherNumber);           // the SUGGESTION the vendor's example overrides
        first.VoucherNumber = 21;
        Assert.True(first.Accept(), first.Message);

        var posted = PostedOnReloadedBook(k, AcctPath.PlainGrid, out _);
        Assert.Equal(21, posted.Number);

        // "…and the next prefilled number will be 22." Measured on a freshly opened screen, which is where the
        // operator meets it.
        var second = OpenAndFill(k, AcctPath.PlainGrid);
        Assert.Equal(22, second.VoucherNumber);
    }

    /// <summary>
    /// The control that stops the fix being "always trust the box". Under <b>Automatic</b> the box is read-only,
    /// the screen's opening preview is only a preview, and the number must come from the ENGINE at post time —
    /// because another entry screen in the same session may have taken that number in the meantime.
    ///
    /// <para>Keyed here by posting a SECOND Journal while a first screen still holds the stale preview 1. The
    /// second voucher must be 2, not 1: if the screen's preview were trusted the book would hold two vouchers
    /// numbered 1.</para>
    /// </summary>
    [Fact]
    public void Under_Automatic_the_engine_numbers_and_a_stale_screen_preview_is_not_trusted()
    {
        var k = NewKit("Automatic Stale Co");
        SetNumbering(k.Vm, VoucherBaseType.Journal, NumberingMethod.Automatic);

        var first = OpenAndFill(k, AcctPath.PlainGrid);
        Assert.False(first.IsVoucherNumberEditable, "Automatic must leave the Voucher No. read-only.");
        Assert.Equal(1, first.VoucherNumber);
        Assert.True(first.Accept(), first.Message);

        // A second voucher through a second screen — the preview opens at 2 and the engine must agree.
        var second = OpenAndFill(k, AcctPath.PlainGrid);
        Assert.True(second.Accept(), second.Message);

        var reloaded = Reload(k.CompanyName);
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Journal && t.IsActive);
        var numbers = reloaded.Vouchers.Where(v => v.TypeId == type.Id).Select(v => v.Number).OrderBy(n => n);
        Assert.Equal(new[] { 1, 2 }, numbers);
    }

    /// <summary>Under <b>None</b> the type is unnumbered by design — the box is read-only and the rendered number
    /// is the empty string. The vendor's dedicated numbering page: <i>"Select this option to disable the voucher
    /// numbering"</i>. This is the one case where a <c>0</c> on the book is the RIGHT answer, and it is asserted so
    /// the fix cannot be mistaken for "every voucher now carries a number".</summary>
    [Fact]
    public void Under_None_the_voucher_stays_unnumbered_and_renders_empty()
    {
        var k = NewKit("None Numbering Co");
        SetNumbering(k.Vm, VoucherBaseType.Journal, NumberingMethod.None);

        var e = OpenAndFill(k, AcctPath.PlainGrid);
        Assert.False(e.IsVoucherNumberEditable, "None must leave the Voucher No. read-only.");
        Assert.True(e.Accept(), e.Message);

        var posted = PostedOnReloadedBook(k, AcctPath.PlainGrid, out var reloaded);
        Assert.Equal(0, posted.Number);
        Assert.Equal("", reloaded.FormatVoucherNumber(posted));
    }

    /// <summary>
    /// 🔴 <b>THE CLAMP IS NOT BELT-AND-BRACES.</b> The typeable control is a plain two-way <c>int</c> binding, so
    /// <c>-5</c> is keyable on a Manual type. Storing it would put a NEGATIVE <c>Voucher.Number</c> on the book —
    /// a state nothing else in this product can produce, which renders as <c>""</c> everywhere (so it is invisible
    /// in every report AND to Prevent Duplicates), and which <c>NextNumber</c>'s <c>max + 1</c> would then quietly
    /// skip past. 0 is sent instead, which under Manual means "unnumbered" — the pre-fix behaviour, kept
    /// deliberately for this one input rather than traded for a worse one.
    ///
    /// <para><b>Mutation (M2):</b> drop the <c>&amp;&amp; VoucherNumber &gt; 0</c> conjunct from
    /// <c>NumberToPost</c> and this reddens with <i>Expected: 0 · Actual: -5</i>.</para>
    /// </summary>
    [Fact]
    public void A_negative_keyed_Voucher_No_is_not_stored_on_the_book()
    {
        var k = NewKit("Negative Keyed Co");
        SetNumbering(k.Vm, VoucherBaseType.Journal, NumberingMethod.Manual);

        var e = OpenAndFill(k, AcctPath.PlainGrid);
        e.VoucherNumber = -5;
        Assert.True(e.Accept(), e.Message);

        var posted = PostedOnReloadedBook(k, AcctPath.PlainGrid, out _);
        Assert.Equal(0, posted.Number);
        Assert.True(posted.Number >= 0, "A negative voucher number reached the book.");
    }

    /// <summary>
    /// <b>THE RESIDUAL, PINNED RATHER THAN DESCRIBED.</b> A Manual type whose box is CLEARED (0) still posts
    /// unnumbered rather than being refused. The vendor requires a number under Manual, so this is a divergence —
    /// but refusing it is a new message on a new path and needs its own grounding and its own row, and inventing
    /// one here is exactly what "clone, never invent" forbids. The current behaviour is therefore asserted, so it
    /// cannot drift in either direction without a test going red and a reader meeting this comment.
    /// </summary>
    [Fact]
    public void Reaching_zero_on_a_Manual_type_still_posts_unnumbered_and_this_pins_it()
    {
        var k = NewKit("Manual Cleared Box Co");
        SetNumbering(k.Vm, VoucherBaseType.Journal, NumberingMethod.Manual);

        var e = OpenAndFill(k, AcctPath.PlainGrid);
        e.VoucherNumber = 0;                            // the operator cleared the box
        Assert.True(e.Accept(), e.Message);

        var posted = PostedOnReloadedBook(k, AcctPath.PlainGrid, out var reloaded);
        Assert.Equal(0, posted.Number);
        Assert.Equal("", reloaded.FormatVoucherNumber(posted));
    }

    // ================================================================ (2) the material-movement screen's THREE branches

    private sealed record JobKit(
        MainWindowViewModel Vm, string CompanyName, StockItem CompA, StockItem Fg, Godown Main, Godown Worker);

    /// <summary>A principal's job-work fixture: Job Order Processing on, CompA at 100 @ ₹50 in Main, a finished
    /// good, and a third-party "Worker Site" godown.</summary>
    private JobKit NewJobKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        vm.ShowGstConfig();
        vm.GstConfig!.EnableJobOrderProcessing = true;
        Assert.True(vm.Company!.EnableJobOrderProcessing);
        vm.Back();

        var c = vm.Company!;
        var masters = new InventoryService(c);
        var raw = masters.CreateStockGroup("Raw Materials");
        var fgGroup = masters.CreateStockGroup("Finished Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var compA = masters.CreateStockItem("CompA", raw.Id, nos.Id);
        var fg = masters.CreateStockItem("PC", fgGroup.Id, nos.Id);
        masters.AddOpeningBalance(compA.Id, c.MainLocation!.Id, 100m, Money.FromRupees(50m));
        var worker = masters.CreateGodown("Worker Site");
        worker.ThirdParty = true;

        _storage.Save(c);
        return new JobKit(vm, companyName, compA, fg, c.MainLocation!, worker);
    }

    /// <summary>Posts a Job Work OUT order for 10 PC against 20 CompA from Main — the order the two-sided
    /// movements auto-fill from.</summary>
    private static void PostOutOrder(JobKit j, string orderNo)
    {
        var vm = j.Vm;
        vm.OpenJobWorkOrder(JobWorkDirection.Out);
        var e = vm.JobWorkOrderEntry!;
        e.FinishedGood = e.StockItems.Single(i => i.Id == j.Fg.Id);
        e.FinishedGoodQtyText = "10";
        e.OrderNo = orderNo;
        var l0 = e.Lines[0];
        l0.SelectedItem = l0.StockItems.Single(i => i.Id == j.CompA.Id);
        l0.SelectedGodown = l0.Godowns.Single(g => g.Id == j.Main.Id);
        l0.QuantityText = "20";
        Assert.True(e.Accept(), e.Message);
    }

    private InventoryVoucher PostedStockOnReloadedBook(JobKit j, VoucherBaseType baseType, out Company reloaded)
    {
        reloaded = Reload(j.CompanyName);
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == baseType && t.IsActive);
        return reloaded.InventoryVouchers.Single(v => v.TypeId == type.Id);
    }

    /// <summary>
    /// <b>BRANCH 1 of 3 — the ONE-SIDED movement, i.e. the inline <c>InventoryVoucher.MaterialMovement(…)</c> call
    /// the census cell names.</b> A worker's pure-inward receipt: destination only, no order, nothing to balance.
    /// The keyed 614 must be on the reloaded book.
    ///
    /// <para><b>Mutation (M3):</b> <c>MaterialMovementEntryViewModel.NumberToPost</c> back to <c>0</c> reddens this
    /// and both branches below.</para>
    /// </summary>
    [Fact]
    public void Material_movement_one_sided_posts_the_typed_Manual_number()
    {
        var j = NewJobKit("MatMove OneSided Co");
        SetNumbering(j.Vm, VoucherBaseType.MaterialIn, NumberingMethod.Manual);

        j.Vm.OpenMaterialMovement(VoucherBaseType.MaterialIn);
        var m = j.Vm.MaterialMovementEntry!;
        Assert.True(m.IsVoucherNumberEditable, "A Manual Material In type must offer a typeable Voucher No.");
        m.VoucherNumber = 614;

        var dest = m.DestinationLines[0];
        dest.SelectedItem = dest.StockItems.Single(i => i.Id == j.CompA.Id);
        dest.SelectedGodown = dest.Godowns.Single(g => g.Id == j.Main.Id);
        dest.QuantityText = "7";
        dest.RateText = "50";
        Assert.True(m.Accept(), m.Message);

        var posted = PostedStockOnReloadedBook(j, VoucherBaseType.MaterialIn, out var reloaded);
        Assert.Equal(614, posted.Number);
        Assert.Equal("614", reloaded.FormatVoucherNumber(posted));
    }

    /// <summary>
    /// 🔴 <b>BRANCH 2 of 3 — THE TWO-SIDED TRANSFER, WHICH THE CENSUS CELL DOES NOT NAME.</b> Sending components
    /// out to a third-party godown against an order routes through
    /// <c>JobWorkService.BuildMaterialOutTransfer</c>, which hard-coded <c>number: 0</c> of its OWN — so fixing
    /// only the screen's inline call would have left the ordinary job-work dispatch still discarding the keyed
    /// number, and a "the material-movement screen is fixed" test would have passed while two thirds of the
    /// screen was broken.
    ///
    /// <para>The branch is asserted, not assumed: the posted voucher must carry BOTH arms (a transfer is
    /// source + destination), which is what distinguishes this build path from branch 1.</para>
    /// </summary>
    [Fact]
    public void Material_movement_two_sided_TRANSFER_posts_the_typed_Manual_number()
    {
        var j = NewJobKit("MatMove Transfer Co");
        PostOutOrder(j, "DKP/789");
        SetNumbering(j.Vm, VoucherBaseType.MaterialOut, NumberingMethod.Manual);

        j.Vm.OpenMaterialMovement(VoucherBaseType.MaterialOut);
        var m = j.Vm.MaterialMovementEntry!;
        Assert.True(m.IsVoucherNumberEditable);
        m.VoucherNumber = 725;
        m.SourceGodown = m.Godowns.Single(g => g.Id == j.Main.Id);
        m.SelectedOrder = m.Orders.Single(o => o.Order?.OrderNo == "DKP/789");
        m.DestinationGodown = m.Godowns.Single(g => g.Id == j.Worker.Id);
        Assert.True(m.Accept(), m.Message);

        var posted = PostedStockOnReloadedBook(j, VoucherBaseType.MaterialOut, out var reloaded);

        // 🔴 THIS IS THE TWO-SIDED BUILDER'S OUTPUT, ASSERTED AND NOT ASSUMED — a transfer fills BOTH arms, and
        // the two arms are two separate collections on the aggregate (`Allocations` is the source side,
        // `DestinationAllocations` the destination side; they are not one list of directions). Without this the
        // case could silently have gone down the one-sided inline branch and proved nothing about the builder.
        Assert.Contains(posted.Allocations, a => a.Direction == StockDirection.Outward && a.GodownId == j.Main.Id);
        Assert.Contains(posted.DestinationAllocations,
            a => a.Direction == StockDirection.Inward && a.GodownId == j.Worker.Id);

        Assert.Equal(725, posted.Number);
        Assert.Equal("725", reloaded.FormatVoucherNumber(posted));
    }

    /// <summary>
    /// 🔴 <b>BRANCH 3 of 3 — THE TWO-SIDED TRANSFORM (Allow Consumption), also unnamed by the cell.</b> Receiving
    /// the finished good while consuming the components routes through
    /// <c>JobWorkService.BuildConsumingMaterialIn</c>, the third hard-coded <c>number: 0</c>.
    ///
    /// <para>Asserted as the TRANSFORM by its own signature: the finished good arrives inward and the components
    /// leave outward in one voucher, valued from live consumed cost (CompA 20 × ₹50 = ₹1,000) — the ER-4
    /// invariant this branch exists for, which also proves the <c>number</c> parameter did not displace an
    /// argument.</para>
    /// </summary>
    [Fact]
    public void Material_movement_two_sided_TRANSFORM_posts_the_typed_Manual_number()
    {
        var j = NewJobKit("MatMove Transform Co");
        PostOutOrder(j, "DKP/790");

        // Dispatch the components to the worker first, so there is stock there to consume.
        j.Vm.OpenMaterialMovement(VoucherBaseType.MaterialOut);
        var outMove = j.Vm.MaterialMovementEntry!;
        outMove.SourceGodown = outMove.Godowns.Single(g => g.Id == j.Main.Id);
        outMove.SelectedOrder = outMove.Orders.Single(o => o.Order?.OrderNo == "DKP/790");
        outMove.DestinationGodown = outMove.Godowns.Single(g => g.Id == j.Worker.Id);
        Assert.True(outMove.Accept(), outMove.Message);

        SetNumbering(j.Vm, VoucherBaseType.MaterialIn, NumberingMethod.Manual);
        j.Vm.OpenMaterialMovement(VoucherBaseType.MaterialIn);
        var m = j.Vm.MaterialMovementEntry!;
        Assert.True(m.AllowConsumption, "This arm must drive the CONSUMING builder, not the inline call.");
        Assert.True(m.IsVoucherNumberEditable);
        m.VoucherNumber = 836;
        m.DestinationGodown = m.Godowns.Single(g => g.Id == j.Main.Id);
        m.SourceGodown = m.Godowns.Single(g => g.Id == j.Worker.Id);
        m.SelectedOrder = m.Orders.Single(o => o.Order?.OrderNo == "DKP/790");
        Assert.True(m.Accept(), m.Message);

        var reloaded = Reload(j.CompanyName);
        var inType = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.MaterialIn && t.IsActive);
        var posted = reloaded.InventoryVouchers.Single(v => v.TypeId == inType.Id);

        Assert.Equal(836, posted.Number);
        Assert.Equal("836", reloaded.FormatVoucherNumber(posted));

        // ER-4: the finished good is valued from the LIVE consumed cost, so the new `number` argument did not
        // slide into the valuation builder's positional list.
        var fgClosing = new StockValuationService(reloaded)
            .ClosingValue(j.Fg.Id, reloaded.FinancialYearStart.AddYears(1));
        Assert.Equal(10m, fgClosing.Quantity);
        Assert.Equal(Money.FromRupees(1000m), fgClosing.Value);
    }

    /// <summary>
    /// The <b>Job Work order</b> screen — <b>Area 9, outside the filing core.</b> Fixed and pinned only because it
    /// is the identical one-line expression as the three in-core screens; <b>no Area-9 census row is claimed for
    /// it</b>, and nothing else about this screen is touched.
    ///
    /// <para><b>Mutation (M4):</b> <c>JobWorkOrderEntryViewModel.NumberToPost</c> back to <c>0</c> reddens this.</para>
    /// </summary>
    [Fact]
    public void Job_work_order_posts_the_typed_Manual_number_area_9_no_row_claimed()
    {
        var j = NewJobKit("JobWork Manual Co");
        SetNumbering(j.Vm, VoucherBaseType.JobWorkOutOrder, NumberingMethod.Manual);

        j.Vm.OpenJobWorkOrder(JobWorkDirection.Out);
        var e = j.Vm.JobWorkOrderEntry!;
        Assert.True(e.IsVoucherNumberEditable);
        e.VoucherNumber = 947;
        e.FinishedGood = e.StockItems.Single(i => i.Id == j.Fg.Id);
        e.FinishedGoodQtyText = "10";
        e.OrderNo = "DKP/947";
        var l0 = e.Lines[0];
        l0.SelectedItem = l0.StockItems.Single(i => i.Id == j.CompA.Id);
        l0.SelectedGodown = l0.Godowns.Single(g => g.Id == j.Main.Id);
        l0.QuantityText = "20";
        Assert.True(e.Accept(), e.Message);

        var posted = PostedStockOnReloadedBook(j, VoucherBaseType.JobWorkOutOrder, out var reloaded);
        Assert.Equal(947, posted.Number);
        Assert.Equal("947", reloaded.FormatVoucherNumber(posted));
    }

    // ================================================================ (3) the measurement the cell does not carry

    /// <summary>
    /// 🔴 <b>A SIXTH SITE, MEASURED AND DELIBERATELY NOT FIXED.</b> <c>PosBillingViewModel</c> also builds its
    /// voucher with <c>number: 0</c>, so a POS bill on a Manual-numbering type posts unnumbered too — but that
    /// screen exposes <b>no <c>VoucherNumber</c> member at all</b>, so there is no typed value being discarded.
    /// It is a MISSING FIELD, which is a different gap with different grounding (does the vendor's POS invoice
    /// offer a Voucher No. box under Manual at all?) and it belongs on its own row, not folded into this one.
    ///
    /// <para>This test exists so the claim is a fact in the suite rather than a sentence in a report: it goes red
    /// the day someone adds the field without the number reaching the book, which is precisely the shape of the
    /// defect this whole file closes.</para>
    /// </summary>
    [Fact]
    public void PosBilling_has_no_voucher_number_field_at_all_so_it_is_a_different_gap()
    {
        var t = typeof(PosBillingViewModel);
        Assert.Null(t.GetProperty("VoucherNumber"));
        Assert.Null(t.GetProperty("IsVoucherNumberEditable"));

        // And the three screens this wave closed DO have both, which is what makes the contrast meaningful
        // rather than an accident of naming.
        foreach (var screen in new[]
                 {
                     typeof(VoucherEntryViewModel),
                     typeof(MaterialMovementEntryViewModel),
                     typeof(JobWorkOrderEntryViewModel),
                     typeof(InventoryVoucherEntryViewModel),
                 })
        {
            Assert.NotNull(screen.GetProperty("VoucherNumber"));
            Assert.NotNull(screen.GetProperty("IsVoucherNumberEditable"));
        }
    }

    // ================================================================ (4) reachable by a user, in the realised tree

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>
    /// 🔴 <b>A CAPABILITY MUST BE REACHABLE BY A USER, NOT MERELY PRESENT IN A VIEW MODEL.</b> Everything above
    /// sets <c>VoucherNumber</c> on the view model. This asserts the other half on the REALISED visual tree: on a
    /// Manual-numbering accounting screen the Voucher No. is an actual, non-read-only <see cref="TextBox"/>
    /// carrying the number, the read-only <c>&lt;Run&gt;</c> preview is NOT painted, and typing into that realised
    /// box is what the posted voucher ends up holding.
    ///
    /// <para>Without this the fix would be provably correct and still unusable — which is exactly how Alt+P
    /// shipped inert, and how three reports shipped printing pages of blank cells.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_Voucher_No_box_is_a_REALISED_editable_TextBox_on_a_Manual_accounting_screen()
    {
        var k = NewKit("Realised Manual Box Co");
        SetNumbering(k.Vm, VoucherBaseType.Journal, NumberingMethod.Manual);

        var window = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        var e = OpenAndFill(k, AcctPath.PlainGrid);
        Pump(window);
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);

        // The realised box, found by the binding the markup actually declares.
        var box = Descendants(window).OfType<TextBox>().SingleOrDefault(
            t => t.GetValue(TextBox.TextProperty) == "1"
              && t.Width is > 100 and < 120
              && !t.Classes.Contains("working-date"));
        Assert.NotNull(box);
        Assert.False(box!.IsReadOnly, "The Voucher No. box painted read-only on a Manual type.");

        // The read-only preview must NOT also be on screen — two Voucher No. renders is the defect the
        // IsVisible pair exists to prevent.
        Assert.DoesNotContain(
            Descendants(window).OfType<TextBlock>().Select(t => t.Text ?? string.Empty),
            s => s.StartsWith("No. ", StringComparison.Ordinal));

        // Type into the REALISED control and let the two-way binding carry it.
        box.Text = "1234";
        Pump(window);
        Assert.Equal(1234, e.VoucherNumber);

        Assert.True(e.Accept(), e.Message);
        Pump(window);

        var posted = PostedOnReloadedBook(k, AcctPath.PlainGrid, out var reloaded);
        Assert.Equal(1234, posted.Number);
        Assert.Equal("1234", reloaded.FormatVoucherNumber(posted));
    }

    /// <summary>
    /// 🔴 <b>THE TWO ACCEPT CHORDS MUST POST THE SAME NUMBER — and this test records a MEASUREMENT rather than
    /// the invariant I went looking for.</b>
    ///
    /// <para><b>What I set out to prove, and why it is not what is asserted.</b> A past wave had a bare
    /// <c>Enter</c> post a REAL journal voucher on a re-homed register, so the standing rule is to re-measure that
    /// hazard whenever a post path moves. Measured here on this screen: <b>bare <c>Enter</c> on
    /// <c>Screen.VoucherEntry</c> DOES accept the voucher.</b> The window's <c>case Key.Enter when
    /// !IsPickerOpen(e)</c> arm calls <c>ActivateSelected(viaAcceptChord: false)</c>, whose
    /// <c>case Screen.VoucherEntry</c> arm calls <c>AcceptVoucherEntryOrAlteration()</c> — and that arm does not
    /// consult <c>viaAcceptChord</c>, which only exists to keep a bare Enter aimed at a REPORT drill out of the
    /// report-accept arms. <c>KeyboardArbitrationTests</c> (<i>"Before the fix this Enter fell to
    /// <c>ActivateSelected</c> (<c>VoucherEntry.Accept()</c>)"</i>) shows the project already treats this as the
    /// baseline and has only guarded the open-picker case.</para>
    ///
    /// <para><b>So this test does NOT assert a refusal, because there is none, and writing one would pin the
    /// wrong answer</b> — the failure mode that has already cost this project a promotion. Whether a bare Enter
    /// ought to raise an "Accept?" confirmation on a voucher entry screen is a real fidelity question about the
    /// vendor's field-by-field Enter walk; it is <b>outside this track</b>, needs its own grounding, and is
    /// reported rather than decided here.</para>
    ///
    /// <para><b>What IS asserted is the invariant this change owns:</b> the number does not depend on which accept
    /// chord the operator used. Two Manual-numbering Journals keyed with 509 and 510 — one accepted by bare
    /// <c>Enter</c>, one by <c>Ctrl+A</c>, both through the real <see cref="MainWindow"/> key handler — must both
    /// reach the book with their typed number. A fix applied to <c>Accept</c> but reached by only one of two
    /// chords is exactly the "present but not reachable" shape the bar forbids.</para>
    /// </summary>
    [AvaloniaFact]
    public void Both_accept_chords_post_the_typed_Manual_number_through_the_real_key_handler()
    {
        var k = NewKit("Chord Parity Manual Co");
        var type = SetNumbering(k.Vm, VoucherBaseType.Journal, NumberingMethod.Manual);

        var window = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        // ---- (a) bare Enter. Measured: on THIS screen it accepts. It must accept with the TYPED number.
        var first = OpenAndFill(k, AcctPath.PlainGrid);
        first.VoucherNumber = 509;
        Pump(window);
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        Assert.True(first.CanAccept, first.Message);    // else the presses below prove nothing

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);

        // ---- (b) Ctrl+A on the SAME window, on a second voucher.
        var second = OpenAndFill(k, AcctPath.PlainGrid);
        second.VoucherNumber = 510;
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);

        var reloaded = Reload(k.CompanyName);
        var numbers = reloaded.Vouchers
            .Where(v => v.TypeId == type.Id)
            .Select(v => v.Number)
            .OrderBy(n => n)
            .ToArray();

        // 🔴 Both chords, both numbers, off the reloaded book. Before the fix this array was [0, 0] — two
        // vouchers the book held and no report could show.
        Assert.Equal(new[] { 509, 510 }, numbers);
    }
}
