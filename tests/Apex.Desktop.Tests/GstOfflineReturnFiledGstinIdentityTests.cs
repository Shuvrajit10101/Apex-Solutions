using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>A11 REVIEW FINDING — THE EXPORTED GSTR-1 FILE WAS NAMED AFTER THE WRONG TAXPAYER, AND SO WAS THE HEADER
/// ON SCREEN.</b> <c>ExportFileName</c> and the <c>GSTIN …</c> header both read <c>_company.Gst!.Gstin</c>, which is
/// <b>always the primary registration's</b>, while the figures and the emitted <c>gstin</c> field follow the
/// registration the operator selected in the picker.
///
/// <para><b>Why it only now reaches a user.</b> Before the GSTR-1 payload took a registration, <c>BuildJson</c>
/// threw <c>GstReportSupport.EnsureRegistrationScoped</c>'s refusal on a multi-registration company and
/// <c>ExportJson</c> reported "Could not write the return file" — no artefact was produced, so the name could not be
/// wrong. Now the payload builds. Measured on the fixture below: the bytes correctly declared
/// <c>gstin</c> <b>33AABCC1206D1ZM</b> while the file was written as
/// <c>GSTR-1_<b>27AAPFU0939F1ZV</b>_042024.json</c> — one taxpayer's return, filed under another's name.</para>
///
/// <para><b>Vendor-attested (R7), opened by content:</b> "<i>If you have multiple registrations, select the required
/// GST Registration.</i>" (<c>help.tallysolutions.com/upload-gstr-1/</c>) — the artefact is per registration, so its
/// identity must be that registration's.</para>
/// </summary>
public sealed class GstOfflineReturnFiledGstinIdentityTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GstOfflineReturnFiledGstinIdentityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexFiledGstinTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinBranch = GstinFor("33", "AABCC1206D");

    /// <summary>
    /// 🔴 <b>THE ARTEFACT'S NAME MATCHES THE ARTEFACT'S CONTENTS.</b> Asserted against the <c>gstin</c> the emitted
    /// bytes actually carry, not against a constant, so the two can never drift apart again.
    /// </summary>
    [Fact]
    public void The_exported_file_name_carries_the_GSTIN_the_payload_is_filed_under()
    {
        var (page, branch) = OpenGstr1ForBranch("Filed Gstin Co");

        using var doc = JsonDocument.Parse(page.BuildJson());
        var payloadGstin = doc.RootElement.GetProperty("gstin").GetString();

        Assert.Equal(GstinBranch, payloadGstin);
        Assert.Equal($"GSTR-1_{payloadGstin}_042024.json", page.ExportFileName);
        Assert.DoesNotContain(GstinMaharashtra, page.ExportFileName);
        _ = branch;
    }

    /// <summary>🔴 The on-screen header names the same registration the figures below belong to.</summary>
    [Fact]
    public void The_on_screen_GSTIN_header_names_the_selected_registration()
    {
        var (page, _) = OpenGstr1ForBranch("Filed Gstin Header Co");

        Assert.Equal($"GSTIN {GstinBranch}", page.GstinText);
    }

    /// <summary>
    /// <b>ER-13.</b> A single-registration company's name and header are exactly what they were: the picker holds
    /// one entry, the scope falls back to the primary, and the primary's GSTIN is the company's own.
    /// </summary>
    [Fact]
    public void A_single_registration_company_keeps_its_existing_file_name_and_header()
    {
        var vm = NewGstCompany("Filed Gstin Single Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr1);
        var page = vm.GstOfflineReturns!;
        page.SelectedPeriod = page.Periods.Single(p => p.Label == "April 2024");

        Assert.False(page.ShowsRegistrationPicker);
        Assert.Equal($"GSTR-1_{GstinMaharashtra}_042024.json", page.ExportFileName);
        Assert.Equal($"GSTIN {GstinMaharashtra}", page.GstinText);
    }

    // ---------------------------------------------------------------- scaffolding

    private (GstOfflineReturnsViewModel Page, GstRegistration Branch) OpenGstr1ForBranch(string companyName)
    {
        var vm = NewGstCompany(companyName);
        var c = vm.Company!;
        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", "33", GstinBranch,
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr1);
        var page = vm.GstOfflineReturns!;
        Assert.True(page.ShowsRegistrationPicker);
        page.SelectedRegistration = page.Registrations.Single(r => r.Id == branch.Id);
        page.SelectedPeriod = page.Periods.Single(p => p.Label == "April 2024");
        return (page, branch);
    }

    private MainWindowViewModel NewGstCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
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
        return vm;
    }
}
