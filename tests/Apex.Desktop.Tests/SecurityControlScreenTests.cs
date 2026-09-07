using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>SECURITY CONTROL — THE ROUTE IN, AND WHAT THE OPERATOR ACTUALLY SEES (census 16.2 slice S4).</b>
///
/// <para><b>Why these tests walk the REALISED VISUAL TREE and not the view-model flags.</b> Asserting
/// <c>vm.SecurityUsers is not null</c> is exactly the test that passes on a build where the screen renders
/// nothing — this project has three dead features filed for precisely that (a fully-tested report with zero
/// <c>src/</c> callers; a 432-line view model with zero references; ~625 lines of cheque rendering whose only
/// writers are test files). So every case here drives the REAL keystrokes through <see cref="MainWindow"/>,
/// pumps layout, and then looks for realised, visible, non-degenerate controls.</para>
///
/// <para>🔴 <b>The two notices are asserted as PIXELS-worth of visible TextBlock, not as view-model strings</b>,
/// because a notice the template forgot to place is the same as no notice at all — and grading this row
/// "COMPLETE" without them in front of the operator would be a promise the architecture cannot keep.</para>
///
/// <para>Headless-safe: visual-tree and text inspection only. No Skia, no rendered frame.</para>
/// </summary>
public sealed class SecurityControlScreenTests
{
    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    /// <summary>Every piece of text actually realised, visible and occupying space on screen.</summary>
    private static List<string> VisibleText(MainWindow w) =>
        Descendants(w)
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds is { Width: > 0, Height: > 0 })
            .Select(t => t.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) Open(string companyName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexSecurity_" + Guid.NewGuid().ToString("N"));
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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Presses Alt+K, then walks the menu to <paramref name="verb"/> with the arrow keys and Enter —
    /// the real keyboard route, not a direct method call, so a row that exists but is not reachable fails.</summary>
    private static void OpenFromCompanyMenu(MainWindow window, MainWindowViewModel vm, string verb)
    {
        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Alt);
        Pump(window);
        Assert.Equal(Screen.CompanyMenu, vm.CurrentScreen);

        var column = vm.Columns.Last();
        var index = CompanyMenu.VerbsOf(column).ToList().IndexOf(verb);
        Assert.True(index >= 0, $"'{verb}' is not offered on the Alt+K company menu at all.");

        // The cursor starts on the first selectable row; step down to the wanted one and press Enter.
        for (var i = 0; i < index; i++)
        {
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(window);
        }
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);
    }

    // ═══════════════════════════════════════════════════════════ the route in

    /// <summary>
    /// 🔴 <b>THE RED TEST FOR THIS WAVE.</b> On today's <c>origin/main</c>, <c>CompanyMenu.OfferedVerbs</c> is
    /// literally <c>{ "Create", "Alter", "Select", "Shut" }</c> and there is no Users and Passwords row at all,
    /// so this fails with "'Users and Passwords' is not offered on the Alt+K company menu at all."
    ///
    /// <para>Vendor, verbatim (help.tallysolutions.com/manage-users-in-tallyprime/): <i>"Press Alt+K (Company)
    /// &gt; Users and Passwords. The Users for Company screen will appear."</i></para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_K_then_Users_and_Passwords_opens_the_Users_for_Company_screen()
    {
        var (window, vm, dir) = Open("Users Route Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);

            Assert.Equal(Screen.SecurityUsers, vm.CurrentScreen);
            Assert.NotNull(vm.SecurityUsers);

            // …and it is REALISED, not merely constructed: the screen's own title is on screen.
            var text = VisibleText(window);
            Assert.Contains(text, t => t.Contains("Users for Company", StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Vendor, verbatim: <i>"Press Alt+K (Company) &gt; Password Policy"</i>. Fails on today's main for
    /// the same reason as the test above.</summary>
    [AvaloniaFact]
    public void Alt_K_then_Password_Policy_opens_the_Password_Policy_screen()
    {
        var (window, vm, dir) = Open("Policy Route Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.PasswordPolicyVerb);

            Assert.Equal(Screen.PasswordPolicy, vm.CurrentScreen);
            Assert.NotNull(vm.PasswordPolicy);
            Assert.Contains(VisibleText(window), t => t.Contains("Password Policy", StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>Escape pops the screen and returns to the company menu — the cascade contract every other panel
    /// keeps. A screen with no way out is not reachable in any useful sense.</summary>
    [AvaloniaFact]
    public void Escape_pops_the_users_screen_back_to_the_company_menu()
    {
        var (window, vm, dir) = Open("Users Escape Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);
            Assert.Equal(Screen.SecurityUsers, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.NotEqual(Screen.SecurityUsers, vm.CurrentScreen);
            Assert.Null(vm.SecurityUsers);
        }
        finally { Cleanup(window, dir); }
    }

    // ═══════════════════════════════════════════════════════════ what the operator is told

    /// <summary>
    /// 🔴 <b>THE ARCHITECTURAL LIMIT IS ON SCREEN.</b> A local application storing its users in its own SQLite
    /// file cannot defend that file from anyone who can read it. Asserting the view-model property would pass on
    /// a build where the template never placed the notice — so this requires a REALISED, VISIBLE TextBlock with
    /// non-zero bounds carrying the text.
    /// </summary>
    [AvaloniaFact]
    public void The_users_screen_states_the_architectural_limit_where_the_operator_can_read_it()
    {
        var (window, vm, dir) = Open("Limit Notice Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);

            Assert.Contains(
                VisibleText(window),
                t => t.Contains(SecurityControl.ArchitecturalLimitNotice, StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE IRRECOVERABLE-LOCKOUT WARNING IS ON SCREEN, ON BOTH SCREENS.</b> Passwords are one-way; a lost
    /// Owner password ends the company. The operator is told BEFORE they type one, and again on the policy
    /// screen, where setting an expiry makes a lockout MORE likely.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(CompanyMenu.UsersAndPasswordsVerb)]
    [InlineData(CompanyMenu.PasswordPolicyVerb)]
    public void Both_security_screens_state_the_irrecoverable_lockout_warning(string verb)
    {
        var (window, vm, dir) = Open("Lockout Notice Co");
        try
        {
            OpenFromCompanyMenu(window, vm, verb);

            Assert.Contains(
                VisibleText(window),
                t => t.Contains(SecurityControl.LockoutWarningNotice, StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE SCOPE LIMIT IS ON SCREEN: these rules are RECORDED, not yet ENFORCED.</b> Nothing in the
    /// application consults a security level before opening a screen, and there is no sign-in step. An operator
    /// who restricted a report here and was not told would believe it was restricted — which would be worse than
    /// having no screen at all. This is why census 16.2 is PARTIAL, and the sentence is on the screen rather than
    /// only in a report nobody using the product will read.
    /// </summary>
    [AvaloniaFact]
    public void The_users_screen_says_the_rules_are_recorded_but_not_yet_enforced()
    {
        var (window, vm, dir) = Open("Enforcement Notice Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);

            Assert.Contains(
                VisibleText(window),
                t => t.Contains(SecurityControl.EnforcementNotice, StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>The Password Policy screen says plainly that its two settings are this application's own — the
    /// vendor documents the capabilities but publishes no field captions.</summary>
    [AvaloniaFact]
    public void The_password_policy_screen_says_its_two_settings_are_this_applications_own()
    {
        var (window, vm, dir) = Open("Policy Sourcing Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.PasswordPolicyVerb);

            Assert.Contains(
                VisibleText(window),
                t => t.Contains(PasswordPolicyViewModel.SourceNoticeText, StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    // ═══════════════════════════════════════════════════════════ R13 on the screen itself

    /// <summary>
    /// 🔴 <b>THE PASSWORD BOX IS MASKED, AND NOTHING ON THE SCREEN EVER SHOWS A PASSWORD.</b> Two limbs: the
    /// realised input carries a <c>PasswordChar</c>, and after the user is created no visible text anywhere in
    /// the window contains the password that was typed.
    ///
    /// <para>Mutation-verified: removing <c>PasswordChar="•"</c> from the template reddens the first limb, and
    /// echoing the password into the status line reddens the second.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_password_field_is_masked_and_no_password_is_ever_shown_on_screen()
    {
        const string password = "Bougainvillea!8812";
        var (window, vm, dir) = Open("Masked Password Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);
            var panel = vm.SecurityUsers!;

            panel.UseUserAccessControl = true;   // seeds Owner + Data Entry Operator
            panel.NewUserName = "admin";
            panel.NewPassword = password;
            Pump(window);

            // Limb 1: the realised input for the password is masked.
            var box = Descendants(window)
                .OfType<TextBox>()
                .FirstOrDefault(t => t.Name == "SecurityNewPasswordBox" && t.IsEffectivelyVisible);
            Assert.True(box is not null,
                "The password input is not realised on the Users for Company screen at all.");
            Assert.True(box!.PasswordChar != '\0',
                "🔴 The password input is NOT masked — it renders the typed password in clear text on screen.");

            Assert.True(panel.CreateUser(), panel.Message);
            Pump(window);

            // Limb 2: nothing visible anywhere in the window carries the password, including the status line…
            Assert.DoesNotContain(
                VisibleText(window),
                t => t.Contains(password, StringComparison.OrdinalIgnoreCase));
            // …and the view model dropped the plaintext the moment the verb ran.
            Assert.Equal(string.Empty, panel.NewPassword);
            // …and what it kept is a one-way verifier that the password still matches.
            var user = vm.Company!.Security.UserByName("admin")!;
            Assert.True(user.HasPassword);
            Assert.True(user.VerifyPassword(password));
            Assert.DoesNotContain(password, user.StoredPasswordVerifier!, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// The list tells the operator only WHETHER a password is set — never anything about it.
    ///
    /// <para>🔴 <b>This test found a real defect, which is why the blank case is in it.</b> As first written,
    /// creating a user with an empty password box called <c>SetPassword("")</c>, so the row reported
    /// <b>"Set"</b> for an account with no password on it. The two states behave identically at sign-in, so no
    /// functional test could see the difference — only this one, which reads what the operator is TOLD. A blank
    /// box now means "no password", and the row says "Not set".</para>
    /// </summary>
    [AvaloniaFact]
    public void The_user_list_reports_only_whether_a_password_is_set()
    {
        var (window, vm, dir) = Open("Password State Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);
            var panel = vm.SecurityUsers!;
            panel.UseUserAccessControl = true;

            panel.NewUserName = "withpw";
            panel.NewPassword = "somethinglong";
            Assert.True(panel.CreateUser(), panel.Message);

            panel.NewUserName = "nopw";
            panel.NewPassword = string.Empty;
            Assert.True(panel.CreateUser(), panel.Message);
            Pump(window);

            Assert.Equal("Set", panel.Users.Single(u => u.UserName == "withpw").PasswordState);
            Assert.Equal("Not set", panel.Users.Single(u => u.UserName == "nopw").PasswordState);
        }
        finally { Cleanup(window, dir); }
    }

    // ═══════════════════════════════════════════════════════════ the screen does the real work

    /// <summary>
    /// Turning the gate on seeds the two vendor-default levels and they are DRAWN — the names reach the screen,
    /// not just the collection. A list the template forgot to bind would pass a collection-count assertion.
    /// </summary>
    [AvaloniaFact]
    public void Turning_on_access_control_seeds_and_draws_the_two_default_levels()
    {
        var (window, vm, dir) = Open("Seeded Levels Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);
            var panel = vm.SecurityUsers!;

            Assert.False(vm.Company!.UseUserAccessControl);
            panel.UseUserAccessControl = true;
            Pump(window);

            Assert.True(vm.Company.UseUserAccessControl);
            var text = VisibleText(window);
            Assert.Contains(text, t => t.Contains("Owner", StringComparison.Ordinal));
            Assert.Contains(text, t => t.Contains("Data Entry Operator", StringComparison.Ordinal));
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 The last-Owner safeguard reaches the SCREEN: the refusal message the domain produces is the one the
    /// operator reads, rather than a silent no-op that looks like the delete worked.
    /// </summary>
    [AvaloniaFact]
    public void Removing_the_last_owner_user_is_refused_and_the_reason_reaches_the_screen()
    {
        var (window, vm, dir) = Open("Last Owner Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);
            var panel = vm.SecurityUsers!;
            panel.UseUserAccessControl = true;

            panel.SelectedLevel = panel.Levels.Single(l => l.LevelName == "Owner");
            panel.NewUserName = "admin";
            panel.NewPassword = "ownerpassword";
            Assert.True(panel.CreateUser(), panel.Message);

            panel.SelectedUser = panel.Users.Single(u => u.UserName == "admin");
            Assert.False(panel.RemoveSelectedUser());
            Pump(window);

            Assert.Equal(SecurityControl.LastOwnerGuard, panel.Message);
            Assert.Contains(VisibleText(window),
                t => t.Contains(SecurityControl.LastOwnerGuard, StringComparison.Ordinal));
            Assert.Single(vm.Company!.Security.Users);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// What the screen writes survives a real save/reopen through the store — the whole point of the v56
    /// migration. A user that vanished on save would not be an access control.
    /// </summary>
    [AvaloniaFact]
    public void Users_created_on_the_screen_survive_a_real_save_and_reopen()
    {
        const string password = "Ptarmigan!5567";
        var dir = Path.Combine(Path.GetTempPath(), "ApexSecurityPersist_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        try
        {
            window.Show();
            vm.NewCompanyName = "Persisted Users Co";
            vm.CreateCompany();
            Pump(window);

            OpenFromCompanyMenu(window, vm, CompanyMenu.UsersAndPasswordsVerb);
            var panel = vm.SecurityUsers!;
            panel.UseUserAccessControl = true;
            panel.SelectedLevel = panel.Levels.Single(l => l.LevelName == "Owner");
            panel.NewUserName = "admin";
            panel.NewPassword = password;
            Assert.True(panel.CreateUser(), panel.Message);
            Assert.True(vm.SaveSecurityUsers());

            window.Close();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // A fresh store over the same folder — the DATABASE, not the in-memory aggregate, is the source.
            var fresh = new CompanyStorage(dir);
            var entry = fresh.ListCompanies().Single(e => e.Name == "Persisted Users Co");
            var reopened = fresh.Load(entry);
            Assert.NotNull(reopened);
            Assert.True(reopened!.UseUserAccessControl);
            var admin = reopened.Security.UserByName("admin");
            Assert.NotNull(admin);
            Assert.True(admin!.VerifyPassword(password));
            Assert.False(admin.VerifyPassword("Ptarmigan!5568"));
        }
        finally
        {
            try
            {
                window.Close();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The Password Policy screen writes a policy that persists, and 0 days means NULL = never expires
    /// rather than "expires immediately".</summary>
    [AvaloniaFact]
    public void The_password_policy_screen_persists_its_two_settings_and_zero_days_means_never()
    {
        var (window, vm, dir) = Open("Policy Persist Co");
        try
        {
            OpenFromCompanyMenu(window, vm, CompanyMenu.PasswordPolicyVerb);
            var panel = vm.PasswordPolicy!;

            panel.MinimumLength = 12;
            panel.ExpiryDays = 0;
            Assert.True(vm.SavePasswordPolicy());

            Assert.Equal(12, vm.Company!.Security.PasswordPolicy.MinimumLength);
            Assert.Null(vm.Company.Security.PasswordPolicy.ExpiryDays);

            panel.ExpiryDays = 45;
            Assert.True(vm.SavePasswordPolicy());
            Assert.Equal(45, vm.Company.Security.PasswordPolicy.ExpiryDays);

            // A negative value is refused rather than clamped — a silently clamped policy is not the one the
            // operator set.
            panel.MinimumLength = -1;
            Assert.False(vm.SavePasswordPolicy());
            Assert.Equal(12, vm.Company.Security.PasswordPolicy.MinimumLength);
        }
        finally { Cleanup(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE DE-BRAND LOCK.</b> Neither screen may render the reference product's name in any user-visible
    /// string. This project has shipped that leak before, from this very menu.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(CompanyMenu.UsersAndPasswordsVerb)]
    [InlineData(CompanyMenu.PasswordPolicyVerb)]
    public void Neither_security_screen_renders_the_vendor_brand(string verb)
    {
        var (window, vm, dir) = Open("De-brand Co");
        try
        {
            OpenFromCompanyMenu(window, vm, verb);

            foreach (var text in VisibleText(window))
                Assert.DoesNotContain("Tally", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { Cleanup(window, dir); }
    }
}
