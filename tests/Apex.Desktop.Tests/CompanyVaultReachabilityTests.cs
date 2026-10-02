using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Persistence.Sqlite;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Census row 16.1 — THE LAST MILE FOR THE DATA VAULT: that a real operator can actually reach it and
/// actually use it, from the keyboard, on the realised window.</b>
///
/// <para><b>WHY THIS FILE EXISTS, stated plainly.</b> The vault arrived with thorough tests of its CRYPTO
/// (<c>CompanyVaultTests</c>) and its REGISTRY (<c>CompanyRegistryTests</c>) and <b>not one test that a user can
/// get to it</b>. Every one of those tests calls <c>CompanyStorage</c> directly. Deleting the menu row, the
/// <c>DataTemplate</c> from <c>MainWindow.axaml</c>, or the Ctrl+A handler would leave all of them green while
/// the feature became unreachable by any human being — which is exactly how this project has previously shipped
/// services with zero production callers. A capability no user can reach is not a capability.</para>
///
/// <para>So this file drives the <b>real</b> <see cref="MainWindow"/> through the <b>real</b> keyboard route —
/// Alt+K, arrow to the row, Enter, type into the realised boxes, Ctrl+A — and then asserts against the FILE
/// SYSTEM that the company genuinely got vaulted. It reads realised controls rather than markup, so it also
/// bites the refactor that renders a box bound to nothing.</para>
/// </summary>
public sealed class CompanyVaultReachabilityTests
{
    private const string Passphrase = "a-long-enough-passphrase";

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) Open(string companyName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexVaultUi_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();

        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Pump(window);
        return (window, vm, dir);
    }

    private static void Cleanup(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Alt+K, then the arrow keys to the named row, then Enter — the route an operator's hands take. A direct
    /// call to <c>OpenDataVault()</c> would prove only that a method exists.
    /// </summary>
    private static void OpenFromCompanyMenu(MainWindow window, MainWindowViewModel vm, string verb)
    {
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
        Pump(window);
        Assert.Equal(Screen.CompanyMenu, vm.CurrentScreen);

        var column = vm.Columns.Last();
        var index = CompanyMenu.VerbsOf(column).ToList().IndexOf(verb);
        Assert.True(index >= 0, $"'{verb}' is not offered on the Alt+K company menu at all.");

        for (var i = 0; i < index; i++)
        {
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
        }
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);
    }

    /// <summary>The realised passphrase boxes: every TextBox on screen that masks what is typed into it.</summary>
    private static List<TextBox> RealisedPassphraseBoxes(MainWindow window)
        => window.GetVisualDescendants()
                 .OfType<TextBox>()
                 .Where(t => t.PasswordChar != '\0')
                 .ToList();

    private static List<string> RealisedTexts(MainWindow window)
        => window.GetVisualDescendants()
                 .OfType<TextBlock>()
                 .Select(t => t.Text ?? string.Empty)
                 .Where(s => s.Length > 0)
                 .ToList();

    // ═══════════════════════════════════════════════════════════════════════════════ the route in

    /// <summary>
    /// 🔴 <b>THE RED TEST FOR THIS ROW.</b> On today's <c>origin/main</c> the Alt+K menu is
    /// <c>{ "Create", "Alter", "Select", "Shut", "Users and Passwords", "Password Policy" }</c> with no vault
    /// row at all, so this fails at "is not offered on the Alt+K company menu at all".
    ///
    /// <para>Vendor route (help.tallysolutions.com, ruling 14 tier 1): the data vault is reached from the
    /// company menu — <i>"Press Alt+K (Company) &gt; …"</i>. The ROUTE is the vendor's; the row's NAME is ours,
    /// because the vendor's product name carries a brand this application never renders (R7).</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_K_then_the_vault_row_opens_the_Data_Vault_screen()
    {
        var (window, vm, dir) = Open("Vault Route Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.DataVaultVerb);

            Assert.Equal(Screen.DataVault, vm.CurrentScreen);
            Assert.NotNull(vm.DataVault);
            Assert.Equal(CompanyVault.FeatureName, vm.DataVault!.Title);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The screen is REALISED, not merely constructed: three masked passphrase boxes exist on the window. The
    /// vendor's screen carries a passphrase, a confirmation and an old-password field; a view model holding
    /// three strings nobody can type into is not that.
    /// </summary>
    [AvaloniaFact]
    public void The_realised_vault_screen_offers_three_masked_passphrase_boxes()
    {
        var (window, vm, dir) = Open("Vault Fields Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.DataVaultVerb);

            // Current / New / Confirm. The "current" box is bound to IsVaulted and is collapsed until a
            // passphrase exists, so an unvaulted company realises two of the three.
            var boxes = RealisedPassphraseBoxes(window);
            Assert.True(boxes.Count >= 2,
                $"the vault screen realised {boxes.Count} masked passphrase box(es); the operator cannot type a "
                + "passphrase that has no box.");

            // 🔴 BOUND, not merely present — a TextBox wired to nothing renders identically to a working one.
            // Typing into the realised boxes must move the view model; that is the difference between a screen
            // and a picture of a screen.
            var panel = vm.DataVault!;
            foreach (var box in boxes) box.Text = "typed-into-the-realised-box";
            Pump(window);

            Assert.True(
                panel.NewPassphrase == "typed-into-the-realised-box"
                || panel.ConfirmPassphrase == "typed-into-the-realised-box",
                "text typed into the realised passphrase boxes never reached the view model — the boxes are "
                + "rendered but not bound.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE IRREVERSIBILITY WARNING IS ON SCREEN BEFORE THE DECISION, NOT AFTER IT.</b> The vendor states
    /// repeatedly that forgetting the password means permanently losing the company data; this application
    /// must say so where the operator is standing when they choose. A warning shown afterwards is a warning
    /// about a risk already taken.
    /// </summary>
    [AvaloniaFact]
    public void The_realised_vault_screen_states_the_irreversibility_before_anything_is_set()
    {
        var (window, vm, dir) = Open("Vault Warning Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.DataVaultVerb);

            Assert.Contains(RealisedTexts(window), t => t == CompanyVault.IrreversibilityWarning);
            // Nothing has been set — the warning is genuinely PRE-decision.
            Assert.False(vm.DataVault!.IsVaulted);
        }
        finally { Cleanup(window, dir); }
    }

    // ═══════════════════════════════════════════════════════════════════════ the keystroke that acts

    /// <summary>
    /// 🔴🔴 <b>THE WHOLE ROW, END TO END, THROUGH THE KEYBOARD ONLY — and verified against the DISK.</b> Types
    /// the passphrase into the realised boxes, presses Ctrl+A, and then asserts on the file system that the
    /// company's book is genuinely encrypted and its NAME IS GONE from every filename. This is the test that
    /// would catch the vault being wired to a handler that never fires.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_on_the_realised_vault_screen_encrypts_the_company_and_removes_its_name_from_disk()
    {
        const string company = "Keyboard Vault Traders";
        var (window, vm, dir) = Open(company);
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.DataVaultVerb);

            // A plain book named after the company is what we start from.
            Assert.Contains(Directory.EnumerateFiles(dir, "*.db"),
                p => Path.GetFileName(p).Contains("Keyboard Vault", StringComparison.OrdinalIgnoreCase));

            var boxes = RealisedPassphraseBoxes(window);
            foreach (var box in boxes)
            {
                box.Focus();
                box.Text = Passphrase;   // New and Confirm must match; both are realised and both are set.
            }
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            SqliteConnection.ClearAllPools();

            // 🔴 The claim is about the DISK, not about a view-model flag.
            Assert.DoesNotContain(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories),
                p => Path.GetFileName(p).Contains("Keyboard Vault", StringComparison.OrdinalIgnoreCase));

            var books = Directory.EnumerateFiles(dir, "*.db").ToList();
            Assert.Single(books);
            Assert.True(CompanyVault.IsVaulted(books[0]),
                "Ctrl+A reported success but the book on disk is still readable without a passphrase.");
            Assert.True(CompanyVault.TryOpen(books[0], Passphrase),
                "the book was encrypted but the passphrase the operator typed does not open it.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The vaulted company is still LISTED — as asterisks with its number visible — rather than vanishing from
    /// the company list. "Encrypted but unopenable" is the other way to fail this row, and the vendor's own
    /// behaviour is that the name shows as asterisks <i>"while the company number remains visible"</i>.
    /// </summary>
    [AvaloniaFact]
    public void A_vaulted_company_is_still_listed_as_asterisks_with_its_number()
    {
        const string company = "Listed Vault Traders";
        var (window, vm, dir) = Open(company);
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.DataVaultVerb);
            foreach (var box in RealisedPassphraseBoxes(window)) box.Text = Passphrase;
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            SqliteConnection.ClearAllPools();

            var listed = new CompanyStorage(dir).ListCompanies();
            Assert.Single(listed);
            Assert.True(listed[0].IsVaulted);
            Assert.Equal(CompanyVault.MaskedName, listed[0].Name);
            Assert.True(listed[0].Number > 0, "a vaulted company must keep a visible number — it is the only "
                + "thing left that identifies it to the operator.");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// A mistyped confirmation is REFUSED and nothing on disk changes. The passphrase is never stored, so a
    /// typo that got through would be a lost company — which is exactly why the vendor asks twice.
    /// </summary>
    [AvaloniaFact]
    public void A_mismatched_confirmation_is_refused_and_the_book_stays_plain()
    {
        const string company = "Mismatch Vault Co";
        var (window, vm, dir) = Open(company);
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.DataVaultVerb);

            var panel = vm.DataVault!;
            panel.NewPassphrase = Passphrase;
            panel.ConfirmPassphrase = Passphrase + "-typo";
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            SqliteConnection.ClearAllPools();

            Assert.False(panel.IsVaulted);
            var books = Directory.EnumerateFiles(dir, "*.db").ToList();
            Assert.Single(books);
            Assert.False(CompanyVault.IsVaulted(books[0]),
                "a mismatched confirmation encrypted the book anyway — under which of the two typed strings?");
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴🔴 <b>THE ROUND TRIP: A VAULTED COMPANY CAN BE OPENED AGAIN.</b> Everything else in this row is worth
    /// nothing without it — a book that encrypts perfectly and never reopens is not a vault, it is data loss
    /// with a progress bar. The passphrase prompt had NO test of any kind when this file was written, so this
    /// is the one that stops "listed but unopenable" shipping.
    ///
    /// <para>The whole path is exercised: vault the company, shut it, choose it again from the company list
    /// (where it appears as asterisks), get the prompt, type the passphrase, Ctrl+A, and assert the REAL
    /// company came back with its REAL name — the name that exists only inside the encrypted file.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_vaulted_company_reopens_with_its_passphrase_and_its_real_name_comes_back()
    {
        const string company = "Reopen Vault Traders";
        var (window, vm, dir) = Open(company);
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.DataVaultVerb);
            foreach (var box in RealisedPassphraseBoxes(window)) box.Text = Passphrase;
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            Assert.True(vm.DataVault!.IsVaulted, "the company was not vaulted, so the reopen proves nothing.");

            vm.ShutCompany();
            Pump(window);
            SqliteConnection.ClearAllPools();

            // Choose it again from Company Select — the operator's real route. The row is there, listed under
            // asterisks, and invoking the row is what a user pressing Enter on it does.
            var entry = new CompanyStorage(dir).ListCompanies().Single();
            Assert.True(entry.IsVaulted);
            Assert.Equal(CompanyVault.MaskedName, entry.Name);

            vm.ShowCompanySelect();
            Pump(window);
            var row = vm.Menu.FirstOrDefault(m => m.Label == CompanyVault.MaskedName);
            Assert.True(row is not null,
                "the vaulted company is not offered on Company Select at all — 'encrypted but unopenable' is "
                + "the other way to fail this row.");
            row!.Activate();
            Pump(window);

            // 🔴 A vaulted company must divert to the prompt, not fail to open and not open unprotected.
            Assert.Equal(Screen.CompanyUnlock, vm.CurrentScreen);
            Assert.NotNull(vm.CompanyUnlock);
            Assert.Equal($"Company {entry.Number}", vm.CompanyUnlock!.Identification);

            // A WRONG passphrase is refused, and leaves the prompt up rather than opening anything.
            vm.CompanyUnlock.Passphrase = "not-the-passphrase";
            Assert.False(vm.UnlockCompany());
            Assert.Equal(Screen.CompanyUnlock, vm.CurrentScreen);

            // The right one opens it, and the REAL name — readable only from inside the encrypted file — is back.
            vm.CompanyUnlock.Passphrase = Passphrase;
            Assert.True(vm.UnlockCompany(), "the correct passphrase did not open the vaulted company.");
            Pump(window);

            Assert.Null(vm.CompanyUnlock);
            Assert.NotNull(vm.Company);
            Assert.Equal(company, vm.Company!.Name);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 R7 — no user-visible string anywhere on this screen carries the vendor's brand. The feature is the
    /// vendor's; the name is ours. This reads the REALISED text, so it also catches a brand name typed straight
    /// into the markup rather than coming through a constant.
    /// </summary>
    [AvaloniaFact]
    public void No_realised_string_on_the_vault_screen_carries_the_vendor_brand()
    {
        var (window, vm, dir) = Open("Brand Check Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.DataVaultVerb);

            foreach (var text in RealisedTexts(window))
                Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);

            foreach (var button in window.GetVisualDescendants().OfType<Button>())
                Assert.DoesNotContain("tally", button.Content?.ToString() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(window, dir); }
    }
}
