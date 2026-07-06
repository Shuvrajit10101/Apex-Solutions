using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-12 (RQ-27 SMTP profile capture): the "SMTP Settings" panel captures the
/// company's outgoing-mail server profile (host / port / TLS / from-address / from-name) and round-trips it
/// through the per-company store (<see cref="CompanyStorage"/> → schema v15 <c>smtp_profile</c> table).
///
/// <para>The persistence itself is trusted (covered by <c>Apex.Persistence.Sqlite.Tests</c>); these tests pin
/// the thin Avalonia layer: the shell opens a settings column, the form round-trips through the repo, re-opening
/// re-hydrates the saved values, an incomplete form cannot be saved, and — critically — the VM exposes NO
/// password/secret member (R13).</para>
/// </summary>
public sealed class SmtpSettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public SmtpSettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexSmtpUiTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel ShellWithCompany()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        return vm;
    }

    // ---------------------------------------------------------------- round-trip through the repo

    [Fact]
    public void Save_then_reopen_round_trips_the_profile()
    {
        var shell = ShellWithCompany();
        var company = shell.Company!;

        var vm = new SmtpSettingsViewModel(_storage, company)
        {
            Host = "smtp.example.com",
            Port = 465,
            UseTls = true,
            FromAddress = "billing@example.com",
            FromName = "Apex Billing",
        };
        Assert.True(vm.Save());

        // A fresh VM re-hydrates from the persisted row.
        var reopened = new SmtpSettingsViewModel(_storage, company);
        Assert.Equal("smtp.example.com", reopened.Host);
        Assert.Equal(465, reopened.Port);
        Assert.True(reopened.UseTls);
        Assert.Equal("billing@example.com", reopened.FromAddress);
        Assert.Equal("Apex Billing", reopened.FromName);
    }

    [Fact]
    public void Defaults_when_no_profile_saved()
    {
        var shell = ShellWithCompany();
        var vm = new SmtpSettingsViewModel(_storage, shell.Company!);
        Assert.Equal(string.Empty, vm.Host);
        Assert.Equal(587, vm.Port);          // STARTTLS default
        Assert.True(vm.UseTls);
        Assert.Equal(string.Empty, vm.FromAddress);
    }

    [Fact]
    public void Optional_from_name_round_trips_as_null()
    {
        var shell = ShellWithCompany();
        var company = shell.Company!;
        var vm = new SmtpSettingsViewModel(_storage, company)
        {
            Host = "smtp.example.com",
            Port = 587,
            FromAddress = "no-reply@example.com",
            FromName = "   ",                // blank ⇒ null
        };
        Assert.True(vm.Save());

        var profile = _storage.GetSmtpProfile(company);
        Assert.NotNull(profile);
        Assert.Null(profile!.FromName);
    }

    [Fact]
    public void Incomplete_form_cannot_be_saved()
    {
        var shell = ShellWithCompany();
        var vm = new SmtpSettingsViewModel(_storage, shell.Company!)
        {
            Host = "",                       // missing host
            FromAddress = "x@example.com",
        };
        Assert.False(vm.CanSave);
        Assert.False(vm.Save());
        Assert.Null(_storage.GetSmtpProfile(shell.Company!));

        vm.Host = "smtp.example.com";
        Assert.True(vm.CanSave);             // now complete
    }

    // ---------------------------------------------------------------- shell wiring

    [Fact]
    public void OpenSmtpSettings_opens_a_column_and_does_not_stack()
    {
        var empty = new MainWindowViewModel(_storage);
        empty.OpenSmtpSettings();            // no company open
        Assert.Null(empty.SmtpSettings);

        var shell = ShellWithCompany();
        shell.OpenSmtpSettings();
        var first = shell.SmtpSettings;
        Assert.NotNull(first);
        Assert.Equal(Screen.SmtpSettings, shell.CurrentScreen);
        shell.OpenSmtpSettings();            // re-press: must not stack a second panel
        Assert.Same(first, shell.SmtpSettings);
    }

    [Fact]
    public void SaveSmtpSettings_forwarder_persists_the_open_panel()
    {
        var shell = ShellWithCompany();
        shell.OpenSmtpSettings();
        shell.SmtpSettings!.Host = "smtp.example.com";
        shell.SmtpSettings!.FromAddress = "a@example.com";

        Assert.True(shell.SaveSmtpSettings());
        var profile = _storage.GetSmtpProfile(shell.Company!);
        Assert.NotNull(profile);
        Assert.Equal("smtp.example.com", profile!.Host);
    }

    // ---------------------------------------------------------------- R13: no password anywhere

    [Fact]
    public void Viewmodel_exposes_no_password_or_secret_member()
    {
        var members = typeof(SmtpSettingsViewModel)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(m => m.Name.ToLowerInvariant());

        foreach (var forbidden in new[] { "password", "secret", "pwd", "credential" })
            Assert.DoesNotContain(members, m => m.Contains(forbidden));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
