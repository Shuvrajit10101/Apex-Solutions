using System;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The keyboard-first "SMTP Settings" panel (RQ-27), hosted as its own cascading Miller-column. It captures the
/// company's outgoing-mail server profile — <see cref="Host"/> / <see cref="Port"/> / <see cref="UseTls"/> /
/// <see cref="FromAddress"/> / <see cref="FromName"/> — and round-trips it through the per-company store
/// (<see cref="CompanyStorage"/> → schema v15 <c>smtp_profile</c> table).
///
/// <para><b>Capture-only, no password (R13).</b> There is deliberately NO password field on this screen or in the
/// persisted row; a credential (if ever) lives in the OS secret store / environment, out of the repo and the DB.
/// <b>Live SMTP send is DEFERRED</b> — nothing consumes this profile to open a socket. The compose panel
/// (<see cref="EmailComposeViewModel"/>) continues to hand off offline (write an <c>.eml</c> / open a mailto).</para>
/// </summary>
public sealed partial class SmtpSettingsViewModel : ViewModelBase
{
    private readonly CompanyStorage _storage;
    private readonly Company _company;

    public string Title => "SMTP Settings";

    /// <summary>The company these settings belong to (shown in the panel).</summary>
    public string CompanyName => _company.Name;

    /// <summary>A one-line, always-visible reminder that this is capture-only — nothing is sent yet (RQ-26/27).</summary>
    public string Notice =>
        "Capture-only — no password is stored and nothing is sent yet. Used by a later phase to wire outgoing mail.";

    /// <summary>The SMTP server host name (e.g. smtp.example.com).</summary>
    [ObservableProperty] private string _host = string.Empty;

    /// <summary>The submission port (587 STARTTLS default, 465 implicit TLS, 25 plain).</summary>
    [ObservableProperty] private int _port = 587;

    /// <summary>Whether the connection uses TLS/SSL (on by default).</summary>
    [ObservableProperty] private bool _useTls = true;

    /// <summary>The envelope/from address outgoing mail is sent as.</summary>
    [ObservableProperty] private string _fromAddress = string.Empty;

    /// <summary>An optional display name paired with the from-address.</summary>
    [ObservableProperty] private string _fromName = string.Empty;

    /// <summary>A status line shown after Save (success or the reason it was rejected).</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Shell ctor: seed the form from the company's saved profile, or the defaults if none is saved.</summary>
    public SmtpSettingsViewModel(CompanyStorage storage, Company company)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var profile = _storage.GetSmtpProfile(company) ?? new SmtpProfile();
        Host = profile.Host;
        Port = profile.Port;
        UseTls = profile.UseTls;
        FromAddress = profile.FromAddress;
        FromName = profile.FromName ?? string.Empty;
    }

    /// <summary>The current form assembled into an <see cref="SmtpProfile"/> value (no password — R13).</summary>
    public SmtpProfile BuildProfile() => new()
    {
        Host = (Host ?? string.Empty).Trim(),
        Port = Port,
        UseTls = UseTls,
        FromAddress = (FromAddress ?? string.Empty).Trim(),
        FromName = string.IsNullOrWhiteSpace(FromName) ? null : FromName.Trim(),
    };

    /// <summary>True when the form has the minimum to save a usable (future) profile: a host, a from-address and a
    /// valid port. Drives the Save button's enabled state.</summary>
    public bool CanSave => BuildProfile().IsComplete;

    partial void OnHostChanged(string value) => OnPropertyChanged(nameof(CanSave));
    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(CanSave));
    partial void OnFromAddressChanged(string value) => OnPropertyChanged(nameof(CanSave));

    /// <summary>Ctrl+A / the Save button: upsert the captured profile for this company (one row per company).
    /// Returns whether it was saved. No password is ever written.</summary>
    public bool Save()
    {
        var profile = BuildProfile();
        if (!profile.IsComplete)
        {
            Status = "Enter a host, a from-address and a valid port (1-65535).";
            return false;
        }
        try
        {
            _storage.SaveSmtpProfile(_company, profile);
            Status = $"Saved SMTP settings for {_company.Name}. No password is stored; nothing is sent.";
            return true;
        }
        catch (Exception ex)
        {
            Status = "Could not save SMTP settings: " + ex.Message;
            return false;
        }
    }
}
