using System;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using Apex.Persistence.Sqlite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// 🔴 <b>Census row 16.1 — the <c>Data Vault</c> screen, reached by Alt+K (Company) &gt; Data Vault.</b> Sets a
/// passphrase on the open company, changes it, or takes the company back out of the vault.
///
/// <para><b>Fidelity (Ruling 14 tier 1).</b> The vendor reaches its equivalent from exactly here —
/// <i>"Press Alt+K (Company) &gt; …"</i> — and its screen carries a passphrase, a confirmation, and an
/// <i>"Old Password"</i> field when altering. Those three fields are cloned.
/// <b>The vendor's product NAME is not</b>: it carries the "Tally" brand and this application must never
/// render that word (R7), so the feature is called the <b>Data Vault</b> here. The name was checked against
/// the whole UI before it was taken — "Vault" appeared nowhere in <c>src/</c> outside doc comments quoting the
/// vendor.</para>
///
/// <para>🔴 <b>THE IRREVERSIBILITY IS STATED BEFORE THE ACT, NOT AFTER IT.</b>
/// <see cref="CompanyVault.IrreversibilityWarning"/> is on this screen from the moment it opens, above the
/// fields, because an operator who discovers that a forgotten passphrase is unrecoverable AFTER setting one
/// has already taken the risk. The vendor states the same thing; this application states it where the
/// decision is made.</para>
///
/// <para><b>Every operation here re-enters through <see cref="CompanyStorage"/></b>, which journals the file
/// swap so a crash mid-way cannot produce a half-vaulted company. Nothing in this class touches a file.</para>
/// </summary>
public sealed partial class CompanyVaultViewModel : ViewModelBase
{
    private readonly CompanyStorage _storage;
    private readonly Company _company;

    /// <summary>The column/screen title. Named from the feature constant so the screen, the menu row and the
    /// tests cannot drift apart.</summary>
    public string Title => CompanyVault.FeatureName;

    /// <summary>The open company, shown so it is never ambiguous WHICH book is about to be encrypted.</summary>
    public string CompanyName => _company.Name;

    /// <summary>True when this company is already in the vault — drives which fields the screen shows.</summary>
    [ObservableProperty] private bool _isVaulted;

    /// <summary>The vendor's <i>"Old Password"</i>. Required to change or remove an existing passphrase.</summary>
    [ObservableProperty] private string _currentPassphrase = string.Empty;

    /// <summary>The vendor's <i>"TallyVault Password"</i> field, de-branded.</summary>
    [ObservableProperty] private string _newPassphrase = string.Empty;

    /// <summary>The vendor's <i>"Confirm …"</i> field. A typo in a passphrase that is never stored is a lost
    /// book, which is precisely why the vendor asks twice and why this does too.</summary>
    [ObservableProperty] private string _confirmPassphrase = string.Empty;

    /// <summary>The result or refusal shown to the operator.</summary>
    [ObservableProperty] private string _message = string.Empty;

    /// <summary>The standing warning. A constant, not composed — see the type remarks.</summary>
    public string Warning => CompanyVault.IrreversibilityWarning;

    /// <summary>What the primary action will do, so the button and the screen agree.</summary>
    public string ActionLabel => IsVaulted ? "Change passphrase (Ctrl+A)" : "Set passphrase (Ctrl+A)";

    /// <summary>The entry this screen last produced — the caller re-reads it after a successful operation,
    /// because enabling or changing the vault MOVES the book to a new file.</summary>
    public CompanyEntry? UpdatedEntry { get; private set; }

    public CompanyVaultViewModel(CompanyStorage storage, Company company, CompanyEntry entry)
    {
        _storage = storage;
        _company = company;
        Entry = entry;
        IsVaulted = entry.IsVaulted;
    }

    /// <summary>The company's current registry entry — which file, and whether it is vaulted.</summary>
    public CompanyEntry Entry { get; private set; }

    partial void OnIsVaultedChanged(bool value) => OnPropertyChanged(nameof(ActionLabel));

    /// <summary>
    /// Ctrl+A — set the passphrase, or change it when one is already set. Returns true on success.
    ///
    /// <para><b>The refusals are checked in the order an operator would hit them</b>, and each returns a
    /// message rather than throwing: the two passphrases must match, the new one must clear the length floor,
    /// and (when changing) the current one must be correct.</para>
    /// </summary>
    public bool Apply()
    {
        if (NewPassphrase != ConfirmPassphrase)
        {
            Message = "The two passphrases do not match.";
            return false;
        }

        var weak = CompanyVault.DescribeWeakness(NewPassphrase);
        if (weak is not null)
        {
            Message = weak;
            return false;
        }

        try
        {
            Entry = IsVaulted
                ? _storage.ChangeVaultPassphrase(Entry, CurrentPassphrase, NewPassphrase)
                : _storage.EnableVault(Entry, _company, NewPassphrase);

            UpdatedEntry = Entry;
            IsVaulted = true;
            Message = $"This company is now in the {CompanyVault.FeatureName}. Its books are encrypted and its "
                + "name no longer appears on disk.";
            Clear();
            return true;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Alt+D — take the company OUT of the vault, restoring a plain book named after it. Requires the current
    /// passphrase. Returns true on success.
    /// </summary>
    public bool Remove()
    {
        if (!IsVaulted)
        {
            Message = $"This company is not in the {CompanyVault.FeatureName}.";
            return false;
        }

        try
        {
            Entry = _storage.DisableVault(Entry, _company, CurrentPassphrase);
            UpdatedEntry = Entry;
            IsVaulted = false;
            Message = $"This company has left the {CompanyVault.FeatureName}. Its books are no longer encrypted.";
            Clear();
            return true;
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            return false;
        }
    }

    private void Clear()
    {
        CurrentPassphrase = string.Empty;
        NewPassphrase = string.Empty;
        ConfirmPassphrase = string.Empty;
    }
}

/// <summary>
/// 🔴 <b>Census row 16.1 — the passphrase prompt shown when a VAULTED company is chosen on Company Select.</b>
/// Without it a vaulted company is listed and unopenable, which is half a feature.
///
/// <para><b>It shows the company NUMBER, never a name</b>, because there is no name to show — the real one is
/// inside the encrypted file. That is the vendor's own behaviour (<i>"displayed with a series of asterisks,
/// while the company number remains visible"</i>) and here it is also a statement of fact about what this
/// application can know before the passphrase arrives.</para>
/// </summary>
public sealed partial class CompanyUnlockViewModel : ViewModelBase
{
    /// <summary>The screen title.</summary>
    public string Title => $"Open Company — {CompanyVault.FeatureName}";

    /// <summary>The entry being opened.</summary>
    public CompanyEntry Entry { get; }

    /// <summary>What identifies the book to the operator: its number. There is nothing else to offer.</summary>
    public string Identification => $"Company {Entry.Number}";

    /// <summary>The typed passphrase.</summary>
    [ObservableProperty] private string _passphrase = string.Empty;

    /// <summary>The refusal, when the passphrase does not open the book.</summary>
    [ObservableProperty] private string _message = string.Empty;

    /// <summary>The standing notice — that a wrong passphrase cannot be diagnosed and a lost one cannot be reset.</summary>
    public string Notice =>
        "This company's books are encrypted. The passphrase is not stored anywhere, so it cannot be looked up "
        + "or reset.";

    public CompanyUnlockViewModel(CompanyEntry entry) => Entry = entry;
}
